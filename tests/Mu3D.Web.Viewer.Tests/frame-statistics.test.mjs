import test from 'node:test';
import assert from 'node:assert/strict';
import {createFrameStatisticsSource, createFrameStatisticsView, createFrameStatisticsOverlay}
  from '../../src/Mu3D.Web.Toolkit/wwwroot/frame-statistics.mjs';

import {TestNode as Node, createTestDocument} from './support/dom-fixture.mjs';

function environment() {
  const observers = [];
  class ResizeObserver {
    constructor(callback) { this.callback = callback; this.observed = null; observers.push(this); }
    observe(node) { this.observed = node; }
    disconnect() { this.observed = null; }
    resize(width) { if (this.observed) { this.observed.width = width; this.callback(); } }
  }
  const document = createTestDocument({width: 238, height: 52});
  document.defaultView.ResizeObserver = ResizeObserver;
  return {document, container: document.createElement('div'), observers};
}
function snapshot(fps = 60, extra = {}) {
  return {sampleCount: 10, windowDurationMilliseconds: 10000 / fps, framesPerSecond: fps,
    averageFrameMilliseconds: 1000 / fps, minimumFrameMilliseconds: 10, maximumFrameMilliseconds: 25,
    presentationSampleCount: 0, rendererSampleCount: 0, drawCallSampleCount: 0, primitiveSampleCount: 0,
    averageAcquireMilliseconds: null, averagePresentationRenderMilliseconds: null,
    averagePresentMilliseconds: null, averagePresentationTotalMilliseconds: null,
    averageRendererPrepareMilliseconds: null, averageRendererEncodeMilliseconds: null,
    averageRendererSubmitMilliseconds: null, averageRendererCacheTrimMilliseconds: null,
    averageRendererTotalMilliseconds: null, averageDrawCallCount: null, averagePrimitiveCount: null,
    resourceCounts: null, ...extra};
}
const parts = view => {
  const [headline, graph, details] = view.element.children;
  return {headline, graph, details};
};

test('source freezes shared snapshots deeply and observes collection settings without duplicates', () => {
  const source = createFrameStatisticsSource(), snapshots = [], options = [], resets = [];
  assert.deepEqual(source.options, {isEnabled: true, snapshotIntervalMilliseconds: 500, drawCallCount: null, primitiveCount: null});
  assert.equal(source.latestSnapshot.sampleCount, 0);
  const stop = source.subscribe(value => snapshots.push(value));
  source.subscribeOptions(value => options.push(value));
  const stopReset = source.subscribeReset(() => resets.push('reset'));
  source.isEnabled = false; source.isEnabled = false;
  source.snapshotIntervalMilliseconds = 0; source.snapshotIntervalMilliseconds = 0;
  assert.equal(options.length, 2); assert.ok(Object.isFrozen(options[0]));
  const borrowed = snapshot(60, {resourceCounts: {meshCount: 3, textureCount: 0}});
  const published = source.publish(borrowed);
  borrowed.framesPerSecond = 1; borrowed.resourceCounts.meshCount = 99;
  assert.equal(source.latestSnapshot.framesPerSecond, 60);
  assert.equal(source.latestSnapshot.resourceCounts.meshCount, 3);
  assert.equal(source.latestSnapshot.resourceCounts.pipelineCount, null);
  assert.equal(source.latestSnapshot.averageRendererTotalMilliseconds, null);
  assert.ok(Object.isFrozen(published)); assert.ok(Object.isFrozen(published.resourceCounts));
  assert.throws(() => { published.resourceCounts.textureCount = 6; });
  assert.throws(() => { source.latestSnapshot = borrowed; });
  source.reset(); assert.equal(snapshots.length, 2); assert.equal(snapshots[1].sampleCount, 0);
  assert.deepEqual(resets, ['reset']); stopReset(); source.reset(); assert.equal(resets.length, 1);
  stop(); source.publish(snapshot(50)); assert.equal(snapshots.length, 3);
  source.dispose(); source.dispose(); assert.throws(() => source.publish(snapshot()));
  assert.throws(() => source.subscribe(() => {}));
});

