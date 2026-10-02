import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {setMaxListeners} from 'node:events';
import {createTestDocument} from './support/dom-fixture.mjs';
import {createViewportEnvironment} from './support/viewport-fixture.mjs';
import {sendEvent as send} from './support/pointer-fixture.mjs';

// Run the real host, scheduler, pointer/Orbit/selection adapters and statistics source/bridge/view.
// The renderer export is a bounded fixture; the managed collector is checked separately in
// ViewerStatisticsChecks. The unrelated scene-node overlay has no DOM/input role in these tests.
setMaxListeners(0);
const moduleUrl = source => `data:text/javascript;base64,${Buffer.from(source).toString('base64')}`;
const fileUrl = relative => new URL(relative, import.meta.url).href;
const hostSource = await readFile(new URL('../Mu3D.Web.Validation/wwwroot/viewer-host.mjs', import.meta.url), 'utf8');
const nodeOverlayStub = moduleUrl(`
  export function createSceneNodeAnchorSource() {
    return {node: token => Object.freeze({token}), clear() {}, dispose() {}};
  }
  export function createSceneNodeOverlay() {
    return {hasActivePointer: false, isEnabled: true, dispose() {},
      bind(element, options) {
        const listeners = new Set(); let target = options.target, localPosition = options.localPosition;
        const binding = {...options,
          get target() { return target; }, set target(value) {
            if (value === target) return; target = value; for (const listener of listeners) listener();
          },
          get localPosition() { return localPosition; }, set localPosition(value) {
            localPosition = value; for (const listener of listeners) listener();
          }, subscribeOptions(listener) { listeners.add(listener); return () => listeners.delete(listener); }};
        globalThis.viewerGizmoFixture.bindings ??= [];
        globalThis.viewerGizmoFixture.bindings.push(binding); return binding;
      }};
  }
`);
const selectionWrapper = moduleUrl(`
  import {createSceneSelectionSource as source, attachSceneSelectionInput as attach}
    from ${JSON.stringify(fileUrl('../../src/Mu3D.Web.Toolkit/wwwroot/scene-selection.mjs'))};
  export function createSceneSelectionSource(...args) {
    const result = source(...args); globalThis.viewerGizmoFixture.selection = result; return result;
  }
  export const attachSceneSelectionInput = attach;
`);
const statisticsWrapper = moduleUrl(`
  import {createFrameStatisticsSource as source, createFrameStatisticsOverlay as overlay}
    from ${JSON.stringify(fileUrl('../../src/Mu3D.Web.Toolkit/wwwroot/frame-statistics.mjs'))};
  export function createFrameStatisticsSource(...args) {
    const result = source(...args); globalThis.viewerGizmoFixture.source = result; return result;
  }
  export function createFrameStatisticsOverlay(...args) {
    const result = overlay(...args); globalThis.viewerGizmoFixture.overlay = result; return result;
  }
`);
const orbitWrapper = moduleUrl(`
  import {attachOrbitInput as attach}
    from ${JSON.stringify(fileUrl('../../src/Mu3D.Web.Toolkit/wwwroot/orbit-input.mjs'))};
  export function attachOrbitInput(canvas, onInput, options) {
    globalThis.viewerGizmoFixture.orbitDeltas = [];
    return attach(canvas, delta => {
      globalThis.viewerGizmoFixture.orbitDeltas.push(delta); onInput(delta);
    }, options);
  }
`);
const imports = {
  './mu3d/viewport-handler.mjs': fileUrl('../../src/Mu3D.Web/wwwroot/viewport-handler.mjs'),
  './mu3d/orbit-input.mjs': orbitWrapper,
  './mu3d/pointer-input.mjs': fileUrl('../../src/Mu3D.Web.Toolkit/wwwroot/pointer-input.mjs'),
  './mu3d/scene-node-overlay.mjs': nodeOverlayStub,
  './mu3d/frame-statistics.mjs': statisticsWrapper,
  './mu3d/scene-selection.mjs': selectionWrapper,
  './viewer-statistics-bridge.mjs': fileUrl('../Mu3D.Web.Validation/wwwroot/viewer-statistics-bridge.mjs'),
  './viewer-node-anchors-bridge.mjs': fileUrl('../Mu3D.Web.Validation/wwwroot/viewer-node-anchors-bridge.mjs'),
  './viewer-selection-bridge.mjs': fileUrl('../Mu3D.Web.Validation/wwwroot/viewer-selection-bridge.mjs'),
};
function loadHost() {
  let source = hostSource;
  for (const [specifier, url] of Object.entries(imports)) {
    assert.ok(source.includes(`'${specifier}'`), `Host still imports ${specifier}`);
    source = source.replace(`'${specifier}'`, JSON.stringify(url));
  }
  return import(moduleUrl(source));
}

