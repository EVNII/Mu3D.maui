import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createManagedViewportHandler, createViewportHandler} from '../../src/Mu3D.Web/wwwroot/viewport-handler.mjs';
import {attachOrbitInput} from '../../src/Mu3D.Web.Toolkit/wwwroot/orbit-input.mjs';
import {attachPointerInput} from '../../src/Mu3D.Web.Toolkit/wwwroot/pointer-input.mjs';

import {createViewportEnvironment as environment} from './support/viewport-fixture.mjs';
import {sendEvent as send} from './support/pointer-fixture.mjs';

test('coalesces edits while rendering, suspends in-flight work safely, resumes with zero elapsed time', async () => {
  const env = environment(), calls = [];
  let finish;
  const host = createViewportHandler({canvas: env.canvas, maxDimension: 4096, continuous: true,
    onError: assert.fail, render: frame => {
      calls.push(frame);
      if (calls.length === 1) return new Promise(resolve => { finish = resolve; });
    }});
  assert.equal(env.frames.size, 0);
  env.show(); env.resize({inlineSize: 205, blockSize: 125});
  const flight = env.next(100);
  host.invalidate(); host.invalidate(true);
  assert.equal(env.frames.size, 0, 'No overlapping render');
  env.hidden(true); finish(); await flight;
  assert.equal(env.frames.size, 0, 'Finishing while hidden must not restart animation');
  assert.equal(calls[0].width, 205, 'Exact physical pixels win over CSS × DPR');
  env.hidden(false); await env.next(100000);
  assert.equal(calls[1].frameIntervalSeconds, 0);
  assert.equal(calls[1].refreshSurface, true);
  await env.next(100020);
  assert.equal(calls[2].frameIntervalSeconds, 0.02);
  host.setContinuous(false);
  assert.equal(env.frames.size, 0);
  host.invalidate(); host.invalidate(); await env.next(110000);
  assert.equal(calls.length, 4);
  assert.equal(calls[3].deltaSeconds, 0, 'On-demand edits must not accumulate idle time');
  host.dispose(); host.invalidate(); env.hidden(true); env.hidden(false);
  assert.equal(env.frames.size, 0);
  assert.ok(env.disconnected());
});

test('zero-size Canvas sleeps until resize; unsupported size reports an error without a frame loop', async () => {
  const env = environment(), calls = [], errors = [];
  const host = createViewportHandler({canvas: env.canvas, maxDimension: 256, continuous: true,
    render: size => calls.push(size), onError: error => errors.push(error)});
  env.canvas.rect.width = 0; env.show(); await env.next(100);
  assert.equal(env.frames.size, 0); assert.equal(calls.length, 0);
  env.canvas.rect.width = 300; env.resize(); await env.next(200);
  assert.equal(errors.length, 1); assert.equal(env.frames.size, 0);
  env.canvas.rect.width = 100; env.resize(); await env.next(300);
  assert.equal(calls.length, 1); assert.equal(env.frames.size, 0, 'Error recovery stays on demand');
  host.dispose();
});

test('DPR change invalidates stale device box and disposal during a frame never restarts scheduling', async () => {
  const env = environment(), calls = [];
  let finish;
  const host = createViewportHandler({canvas: env.canvas, maxDimension: 4096, continuous: true,
    onError: assert.fail, render: size => { calls.push(size); return new Promise(resolve => { finish = resolve; }); }});
  env.show(); env.resize({inlineSize: 200, blockSize: 120});
  const first = env.next(100); finish(); await first;
  globalThis.devicePixelRatio = 3;
  env.media[0].dispatchEvent(new Event('change'));
  const second = env.next(120);
  assert.equal(calls[1].width, 300); assert.equal(calls[1].refreshSurface, true);
  host.dispose(); finish(); await second;
  assert.equal(env.frames.size, 0);
});

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return {promise, resolve, reject};
}