test('malformed publications and options are rejected atomically; distinct duplicate listeners unsubscribe independently', () => {
  const source = createFrameStatisticsSource(); source.publish(snapshot(45));
  for (const value of [snapshot(NaN), snapshot(60, {averagePresentMilliseconds: -1}),
    snapshot(60, {sampleCount: 1.5}), snapshot(60, {resourceCounts: {estimatedCpuBytes: Infinity}}), null])
    assert.throws(() => source.publish(value));
  assert.equal(source.latestSnapshot.framesPerSecond, 45);
  assert.throws(() => { source.isEnabled = 'false'; });
  assert.throws(() => { source.snapshotIntervalMilliseconds = -1; });
  assert.throws(() => { source.snapshotIntervalMilliseconds = 1e15; });
  assert.deepEqual(source.options, {isEnabled: true, snapshotIntervalMilliseconds: 500, drawCallCount: null, primitiveCount: null});
  let calls = 0; const listener = () => calls++;
  const a = source.subscribe(listener), b = source.subscribe(listener);
  a(); source.publish(snapshot(50)); assert.equal(calls, 1); b(); b();
  source.publish(snapshot(55)); assert.equal(calls, 1); source.dispose();
});

test('native default hierarchy, waiting text, compact missing-data text and display options', () => {
  const {document} = environment(), source = createFrameStatisticsSource();
  const view = createFrameStatisticsView({document, source}); const {headline, graph, details} = parts(view);
  assert.equal(view.isDetailed, false); assert.equal(view.isGraphVisible, true);
  assert.equal(graph.tag, 'svg'); assert.equal(graph.style.height, '52px');
  assert.equal(graph.style.backgroundColor, 'rgb(0 3.5% 9%)');
  assert.equal(view.element.style.padding, '10px'); assert.equal(view.element.style.gap, '3px');
  assert.equal(headline.style.fontSize, '16px'); assert.equal(headline.style.fontWeight, 'bold');
  assert.equal(details.style.fontSize, '12px'); assert.equal(headline.style.color, '#ffffff');
  assert.equal(headline.textContent, 'Waiting for frame samples…');
  assert.equal(details.textContent, 'No completed frame interval has been sampled.');
  source.publish(snapshot(50));
  assert.equal(headline.textContent, '50 FPS (50–50)  ·  20.00 ms');
  assert.equal(details.textContent, 'Render —  ·  Present —\nDraws —  ·  Primitives —');
  const options = []; view.subscribeOptions(value => options.push(value));
  view.textColor = '#123456'; view.textColor = '#123456';
  view.graphColor = '#fedcba'; view.graphBackgroundColor = '#102030';
  view.isGraphVisible = false;
  assert.equal(options.length, 4); assert.equal(details.style.color, '#123456');
  assert.equal(graph.style.display, 'none'); assert.equal(graph.style.backgroundColor, '#102030');
  source.publish(snapshot(40)); assert.deepEqual(view.history, [50, 40]);
  view.isGraphVisible = true; assert.equal(graph.style.display, 'block');
  assert.equal(graph.children[0].attributes.stroke, '#fedcba');
  view.dispose(); source.dispose();
});

