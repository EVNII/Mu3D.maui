import test from 'node:test';
import assert from 'node:assert/strict';
import {createSceneSelectionSource, attachSceneSelectionInput}
  from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-selection.mjs';
import {attachPointerInput} from '../../src/Mu3D.Web.Toolkit/wwwroot/pointer-input.mjs';
import {attachOrbitInput} from '../../src/Mu3D.Web.Toolkit/wwwroot/orbit-input.mjs';
import {createPointerEnvironment as environment, primaryPointer as pointer, sendEvent as send}
  from './support/pointer-fixture.mjs';

const a = Object.freeze({name: 'a'}), b = Object.freeze({name: 'b'});
const hit = distance => ({distance, worldPosition: [0, 1, 2], triangle: {index: 3}});
function fixture(options = {}) {
  const commits = [], queries = [];
  let candidates = [{node: a, hit: hit(1)}, {node: b, hit: hit(2)}];
  const source = createSceneSelectionSource({
    validateNode(value) { if (value !== a && value !== b) throw new Error('Foreign node.'); },
    hitTest(position, mask) { queries.push({position, mask}); return candidates; },
    commitSelection(value) { commits.push(value); }, ...options,
  });
  return {source, commits, queries, set candidates(value) { candidates = value; }};
}

test('selection defaults, initial commit and code binding changes are observable exactly once', () => {
  const f = fixture(), changes = [], options = [];
  assert.equal(f.source.selectedNode, null);
  assert.deepEqual(f.commits, [null]);
  assert.deepEqual(f.source.options, {isEnabled: true, clearSelectionOnMiss: true, selectionMask: 0xffffffff});
  assert.ok(Object.isFrozen(f.source.options));
  f.source.subscribeSelection(value => {
    assert.equal(f.commits.at(-1), value.newNode, 'Host commits before state notification.');
    assert.equal(f.source.selectedNode, value.newNode); assert.ok(Object.isFrozen(value)); changes.push(value);
  });
  f.source.subscribeOptions(value => options.push(value));
  f.source.selectedNode = a; f.source.selectedNode = a; f.source.selectedNode = b;
  assert.deepEqual(changes, [{oldNode: null, newNode: a}, {oldNode: a, newNode: b}]);
  f.source.selectionMask = 0; f.source.selectionMask = 0; f.source.isEnabled = false;
  assert.equal(f.source.selectedNode, b, 'Options preserve application selection.');
  assert.equal(options.length, 2); assert.equal(f.source.requestSelection({x: 3, y: 4}), b);
  assert.equal(f.queries.length, 0); f.source.dispose();
});

test('ordered shared hit evidence is immutable; applications may override or cancel the proposal', () => {
  const f = fixture(), changes = [], raw = hit(1);
  f.candidates = [{node: a, hit: raw}, {node: b, hit: hit(2)}];
  f.source.subscribeSelection(value => changes.push(value));
  let cancel = false, request;
  f.source.subscribeRequested(value => {
    request = value;
    assert.deepEqual(value.candidates.map(candidate => candidate.node), [a, b]);
    assert.equal(value.selectedNode, a); assert.ok(Object.isFrozen(value.candidates));
    assert.ok(Object.isFrozen(value.candidates[0])); assert.ok(Object.isFrozen(value.candidates[0].hit));
    assert.ok(Object.isFrozen(value.candidates[0].hit.worldPosition));
    assert.ok(Object.isFrozen(value.viewportPositionPixels));
    value.selectedNode = b; value.cancel = cancel;
  });
  assert.equal(f.source.requestSelection({x: 250, y: 75}), b);
  raw.worldPosition[0] = 999; assert.equal(request.candidates[0].hit.worldPosition[0], 0);
  assert.deepEqual(f.queries[0], {position: {x: 250, y: 75}, mask: 0xffffffff});
  cancel = true; f.source.selectedNode = a;
  const commits = f.commits.length;
  assert.equal(f.source.requestSelection({x: 250, y: 75}), a);
  assert.equal(f.commits.length, commits); assert.equal(changes.length, 2); f.source.dispose();
});

test('miss policy keeps or clears selection and always permits an application override', () => {
  const f = fixture({selectedNode: a}), requests = [];
  f.candidates = []; f.source.clearSelectionOnMiss = false;
  const stop = f.source.subscribeRequested(request => requests.push(request));
  f.source.requestSelection({x: 2, y: 4});
  assert.equal(f.source.selectedNode, a); assert.equal(requests[0].selectedNode, a);
  assert.equal(requests[0].candidates.length, 0);
  f.source.clearSelectionOnMiss = true; f.source.requestSelection({x: 2, y: 4});
  assert.equal(f.source.selectedNode, null); assert.equal(requests[1].selectedNode, null);
  stop(); f.source.subscribeRequested(request => { request.selectedNode = b; });
  f.source.requestSelection({x: 2, y: 4}); assert.equal(f.source.selectedNode, b); f.source.dispose();
});

