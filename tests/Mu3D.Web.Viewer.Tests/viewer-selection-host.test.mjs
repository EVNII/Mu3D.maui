import test from 'node:test';
import assert from 'node:assert/strict';
import {createSceneNodeAnchorSource} from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-node-overlay.mjs';
import {createSceneSelectionSource} from '../../src/Mu3D.Web.Toolkit/wwwroot/scene-selection.mjs';
import {createViewerSelectionBridge} from '../Mu3D.Web.Validation/wwwroot/viewer-selection-bridge.mjs';

function fixture() {
  const catalog = {SourceId: 'managed-scene', Nodes: ['blue', 'cone', 'green', 'model']
    .map((Key, index) => ({Key, Token: `node-${index + 1}`}))};
  const anchors = createSceneNodeAnchorSource({sourceId: catalog.SourceId,
    configureAnchor() { assert.fail('Selection borrows handles without creating anchor registrations'); },
    removeAnchor() { assert.fail('Selection does not remove borrowed anchor registrations'); },
    subscribeFrames() { assert.fail('Selection owns no scene-frame subscription'); }});
  const nodes = Object.fromEntries(catalog.Nodes.map(entry => [entry.Key, anchors.node(entry.Token)]));
  const calls = [];
  let response = {SourceId: catalog.SourceId, Candidates: []}, failCommit = false;
  const renderer = {
    GetViewerNodeCatalog: () => JSON.stringify(catalog),
    HitTestViewerSelection(x, y, mask) { calls.push(['hit', x, y, mask]); return JSON.stringify(response); },
    SelectViewerNode(token) {
      calls.push(['commit', token]); if (failCommit) throw new Error('Managed commit failed');
      return JSON.stringify({Version: 1, Token: token || null, TargetVisible: Boolean(token), HighlightEnabled: true});
    },
    ConfigureViewerHighlight(enabled) { calls.push(['highlight', enabled]); },
  };
  const bridge = createViewerSelectionBridge(renderer, nodes, createSceneSelectionSource,
    () => calls.push(['invalidate']), () => calls.push(['cancel-interaction']));
  return {bridge, nodes, anchors, calls, set response(value) { response = value; },
    set failCommit(value) { failCommit = value; },
    candidate(Token, Distance = 2) { return {Token, Distance, TriangleIndex: 7,
      WorldPosition: [1, 2, 3], LocalPosition: [.5, 1, 1.5]}; }};
}

test('selection bridge commits before notifying and shares real opaque node handles with anchored UI', () => {
  const env = fixture();
  try {
    assert.deepEqual(env.calls, [['cancel-interaction'], ['commit', 'node-4'], ['invalidate']],
      'The initial editable group is explicit application state with the same managed target');
    const notifications = [];
    env.bridge.source.subscribeSelection(change => {
      notifications.push(change); env.calls.push(['notify', change.newNode]);
    });
    env.response = {SourceId: 'managed-scene', Candidates: [env.candidate('node-1'), env.candidate('node-3', 3)]};
    let evidence;
    env.bridge.source.subscribeRequested(request => { evidence = request.candidates; request.selectedNode = request.candidates[1].node; });
    env.bridge.source.requestSelection({x: 660, y: 360});
    assert.equal(env.bridge.source.selectedNode, env.nodes.green);
    assert.equal(notifications[0].oldNode, env.nodes.model);
    assert.equal(notifications[0].newNode, env.nodes.green);
    assert.deepEqual(env.calls.slice(3), [['hit', 660, 360, 0xffffffff], ['cancel-interaction'],
      ['commit', 'node-3'], ['invalidate'], ['notify', env.nodes.green]]);
    assert.equal(evidence[0].node, env.nodes.blue); assert.deepEqual(Object.keys(evidence[0].node), []);
    assert.equal(evidence[0].hit.Token, 'node-1'); assert.equal(evidence[0].hit.TriangleIndex, 7);
    assert.ok(Object.isFrozen(evidence) && Object.isFrozen(evidence[0].hit.WorldPosition));
    const count = env.calls.length; env.bridge.source.selectedNode = env.nodes.green;
    assert.equal(env.calls.length, count, 'Repeating bound state creates no commit or write-back loop');
  } finally { env.bridge.dispose(); env.anchors.dispose(); }
});

test('foreign handles, source identities and candidate tokens cannot mutate current selection', () => {
  const env = fixture();
  try {
    assert.throws(() => env.bridge.source.selectedNode = Object.freeze({}), /another scene source/);
    env.response = {SourceId: 'replaced-scene', Candidates: [env.candidate('node-1')]};
    assert.throws(() => env.bridge.source.requestSelection({x: 4, y: 5}), /another scene source/);
    env.response = {SourceId: 'managed-scene', Candidates: [env.candidate('removed-token')]};
    assert.throws(() => env.bridge.source.requestSelection({x: 4, y: 5}), /not in this scene catalog/);
    assert.equal(env.bridge.source.selectedNode, env.nodes.model);
    assert.deepEqual(env.calls.filter(call => call[0] === 'commit'), [['commit', 'node-4']]);
  } finally { env.bridge.dispose(); env.anchors.dispose(); }
});

test('failed managed commits remain atomic and disposal releases only owned observable selection state', () => {
  const env = fixture();
  let notifications = 0;
  env.bridge.source.subscribeSelection(() => notifications++);
  env.failCommit = true;
  assert.throws(() => env.bridge.source.selectedNode = env.nodes.blue, /Managed commit failed/);
  assert.equal(env.bridge.source.selectedNode, env.nodes.model); assert.equal(notifications, 0);
  assert.deepEqual(env.calls.slice(-2), [['cancel-interaction'], ['commit', 'node-1']]);
  env.failCommit = false; env.bridge.configureHighlight(false);
  assert.deepEqual(env.calls.slice(-2), [['highlight', false], ['invalidate']]);
  assert.equal(env.bridge.source.selectedNode, env.nodes.model, 'Highlight display does not redefine selected state');
  const count = env.calls.length;
  env.bridge.dispose(); env.bridge.dispose();
  assert.equal(env.bridge.source.isDisposed, true); assert.equal(env.calls.length, count);
  assert.equal(env.anchors.node('node-1'), env.nodes.blue, 'The separate anchor source and its borrowed handles remain live');
  assert.throws(() => env.bridge.source.selectedNode = env.nodes.blue, /disposed/);
  assert.throws(() => env.bridge.configureHighlight(true), /disposed/);
  env.anchors.dispose();
});