test('detailed formatter follows native row order, invariant decimal/grouping and byte thresholds', () => {
  const {document} = environment(), source = createFrameStatisticsSource();
  const view = createFrameStatisticsView({document, source, isDetailed: true});
  source.publish(snapshot(59.5, {averageFrameMilliseconds: 16.805, minimumFrameMilliseconds: 10.5,
    maximumFrameMilliseconds: 25.5, presentationSampleCount: 10, rendererSampleCount: 10,
    drawCallSampleCount: 10, primitiveSampleCount: 10, averageAcquireMilliseconds: 0.125,
    averagePresentationRenderMilliseconds: 1.25, averagePresentMilliseconds: 0,
    averagePresentationTotalMilliseconds: 1.375, averageRendererPrepareMilliseconds: 0,
    averageRendererEncodeMilliseconds: 1.005, averageRendererSubmitMilliseconds: 0.25,
    averageRendererCacheTrimMilliseconds: 0.1, averageRendererTotalMilliseconds: 1.355,
    averageDrawCallCount: 4.25, averagePrimitiveCount: 1234.55,
    resourceCounts: {meshCount: 3, vertexCount: 10000, materialCount: 4, textureCount: 0,
      bufferCount: null, pipelineCount: 2, estimatedCpuBytes: 1024, estimatedGpuBytes: 1572864}}));
  const {headline, details} = parts(view);
  assert.equal(headline.textContent, '60 FPS (60–60)  ·  16.81 ms');
  assert.equal(details.textContent,
    'Frame  min 10.50 ms  ·  avg 16.81 ms  ·  max 25.50 ms  ·  samples 10\n' +
    'Presentation  acquire 0.13 ms  ·  render 1.25 ms  ·  present 0.00 ms  ·  total 1.38 ms\n' +
    'Renderer  prepare 0.00 ms  ·  encode 1.01 ms  ·  submit 0.25 ms  ·  trim 0.10 ms  ·  total 1.36 ms\n' +
    'Render 1.36 ms  ·  Present 1.38 ms\nDraws 4.3  ·  Primitives 1234.6\n' +
    'Resources  meshes 3  ·  vertices 10,000  ·  materials 4  ·  textures 0  ·  pipelines 2  ·  CPU 1.0 KiB  ·  GPU 1.5 MiB');
  source.publish(snapshot(60, {resourceCounts: {estimatedCpuBytes: 1023, estimatedGpuBytes: 1048576}}));
  assert.ok(details.textContent.endsWith('Resources  CPU 1023 B  ·  GPU 1.0 MiB'));
  source.publish({...source.latestSnapshot, sampleCount: 0, resourceCounts: {meshCount: 0}});
  assert.equal(details.textContent, 'Resources  meshes 0'); assert.deepEqual(view.history, []);
  view.dispose(); source.dispose();
});

test('120 published-snapshot history evicts old extrema and graph uses native right alignment and scale', () => {
  const {document, observers} = environment(), source = createFrameStatisticsSource();
  const view = createFrameStatisticsView({document, source}); const {headline, graph} = parts(view);
  source.publish(snapshot(30));
  assert.equal(graph.children.length, 4);
  const bar = graph.children[3];
  assert.equal(bar.attributes.x, '237.28'); assert.equal(bar.attributes.y, '26');
  assert.equal(bar.attributes.width, '1.44'); assert.equal(bar.attributes.height, '26');
  assert.equal(bar.attributes.opacity, '0.22');
  assert.deepEqual(graph.children.slice(0, 3).map(node => node.attributes.y1), ['13', '26', '39']);
  source.publish(snapshot(120));
  assert.equal(graph.children[3].attributes.x, '235.28');
  assert.equal(graph.children[3].attributes.y, '39');
  assert.equal(graph.children[4].attributes.y, '0');
  assert.equal(graph.children[5].attributes['stroke-width'], '1.5');
  assert.equal(graph.children[5].attributes['vector-effect'], 'non-scaling-stroke');
  observers[0].resize(119); assert.equal(graph.attributes.viewBox, '0 0 119 52');
  assert.equal(graph.children[3].attributes.width, '1');
  source.reset(); for (let fps = 1; fps <= 122; fps++) source.publish(snapshot(fps));
  assert.equal(view.history.length, 120); assert.equal(view.history[0], 3); assert.equal(view.history[119], 122);
  assert.equal(headline.textContent, '122 FPS (3–122)  ·  8.20 ms');
  assert.equal(graph.children[3].attributes.x, '-0.5');
  assert.throws(() => view.history.push(9)); view.dispose(); source.dispose();
});