test('an invalidation during a failed asynchronous frame retries once without restarting continuous rendering', async () => {
  const env = environment(), calls = [], errors = [], finish = deferred();
  const host = createViewportHandler({canvas: env.canvas, maxDimension: 4096, continuous: true,
    onError: error => errors.push(error), render: frame => {
      calls.push(frame);
      if (calls.length === 1) return finish.promise;
    }});
  try {
    env.show(); const first = env.next(100);
    host.invalidate(); host.invalidate(true);
    const failure = new Error('Loading frame failed');
    finish.reject(failure); await first;
    assert.deepEqual(errors, [failure]);
    assert.equal(env.frames.size, 1, 'The newer explicit redraw survives the older frame failure');
    await env.next(120);
    assert.equal(calls.length, 2);
    assert.equal(calls[1].refreshSurface, true);
    assert.equal(calls[1].frameIntervalSeconds, 0);
    assert.equal(env.frames.size, 0, 'A successful retry does not resume failed continuous playback');
  } finally { host.dispose(); }
});

test('managed callbacks share a serialized frame and immutable coalesced properties without export conventions', async () => {
  const env = environment(), calls = [], frames = [], snapshots = [];
  const entered = deferred(), finish = deferred();
  const initial = {exposure: 0, hdr: true};
  const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096, properties: initial,
    onError: assert.fail,
    callbacks: {
      async connect(properties) { calls.push('connect'); snapshots.push(properties); entered.resolve(); await finish.promise; },
      resize(frame) { calls.push('resize'); frames.push(frame); },
      setProperties(properties) { calls.push('properties'); snapshots.push(properties); },
      draw(frame, properties) { calls.push('draw'); snapshots.push(properties); return {value: properties.exposure}; },
      disconnect() { calls.push('disconnect'); },
    },
    onFrame(result, frame, properties) { calls.push('publish'); assert.equal(result.value, properties.exposure); },
  });
  initial.exposure = 99;
  env.show(); env.resize({inlineSize: 205, blockSize: 125});
  const first = env.next(100);
  await entered.promise;
  assert.equal(host.setProperties({exposure: -1}), true);
  assert.equal(host.setProperties({exposure: -2}), true);
  assert.equal(host.setProperties({exposure: -2, hdr: true}), false);
  assert.equal(env.frames.size, 0, 'An edit cannot overlap the pending connect/draw chain');
  finish.resolve(); await first;
  assert.deepEqual(calls, ['connect', 'resize', 'properties', 'draw', 'publish']);
  assert.ok(snapshots.every(Object.isFrozen));
  assert.ok(snapshots.every(properties => properties.exposure === 0), 'The active frame keeps one coherent snapshot');
  assert.deepEqual([frames[0].width, frames[0].height, frames[0].dpr, frames[0].sizing],
    [205, 125, 2, 'device-pixel-content-box']);
  await env.next(120);
  assert.deepEqual(calls.slice(5), ['properties', 'draw', 'publish'], 'Only the latest pending edit is applied');
  assert.equal(snapshots.at(-1).exposure, -2);
  assert.equal(env.frames.size, 0);
  assert.equal(host.setProperties({}), false); assert.equal(env.frames.size, 0);
  host.invalidate(true); await env.next(140);
  assert.deepEqual(calls.slice(8), ['resize', 'draw', 'publish'], 'Same-size display events still refresh the surface');
  assert.equal(frames.at(-1).refreshSurface, true);
  await host.dispose();
  assert.equal(calls.at(-1), 'disconnect');
});

