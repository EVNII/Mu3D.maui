import test from 'node:test';
import assert from 'node:assert/strict';
import {createSceneNodeAnchorSource, createSceneNodeOverlay}
  from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-node-overlay.mjs';
import {TestNode as Node, createOverlayDom as environment, anchorPoint as point, sendTargetedEvent as send}
  from './support/dom-fixture.mjs';

// Owned-DOM/host-stream fixture; physical layout, hit testing and WASM bridge execution remain
// separate browser checks. This adapter receives no renderer, frame clock, RAF or timer callback.
function host(sourceId = 'scene-a') {
  const registrations = new Map(), configures = [], removes = [], subscribers = new Set(), oldCallbacks = [];
  let revision = 0, sequence = 0, frameId = 0, subscriptions = 0, disconnections = 0, failConfigure = false;
  const source = createSceneNodeAnchorSource({sourceId,
    configureAnchor(value) {
      if (failConfigure) throw new Error('Injected registration failure.');
      configures.push(value); const id = value.id ?? `host-anchor-${++sequence}`;
      registrations.set(id, value); return {id, revision: ++revision, sourceId};
    },
    removeAnchor(id) { removes.push(id); registrations.delete(id); return ++revision; },
    subscribeFrames(listener) {
      subscriptions++; subscribers.add(listener); oldCallbacks.push(listener);
      return () => { disconnections++; subscribers.delete(listener); };
    }});
  return {source, registrations, configures, removes, oldCallbacks,
    get revision() { return revision; }, get subscriptions() { return subscriptions; },
    get disconnections() { return disconnections; },
    set failConfigure(value) { failConfigure = value; },
    emit(value) { for (const listener of [...subscribers]) listener(value); },
    frame(extra = {}) {
      const value = {SourceId: sourceId, Revision: revision, FrameId: ++frameId, Width: 500, Height: 300,
        Points: [...registrations.keys()].map(Id => point(Id)), ...extra};
      this.emit(value); return value;
    },
  };
}
test('normal node binding hides before a submitted frame and preserves native defaults without IDs/batches', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const target = h.source.node('node-a'), binding = overlay.bind(e.button, {target});
  assert.equal(target, h.source.node('node-a')); assert.ok(Object.isFrozen(target)); assert.deepEqual(Object.keys(target), []);
  assert.deepEqual(binding.options, {target, localPosition: [0, 0, 0], offset: [8, -28], pivot: [0, 0],
    interactive: true, hideWhenOutsideViewport: true});
  assert.deepEqual(h.configures[0], {id: null, target: 'node-a', localPosition: [0, 0, 0]});
  const wrapper = e.button.parentNode; assert.ok(wrapper.hidden); h.frame();
  assert.equal(wrapper.hidden, false); assert.equal(wrapper.inert, false);
  assert.equal(wrapper.style.left, 'calc(50% + 8px)'); assert.equal(wrapper.style.top, 'calc(25% + -28px)');
  assert.equal(wrapper.style.transform, 'translate(0%, 0%)'); assert.equal(h.subscriptions, 1);
  overlay.dispose(); assert.deepEqual(e.origin.children, [e.label, e.button]);
  assert.equal(h.registrations.size, 0); assert.equal(h.disconnections, 1);
  assert.equal(h.source.node('node-a'), target, 'Borrowed source and nodes survive overlay disposal'); h.source.dispose();
});

test('target/local-point mutations hide the former position until matching source revision and notify once', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.label, {target: h.source.node('a')}), changes = [];
  binding.subscribeOptions(value => changes.push(value)); const old = h.frame(), wrapper = e.label.parentNode;
  binding.target = h.source.node('b'); assert.equal(changes.length, 1); assert.ok(wrapper.hidden);
  assert.equal(h.configures.at(-1).id, h.configures[0].id ?? old.Points[0].Id);
  h.emit({...old, FrameId: 99}); assert.ok(wrapper.hidden, 'Old projection revision cannot show a retargeted element');
  h.frame({FrameId: 100, Points: [point(old.Points[0].Id, 100, 30)]});
  assert.equal(wrapper.style.left, 'calc(20% + 8px)'); assert.equal(wrapper.hidden, false);
  const localPosition = [1, 2, 3]; binding.localPosition = localPosition; localPosition[0] = 99;
  assert.ok(wrapper.hidden); assert.deepEqual(binding.localPosition, [1, 2, 3]);
  assert.ok(Object.isFrozen(binding.options)); assert.ok(Object.isFrozen(binding.localPosition));
  assert.deepEqual(h.configures.at(-1).localPosition, [1, 2, 3]); assert.equal(changes.length, 2);
  binding.localPosition = [1, 2, 3]; binding.target = h.source.node('b'); assert.equal(changes.length, 2);
  binding.target = null; assert.equal(h.registrations.size, 0); assert.ok(wrapper.hidden);
  assert.equal(changes.length, 3); assert.equal(binding.target, null); overlay.dispose(); h.source.dispose();
});