test('source binding replacement/null, detach/reattach and disposal preserve borrowed-source ownership', () => {
  const {document, container, observers} = environment();
  const a = createFrameStatisticsSource(), b = createFrameStatisticsSource();
  a.publish(snapshot(60)); b.publish(snapshot(90));
  const view = createFrameStatisticsView({document, source: a}); container.appendChild(view.element);
  const updates = []; view.subscribe(value => updates.push(value));
  a.publish(snapshot(50)); view.detach(); a.publish(snapshot(40));
  assert.deepEqual(view.history, [60, 50]); assert.equal(view.latestSnapshot.framesPerSecond, 50);
  assert.equal(observers[0].observed, null);
  view.attach(); assert.equal(view.latestSnapshot.framesPerSecond, 40);
  assert.deepEqual(view.history, [60, 50], 'Resuming applies latest without adding a duplicate history sample.');
  const before = updates.length; view.source = b;
  assert.equal(updates.length, before + 2); assert.deepEqual(view.history, [90]);
  a.publish(snapshot(20)); assert.deepEqual(view.history, [90]);
  view.source = null; assert.equal(view.latestSnapshot.sampleCount, 0); assert.deepEqual(view.history, []);
  assert.equal(parts(view).headline.textContent, 'Waiting for frame samples…');
  b.publish(snapshot(70)); assert.equal(view.latestSnapshot.sampleCount, 0);
  view.source = b; view.detach(); b.reset(); view.attach(); assert.deepEqual(view.history, []);
  view.dispose(); view.dispose(); assert.equal(container.children.length, 0);
  assert.equal(observers[0].observed, null);
  assert.throws(() => { view.source = a; }); assert.throws(() => view.attach());
  a.publish(snapshot(15)); b.publish(snapshot(80)); a.dispose(); b.dispose();
});

test('optional collection counts distinguish null/zero and remain observable and preserved across reset', () => {
  const {container} = environment(), source = createFrameStatisticsSource();
  const overlay = createFrameStatisticsOverlay({container, source}), changes = [], options = [];
  source.subscribeOptions(value => changes.push(value)); overlay.subscribeOptions(value => options.push(value));
  assert.equal(overlay.drawCallCount, null); assert.equal(overlay.primitiveCount, null);
  overlay.drawCallCount = 0; overlay.drawCallCount = 0; source.primitiveCount = 1234;
  assert.equal(source.drawCallCount, 0); assert.equal(overlay.primitiveCount, 1234);
  assert.equal(changes.length, 2); assert.equal(options.length, 2);
  for (const value of [-1, 1.5, Infinity, Number.MAX_SAFE_INTEGER + 1, undefined])
    assert.throws(() => { source.drawCallCount = value; });
  assert.throws(() => { overlay.primitiveCount = -1; });
  overlay.reset(); assert.equal(source.drawCallCount, 0); assert.equal(source.primitiveCount, 1234);
  assert.equal(changes.length, 2); overlay.primitiveCount = null;
  assert.equal(source.primitiveCount, null); assert.equal(changes.length, 3);
  overlay.dispose(); source.dispose();
});

test('manual snapshot requests call the borrowed host capture boundary even while disabled', () => {
  const {container} = environment(), source = createFrameStatisticsSource({isEnabled: false});
  const overlay = createFrameStatisticsOverlay({container, source});
  assert.equal(source.publishSnapshot().sampleCount, 0); assert.deepEqual(overlay.view.history, []);
  let captures = 0; const publications = [];
  source.subscribe(value => publications.push(value));
  const stop = source.subscribePublish(() => { captures++; source.publish(snapshot(50)); });
  assert.equal(overlay.publishSnapshot().framesPerSecond, 50);
  assert.equal(captures, 1); assert.equal(publications.length, 1); assert.deepEqual(overlay.view.history, [50]);
  stop(); assert.equal(source.publishSnapshot().framesPerSecond, 50); assert.equal(captures, 1);
  overlay.dispose(); source.dispose();
  assert.throws(() => source.publishSnapshot()); assert.throws(() => source.subscribePublish(() => {}));
  assert.throws(() => overlay.publishSnapshot());
});