test('managed disposal detaches immediately and waits every callback phase before one disconnect', async t => {
  const phases = ['connect', 'resize', 'properties', 'draw', 'publish'];
  for (const stoppedAt of phases) await t.test(stoppedAt, async () => {
    const env = environment(), calls = [], entered = deferred(), finish = deferred();
    async function phase(name) {
      calls.push(name);
      if (name === stoppedAt) { entered.resolve(); await finish.promise; }
      return 'frame';
    }
    const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096, continuous: true,
      onError: assert.fail,
      callbacks: {
        connect: () => phase('connect'), resize: () => phase('resize'),
        setProperties: () => phase('properties'), draw: () => phase('draw'),
        disconnect: () => phase('disconnect'),
      }, onFrame: () => phase('publish')});
    env.show(); const frame = env.next(100); await entered.promise;
    const closing = host.dispose();
    assert.equal(host.dispose(), closing, 'Repeated disposal returns the same completion promise');
    assert.equal(host.isDisposed, true); assert.ok(env.disconnected());
    assert.throws(() => host.setProperties({value: 1}), /disposed/);
    host.invalidate(true); host.setContinuous(true); env.hidden(true); env.hidden(false);
    assert.equal(env.frames.size, 0);
    let closed = false; closing.then(() => { closed = true; });
    await Promise.resolve(); assert.equal(closed, false, 'The pending callback still owns its resources');
    assert.equal(calls.includes('disconnect'), false);
    finish.resolve(); await frame; await closing;
    assert.deepEqual(calls, [...phases.slice(0, phases.indexOf(stoppedAt) + 1), 'disconnect'],
      'Later draw/publication phases cannot run after disposal');
    assert.equal(env.frames.size, 0);
  });
});

test('a failed managed connection closes and cleans its partial attachment before reporting the error', async () => {
  const env = environment(), calls = [], errors = [], cleanup = deferred(), cleaning = deferred();
  const failure = new Error('Partial attach failed');
  const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096, continuous: true,
    onError: error => errors.push(error),
    callbacks: {
      connect() { calls.push('connect'); throw failure; },
      draw() { calls.push('draw'); },
      async disconnect() { calls.push('disconnect'); cleaning.resolve(); await cleanup.promise; },
    }, onFrame() { calls.push('publish'); }});
  env.show(); const frame = env.next(100); await cleaning.promise;
  assert.equal(host.isDisposed, true); assert.ok(env.disconnected());
  assert.equal(errors.length, 0, 'Connection error publication waits for partial-resource cleanup');
  cleanup.resolve(); await frame;
  assert.deepEqual(calls, ['connect', 'disconnect']); assert.deepEqual(errors, [failure]);
  const closing = host.dispose(); assert.equal(host.dispose(), closing); await closing;
  host.invalidate(); host.setContinuous(true); env.show(); assert.equal(env.frames.size, 0);
});

test('a connection rejection after explicit disposal cleans once without publishing a late error', async () => {
  const env = environment(), entered = deferred(), finish = deferred(), errors = [];
  let disconnects = 0;
  const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096,
    onError: error => errors.push(error), callbacks: {
      connect() { entered.resolve(); return finish.promise; }, draw: assert.fail,
      disconnect() { disconnects++; },
    }, onFrame: assert.fail});
  env.show(); const frame = env.next(100); await entered.promise;
  const closing = host.dispose();
  finish.reject(new Error('A now-obsolete connect completion'));
  await frame; await closing;
  assert.equal(disconnects, 1); assert.deepEqual(errors, []); assert.equal(env.frames.size, 0);
});

test('connection and cleanup failures are preserved and disconnect is never retried', async () => {
  const env = environment(), errors = [];
  const attachError = new Error('attach'), cleanupError = new Error('cleanup');
  let disconnects = 0;
  const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096,
    onError: error => errors.push(error), callbacks: {
      connect() { throw attachError; }, draw: assert.fail,
      disconnect() { disconnects++; throw cleanupError; },
    }});
  env.show(); await env.next(100);
  assert.ok(errors[0] instanceof AggregateError);
  assert.deepEqual(errors[0].errors, [attachError, cleanupError]);
  const closing = host.dispose(); assert.equal(host.dispose(), closing);
  await assert.rejects(closing, error => error === cleanupError);
  assert.equal(disconnects, 1);
});

