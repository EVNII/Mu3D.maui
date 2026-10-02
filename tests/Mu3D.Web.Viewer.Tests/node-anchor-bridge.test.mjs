import test from 'node:test';
import assert from 'node:assert/strict';
import {createSceneNodeAnchorSource} from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-node-overlay.mjs';
import {createViewerNodeAnchorBridge} from '../Mu3D.Web.Validation/wwwroot/viewer-node-anchors-bridge.mjs';

function environment() {
  let revision = 0;
  const calls = [], anchors = new Map();
  const renderer = {
    GetViewerNodeCatalog: () => JSON.stringify({SourceId: 'scene-one', Nodes: [
      {Key: 'blue', Token: 'node-1'}, {Key: 'green', Token: 'node-2'}]}),
    ConfigureViewerNodeAnchor(id, target, x, y, z) {
      id ||= `anchor-${anchors.size + 1}`;
      anchors.set(id, {target, x, y, z}); calls.push(['configure', id, target, x, y, z]);
      return JSON.stringify({id, revision: ++revision, sourceId: 'scene-one'});
    },
    RemoveViewerNodeAnchor(id) { anchors.delete(id); calls.push(['remove', id]); return ++revision; },
  };
  let upstream;
  const bridge = createViewerNodeAnchorBridge(renderer, config => {
    upstream = config; return createSceneNodeAnchorSource(config);
  }, () => calls.push(['invalidate']));
  return {bridge, calls, anchors, upstream};
}

test('viewer bridge exposes opaque catalog handles and forwards one shared batch stream', () => {
  const {bridge, calls, upstream} = environment();
  assert.ok(Object.isFrozen(bridge.nodes)); assert.deepEqual(Object.keys(bridge.nodes.blue), []);
  assert.equal(bridge.nodes.blue, bridge.source.node('node-1'));
  const registration = upstream.configureAnchor({id: null, target: 'node-1', localPosition: [0, 0.8, 0]});
  assert.deepEqual(calls, [['configure', 'anchor-1', 'node-1', 0, 0.8, 0], ['invalidate']]);
  const frames = [], stop = bridge.source.subscribe(frame => frames.push(frame));
  // No JS facade registration exists in this bridge-only test, so revision is still zero.
  const frame = {SourceId: 'scene-one', Revision: 0, FrameId: 1, Width: 500, Height: 300, Points: []};
  bridge.publishFrame(frame); assert.equal(frames.length, 1); assert.equal(frames[0].FrameId, 1);
  upstream.removeAnchor(registration.id); assert.deepEqual(calls.slice(-2), [['remove', 'anchor-1'], ['invalidate']]);
  stop(); bridge.dispose(); bridge.publishFrame({...frame, FrameId: 2}); assert.equal(frames.length, 1);
});

test('disposal or unsubscribe during publication suppresses later callbacks and remains idempotent', () => {
  const {bridge, upstream} = environment();
  let later = 0;
  upstream.subscribeFrames(() => bridge.dispose()); upstream.subscribeFrames(() => later++);
  bridge.publishFrame(null); bridge.dispose(); assert.equal(later, 0);
  assert.throws(() => bridge.source.node('node-1'), /disposed/);
  assert.throws(() => upstream.configureAnchor({id: null, target: 'node-1', localPosition: [0, 0, 0]}), /disposed/);
});
