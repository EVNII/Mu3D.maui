// Validation-host bridge only. The reusable source/view/overlay own no render clock or collector.
// A version in the ordinary frame report makes full snapshot transfer follow publication cadence.
export function createViewerStatisticsBridge(renderer, source) {
  let version = 0, disposed = false;
  function configure(options) {
    if (disposed) return;
    renderer.ConfigureViewerStatistics(options.isEnabled, options.snapshotIntervalMilliseconds,
      options.drawCallCount ?? -1, options.primitiveCount ?? -1);
  }
  function publish(payload) {
    const report = JSON.parse(payload);
    version = report.version;
    source.publish(report.snapshot);
  }
  configure(source.options);
  const stopOptions = source.subscribeOptions(configure);
  const stopReset = source.subscribeReset(() => {
    if (disposed) return;
    // The source already published its empty snapshot. Retain the managed version without
    // duplicating that notification or requesting a render just to reset diagnostics.
    version = JSON.parse(renderer.ResetViewerStatistics()).version;
  });
  const stopPublish = source.subscribePublish(() => {
    if (!disposed) publish(renderer.PublishViewerStatistics());
  });
  return {
    update(nextVersion) {
      if (!disposed && nextVersion !== version) publish(renderer.GetViewerStatisticsSnapshot());
    },
    dispose() {
      if (disposed) return;
      disposed = true; stopOptions(); stopReset(); stopPublish();
    },
  };
}
