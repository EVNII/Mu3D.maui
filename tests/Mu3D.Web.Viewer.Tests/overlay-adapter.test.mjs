import test from 'node:test';
import assert from 'node:assert/strict';
import {createSceneAnchorOverlay} from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-anchor-overlay.mjs';
import {TestNode as Node, createOverlayDom, anchorPoint as point, sendTargetedEvent as send}
  from './support/dom-fixture.mjs';

// Minimal DOM ownership/event fixture. Browser acceptance separately checks actual CSS/hit testing.
function environment() {
  const dom = createOverlayDom(), changes = [];
  const overlay = createSceneAnchorOverlay({container: dom.container, onInteraction: active => changes.push(active)});
  return {...dom, changes, overlay, layer: dom.container.children[0]};
}
const frame = (FrameId, Points) => ({FrameId, Width: 500, Height: 300, Points});

test('one batch positions arbitrary elements in CSS space, retargets, rejects stale/invalid frames atomically', () => {
  const {overlay, label, button} = environment();
  overlay.bind(label, {anchor: 'a', offset: [0, -6], pivot: [0.5, 1]});
  const binding = overlay.bind(button, {anchor: 'b', interactive: true});
  const a = label.parentNode, b = button.parentNode;
  assert.ok(a.hidden && b.hidden, 'No position is invented before the first frame');
  const batch = frame(7, [point('a'), point('b', 400)]);
  overlay.update(batch);
  assert.equal(a.style.left, 'calc(50% + 0px)'); assert.equal(a.style.top, 'calc(25% + -6px)');
  assert.equal(a.style.transform, 'translate(-50%, -100%)');
  assert.equal(b.style.left, 'calc(80% + 8px)');
  assert.equal(a.dataset.frameId, b.dataset.frameId);
  assert.ok(a.inert); assert.equal(b.inert, false);
  batch.Points[0].X = 0;
  binding.setAnchor('a');
  assert.equal(b.style.left, 'calc(50% + 8px)', 'Copied frame points cannot be mutated by the caller');
  assert.equal(overlay.update(frame(6, [point('a', 0)])), false);
  assert.equal(b.style.left, 'calc(50% + 8px)');
  assert.throws(() => overlay.update(frame(8, [point('a', NaN)])));
  assert.throws(() => overlay.update(frame(8, [point('a'), point('a')])));
  assert.equal(a.dataset.frameId, '7', 'Rejected batches leave the whole displayed batch intact');
  overlay.dispose();
});

test('missing/behind/frustum anchors hide, optional outside projection clips, enable and clear remove stale positions', () => {
  const {overlay, label, button} = environment();
  overlay.bind(label, {anchor: 'a'});
  overlay.bind(button, {anchor: 'a', hideWhenOutsideViewport: false, interactive: true});
  const a = label.parentNode, b = button.parentNode;
  overlay.update(frame(1, [point('a', 550, 75, {InsideViewport: false})]));
  assert.ok(a.hidden); assert.equal(b.hidden, false); assert.equal(b.style.left, 'calc(110.00000000000001% + 8px)');
  overlay.update(frame(2, [point('a', 0, 0, {Projected: false})]));
  assert.ok(a.hidden && b.hidden);
  overlay.update(frame(3, [point('a')])); overlay.setEnabled(false);
  assert.ok(a.hidden && b.hidden && b.inert);
  overlay.setEnabled(true); assert.equal(b.hidden, false);
  overlay.update(frame(4, [])); assert.ok(b.hidden);
  overlay.update(frame(5, [point('a')])); overlay.clear(); assert.ok(a.hidden && b.hidden);
  overlay.dispose();
});