function environment() {
  const inputs = [], errors = [], endCalls = [];
  let angle = 0, frame = 0;
  let dragging = false, hit = true, statisticsVersion = 0, previous, publishedAt;
  let selected = 'model', selectionVersion = 0, highlightEnabled = true, selectionCandidates = [];
  const selectionCalls = [], selectionHits = [], highlightCalls = [];
  let sampleCount = 0, sum = 0, minimum = Infinity, maximum = 0;
  let statisticsOptions = {isEnabled: true, interval: 500}, latest;
  const document = createTestDocument({rect: {left: 20, top: 40, width: 640, height: 360}});
  const elements = document.elements;
  const canvas = document.getElementById('mu3d-canvas');
  Object.assign(canvas, {width: 640, height: 360, clientHeight: 360, capture: null,
    setPointerCapture(id) { this.capture = id; }, hasPointerCapture(id) { return this.capture === id; },
    releasePointerCapture() { this.capture = null; }, focus() {},
    getContext() { return {getConfiguration: () => ({format: 'rgba16float', colorSpace: 'srgb',
      alphaMode: 'opaque', toneMapping: {mode: 'extended'}})}; }});
  for (const [id, value] of Object.entries({metallic: '0.35', roughness: '0.28', 'gizmo-mode': '0',
    'gizmo-space': 'world', 'overlay-target': 'green', 'overlay-height': '0.8'}))
    document.getElementById(id).value = value;
  document.getElementById('gizmo-enabled').checked = true;
  const viewport = createViewportEnvironment({canvas, dpr: 1});
  globalThis.location = {search: '?animate=0', reload() { assert.fail('No reload expected'); }};
  globalThis.viewerGizmoFixture = {};
  const payload = () => JSON.stringify({version: statisticsVersion,
    snapshot: latest ?? globalThis.viewerGizmoFixture.source.latestSnapshot});
  const selectedReport = () => ({Version: selectionVersion, Token: selected, TargetVisible: selected !== null,
    HighlightEnabled: highlightEnabled, Position: selected === null ? null : [0, 0, 0],
    Rotation: selected === null ? null : [0, 0, 0, 1], Scale: selected === null ? null : [1, 1, 1]});
  const renderer = {
    GetMaximumDimension: () => 4096,
    GetViewerNodeCatalog: () => JSON.stringify({SourceId: 'fixture', Nodes:
      ['blue', 'cone', 'green', 'model'].map(Key => ({Key, Token: Key}))}),
    HitTestViewerSelection(x, y, mask) {
      selectionHits.push({x, y, mask});
      return JSON.stringify({SourceId: 'fixture', Candidates: selectionCandidates});
    },
    SelectViewerNode(token) {
      const value = token || null; selectionCalls.push(value);
      if (value !== selected) { selected = value; selectionVersion++; }
      return JSON.stringify(selectedReport());
    },
    ConfigureViewerHighlight(value) { highlightEnabled = value; highlightCalls.push(value); },
    ConfigureViewerStatistics(isEnabled, interval) { statisticsOptions = {isEnabled, interval}; },
    GetViewerStatisticsSnapshot: payload,
    ResetViewerStatistics() { statisticsVersion++; latest = {...globalThis.viewerGizmoFixture.source.latestSnapshot,
      sampleCount: 0}; return payload(); },
    PublishViewerStatistics() { statisticsVersion++; return payload(); },
    async RenderFrame() { return JSON.stringify({ScenePatches: [1, 2, 4], CanvasPatches: [1, 2, 4]}); },
    async RenderViewerFrame(width, height, ev, hdr, refresh, json) {
      const input = JSON.parse(json); inputs.push(input); canvas.width = width; canvas.height = height;
      if (input.Playing && !dragging) angle += input.DeltaSeconds * 0.45;
      // Fixture versioning depends on the actual host continuity signal and completed RAF time.
      // It is intentionally not a substitute for shared collector arithmetic in the managed test.
      if (statisticsOptions.isEnabled) {
        if (!input.FrameIntervalSeconds || refresh) { previous = viewport.now; publishedAt = undefined; }
        else if (previous !== undefined) {
          const duration = viewport.now - previous; previous = viewport.now;
          if (duration > 0) {
            sampleCount++; sum += duration; minimum = Math.min(minimum, duration); maximum = Math.max(maximum, duration);
            if (publishedAt === undefined || viewport.now - publishedAt >= statisticsOptions.interval) {
              latest = {...globalThis.viewerGizmoFixture.source.latestSnapshot, sampleCount,
                windowDurationMilliseconds: sum, framesPerSecond: sampleCount * 1000 / sum,
                averageFrameMilliseconds: sum / sampleCount, minimumFrameMilliseconds: minimum,
                maximumFrameMilliseconds: maximum};
              statisticsVersion++; publishedAt = viewport.now;
            }
          }
        } else previous = viewport.now;
      }
      return JSON.stringify({Frame: ++frame, Angle: angle, StatisticsVersion: statisticsVersion,
        NodeAnchors: null, Dragging: dragging, ActiveAxis: dragging ? 'X' : null,
        ModelPosition: [0, 0, 0], ModelScale: [1, 1, 1], Selection: selectedReport()});
    },
    BeginViewerDrag() { if (!hit) return false; dragging = true; return true; },
    UpdateViewerDrag() {}, EndViewerDrag(cancel) { dragging = false; endCalls.push(cancel); },
    ConfigureViewerGizmo() {}, Shutdown() {},
  };
  return {...viewport, elements, inputs, endCalls, renderer, errors, selectionCalls, selectionHits, highlightCalls,
    get source() { return globalThis.viewerGizmoFixture.source; },
    get overlay() { return globalThis.viewerGizmoFixture.overlay; },
    get version() { return statisticsVersion; },
    get selection() { return globalThis.viewerGizmoFixture.selection; },
    get orbitDeltas() { return globalThis.viewerGizmoFixture.orbitDeltas; },
    get card() { return globalThis.viewerGizmoFixture.bindings[1]; },
    set candidates(value) { selectionCandidates = value.map((Token, index) => ({Token, Distance: index + 1,
      TriangleIndex: 0, WorldPosition: [0, 0, 0], LocalPosition: [0, 0, 0]})); },
    set hit(value) { hit = value; },
    teardown() { send(window, 'pagehide'); },
  };
}
const contact = {isPrimary: true, pointerId: 7, pointerType: 'mouse', button: 0, buttons: 1,
  clientX: 350, clientY: 220};
