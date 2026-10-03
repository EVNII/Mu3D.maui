import test from 'node:test';
import assert from 'node:assert/strict';
import {attach} from '../../samples/Mu3D.Gallery/Web/wwwroot/painting-picker.mjs';
import {createViewportEnvironment} from './support/viewport-fixture.mjs';
import {sendEvent, primaryPointer} from './support/pointer-fixture.mjs';

const flush = async () => { for (let i = 0; i < 12; i++) await Promise.resolve(); };
function open({hit = 1, wait = async () => {}} = {}) {
  const env = createViewportEnvironment(), calls = [];
  const picker = attach({querySelector: () => env.canvas}, {
    invokeMethod(method, ...args) { calls.push([method, ...args]); return hit; },
    async invokeMethodAsync(method, ...args) { calls.push([method, ...args]); await wait(); },
  });
  return {...env, picker, calls};
}
test('Painting drag uses CSS coordinates, captures one pointer and delivers the final point', async () => {
  const env = open();
  try {
    sendEvent(env.canvas, 'pointerdown', primaryPointer);
    assert.equal(env.canvas.capture, primaryPointer.pointerId);
    sendEvent(env.canvas, 'pointermove', {...primaryPointer, pointerId: 12, clientX: 25});
    sendEvent(env.canvas, 'pointermove', {...primaryPointer, clientX: 110});
    sendEvent(env.canvas, 'pointerup', {...primaryPointer, clientX: 140});
    await flush();
    assert.deepEqual(env.calls[0], ['BeginPick', .5, .5, 100, 60]);
    assert.deepEqual(env.calls.filter(c => c[0] === 'Pick').at(-1), ['Pick', 1, 1.2, .5, 100, 60]);
    assert.equal(env.canvas.capture, null);
  } finally { await env.picker.dispose(); }
});
test('Painting coalesces intermediate motion while preserving release during a managed callback', async () => {
  let release;
  const gate = new Promise(resolve => { release = resolve; });
  const env = open({wait: () => gate});
  try {
    sendEvent(env.canvas, 'pointerdown', primaryPointer);
    for (let x = 71; x <= 110; x++) sendEvent(env.canvas, 'pointermove', {...primaryPointer, clientX: x});
    sendEvent(env.canvas, 'pointerup', {...primaryPointer, clientX: 115});
    assert.equal(env.calls.filter(c => c[0] === 'Pick').length, 1);
    release(); await flush();
    const picks = env.calls.filter(c => c[0] === 'Pick');
    assert.equal(picks.length, 2); assert.equal(picks[1][2], .95);
  } finally { release(); await env.picker.dispose(); }
});
test('Exploratory swatches pick once; rejected contacts and secondary buttons do not capture', async () => {
  const env = open({hit: 3});
  try {
    sendEvent(env.canvas, 'pointerdown', {...primaryPointer, button: 2});
    sendEvent(env.canvas, 'pointerdown', {...primaryPointer, isPrimary: false});
    assert.equal(env.calls.length, 0);
    sendEvent(env.canvas, 'pointerdown', primaryPointer);
    sendEvent(env.canvas, 'pointermove', {...primaryPointer, clientX: 90});
    sendEvent(env.canvas, 'pointerup', primaryPointer); await flush();
    assert.equal(env.calls.filter(c => c[0] === 'Pick').length, 1);
    assert.equal(env.canvas.capture, null);
  } finally { await env.picker.dispose(); }
  const rejected = open({hit: 0});
  try {
    sendEvent(rejected.canvas, 'pointerdown', primaryPointer); await flush();
    assert.equal(rejected.calls.some(c => c[0] === 'Pick'), false);
  } finally { await rejected.picker.dispose(); }
});
test('Resize and disposal retire capture and pending updates before managed references can be released', async () => {
  let release; const gate = new Promise(resolve => { release = resolve; });
  const env = open({wait: () => gate});
  sendEvent(env.canvas, 'pointerdown', primaryPointer);
  sendEvent(env.canvas, 'pointermove', {...primaryPointer, clientX: 90});
  env.resize(); assert.equal(env.canvas.capture, null);
  let complete = false; const disposal = env.picker.dispose().then(() => { complete = true; });
  await flush(); assert.equal(complete, false);
  release(); await disposal;
  const count = env.calls.length;
  sendEvent(env.canvas, 'pointerdown', primaryPointer); sendEvent(env.document.defaultView, 'blur');
  assert.equal(env.calls.length, count);
  assert.equal(env.calls.filter(c => c[0] === 'Pick').length, 1);
});