test('native control defaults are preserved, pointer/focus ownership releases on hide and visibility change', () => {
  const {overlay, label, button, document, layer, changes} = environment();
  overlay.bind(label, {anchor: 'a'}); overlay.bind(button, {anchor: 'a', interactive: true});
  overlay.update(frame(1, [point('a')]));
  assert.equal(send(layer, 'pointerdown', button, {pointerId: 8}).defaultPrevented, false);
  assert.equal(overlay.hasActivePointer, true); assert.deepEqual(changes, [true]);
  send(document.defaultView, 'pointerup', button, {pointerId: 9}); assert.ok(overlay.hasActivePointer);
  send(document.defaultView, 'pointercancel', button, {pointerId: 8}); assert.equal(overlay.hasActivePointer, false);
  document.activeElement = button; send(layer, 'focusin', button); assert.ok(overlay.isInteracting);
  assert.equal(overlay.hasActivePointer, false, 'Focus alone must not reject the next Canvas contact');
  assert.equal(send(layer, 'keydown', button, {key: 'Enter'}).defaultPrevented, false);
  overlay.setEnabled(false); assert.equal(document.activeElement, null); assert.equal(overlay.isInteracting, false);
  overlay.setEnabled(true); send(layer, 'pointerdown', label, {pointerId: 2});
  assert.equal(overlay.hasActivePointer, false, 'Decorative elements never claim tools');
  send(layer, 'pointerdown', button, {pointerId: 3});
  document.visibilityState = 'hidden'; document.dispatchEvent(new Event('visibilitychange'));
  assert.equal(overlay.hasActivePointer, false); assert.ok(button.parentNode.hidden);
  overlay.dispose(); const count = changes.length;
  send(layer, 'pointerdown', button, {pointerId: 4}); assert.equal(changes.length, count);
});

test('disposal restores DOM order and preserves application state, while external reparenting stays application-owned', () => {
  const {overlay, label, button, origin, container} = environment();
  let clicks = 0; label.style.color = 'red'; button.addEventListener('click', () => clicks++);
  const first = overlay.bind(label, {anchor: 'a'}); overlay.bind(button, {anchor: 'a'});
  assert.throws(() => overlay.bind(button, {anchor: 'b'}));
  first.dispose(); first.dispose(); overlay.dispose(); overlay.dispose();
  assert.deepEqual(origin.children, [label, button]); assert.equal(container.children.length, 0);
  assert.equal(label.style.color, 'red'); button.dispatchEvent(new Event('click')); assert.equal(clicks, 1);
  assert.throws(() => overlay.update(frame(1, [])));
  const next = createSceneAnchorOverlay({container}); next.bind(button, {anchor: 'a'});
  const moved = new Node(origin.ownerDocument); moved.appendChild(button); next.dispose();
  assert.equal(button.parentNode, moved, 'Disposal cannot steal externally moved elements');
});

test('binding option updates are atomic and release native focus/pointer leases without moving the element', () => {
  const {overlay, button, layer, document} = environment();
  const binding = overlay.bind(button, {anchor: 'a', interactive: true});
  const wrapper = button.parentNode;
  overlay.update(frame(1, [point('a')]));
  send(layer, 'pointerdown', button, {pointerId: 4});
  document.activeElement = button; send(layer, 'focusin', button);
  assert.throws(() => binding.setOptions({offset: [NaN, 0], interactive: false}));
  assert.equal(overlay.hasActivePointer, true); assert.equal(document.activeElement, button);
  binding.setOptions({offset: [0, -28], pivot: [0, 0], interactive: false});
  assert.equal(button.parentNode, wrapper);
  assert.equal(wrapper.style.top, 'calc(25% + -28px)');
  assert.equal(wrapper.style.transform, 'translate(0%, 0%)');
  assert.ok(wrapper.inert); assert.equal(overlay.isInteracting, false);
  assert.equal(document.activeElement, null); assert.equal(overlay.hasActivePointer, false);
  binding.dispose(); assert.throws(() => binding.setOptions({interactive: true})); overlay.dispose();
});

test('a DOM append failure restores borrowed controls and their exact original slot', () => {
  const {overlay, button, label, origin, layer} = environment();
  const append = layer.appendChild;
  layer.appendChild = () => { throw new Error('DOM rejected wrapper'); };
  assert.throws(() => overlay.bind(button, {anchor: 'a'}), /DOM rejected/);
  assert.deepEqual(origin.children, [label, button]); assert.equal(layer.children.length, 0);
  layer.appendChild = append;
  const binding = overlay.bind(button, {anchor: 'a'}); binding.dispose();
  assert.deepEqual(origin.children, [label, button]); overlay.dispose();
});