async function start() {
  const env = environment(), host = await loadHost();
  const ready = host.startViewerHost({getConfig: () => ({mainAssemblyName: 'fixture'}),
    getAssemblyExports: async () => ({Mu3D: {Web: {Validation: {RenderProbe: env.renderer}}}})},
    message => env.errors.push(message), () => {});
  await Promise.resolve(); env.show(); await env.next(1000); await ready;
  assert.equal(env.frames.size, 0, 'Paused startup is on demand');
  return env;
}

// Every case gets a fresh host and guaranteed teardown, including failed assertions.
function viewerTest(title, run, cases = [undefined]) {
  test(title, async () => {
    for (const value of cases) {
      const env = await start();
      try { await run(env, value); }
      finally { env.teardown(); }
    }
  });
}

viewerTest('actual paused Gizmo host publishes fresh FPS/history using the existing active-capture RAF', async env => {
  send(env.canvas, 'pointerdown', contact);
  await env.next(2000); // Starting capture deliberately establishes a new cadence boundary.
  assert.equal(env.inputs.at(-1).FrameIntervalSeconds, 0);
  for (let timestamp = 2020; timestamp <= 2640; timestamp += 20) {
    send(env.canvas, 'pointermove', {...contact, clientX: 350 + (timestamp - 2000) / 10});
    await env.next(timestamp); // The previous asynchronous frame is fully completed before each move.
  }
  assert.ok(env.inputs.slice(2).every(input => input.Playing === false), 'Animation remains paused');
  assert.ok(env.inputs.slice(3).every(input => Math.abs(input.FrameIntervalSeconds - 0.02) < 1e-9));
  assert.equal(env.version, 2, 'The immediate sample and next 500 ms publication both arrive');
  assert.deepEqual(env.overlay.view.history, [50, 50], 'The real statistics view receives both publications');
  assert.equal(env.source.latestSnapshot.sampleCount, 26);
  assert.equal(JSON.parse(env.elements.get('viewer-statistics').dataset.result).Angle, 0);
  assert.match(env.overlay.view.element.children[0].textContent, /^50 FPS/);
  assert.equal(env.frames.size, 1, 'The same scheduler remains continuous while captured, even at rest');
  const retained = env.source.latestSnapshot;
  send(env.canvas, 'pointerup', {...contact, buttons: 0}); await env.next(2660);
  assert.deepEqual(env.endCalls, [false]); assert.equal(env.canvas.capture, null); assert.equal(env.frames.size, 0);
  assert.equal(env.source.latestSnapshot, retained, 'Ending capture retains the published snapshot/history');
  send(env.elements.get('metallic'), 'input'); await env.next(90000);
  assert.equal(env.inputs.at(-1).FrameIntervalSeconds, 0, 'A separate paused edit excludes idle time');
  assert.equal(env.frames.size, 0); assert.deepEqual(env.overlay.view.history, [50, 50]);
  assert.equal(env.errors.length, 1, 'Only the expected startup pixel-check log was written');
});