test('retarget and detach ignore already-queued callbacks from obsolete source subscriptions', () => {
  const {document} = environment(), queued = [];
  const a = {latestSnapshot: snapshot(60), subscribe(callback) { queued.push(callback); return () => {}; }};
  const b = createFrameStatisticsSource(); b.publish(snapshot(90));
  const view = createFrameStatisticsView({document, source: a});
  view.source = b; queued[0](snapshot(10)); assert.deepEqual(view.history, [90]);
  view.source = a; view.detach(); view.attach();
  const oldHistory = view.history; queued[1](snapshot(5)); assert.deepEqual(view.history, oldHistory);
  view.dispose(); queued[2](snapshot(1)); assert.deepEqual(view.history, oldHistory); b.dispose();
});

test('source replacement during publication cannot overwrite newly bound view/overlay with a stale event', () => {
  const {document, container} = environment(), a = createFrameStatisticsSource(), b = createFrameStatisticsSource();
  b.publish(snapshot(90)); let view, overlay;
  a.subscribe(() => { view.source = b; overlay.source = b; });
  view = createFrameStatisticsView({document, source: a});
  overlay = createFrameStatisticsOverlay({container, source: a});
  const updates = []; overlay.subscribe(value => updates.push(value));
  a.publish(snapshot(10));
  assert.equal(view.latestSnapshot.framesPerSecond, 90); assert.deepEqual(view.history, [90]);
  assert.equal(overlay.latestSnapshot.framesPerSecond, 90); assert.deepEqual(overlay.view.history, [90]);
  assert.equal(updates.length, 1); assert.equal(updates[0].framesPerSecond, 90);
  view.dispose(); overlay.dispose(); a.dispose(); b.dispose();
});

test('reentrant overlay detach/reattach rejects callbacks from the previous attachment even with the same source', () => {
  const {container} = environment(), source = createFrameStatisticsSource(); let overlay;
  source.subscribe(() => { overlay.detach(); overlay.attach(); });
  source.subscribeOptions(() => { overlay.detach(); overlay.attach(); });
  overlay = createFrameStatisticsOverlay({container, source});
  const publications = [], changes = [];
  overlay.subscribe(value => publications.push(value)); overlay.subscribeOptions(value => changes.push(value));
  source.publish(snapshot(50)); assert.equal(overlay.latestSnapshot.framesPerSecond, 50);
  assert.deepEqual(overlay.view.history, [50]); assert.equal(publications.length, 0);
  source.isEnabled = false; assert.equal(changes.length, 0); assert.equal(overlay.isEnabled, false);
  overlay.dispose(); source.dispose();
});

test('an immediate custom source subscription initializes once and reattachment does not grow history', () => {
  const {document} = environment();
  const source = {latestSnapshot: snapshot(50), subscribe(callback) { callback(this.latestSnapshot); return () => {}; }};
  const view = createFrameStatisticsView({document, source});
  assert.deepEqual(view.history, [50]);
  view.detach(); source.latestSnapshot = snapshot(40); view.attach();
  assert.equal(view.latestSnapshot.framesPerSecond, 40); assert.deepEqual(view.history, [50]); view.dispose();
});

