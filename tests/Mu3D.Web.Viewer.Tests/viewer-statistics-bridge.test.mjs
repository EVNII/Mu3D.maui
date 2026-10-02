import test from 'node:test';
import assert from 'node:assert/strict';
import {createFrameStatisticsSource} from '../../src/Mu3D.Web.Toolkit/wwwroot/frame-statistics.mjs';
import {createViewerStatisticsBridge} from '../Mu3D.Web.Validation/wwwroot/viewer-statistics-bridge.mjs';

function fixture() {
  const source = createFrameStatisticsSource(), configurations = [], publications = [];
  let version = 0, reads = 0, resets = 0, captures = 0;
  let snapshot = source.latestSnapshot;
  const payload = () => JSON.stringify({version, snapshot});
  const renderer = {
    ConfigureViewerStatistics(...options) { configurations.push(options); },
    GetViewerStatisticsSnapshot() { reads++; return payload(); },
    ResetViewerStatistics() { resets++; version++; snapshot = {...source.latestSnapshot, sampleCount: 0}; return payload(); },
    PublishViewerStatistics() { captures++; version++; return payload(); },
  };
  source.subscribe(value => publications.push(value));
  const bridge = createViewerStatisticsBridge(renderer, source);
  return {source, configurations, publications, bridge,
    submit(fps = 50) {
      snapshot = {...source.latestSnapshot, sampleCount: 10, framesPerSecond: fps,
        averageFrameMilliseconds: 1000 / fps};
      return ++version;
    },
    get version() { return version; },
    get calls() { return {reads, resets, captures}; },
  };
}

test('versioned bridge transfers only new publications and maps unavailable counts explicitly', () => {
  const f = fixture();
  assert.deepEqual(f.configurations, [[true, 500, -1, -1]]);
  f.bridge.update(0); assert.equal(f.calls.reads, 0);
  f.bridge.update(f.submit());
  assert.equal(f.calls.reads, 1); assert.equal(f.source.latestSnapshot.framesPerSecond, 50);
  for (let frame = 0; frame < 100; frame++) f.bridge.update(f.version);
  assert.equal(f.calls.reads, 1); assert.equal(f.publications.length, 1);
  f.source.drawCallCount = 0; f.source.primitiveCount = 12;
  f.source.snapshotIntervalMilliseconds = 50; f.source.isEnabled = false;
  assert.deepEqual(f.configurations.at(-1), [false, 50, 0, 12]);
  assert.equal(f.calls.reads, 1); // Options never request a frame or recapture a snapshot.
  f.bridge.update(f.submit(25));
  assert.equal(f.source.latestSnapshot.framesPerSecond, 25);
  f.bridge.dispose(); f.source.dispose();
});

test('manual capture while disabled and reset synchronize versions without duplicate empty publication', () => {
  const f = fixture();
  f.bridge.update(f.submit()); f.source.isEnabled = false;
  const current = f.source.publishSnapshot();
  assert.equal(f.calls.captures, 1); assert.equal(current.framesPerSecond, 50);
  f.bridge.update(f.version); assert.equal(f.calls.reads, 1);
  const beforeReset = f.publications.length;
  f.source.reset();
  assert.equal(f.calls.resets, 1); assert.equal(f.publications.length, beforeReset + 1);
  assert.equal(f.source.latestSnapshot.sampleCount, 0);
  f.bridge.update(f.version); assert.equal(f.calls.reads, 1);
  f.bridge.update(f.submit(60));
  assert.equal(f.calls.reads, 2); assert.equal(f.source.latestSnapshot.framesPerSecond, 60);
  f.bridge.dispose(); f.source.dispose();
});

test('disposing the bridge detaches all managed callbacks while leaving the borrowed source usable', () => {
  const f = fixture();
  f.bridge.dispose(); f.bridge.dispose();
  f.source.isEnabled = false; f.source.reset(); f.source.publishSnapshot();
  f.bridge.update(f.submit());
  assert.deepEqual(f.calls, {reads: 0, resets: 0, captures: 0});
  assert.equal(f.configurations.length, 1);
  f.source.publish({...f.source.latestSnapshot, framesPerSecond: 45});
  assert.equal(f.source.latestSnapshot.framesPerSecond, 45);
  f.source.dispose();
});

test('snapshot callbacks can synchronously reset without restoring an obsolete managed version', () => {
  const f = fixture();
  f.source.subscribe(snapshot => { if (snapshot.sampleCount > 0) f.source.reset(); });
  f.bridge.update(f.submit());
  assert.equal(f.source.latestSnapshot.sampleCount, 0);
  assert.equal(f.calls.resets, 1);
  const notifications = f.publications.length;
  f.bridge.update(f.version);
  assert.equal(f.calls.reads, 1); assert.equal(f.publications.length, notifications);
  f.bridge.dispose(); f.source.dispose();
});

test('disposal during copied option notification prevents a queued callback from configuring managed state', () => {
  const source = createFrameStatisticsSource(), configurations = [];
  let bridge;
  source.subscribeOptions(() => bridge.dispose());
  bridge = createViewerStatisticsBridge({ConfigureViewerStatistics(...args) { configurations.push(args); }}, source);
  source.isEnabled = false;
  assert.equal(configurations.length, 1);
  source.dispose();
});
