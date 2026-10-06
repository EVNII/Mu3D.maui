# Native Toolkit host regressions

This nonpackable headless executable links the actual pointer behavior, pointer state, generic
overlay, anchor layer, frame-statistics behavior/view/overlay and maintained internal scene policy.
It uses Microsoft.Maui.Controls 10.0.90
controls, bindings, visual parenting and event senders, plus real Core projection and Toolkit
controllers/arbitration. The bounded MAUI internal event senders are invoked through reflection
only in this test host; no reflection is added to product code or Web/AOT paths.

The GPU SceneView/declarative feature protocol and native platform capture are explicit fixtures.
Passing these regressions does not execute a native handler, GPU, physical capture or UI. The real
Mu3D.Maui and Mu3D.Maui.Toolkit assemblies also require a target-OS build; runtime UI/capture remains
a separate authorized check.

Coverage includes eligibility loss at move/release/frame, pose restoration, lease/capture-entry
cleanup, retained targets, source/extent replacement, callback/input reentrancy, failed callbacks,
externally owned edits, anchor placement/clipping, two-item batch invalidation and subscription
cleanup through detach/dispose/load/manager transitions.
Statistics checks use real typed two-way bindings and INPC state: panel taps update view, overlay
and source, subsequent source changes remain effective, and graph preference/history survive mode
changes. They also cover the legacy bool alias, complete border activation, input leases and
attachment teardown. The explicit frame/session fixture substitutes GPU presentation only.