viewerTest('cancel, Escape, capture loss, blur, hide and teardown release active Gizmo cadence', async (env, reason) => {
  send(env.canvas, 'pointerdown', contact); await env.next(2000); await env.next(2020);
  assert.equal(env.version, 1); const retained = env.source.latestSnapshot;
  if (reason === 'cancel') send(env.canvas, 'pointercancel', {pointerId: 7});
  else if (reason === 'escape') send(env.canvas, 'keydown', {key: 'Escape'});
  else if (reason === 'lost-capture') send(env.canvas, 'lostpointercapture', {pointerId: 7});
  else if (reason === 'blur') send(window, 'blur');
  else if (reason === 'hide') env.hidden(true);
  else env.teardown();
  assert.deepEqual(env.endCalls, [true], reason);
  assert.equal(env.canvas.capture, null, reason);
  if (reason === 'hide') {
    assert.equal(env.frames.size, 0); env.hidden(false); await env.next(90000);
    assert.equal(env.inputs.at(-1).FrameIntervalSeconds, 0, 'Resume primes a new boundary');
  } else if (reason !== 'dispose') await env.next(2040);
  assert.equal(env.frames.size, 0, `${reason} cannot retain an idle render loop`);
  assert.equal(env.source.latestSnapshot, retained, `${reason} preserves borrowed diagnostics history`);
}, ['cancel', 'escape', 'lost-capture', 'blur', 'hide', 'dispose']);

