import test from 'node:test';
import assert from 'node:assert/strict';
import {attachOrbitInput} from '../../src/Mu3D.Web.Toolkit/wwwroot/orbit-input.mjs';
import {attachSceneSelectionInput, createSceneSelectionSource}
  from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-selection.mjs';
import {createPointerEnvironment, primaryPointer as pointer, sendEvent as send}
  from './support/pointer-fixture.mjs';

const environment = () => createPointerEnvironment({clientHeight: 60});
const move = (canvas, x, y = 70, properties = {}) =>
  send(canvas, 'pointermove', {...pointer, clientX: x, clientY: y, ...properties});
const xRotation = pixels => -pixels / 100 * Math.PI * 2;

test('Orbit validates its opt-in CSS threshold before acquiring host state', () => {
  for (const dragThreshold of [-1, NaN, Infinity, '6', null]) {
    const e = environment();
    assert.throws(() => attachOrbitInput(e.canvas, () => {}, {dragThreshold}), /finite non-negative CSS-pixel/);
    assert.equal(e.canvas.style.touchAction, 'pan-y'); assert.equal(e.canvas.capture, null);
  }
});

test('shared tap/drag tolerance accepts stationary, jitter and inclusive radial boundary positions', () => {
  for (const [x, y] of [[70, 70], [71, 70], [73, 70], [76, 70], [70, 76], [73, 74]]) {
    const e = environment(), queries = [], commands = [];
    const source = createSceneSelectionSource({hitTest: position => { queries.push(position); return []; }});
    const selection = attachSceneSelectionInput(e.canvas, source, {tapTolerance: 6});
    const orbit = attachOrbitInput(e.canvas, command => { commands.push(command); selection.cancel(); }, {dragThreshold: 6});
    send(e.canvas, 'pointerdown', pointer); move(e.canvas, x, y);
    assert.equal(e.canvas.capture, 7, 'Orbit keeps its usual pointer capture while deciding drag intent.');
    assert.equal(commands.length, 0); assert.equal(selection.isActive, true);
    send(e.canvas, 'pointerup', {...pointer, clientX: x, clientY: y, buttons: 0});
    assert.equal(queries.length, 1, `${x - 70}/${y - 70} CSS-pixel displacement remains a tap.`);
    assert.ok(Math.abs(queries[0].x - (x - 20) * 3) < 1e-10);
    assert.ok(Math.abs(queries[0].y - (y - 40) * 3) < 1e-10);
    assert.equal(e.canvas.capture, null); orbit.dispose(); selection.dispose(); source.dispose();
  }
});

test('cumulative excursion activates full displacement once and preserves subsequent small deltas/backtracking', () => {
  const e = environment(), commands = [];
  const orbit = attachOrbitInput(e.canvas, command => commands.push(command), {dragThreshold: 6});
  send(e.canvas, 'pointerdown', pointer);
  move(e.canvas, 72); move(e.canvas, 74); move(e.canvas, 76);
  assert.equal(commands.length, 0, 'Accumulated event count is not a distance threshold.');
  move(e.canvas, 78); move(e.canvas, 79); move(e.canvas, 72); move(e.canvas, 72);
  assert.deepEqual(commands.map(command => command.rotateX), [xRotation(8), xRotation(1), xRotation(-7)]);
  assert.ok(commands.every(command => command.rotateY === 0 && command.dolly === 0));
  send(e.canvas, 'pointerup', {...pointer, clientX: 72, buttons: 0});
  send(e.canvas, 'pointerdown', pointer); move(e.canvas, 75);
  assert.equal(commands.length, 3, 'The next contact gets a new independent threshold.');
  orbit.dispose();
});