test('layout options refresh the current CSS point without bridge calls, and release native interaction', () => {
  const h = host(), e = environment(), interactions = [];
  const overlay = createSceneNodeOverlay({container: e.container, source: h.source,
    onInteraction: value => interactions.push(value)});
  const binding = overlay.bind(e.button, {target: h.source.node('a')}); h.frame();
  const wrapper = e.button.parentNode, layer = e.container.children[0], configured = h.configures.length;
  send(layer, 'pointerdown', e.button, {pointerId: 8}); assert.equal(overlay.hasActivePointer, true);
  e.document.activeElement = e.button; send(layer, 'focusin', e.button);
  binding.interactive = false; assert.equal(overlay.isInteracting, false); assert.equal(e.document.activeElement, null);
  assert.ok(wrapper.inert); assert.equal(wrapper.style.pointerEvents, 'none');
  binding.offset = [2, -4]; binding.pivot = [0.5, 1];
  assert.equal(wrapper.style.left, 'calc(50% + 2px)'); assert.equal(wrapper.style.top, 'calc(25% + -4px)');
  assert.equal(wrapper.style.transform, 'translate(-50%, -100%)'); assert.equal(h.configures.length, configured);
  assert.deepEqual(interactions, [true, false]); overlay.dispose(); h.source.dispose();
});

test('null source/targets and projection visibility stay explicit; optional clipping and enable are mutable', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container});
  const binding = overlay.bind(e.label), wrapper = e.label.parentNode;
  assert.ok(wrapper.hidden); assert.equal(h.configures.length, 0);
  overlay.source = h.source; binding.target = h.source.node('a'); const id = [...h.registrations.keys()][0];
  h.frame({Points: [point(id, 550, 75, {InsideViewport: false})]}); assert.ok(wrapper.hidden);
  binding.hideWhenOutsideViewport = false; assert.equal(wrapper.hidden, false);
  h.frame({Points: [point(id, 0, 0, {Projected: false})]}); assert.ok(wrapper.hidden);
  h.frame(); overlay.isEnabled = false; assert.ok(wrapper.hidden); assert.equal(h.registrations.size, 1);
  overlay.isEnabled = true; assert.equal(wrapper.hidden, false);
  h.frame({Points: []}); assert.ok(wrapper.hidden); h.frame(); overlay.source = null;
  assert.ok(wrapper.hidden); assert.equal(binding.target, null); assert.equal(h.registrations.size, 0);
  overlay.dispose(); h.source.dispose();
});

test('two overlays share exactly one upstream frame observer and unregister only their own anchors', () => {
  const h = host(), a = environment(), b = environment();
  const first = createSceneNodeOverlay({container: a.container, source: h.source});
  const second = createSceneNodeOverlay({container: b.container, source: h.source});
  first.bind(a.label, {target: h.source.node('a')}); second.bind(b.label, {target: h.source.node('b')});
  assert.equal(h.subscriptions, 1); assert.equal(h.registrations.size, 2); h.frame();
  assert.equal(a.label.parentNode.hidden, false); assert.equal(b.label.parentNode.hidden, false);
  first.dispose(); assert.equal(h.registrations.size, 1); assert.equal(h.disconnections, 0);
  assert.ok(b.label.parentNode.hidden, 'Removing any registration invalidates the complete old batch');
  h.frame(); assert.equal(b.label.parentNode.hidden, false); second.dispose();
  assert.equal(h.disconnections, 1); assert.equal(h.registrations.size, 0); h.source.dispose();
});

test('source replacement unsubscribes and clears opaque targets once, retaining other binding options', () => {
  const old = host('old'), next = host('next'), e = environment();
  const overlay = createSceneNodeOverlay({container: e.container, source: old.source});
  const binding = overlay.bind(e.button, {target: old.source.node('a'), offset: [1, 2]}), changes = [], options = [];
  binding.subscribeOptions(value => changes.push(value)); overlay.subscribeOptions(value => options.push(value)); old.frame();
  overlay.source = next.source; assert.equal(binding.target, null); assert.deepEqual(binding.offset, [1, 2]);
  assert.equal(changes.length, 1); assert.equal(options.length, 1); assert.equal(options[0].source, next.source);
  assert.ok(e.button.parentNode.hidden); assert.equal(old.registrations.size, 0); assert.equal(old.disconnections, 1);
  assert.throws(() => { binding.target = old.source.node('b'); });
  old.oldCallbacks[0]({SourceId: 'old', Revision: old.revision, FrameId: 100, Width: 500, Height: 300, Points: []});
  assert.ok(e.button.parentNode.hidden); binding.target = next.source.node('b'); next.frame();
  assert.equal(e.button.parentNode.hidden, false); overlay.source = next.source; assert.equal(options.length, 1);
  overlay.dispose(); old.source.dispose(); next.source.dispose();
});

