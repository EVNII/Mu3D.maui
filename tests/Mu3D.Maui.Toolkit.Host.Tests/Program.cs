List<string> failures = [];
void Run(string name, Action test)
{
    try { test(); Console.WriteLine($"PASS: {name}"); }
    catch (Exception exception) { failures.Add($"{name}: {exception}"); }
}

Run("real MAUI Gizmo pointer behavior eligibility/cancel/capture/lifetime", PointerBehaviorChecks.Verify);
Run("real MAUI generic overlay eligibility/projection/input/lifetime", OverlayChecks.VerifyGeneric);
Run("real MAUI node-anchor layer eligibility/projection/load/manager lifetime", OverlayChecks.VerifyLayer);
foreach (string failure in failures) Console.Error.WriteLine(failure);
if (failures.Count != 0) return 1;
Console.WriteLine("PASS: actual source-linked MAUI adapters on real Controls with explicit GPU-view/platform-capture fixture; no native UI, GPU or platform capture execution claim.");
return 0;