viewerTest('a Gizmo miss and inspected pen contact never opt paused rendering into continuous capture', async (env, pen) => {
  env.hit = false; env.elements.get('pen-inspect').checked = pen;
  send(env.canvas, 'pointerdown', {...contact, pointerType: pen ? 'pen' : 'mouse'});
  assert.equal(env.frames.size, 0, 'Contact without active Gizmo does not start rendering');
  send(env.canvas, 'pointerup', {...contact, pointerType: pen ? 'pen' : 'mouse', buttons: 0});
  if (!pen) {
    assert.equal(env.frames.size, 1, 'The completed empty tap requests one deselection frame');
    await env.next(2000);
  }
  assert.equal(env.frames.size, 0); assert.deepEqual(env.endCalls, []);
}, [false, true]);

viewerTest('completed selection taps and bound target changes share the selected Gizmo/card state', async env => {
  env.hit = false; env.candidates = ['blue'];
  assert.equal(env.selection.selectedNode.token, 'model', 'The demo starts with the prior editable group explicitly selected');
  send(env.canvas, 'pointerdown', contact);
  assert.equal(env.selectionHits.length, 0, 'A contact does not commit before its completed tap');
  send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.deepEqual(env.selectionHits, [{x: 330, y: 180, mask: 0xffffffff}]);
  assert.equal(env.selection.selectedNode.token, 'blue');
  assert.equal(env.card.target, env.selection.selectedNode, 'Tracked UI borrows the selected source-scoped handle');
  assert.equal(env.elements.get('overlay-target').value, 'blue');
  await env.next(2000);
  assert.equal(JSON.parse(env.elements.get('viewer-statistics').dataset.result).Selection.Token, 'blue');
  const target = env.elements.get('overlay-target'); target.value = 'green'; send(target, 'change');
  assert.equal(env.selection.selectedNode.token, 'green');
  assert.equal(env.card.target, env.selection.selectedNode);
  assert.deepEqual(env.selectionCalls, ['model', 'blue', 'green']);
  await env.next(2020); assert.equal(env.frames.size, 0, 'Selection uses the existing on-demand scheduler');
  env.candidates = []; send(env.canvas, 'pointerdown', contact); send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.equal(env.selection.selectedNode, null); assert.equal(env.card.target, null);
  assert.equal(env.elements.get('overlay-target').value, 'none');
  await env.next(2040);
  assert.equal(env.errors.length, 1, 'Only the expected startup numeric check was logged');
});

viewerTest('selection policies, ranking overrides and physical-pixel mapping stay application owned', async env => {
  env.hit = false;
  globalThis.devicePixelRatio = 2; env.resize(); await env.next(2000);
  env.candidates = ['blue', 'green'];
  const rank = env.selection.subscribeRequested(request => request.selectedNode = request.candidates[1].node);
  send(env.canvas, 'pointerdown', contact); send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.deepEqual(env.selectionHits[0], {x: 660, y: 360, mask: 0xffffffff},
    'A CSS-pixel contact becomes the actual double-density backing-pixel raycast position');
  assert.equal(env.selection.selectedNode.token, 'green', 'Application ranking overrides the nearest proposal');
  rank(); await env.next(2020);
  const miss = env.elements.get('selection-clear-miss'); miss.checked = false; send(miss, 'change');
  env.candidates = []; send(env.canvas, 'pointerdown', contact); send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.equal(env.selection.selectedNode.token, 'green'); assert.equal(env.frames.size, 0);
  const enabled = env.elements.get('selection-enabled'); enabled.checked = false; send(enabled, 'change');
  env.candidates = ['blue']; send(env.canvas, 'pointerdown', contact); send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.equal(env.selectionHits.length, 2, 'Disabling input leaves selected state intact and does not raycast');
  const target = env.elements.get('overlay-target'); target.value = 'cone'; send(target, 'change');
  assert.equal(env.selection.selectedNode.token, 'cone', 'Bound programmatic changes remain available when tap selection is disabled');
  await env.next(2040);
  const highlight = env.elements.get('highlight-enabled'); highlight.checked = false; send(highlight, 'change');
  assert.deepEqual(env.highlightCalls, [false]); assert.equal(env.selection.selectedNode.token, 'cone');
  await env.next(2060);
});