test('failed managed frame phases recover explicitly, including a failed same-size surface refresh', async t => {
  for (const failedPhase of ['resize', 'properties', 'draw', 'publish']) await t.test(failedPhase, async () => {
    const env = environment(), calls = [], errors = [];
    let failNext = false;
    function phase(name) {
      calls.push(name);
      if (failNext && name === failedPhase) { failNext = false; throw new Error(`failed ${name}`); }
      return 'frame';
    }
    const host = createManagedViewportHandler({canvas: env.canvas, maxDimension: 4096,
      onError: error => errors.push(error), callbacks: {
        connect: () => phase('connect'), resize: () => phase('resize'),
        setProperties: () => phase('properties'), draw: () => phase('draw'),
        disconnect: () => phase('disconnect'),
      }, onFrame: () => phase('publish')});
    env.show(); await env.next(100);
    failNext = true;
    if (failedPhase === 'properties') host.setProperties({newValue: 1});
    else host.invalidate(failedPhase === 'resize');
    await env.next(120);
    assert.equal(errors.length, 1); assert.equal(host.isDisposed, false); assert.equal(env.frames.size, 0);
    const boundary = calls.length;
    host.invalidate(); await env.next(140);
    assert.ok(calls.slice(boundary).includes(failedPhase), 'The failed phase must be retried');
    assert.equal(calls.filter(name => name === 'connect').length, 1, 'Frame failure does not replace the connection');
    assert.equal(calls.at(-1), 'publish'); assert.equal(errors.length, 1);
    await host.dispose();
  });
});

test('actual static render host delegates sizing, intent, refresh and safe shutdown to the common handler', async () => {
  const env = environment(), elements = new Map(), calls = [], logs = [], statuses = [];
  for (const id of ['exposure', 'output-mode', 'resolution', 'display-state', 'pixel-report'])
    elements.set(id, Object.assign(new EventTarget(), {value: '', textContent: '', dataset: {}}));
  elements.set('mu3d-canvas', env.canvas);
  document.querySelector = selector => elements.get(selector.slice(1));
  globalThis.location = {href: 'http://localhost/Render/?output=hdr', search: '?output=hdr', reload: assert.fail};
  globalThis.history = {replaceState(_, __, url) { location.href = String(url); }};
  let configuration, frame = 0, finish;
  const drawing = deferred(), disconnected = deferred();
  env.canvas.getContext = () => ({getConfiguration: () => configuration});
  const renderer = {GetMaximumDimension: () => 4096,
    async RenderFrame(width, height, exposure, hdr, refresh) {
      calls.push({width, height, exposure, hdr, refresh});
      if (finish) { drawing.resolve(); await finish.promise; }
      env.canvas.width = width; env.canvas.height = height;
      configuration = {format: hdr ? 'rgba16float' : 'bgra8unorm', colorSpace: 'srgb', alphaMode: 'opaque',
        toneMapping: {mode: hdr ? 'extended' : 'standard'}};
      return JSON.stringify({Frame: ++frame, ScenePatches: [0.5, 1, 2, 4], CanvasPatches: [0.5, 1, 2, 4]});
    }, Shutdown() { calls.push('shutdown'); disconnected.resolve(); }};
  const source = (await readFile(new URL('../Mu3D.Web.Validation/wwwroot/render-host.mjs', import.meta.url), 'utf8'))
    .replace("'./mu3d/viewport-handler.mjs'", JSON.stringify(new URL('../../src/Mu3D.Web/wwwroot/viewport-handler.mjs', import.meta.url).href));
  const {startRenderHost} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
  const ready = startRenderHost({getConfig: () => ({mainAssemblyName: 'fixture'}),
    getAssemblyExports: async () => ({Mu3D: {Web: {Validation: {RenderProbe: renderer}}}})},
    value => logs.push(value), (...value) => statuses.push(value));
  await Promise.resolve(); env.show(); env.resize({inlineSize: 205, blockSize: 125});
  await env.next(100); await ready;
  assert.deepEqual(calls[0], {width: 205, height: 125, exposure: 0, hdr: true, refresh: true});
  assert.equal(elements.get('resolution').dataset.sizing, 'device-pixel-content-box');
  assert.equal(elements.get('display-state').dataset.format, 'rgba16float');
  assert.equal(statuses.at(-1)[0], 'passed');
  env.resize({inlineSize: 205, blockSize: 125}); await env.next(120);
  assert.equal(calls.length, 1, 'A same-size observer event is still deduplicated by demo policy');
  elements.get('exposure').value = '-2'; elements.get('exposure').dispatchEvent(new Event('change'));
  await env.next(140); assert.equal(calls[1].exposure, -2);
  elements.get('output-mode').value = 'sdr'; elements.get('output-mode').dispatchEvent(new Event('change'));
  await env.next(160); assert.equal(calls[2].hdr, false);
  assert.equal(elements.get('display-state').dataset.range, 'standard');
  assert.equal(new URL(location.href).searchParams.get('output'), 'sdr');
  window.dispatchEvent(new Event('focus')); await env.next(180); assert.equal(calls[3].refresh, true);
  finish = deferred(); window.dispatchEvent(new Event('focus'));
  const pending = env.next(200);
  // Wait for the actual diagnostic callback rather than an assumed number of microtasks.
  await drawing.promise;
  const logCount = logs.length, result = elements.get('pixel-report').dataset.result;
  window.dispatchEvent(new Event('pagehide'));
  assert.ok(env.disconnected()); assert.equal(calls.includes('shutdown'), false);
  finish.resolve(); await pending;
  // The handler teardown resumes after its callback chain settles.
  await disconnected.promise;
  assert.equal(logs.length, logCount); assert.equal(elements.get('pixel-report').dataset.result, result);
  assert.equal(calls.filter(value => value === 'shutdown').length, 1); assert.equal(env.frames.size, 0);
  elements.get('exposure').dispatchEvent(new Event('change')); assert.equal(env.frames.size, 0);
});