test('actual coalesced excursions cross the threshold even when the parent returns to tap range', () => {
  const e = environment(), commands = [], queries = [];
  const source = createSceneSelectionSource({hitTest: point => { queries.push(point); return []; }});
  const selection = attachSceneSelectionInput(e.canvas, source, {tapTolerance: 6});
  const orbit = attachOrbitInput(e.canvas, command => { commands.push(command); selection.cancel(); }, {dragThreshold: 6});
  send(e.canvas, 'pointerdown', pointer);
  move(e.canvas, 71, 70, {getCoalescedEvents: () => [
    {...pointer, clientX: 74}, {...pointer, clientX: 78}, {...pointer, clientX: 72},
  ]});
  assert.deepEqual(commands.map(command => command.rotateX), [xRotation(8), xRotation(-6), xRotation(-1)],
    'Process actual ordered samples and the distinct final parent position without swallowing displacement.');
  send(e.canvas, 'pointerup', {...pointer, clientX: 71, buttons: 0});
  assert.equal(queries.length, 0, 'Returning inside the tap radius cannot restore a recognized drag.');
  send(e.canvas, 'pointerdown', pointer);
  move(e.canvas, 78, 70, {getCoalescedEvents: () => [{...pointer, clientX: 78}]});
  assert.equal(commands.length, 4, 'A matching parent position does not duplicate the coalesced command.');
  assert.equal(commands[3].rotateX, xRotation(8));
  orbit.dispose(); selection.dispose(); source.dispose();
});

test('default Orbit remains immediate while stationary pointer/wheel reports emit no camera command', () => {
  const e = environment(), commands = [], orbit = attachOrbitInput(e.canvas, command => commands.push(command));
  send(e.canvas, 'pointerdown', pointer); move(e.canvas, 70); move(e.canvas, 71); move(e.canvas, 71);
  assert.equal(commands.length, 1); assert.equal(commands[0].rotateX, xRotation(1));
  send(e.canvas, 'wheel', {deltaY: 0, deltaMode: 0});
  assert.equal(commands.length, 1, 'A zero wheel report is not a camera command.');
  orbit.dispose();
});

test('wheel and keyboard camera commands retain immediate precedence below the drag threshold', () => {
  const e = environment(), commands = [], orbit = attachOrbitInput(e.canvas, command => commands.push(command), {dragThreshold: 6});
  send(e.canvas, 'pointerdown', pointer); move(e.canvas, 73);
  assert.equal(send(e.canvas, 'wheel', {deltaY: 100, deltaMode: 0}).defaultPrevented, true);
  assert.equal(commands[0].dolly, -0.1);
  send(e.canvas, 'keydown', {key: 'ArrowRight'}); assert.equal(commands[1].rotateX, 0.1);
  assert.equal(send(e.canvas, 'wheel', {deltaY: 100, ctrlKey: true}).defaultPrevented, false);
  assert.equal(send(e.canvas, 'keydown', {key: 'ArrowRight', metaKey: true}).defaultPrevented, false);
  assert.equal(commands.length, 2); orbit.dispose();
});

test('pending and activated drags release capture through cancel, disabled input and host lifecycle', () => {
  for (const activated of [false, true]) for (const reason of ['cancel', 'disabled', 'blur', 'hidden', 'capture', 'pointercancel', 'dispose']) {
    const e = environment(), commands = []; let enabled = true;
    const orbit = attachOrbitInput(e.canvas, command => commands.push(command), {dragThreshold: 6, isEnabled: () => enabled});
    send(e.canvas, 'pointerdown', pointer); move(e.canvas, activated ? 78 : 73);
    const before = commands.length;
    if (reason === 'cancel') orbit.cancel();
    else if (reason === 'disabled') { enabled = false; move(e.canvas, 79); }
    else if (reason === 'blur') send(e.window, 'blur');
    else if (reason === 'hidden') { e.document.visibilityState = 'hidden'; send(e.document, 'visibilitychange'); }
    else if (reason === 'capture') { e.canvas.capture = null; send(e.canvas, 'lostpointercapture', {pointerId: 7}); }
    else if (reason === 'pointercancel') send(e.canvas, 'pointercancel', {pointerId: 7});
    else orbit.dispose();
    assert.equal(e.canvas.capture, null, `${reason}/${activated} releases owned capture.`);
    enabled = true; move(e.canvas, 90); assert.equal(commands.length, before, `${reason}/${activated} cancels later movement.`);
    orbit.dispose(); orbit.dispose(); assert.equal(e.canvas.style.touchAction, 'pan-y');
  }
});