viewerTest('ordinary clicks retain selection through stationary and small pointer jitter without Orbit motion', async (env, distance) => {
  env.hit = false; env.candidates = ['blue'];
  send(env.canvas, 'pointerdown', contact);
  const sample = {...contact, clientX: contact.clientX + distance};
  send(env.canvas, 'pointermove', sample);
  send(env.canvas, 'pointermove', sample);
  assert.deepEqual(env.orbitDeltas, [], `${distance} CSS-pixel jitter cannot move the camera or cancel a tap`);
  assert.equal(env.frames.size, 0, 'An undecided contact does not request camera rendering');
  send(env.canvas, 'pointerup', {...sample, buttons: 0});
  assert.deepEqual(env.selectionHits, [{x: 330 + distance, y: 180, mask: 0xffffffff}],
    'Selection uses the completed position, including the shared 6 CSS-pixel boundary');
  assert.equal(env.selection.selectedNode.token, 'blue');
  assert.equal(env.card.target, env.selection.selectedNode);
  assert.deepEqual(env.selectionCalls, ['model', 'blue']);
  assert.deepEqual(env.endCalls, [], 'A missed Gizmo was not edited');
  assert.equal(env.canvas.capture, null, 'Completed taps release Orbit capture');
  await env.next(2000);
  const input = env.inputs.at(-1);
  assert.deepEqual([input.RotateX, input.RotateY, input.Dolly], [0, 0, 0]);
  assert.equal(env.frames.size, 0, 'The tap retains paused on-demand scheduling');
}, [0, 1, 3, 6]);

viewerTest('actual Orbit drags reject selection after cumulative, backtracking or coalesced threshold escapes', async (env, pattern) => {
  env.hit = false; env.candidates = ['blue'];
  send(env.canvas, 'pointerdown', contact);
  if (pattern === 'coalesced') {
    send(env.canvas, 'pointermove', {...contact, clientX: contact.clientX + 1,
      getCoalescedEvents: () => [{...contact, clientX: contact.clientX + 7},
        {...contact, clientX: contact.clientX + 1}]});
  } else {
    const distances = pattern === 'cumulative' ? [2, 4, 6, 7, 9] : [7, 0, 1];
    for (const distance of distances)
      send(env.canvas, 'pointermove', {...contact, clientX: contact.clientX + distance});
  }
  assert.ok(env.orbitDeltas.length > 0, `${pattern} starts real Orbit input after leaving tap tolerance`);
  const commands = env.orbitDeltas.length;
  send(env.canvas, 'pointermove', {...contact, clientX: contact.clientX + 12});
  assert.ok(env.orbitDeltas.length > commands, 'A recognized drag keeps emitting subsequent camera movement');
  send(env.canvas, 'pointerup', {...contact, clientX: contact.clientX + 12, buttons: 0});
  assert.equal(env.selectionHits.length, 0, `${pattern} cannot become a completed selection tap`);
  assert.deepEqual(env.selectionCalls, ['model']);
  assert.equal(env.canvas.capture, null);
  await env.next(2000);
  assert.ok(env.inputs.at(-1).RotateX < 0, 'The existing renderer receives actual camera rotation');
  assert.equal(env.frames.size, 0, 'Orbit does not create a second or continuous frame loop');
  send(env.canvas, 'pointerdown', contact);
  send(env.canvas, 'pointermove', contact);
  send(env.canvas, 'pointerup', {...contact, buttons: 0});
  assert.equal(env.selectionHits.length, 1, 'The next ordinary click starts a fresh gesture and can select');
  assert.equal(env.selection.selectedNode.token, 'blue');
  await env.next(2020);
}, ['cumulative', 'backtrack', 'coalesced']);