test('invalid options, foreign handles, failed hits and failed host commit leave prior state intact', () => {
  let fail = false;
  const f = fixture({selectedNode: a, commitSelection() { if (fail) throw new Error('Host failed.'); }});
  let notifications = 0; f.source.subscribeSelection(() => notifications++);
  for (const value of [-1, 0x100000000, 1.5, NaN, 'all'])
    assert.throws(() => { f.source.selectionMask = value; }, /unsigned 32-bit/);
  assert.throws(() => { f.source.isEnabled = 1; }, /boolean/);
  assert.throws(() => { f.source.clearSelectionOnMiss = null; }, /boolean/);
  assert.throws(() => { f.source.selectedNode = {}; }, /Foreign/);
  assert.throws(() => { f.source.selectedNode = 'node'; }, /borrowed node/);
  assert.throws(() => f.source.requestSelection({x: Infinity, y: 0}), /finite/);
  f.candidates = [{node: b, hit: {distance: NaN}}];
  assert.throws(() => f.source.requestSelection({x: 1, y: 1}), /finite numbers/);
  f.candidates = [{node: b, hit: hit(1)}]; fail = true;
  assert.throws(() => { f.source.selectedNode = b; }, /Host failed/);
  assert.throws(() => f.source.requestSelection({x: 1, y: 1}), /Host failed/);
  assert.equal(f.source.selectedNode, a); assert.equal(notifications, 0); f.source.dispose();
});

test('nested requests and binding changes suppress stale proposals and stale selection notifications', () => {
  const f = fixture(), seen = [];
  let nested = false;
  f.source.subscribeRequested(request => {
    if (!nested) {
      nested = true; f.candidates = [{node: b, hit: hit(2)}];
      f.source.requestSelection({x: 9, y: 9});
    }
  });
  f.source.subscribeRequested(request => seen.push(request.viewportPositionPixels.x));
  f.source.requestSelection({x: 1, y: 1});
  assert.equal(f.source.selectedNode, b); assert.deepEqual(seen, [9]);
  f.source.subscribeSelection(change => { if (change.newNode === a) f.source.selectedNode = b; });
  const tail = []; f.source.subscribeSelection(change => tail.push(change.newNode));
  f.source.selectedNode = a;
  assert.deepEqual(tail, [b], 'Later observers never receive the superseded outer selection.');
  assert.equal(f.source.selectedNode, b); f.source.dispose();
});

test('reentrant hit/validation/options changes and disposal cannot commit or deliver stale state', () => {
  let source, interfere = false;
  source = createSceneSelectionSource({
    validateNode(value) { if (interfere && value === a) source.isEnabled = false; },
    hitTest() { if (interfere) source.isEnabled = false; return [{node: a, hit: hit(1)}]; },
  });
  interfere = true; source.requestSelection({x: 1, y: 1}); assert.equal(source.selectedNode, null);
  source.isEnabled = true; source.selectedNode = a; assert.equal(source.selectedNode, null);
  interfere = false;
  const seen = [];
  source.subscribeOptions(value => { if (value.selectionMask === 1) source.selectionMask = 2; });
  source.subscribeOptions(value => seen.push(value.selectionMask));
  source.selectionMask = 1; assert.deepEqual(seen, [2]);
  source.subscribeRequested(() => source.dispose());
  source.isEnabled = true; source.requestSelection({x: 1, y: 1});
  assert.ok(source.isDisposed); assert.equal(source.selectedNode, null);
  assert.throws(() => { source.selectedNode = a; }, /disposed/);
  assert.throws(() => source.subscribeSelection(() => {}), /disposed/); source.dispose();
});

test('option updates during a selection notification do not lose the still-current selection change', () => {
  const f = fixture(), selectionTail = [];
  f.source.subscribeSelection(() => { f.source.selectionMask = 1; });
  f.source.subscribeSelection(change => selectionTail.push(change.newNode));
  f.source.selectedNode = a;
  assert.deepEqual(selectionTail, [a], 'A changed option preserves the still-current selection notification.');
  f.source.selectedNode = null;
  assert.deepEqual(selectionTail, [a, null], 'An unchanged option assignment also preserves it.');
  f.source.dispose();
  const next = fixture(), optionTail = [];
  next.source.subscribeOptions(() => { next.source.selectedNode = b; });
  next.source.subscribeOptions(value => optionTail.push(value.selectionMask));
  next.source.selectionMask = 1;
  assert.deepEqual(optionTail, [1], 'A selection change does not stale the current option value.');
  next.source.dispose();
});