test('detach restores borrowed DOM/releases registrations and reattach retains writable targets/options', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.button, {target: h.source.node('a')}), target = binding.target;
  e.button.style.color = 'red'; let clicks = 0; e.button.addEventListener('click', () => clicks++); h.frame();
  const oldCallback = h.oldCallbacks[0]; overlay.detach(); overlay.detach();
  assert.equal(overlay.isAttached, false); assert.deepEqual(e.origin.children, [e.label, e.button]);
  assert.equal(binding.target, target); assert.equal(h.registrations.size, 0); assert.equal(h.disconnections, 1);
  binding.target = h.source.node('b'); binding.offset = [3, 4]; const count = h.configures.length;
  assert.equal(h.configures.length, count); overlay.attach(); overlay.attach();
  assert.equal(overlay.isAttached, true); assert.equal(h.subscriptions, 2); assert.ok(e.button.parentNode.hidden);
  oldCallback({SourceId: 'scene-a', Revision: h.revision, FrameId: 999, Width: 500, Height: 300,
    Points: [...h.registrations.keys()].map(id => point(id))});
  assert.ok(e.button.parentNode.hidden, 'Prior attachment callbacks cannot replace current placement');
  h.frame(); assert.equal(e.button.parentNode.style.left, 'calc(50% + 3px)');
  assert.equal(e.button.style.color, 'red'); e.button.dispatchEvent(new Event('click')); assert.equal(clicks, 1);
  overlay.dispose(); assert.deepEqual(e.origin.children, [e.label, e.button]); h.source.dispose();
});

test('matching batches are frozen atomically and reject old/foreign/malformed frames without corrupting placement', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  overlay.bind(e.label, {target: h.source.node('a')}); const value = h.frame(), wrapper = e.label.parentNode;
  assert.ok(Object.isFrozen(h.source.latestFrame)); assert.ok(Object.isFrozen(h.source.latestFrame.Points));
  value.Points[0].X = 0; assert.equal(h.source.latestFrame.Points[0].X, 250);
  h.emit({...value, FrameId: 0}); assert.equal(wrapper.style.left, 'calc(50% + 8px)');
  h.emit({...value, SourceId: 'foreign', FrameId: 99}); h.emit({...value, Revision: h.revision - 1, FrameId: 99});
  assert.equal(wrapper.style.left, 'calc(50% + 8px)');
  assert.throws(() => h.emit({...value, FrameId: 100, Points: [point(value.Points[0].Id, NaN)]}));
  assert.throws(() => h.emit({...value, FrameId: 100, Points: [point('duplicate'), point('duplicate')]}));
  assert.equal(wrapper.dataset.frameId, '1'); assert.equal(h.source.latestFrame.FrameId, 1);
  overlay.dispose(); h.source.dispose();
});

test('host null/explicit clears hide every borrower; old sequence cannot resurrect a cleared frame', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  overlay.bind(e.button, {target: h.source.node('a')}); const value = h.frame(); h.emit(null);
  assert.equal(h.source.latestFrame, null); assert.ok(e.button.parentNode.hidden); h.emit(value);
  assert.ok(e.button.parentNode.hidden); h.frame(); assert.equal(e.button.parentNode.hidden, false);
  h.source.clear(); assert.ok(e.button.parentNode.hidden); assert.equal(h.registrations.size, 1);
  h.frame(); overlay.clear(); assert.ok(e.button.parentNode.hidden); h.frame();
  assert.equal(e.button.parentNode.hidden, false); overlay.dispose(); h.source.dispose();
});

test('foreign handles/options and failed bridge/DOM constructors preserve existing registrations and controls', () => {
  const h = host(), foreign = host('foreign'), e = environment();
  const overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  for (const options of [{target: foreign.source.node('a')}, {localPosition: [1e300, 0, 0]},
    {offset: [NaN, 0]}, {pivot: [0]}, {interactive: 'false'}, {hideWhenOutsideViewport: 0},
    {localPosition: null}, {offset: null}, {interactive: null}, null])
    assert.throws(() => overlay.bind(e.button, options));
  assert.equal(h.configures.length, 0); assert.deepEqual(e.origin.children, [e.label, e.button]);
  assert.throws(() => overlay.bind(e.container, {target: h.source.node('a')}));
  assert.equal(h.configures.length, 0); h.failConfigure = true;
  assert.throws(() => overlay.bind(e.button, {target: h.source.node('a')}));
  assert.deepEqual(e.origin.children, [e.label, e.button]); assert.equal(h.registrations.size, 0);
  h.failConfigure = false; const binding = overlay.bind(e.button, {target: h.source.node('a')}); h.frame();
  const original = binding.options, configured = h.configures.length;
  assert.throws(() => { binding.localPosition = [1e300, 0, 0]; });
  assert.throws(() => { binding.target = foreign.source.node('a'); });
  assert.equal(binding.options, original); assert.equal(h.configures.length, configured);
  assert.equal(e.button.parentNode.hidden, false); assert.throws(() => overlay.bind(e.button));
  overlay.dispose(); h.source.dispose(); foreign.source.dispose();
});