test('orbit adapter owns only its pointer, leaves page pinch alone, and restores host state on disposal', () => {
  const env = environment(), commands = [], canvas = env.canvas;
  const adapter = attachOrbitInput(canvas, command => commands.push(command));
  send(canvas, 'pointerdown', {isPrimary: true, button: 0, pointerId: 7, clientX: 10, clientY: 10});
  send(canvas, 'pointercancel', {pointerId: 8});
  assert.equal(canvas.capture, 7, 'Another pointer cannot release our gesture');
  send(canvas, 'pointermove', {pointerId: 7, clientX: 35, clientY: 10});
  assert.equal(commands[0].rotateX, -Math.PI / 2);
  send(canvas, 'pointercancel', {pointerId: 7});
  send(canvas, 'pointermove', {pointerId: 7, clientX: 50, clientY: 10});
  assert.equal(canvas.capture, null); assert.equal(commands.length, 1);
  assert.equal(send(canvas, 'wheel', {ctrlKey: true, deltaY: 100}).defaultPrevented, false);
  assert.equal(commands.length, 1);
  assert.equal(send(canvas, 'wheel', {deltaMode: 1, deltaY: 10}).defaultPrevented, true);
  assert.equal(commands[1].dolly, -0.16);
  send(canvas, 'keydown', {key: 'ArrowRight'});
  assert.equal(commands[2].rotateX, 0.1);
  send(canvas, 'pointerdown', {isPrimary: true, button: 0, pointerId: 9, clientX: 0, clientY: 0});
  adapter.dispose(); adapter.dispose();
  assert.equal(canvas.style.touchAction, 'pan-y'); assert.equal(canvas.capture, null);
  send(canvas, 'keydown', {key: 'ArrowRight'});
  assert.equal(commands.length, 3);
});