test('synchronous commit rejects nested mutations and failure rolls back the source', () => {
  let source, nested = false;
  source = createSceneSelectionSource({hitTest: () => [], commitSelection() {
    if (nested) source.selectedNode = a;
  }});
  nested = true;
  assert.throws(() => { source.selectedNode = b; }, /during its synchronous commit/);
  assert.equal(source.selectedNode, null);
  nested = false; source.selectedNode = b; assert.equal(source.selectedNode, b); source.dispose();
});

function tap(canvas, properties = {}) {
  send(canvas, 'pointerdown', {...pointer, ...properties});
  return send(canvas, 'pointerup', {...pointer, buttons: 0, ...properties});
}

test('selection input observes completed taps in physical Canvas pixels without consuming/capturing', () => {
  const e = environment(), f = fixture(), adapter = attachSceneSelectionInput(e.canvas, f.source);
  const down = send(e.canvas, 'pointerdown', pointer);
  assert.ok(adapter.isActive); assert.equal(f.queries.length, 0); assert.equal(down.defaultPrevented, false);
  assert.equal(e.canvas.capture, null); assert.equal(e.canvas.focuses, 0);
  const up = send(e.canvas, 'pointerup', {...pointer, buttons: 0});
  assert.equal(up.defaultPrevented, false); assert.deepEqual(f.queries[0].position, {x: 150, y: 90});
  assert.equal(f.source.selectedNode, a); assert.equal(adapter.isActive, false);
  e.canvas.width = 200; e.canvas.height = 120; tap(e.canvas);
  assert.deepEqual(f.queries[1].position, {x: 100, y: 60}, 'Canvas physical extent, rather than assumed DPR, wins.');
  adapter.dispose(); tap(e.canvas); assert.equal(f.queries.length, 2); assert.equal(f.source.isDisposed, false);
  f.source.selectedNode = b; assert.equal(f.source.selectedNode, b); f.source.dispose();
});

test('actual pointer-Gizmo and Orbit composition preserves captures, taps and recognized camera precedence', () => {
  const e = environment(), f = fixture(), samples = [], commands = [];
  let claim = false;
  const raw = attachPointerInput(e.canvas, {acceptContact: () => claim, onSamples: values => samples.push(...values)});
  const selection = attachSceneSelectionInput(e.canvas, f.source);
  const orbit = attachOrbitInput(e.canvas, command => { selection.cancel(); commands.push(command); },
    {isEnabled: () => !raw.isActive});
  tap(e.canvas); assert.equal(f.queries.length, 1, 'Orbit down capture alone does not suppress a completed tap.');
  assert.equal(e.canvas.capture, null);
  send(e.canvas, 'pointerdown', pointer);
  send(e.canvas, 'pointermove', {...pointer, clientX: 71});
  send(e.canvas, 'pointerup', {...pointer, buttons: 0, clientX: 71});
  assert.equal(commands.length, 1); assert.equal(f.queries.length, 1, 'Even a 1 CSS-pixel recognized camera command cancels.');
  claim = true; tap(e.canvas);
  assert.equal(f.queries.length, 1); assert.ok(samples.some(value => value.captured));
  assert.equal(e.canvas.capture, null); orbit.dispose(); selection.dispose(); raw.dispose(); f.source.dispose();
});