test('reentrant revision changes invalidate a delivered batch before later source observers see it', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.label, {target: h.source.node('a')}), later = [];
  let retarget = true;
  const stopFirst = h.source.subscribe(value => { if (value && retarget) { retarget = false; binding.target = h.source.node('b'); } });
  const stopLater = h.source.subscribe(value => later.push(value)); h.frame();
  assert.deepEqual(later, [null]); assert.ok(e.label.parentNode.hidden);
  h.frame(); assert.equal(later.at(-1).Revision, h.revision); assert.equal(e.label.parentNode.hidden, false);
  stopFirst(); stopLater(); overlay.dispose(); h.source.dispose();
});

test('source and binding disposal are idempotent and preserve externally moved application content', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.button, {target: h.source.node('a')}); h.frame();
  const moved = new Node(e.document); moved.appendChild(e.button);
  h.source.dispose(); h.source.dispose(); assert.equal(h.registrations.size, 0); assert.equal(h.disconnections, 1);
  assert.throws(() => h.source.node('b')); assert.throws(() => h.source.subscribe(() => {}));
  assert.throws(() => h.source.clear()); binding.dispose(); binding.dispose();
  overlay.dispose(); overlay.dispose(); assert.equal(e.button.parentNode, moved);
  assert.throws(() => { binding.target = null; }); assert.throws(() => overlay.bind(e.label));
  assert.equal(e.container.children.length, 0);
});

test('FP32-equivalent local points are normalized before equality, bridge mutation and notifications', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.label, {target: h.source.node('a'), localPosition: [0, 0.8, 0]}), changes = [];
  binding.subscribeOptions(value => changes.push(value));
  assert.deepEqual(binding.localPosition, [0, Math.fround(0.8), 0]);
  assert.deepEqual(h.configures[0].localPosition, binding.localPosition); h.frame();
  const configured = h.configures.length, revision = h.revision;
  binding.localPosition = [0, Math.fround(0.8), 0];
  assert.equal(h.configures.length, configured); assert.equal(h.revision, revision); assert.equal(changes.length, 0);
  assert.equal(e.label.parentNode.hidden, false); overlay.dispose(); h.source.dispose();
});

test('source disposal observers can recursively dispose the source/overlay without duplicate host removals', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  overlay.bind(e.label, {target: h.source.node('a')}); overlay.bind(e.button, {target: h.source.node('b')}); h.frame();
  let calls = 0;
  h.source.subscribe(value => {
    if (value === null) { calls++; h.source.dispose(); overlay.dispose(); }
  });
  h.source.dispose(); assert.equal(calls, 1); assert.equal(h.removes.length, 2);
  assert.equal(new Set(h.removes).size, 2); assert.equal(h.registrations.size, 0); assert.equal(h.disconnections, 1);
  assert.deepEqual(e.origin.children, [e.label, e.button]); assert.equal(e.container.children.length, 0);
});

test('null invalidation observers may dispose a source during release, while constructor observer errors roll back ownership', () => {
  const h = host(), e = environment(), overlay = createSceneNodeOverlay({container: e.container, source: h.source});
  const binding = overlay.bind(e.label, {target: h.source.node('a')});
  h.source.subscribe(value => { if (value === null) h.source.dispose(); });
  binding.target = null; assert.equal(h.removes.length, 1); assert.equal(h.registrations.size, 0); overlay.dispose();

  const failing = host('failing'), other = environment();
  const failingOverlay = createSceneNodeOverlay({container: other.container, source: failing.source});
  const stop = failing.source.subscribe(value => { if (value === null) throw new Error('Injected observer failure.'); });
  assert.throws(() => failingOverlay.bind(other.button, {target: failing.source.node('a')}));
  assert.equal(failing.registrations.size, 0); assert.equal(failing.removes.length, 1);
  assert.deepEqual(other.origin.children, [other.label, other.button]);
  stop(); failingOverlay.dispose(); failing.source.dispose();
});
