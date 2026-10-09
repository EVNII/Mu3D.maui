using Microsoft.Maui.Dispatching;

DispatcherProvider.SetCurrent(new StatisticsTestDispatcherProvider());
List<string> failures = [];
void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception exception) { failures.Add($"{name}: {exception}"); }
}

Run("real MAUI Gizmo pointer behavior eligibility/cancel/capture/lifetime", PointerBehaviorChecks.Verify);
Run("real MAUI generic overlay eligibility/projection/input/lifetime", OverlayChecks.VerifyGeneric);
Run("real MAUI node-anchor layer eligibility/projection/load/manager lifetime", OverlayChecks.VerifyLayer);
Run("real MAUI statistics display binding/tap/graph/overlay/lifetime", FrameStatisticsChecks.Verify);
Run("Feed virtualized global item indexes and partial viewport intersection", ProductFeedRangeChecks.Verify);
Run("Gallery first-entry deferred native readiness, selection and teardown", GalleryActivationChecks.Verify);
Run("Gallery model cache clear and re-entry reconstruction", ProductAssetRetentionChecks.Verify);
foreach (string failure in failures) Console.Error.WriteLine(failure);
if (failures.Count != 0) return 1;
Console.WriteLine("PASS: actual source-linked MAUI adapters on real Controls with explicit GPU-view/platform-capture fixture; no native UI, GPU or platform capture execution claim.");
return 0;