test('pen samples preserve pressure/angles/buttons and coalesced order; capture excludes camera and other contacts', () => {
  const {canvas} = environment(), batches = [], commands = [];
  const pointer = attachPointerInput(canvas, {acceptContact: s => s.pointerType === 'pen', onSamples: s => batches.push(s)});
  const orbit = attachOrbitInput(canvas, s => commands.push(s), {isEnabled: () => !pointer.isActive});
  const pen = {isPrimary: true, pointerId: 3, pointerType: 'pen', button: 0, buttons: 1,
    clientX: 70, clientY: 70, pressure: 0.25, tiltX: -20, tiltY: 35, twist: 123};
  send(canvas, 'pointerdown', pen);
  assert.equal(canvas.capture, 3);
  assert.deepEqual([batches[0][0].x, batches[0][0].y], [100, 60]);
  assert.deepEqual([batches[0][0].pressure, batches[0][0].tiltX, batches[0][0].twist], [0.25, -20, 123]);
  assert.equal(batches[0][0].altitudeAngle, null, 'Missing data is unknown, not synthesized');
  send(canvas, 'pointerdown', {...pen, pointerId: 4, pointerType: 'touch'});
  send(canvas, 'pointercancel', {pointerId: 4});
  assert.equal(canvas.capture, 3);
  send(canvas, 'pointermove', {...pen, getCoalescedEvents: () => [
    {...pen, pressure: 0.4, clientX: 90}, {...pen, pressure: 0.8, clientX: 140, buttons: 3} ]});
  assert.equal(batches.at(-1).length, 2, 'Do not duplicate the parent event after coalesced samples');
  assert.equal(batches.at(-1)[1].x, 240, 'Captured coordinates can leave the Canvas');
  assert.equal(batches.at(-1)[1].barrel, true);
  send(canvas, 'wheel', {deltaY: 100});
  send(canvas, 'keydown', {key: 'ArrowRight'});
  assert.equal(commands.length, 0, 'Claimed pen/gizmo must not change the camera');
  send(canvas, 'pointerup', {...pen, pressure: 0, buttons: 0});
  assert.equal(canvas.capture, null);
  assert.equal(batches.at(-1)[0].phase, 'up');
  send(canvas, 'pointerdown', {...pen, buttons: 32, button: 5});
  assert.equal(batches.at(-1)[0].eraser, true);
  send(canvas, 'keydown', {key: 'Escape'});
  assert.equal(batches.at(-1)[0].phase, 'cancel');
  assert.equal(canvas.capture, null);
  orbit.dispose(); pointer.dispose();
});

test('unclaimed pointers reach Orbit; hide/blur/lost-capture/disposal cancel exactly once', () => {
  for (const reason of ['hide', 'blur', 'lostpointercapture', 'dispose']) {
    const env = environment(), batches = [], commands = [], canvas = env.canvas;
    const pointer = attachPointerInput(canvas, {acceptContact: s => s.pointerType === 'pen', onSamples: s => batches.push(s)});
    const orbit = attachOrbitInput(canvas, s => commands.push(s), {isEnabled: () => !pointer.isActive});
    const contact = {isPrimary: true, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, clientX: 30, clientY: 50};
    send(canvas, 'pointerdown', contact);
    send(canvas, 'pointermove', {...contact, clientX: 40});
    send(canvas, 'pointerup', {...contact, buttons: 0});
    assert.equal(commands.length, 1, 'A miss must fall through to the camera');
    send(canvas, 'pointerdown', {...contact, pointerType: 'pen'});
    if (reason === 'hide') env.hidden(true);
    else if (reason === 'blur') window.dispatchEvent(new Event('blur'));
    else if (reason === 'dispose') pointer.dispose();
    else send(canvas, reason, {pointerId: 1});
    pointer.cancel(); pointer.dispose(); orbit.dispose();
    assert.equal(batches.flat().filter(s => s.phase === 'cancel').length, 1, reason);
    assert.equal(canvas.capture, null);
    assert.equal(canvas.style.touchAction, 'pan-y');
  }
});