test('overlay defaults, all placements and mutable width/margin/colors are pass-through without input listeners', () => {
  const {container} = environment(), source = createFrameStatisticsSource();
  const overlay = createFrameStatisticsOverlay({container, source});
  const panel = overlay.element.children[0];
  assert.equal(overlay.placement, 'top-right'); assert.equal(overlay.margin, 12); assert.equal(overlay.maximumWidth, 520);
  assert.equal(overlay.element.style.pointerEvents, 'none'); assert.equal(panel.style.pointerEvents, 'none');
  assert.equal(panel.style.border, '1px solid #53647d'); assert.equal(panel.style.borderRadius, '7px');
  assert.equal(panel.style.backgroundColor, 'rgba(25, 36, 56, 0.8509803921568627)');
  for (const [placement, vertical, horizontal] of [['top-left', 'flex-start', 'flex-start'],
    ['top-center', 'flex-start', 'center'], ['top-right', 'flex-start', 'flex-end'],
    ['center-left', 'center', 'flex-start'], ['center', 'center', 'center'],
    ['center-right', 'center', 'flex-end'], ['bottom-left', 'flex-end', 'flex-start'],
    ['bottom-center', 'flex-end', 'center'], ['bottom-right', 'flex-end', 'flex-end']]) {
    overlay.placement = placement;
    assert.equal(overlay.element.style.alignItems, vertical); assert.equal(overlay.element.style.justifyContent, horizontal);
  }
  overlay.margin = 3.5; overlay.maximumWidth = 250; overlay.backgroundColor = 'transparent';
  assert.equal(overlay.element.style.padding, '3.5px'); assert.equal(panel.style.maxWidth, '250px');
  assert.equal(panel.style.backgroundColor, 'transparent');
  assert.throws(() => { overlay.margin = -1; }); assert.throws(() => { overlay.maximumWidth = 0; });
  assert.throws(() => { overlay.placement = 'side'; });
  assert.equal(overlay.margin, 3.5); assert.equal(overlay.maximumWidth, 250);
  overlay.dispose(); source.dispose();
});

test('overlay visibility retains history and source collection while latest publication remains observable', () => {
  const {container} = environment(), source = createFrameStatisticsSource();
  const overlay = createFrameStatisticsOverlay({container, source}), updates = [], options = [];
  overlay.subscribe(value => updates.push(value)); overlay.subscribeOptions(value => options.push(value));
  source.publish(snapshot(60)); source.publish(snapshot(50)); overlay.isVisible = false;
  assert.equal(overlay.element.hidden, true); assert.equal(source.isEnabled, true);
  source.publish(snapshot(40)); assert.equal(overlay.latestSnapshot.framesPerSecond, 40);
  assert.equal(updates.length, 3); assert.deepEqual(overlay.view.history, [60, 50]);
  overlay.isVisible = true;
  assert.equal(overlay.view.latestSnapshot.framesPerSecond, 40); assert.deepEqual(overlay.view.history, [60, 50]);
  overlay.isGraphVisible = false; overlay.isGraphVisible = false; assert.equal(options.length, 3);
  source.publish(snapshot(30)); assert.deepEqual(overlay.view.history, [60, 50, 30]);
  overlay.isVisible = false; source.reset(); overlay.isVisible = true;
  assert.deepEqual(overlay.view.history, []); assert.equal(overlay.view.latestSnapshot.sampleCount, 0);
  overlay.dispose(); source.dispose();
});

test('an initially hidden overlay waits to display history, and invalid construction borrows no subscriptions', () => {
  const {container, observers} = environment(), source = createFrameStatisticsSource();
  source.publish(snapshot(60));
  assert.throws(() => createFrameStatisticsOverlay({container, source, maximumWidth: 0}));
  assert.throws(() => createFrameStatisticsOverlay({container, source, isDetailed: 'true'}));
  assert.throws(() => createFrameStatisticsOverlay({container, source, isEnabled: false, snapshotIntervalMilliseconds: -1}));
  assert.throws(() => createFrameStatisticsOverlay({container, source, drawCallCount: 3, primitiveCount: -1}));
  assert.equal(source.isEnabled, true);
  assert.equal(source.drawCallCount, null);
  assert.equal(container.children.length, 0); assert.equal(observers.length, 0);
  const overlay = createFrameStatisticsOverlay({container, source, isVisible: false});
  assert.deepEqual(overlay.view.history, []); assert.equal(overlay.view.latestSnapshot.sampleCount, 0);
  source.publish(snapshot(50)); assert.equal(overlay.latestSnapshot.framesPerSecond, 50);
  overlay.isVisible = true; assert.deepEqual(overlay.view.history, [50]);
  overlay.dispose(); source.dispose();
  assert.throws(() => createFrameStatisticsOverlay({container, source}));
  assert.equal(container.children.length, 0); assert.equal(observers.at(-1).observed, null);
});