viewerTest('wheel/keys, Gizmo/pen ownership and canceled contacts cannot commit object selection', async (env, reason) => {
  env.candidates = ['blue']; env.hit = reason === 'gizmo';
  env.elements.get('pen-inspect').checked = reason === 'pen';
  const sample = {...contact, pointerType: reason === 'pen' ? 'pen' : 'mouse'};
  send(env.canvas, 'pointerdown', sample);
  if (reason === 'wheel') send(env.canvas, 'wheel', {deltaY: 5, deltaMode: 0});
  else if (reason === 'rotate-key') send(env.canvas, 'keydown', {key: 'ArrowLeft'});
  else if (reason === 'dolly-key') send(env.canvas, 'keydown', {key: '+'});
  else if (reason === 'cancel') send(env.canvas, 'pointercancel', {pointerId: sample.pointerId});
  else if (reason === 'escape') send(env.canvas, 'keydown', {key: 'Escape'});
  else if (reason === 'blur') send(window, 'blur');
  else if (reason === 'hide') env.hidden(true);
  else if (reason === 'unload') env.teardown();
  send(env.canvas, 'pointerup', {...sample, buttons: 0});
  assert.equal(env.selectionHits.length, 0, `${reason} does not propose selection`);
  assert.deepEqual(env.selectionCalls, ['model'], `${reason} retains the original explicit selection`);
  if (['wheel', 'rotate-key', 'dolly-key'].includes(reason))
    assert.equal(env.orbitDeltas.length, 1, 'Explicit wheel and key commands act immediately, without a drag threshold');
  else assert.deepEqual(env.orbitDeltas, [], `${reason} does not leak camera input`);
  if (reason === 'unload') {
    assert.equal(env.selection.isDisposed, true);
    assert.equal(env.canvas.capture, null);
    assert.equal(env.frames.size, 0);
    send(env.canvas, 'pointermove', {...contact, clientX: contact.clientX + 20});
    send(env.canvas, 'wheel', {deltaY: 5, deltaMode: 0});
    send(env.canvas, 'keydown', {key: 'ArrowLeft'});
    assert.deepEqual(env.orbitDeltas, [], 'Unloading detaches all Orbit listeners');
    assert.equal(env.frames.size, 0, 'Disposed input cannot restart rendering');
    let reloads = 0;
    location.reload = () => { reloads++; };
    send(window, 'pageshow', {persisted: false});
    assert.equal(reloads, 0, 'A normal pageshow does not reload');
    send(window, 'pageshow', {persisted: true});
    assert.equal(reloads, 1, 'History-cache restoration reloads after pagehide disposed the managed session');
    assert.equal(env.frames.size, 0, 'Restoration cannot resume the disposed frame scheduler');
  }
}, ['wheel', 'rotate-key', 'dolly-key', 'gizmo', 'pen', 'cancel', 'escape', 'blur', 'hide', 'unload']);

viewerTest('changing the bound selection cancels an active Gizmo before committing the new target', async env => {
  send(env.canvas, 'pointerdown', contact); await env.next(2000); await env.next(2020);
  const target = env.elements.get('overlay-target'); target.value = 'blue'; send(target, 'change');
  assert.deepEqual(env.endCalls, [true]); assert.equal(env.canvas.capture, null);
  assert.deepEqual(env.selectionCalls, ['model', 'blue']); assert.equal(env.card.target, env.selection.selectedNode);
  await env.next(2040);
  assert.equal(env.frames.size, 0, 'Canceling the old target restores paused on-demand scheduling');
  assert.equal(env.selectionHits.length, 0, 'A binding change does not pretend to be a geometric pointer request');
});