test('movement excursions, coalesced samples, bounds and overlapping/non-primary/right contacts do not select', () => {
  const e = environment(), f = fixture(), adapter = attachSceneSelectionInput(e.canvas, f.source);
  send(e.canvas, 'pointerdown', pointer);
  send(e.canvas, 'pointermove', {...pointer, clientX: 77});
  send(e.canvas, 'pointerup', {...pointer, buttons: 0});
  send(e.canvas, 'pointerdown', pointer);
  send(e.canvas, 'pointermove', {...pointer, getCoalescedEvents: () => [{...pointer, clientX: 77}, pointer]});
  send(e.canvas, 'pointerup', {...pointer, buttons: 0});
  send(e.canvas, 'pointerdown', pointer);
  send(e.canvas, 'pointerdown', {...pointer, pointerId: 8, isPrimary: false});
  send(e.canvas, 'pointerup', {...pointer, pointerId: 8, isPrimary: false});
  send(e.canvas, 'pointerup', {...pointer, buttons: 0});
  tap(e.canvas, {isPrimary: false}); tap(e.canvas, {button: 2});
  tap(e.canvas, {clientX: 19});
  send(e.canvas, 'pointerdown', pointer);
  send(e.canvas, 'pointerup', {...pointer, clientX: 121, buttons: 0});
  send(e.canvas, 'pointerdown', pointer); send(e.canvas, 'pointerleave', pointer);
  send(e.window, 'pointerup', {...pointer, clientX: 300, buttons: 0});
  assert.equal(f.queries.length, 0);
  tap(e.canvas); assert.equal(f.queries.length, 1, 'An outside end does not leave stale contacts.');
  adapter.dispose(); f.source.dispose();
});

test('cancel, Escape, blur, hidden page, lost capture, disable and teardown suppress pending taps', () => {
  for (const reason of ['cancel', 'Escape', 'blur', 'hide', 'capture', 'disabled', 'external-disabled', 'pagehide', 'dispose']) {
    const e = environment(), f = fixture(); let inputEnabled = true;
    const adapter = attachSceneSelectionInput(e.canvas, f.source, {isInputEnabled: () => inputEnabled});
    send(e.canvas, 'pointerdown', pointer);
    if (reason === 'cancel') send(e.canvas, 'pointercancel', pointer);
    else if (reason === 'Escape') send(e.canvas, 'keydown', {key: 'Escape'});
    else if (reason === 'blur' || reason === 'pagehide') send(e.window, reason);
    else if (reason === 'hide') { e.document.visibilityState = 'hidden'; send(e.document, 'visibilitychange'); }
    else if (reason === 'capture') send(e.canvas, 'lostpointercapture', pointer);
    else if (reason === 'disabled') f.source.isEnabled = false;
    else if (reason === 'external-disabled') inputEnabled = false;
    else adapter.dispose();
    send(e.canvas, 'pointerup', {...pointer, buttons: 0});
    assert.equal(f.queries.length, 0, reason); adapter.dispose(); f.source.dispose();
  }
});

test('source replacement and code/option updates cancel contacts while leaving borrowed sources alive', () => {
  const e = environment(), old = fixture(), next = fixture(), adapter = attachSceneSelectionInput(e.canvas, old.source);
  send(e.canvas, 'pointerdown', pointer); adapter.source = next.source;
  send(e.canvas, 'pointerup', {...pointer, buttons: 0});
  assert.equal(old.queries.length + next.queries.length, 0); assert.equal(old.source.isDisposed, false);
  old.source.isEnabled = false; tap(e.canvas); assert.equal(next.queries.length, 1, 'Old source is detached.');
  send(e.canvas, 'pointerdown', pointer); next.source.selectedNode = b;
  send(e.canvas, 'pointerup', {...pointer, buttons: 0}); assert.equal(next.queries.length, 1);
  send(e.canvas, 'pointerdown', pointer); next.source.selectionMask = 1;
  send(e.canvas, 'pointerup', {...pointer, buttons: 0}); assert.equal(next.queries.length, 1);
  next.source.dispose(); tap(e.canvas); assert.equal(next.queries.length, 1);
  assert.throws(() => { adapter.source = {}; }, /live createSceneSelectionSource/);
  adapter.dispose(); adapter.dispose(); old.source.dispose();
});

test('input failures report once and unrelated HTML or prevented input never triggers selection', () => {
  const e = environment(), errors = [], source = createSceneSelectionSource({hitTest() { throw new Error('Raycast failed.'); }});
  const adapter = attachSceneSelectionInput(e.canvas, source, {onError: error => errors.push(error)});
  const htmlButton = new EventTarget(); tap(htmlButton); assert.equal(errors.length, 0);
  const prevented = Object.assign(new Event('pointerdown', {cancelable: true}), pointer);
  prevented.preventDefault(); e.canvas.dispatchEvent(prevented);
  send(e.canvas, 'pointerup', {...pointer, buttons: 0}); assert.equal(errors.length, 0);
  tap(e.canvas); assert.equal(errors.length, 1); assert.match(errors[0].message, /Raycast failed/);
  assert.equal(adapter.isActive, false); adapter.dispose(); source.dispose();
  assert.throws(() => attachSceneSelectionInput(e.canvas, fixture().source, {tapTolerance: -1}), /non-negative/);
});