test('overlay detach/reattach preserves displayed history and handles source replacement while detached', () => {
  const {container} = environment(), a = createFrameStatisticsSource(), b = createFrameStatisticsSource({drawCallCount: 7});
  const overlay = createFrameStatisticsOverlay({container, source: a});
  a.publish(snapshot(60)); a.publish(snapshot(50)); overlay.detach(); a.publish(snapshot(40));
  assert.equal(overlay.view.source, a); assert.deepEqual(overlay.view.history, [60, 50]);
  overlay.attach(); assert.equal(overlay.view.latestSnapshot.framesPerSecond, 40);
  assert.deepEqual(overlay.view.history, [60, 50]);
  overlay.detach(); b.publish(snapshot(80)); overlay.source = b;
  assert.equal(overlay.view.source, b); assert.deepEqual(overlay.view.history, []);
  assert.equal(overlay.drawCallCount, 7); overlay.attach(); assert.deepEqual(overlay.view.history, [80]);
  overlay.detach(); b.reset(); overlay.attach(); assert.deepEqual(overlay.view.history, []);
  overlay.dispose(); a.dispose(); b.dispose();
});

test('overlay settings bind in both directions exactly once; replacement and detach release subscriptions', () => {
  const {container} = environment(), a = createFrameStatisticsSource(), b = createFrameStatisticsSource();
  const sibling = new Node(container.ownerDocument, 'canvas'); container.appendChild(sibling);
  const overlay = createFrameStatisticsOverlay({container, source: a}), options = [], updates = [], resets = [];
  overlay.subscribeOptions(value => options.push(value)); overlay.subscribe(value => updates.push(value));
  a.subscribeReset(() => resets.push('a')); b.subscribeReset(() => resets.push('b'));
  overlay.isEnabled = false; overlay.isEnabled = false;
  a.snapshotIntervalMilliseconds = 250;
  assert.equal(options.length, 2); assert.equal(a.isEnabled, false); assert.equal(overlay.snapshotIntervalMilliseconds, 250);
  assert.equal(overlay.isVisible, true);
  b.publish(snapshot(80)); overlay.source = b;
  assert.equal(options.length, 3); assert.equal(overlay.isEnabled, true); assert.equal(overlay.snapshotIntervalMilliseconds, 500);
  assert.deepEqual(overlay.view.history, [80]); const count = updates.length;
  a.publish(snapshot(10)); a.isEnabled = true;
  assert.equal(updates.length, count); assert.equal(options.length, 3);
  overlay.reset(); assert.deepEqual(resets, ['b']); assert.equal(updates.at(-1).sampleCount, 0);
  overlay.detach(); assert.equal(container.children.length, 1); assert.equal(container.children[0], sibling);
  b.publish(snapshot(70)); b.snapshotIntervalMilliseconds = 125;
  assert.equal(options.length, 3); assert.equal(overlay.snapshotIntervalMilliseconds, 125);
  overlay.attach(); assert.deepEqual(overlay.view.history, [70]);
  overlay.source = null; assert.equal(overlay.latestSnapshot.sampleCount, 0); assert.deepEqual(overlay.view.history, []);
  b.publish(snapshot(40)); assert.equal(overlay.latestSnapshot.sampleCount, 0);
  overlay.dispose(); overlay.dispose(); assert.equal(container.children.length, 1);
  assert.throws(() => { overlay.isVisible = true; }); assert.throws(() => overlay.attach());
  a.publish(snapshot(12)); b.publish(snapshot(45)); assert.deepEqual(resets, ['b']); a.dispose(); b.dispose();
});
