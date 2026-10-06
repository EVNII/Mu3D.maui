using System.ComponentModel;
using System.Reflection;
using Microsoft.Maui.Dispatching;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Diagnostics;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Diagnostics;
using MauiPoint = Microsoft.Maui.Graphics.Point;

internal static class FrameStatisticsChecks
{
    internal static void Verify()
    {
        VerifyView();
        VerifyLegacyBinding();
        VerifyOverlay();
    }

    private static void VerifyView()
    {
        using FrameStatisticsBehavior source = new();
        using FrameStatisticsView view = new() { Source = source };
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal && !view.IsDetailed,
            "The library view retains its Normal display default.");
        StatisticsSource model = new() { Mode = FrameStatisticsDisplayMode.Compact };
        view.SetBinding(FrameStatisticsView.DisplayModeProperty,
            static (StatisticsSource state) => state.Mode, source: model);
        VerticalStackLayout layout = (VerticalStackLayout)view.Content;
        Label headline = (Label)layout.Children[0];
        GraphicsView graph = (GraphicsView)layout.Children[1];
        Label details = (Label)layout.Children[2];
        TapGestureRecognizer tap = view.GestureRecognizers.OfType<TapGestureRecognizer>().Single();
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Compact && !view.IsDetailed &&
            headline.Text == "— FPS" && !details.IsVisible && !graph.IsVisible && view.IsGraphVisible &&
            headline.FontSize == 12d && view.Padding == new Thickness(6d, 4d) && view.WidthRequest == -1d,
            "Bound Compact shows only waiting FPS with natural width and temporarily hides the graph.");

        Tap(tap, view);
        Check(model.Mode == FrameStatisticsDisplayMode.Normal && view.DisplayMode == model.Mode &&
            graph.IsVisible && details.IsVisible && view.Padding == new Thickness(10d),
            "A real MAUI tap advances Compact to Normal and writes to the INPC source.");
        Tap(tap, view);
        Check(model.Mode == FrameStatisticsDisplayMode.Detail && view.IsDetailed && graph.IsVisible,
            "The next real tap selects Detail and synchronizes its legacy alias.");
        Tap(tap, view);
        Check(model.Mode == FrameStatisticsDisplayMode.Compact && !view.IsDetailed && !graph.IsVisible,
            "Detail cycles back to Compact without the false alias overwriting it with Normal.");
        model.Mode = FrameStatisticsDisplayMode.Detail;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Detail && view.IsDetailed,
            "The original two-way binding still accepts a source update after all three taps.");
        model.Mode = FrameStatisticsDisplayMode.Compact;
        Lifecycle(view, "SendLoaded");
        source.Collector.RecordFrame(new FrameStatisticsSample(TimeSpan.FromMilliseconds(20d)));
        source.PublishSnapshot();
        Check(headline.Text == "50 FPS" && details.Text == string.Empty && !graph.IsVisible,
            "A published snapshot remains one FPS line even with retained graph history.");
        view.IsGraphVisible = false;
        Tap(tap, view);
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal &&
            headline.Text == "50 FPS (50–50)  ·  20.00 ms" && details.IsVisible && !graph.IsVisible,
            "Normal restores short statistics and range while honoring an explicitly hidden graph.");
        Tap(tap, view);
        Check(view.IsDetailed && details.Text.Contains("samples 1", StringComparison.Ordinal) && !graph.IsVisible,
            "Detail restores full statistics while preserving the graph preference.");
        Tap(tap, view);
        view.IsGraphVisible = true;
        Check(!graph.IsVisible, "Changing graph preference cannot expose a graph in Compact.");
        Tap(tap, view);
        Check(graph.IsVisible, "Returning to Normal restores the current graph preference.");

        view.IsDetailed = true;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Detail && model.Mode == view.DisplayMode,
            "The legacy true setter still selects Detail through the live mode binding.");
        view.IsDetailed = false;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal && model.Mode == view.DisplayMode,
            "The legacy false setter still selects Normal through the live mode binding.");
        Lifecycle(view, "SendUnloaded");
        view.Dispose();
        FrameStatisticsDisplayMode retainedMode = view.DisplayMode;
        Tap(tap, view);
        Check(view.DisplayMode == retainedMode && !view.GestureRecognizers.Contains(tap),
            "Disposing the actual view removes its tap route, including a retained recognizer callback.");
    }

    private static void VerifyLegacyBinding()
    {
        StatisticsSource model = new() { Detailed = true };
        using FrameStatisticsView view = new();
        view.SetBinding(FrameStatisticsView.IsDetailedProperty,
            static (StatisticsSource state) => state.Detailed, source: model);
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Detail && view.IsDetailed,
            "An existing boolean binding continues to select Detail.");
        Tap(view.GestureRecognizers.OfType<TapGestureRecognizer>().Single(), view);
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Compact && !view.IsDetailed && !model.Detailed,
            "The newly two-way legacy binding observes Compact as false without destroying Compact.");
        view.IsDetailed = false;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal && !model.Detailed,
            "An explicit repeated CLR false assignment selects Normal from Compact.");
        model.Detailed = true;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Detail && view.IsDetailed,
            "The legacy binding is not shadowed by the mode mirror after a tap.");
        model.Detailed = false;
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal && !view.IsDetailed,
            "A later legacy source update still selects Normal.");
    }

    private static void VerifyOverlay()
    {
        Mu3DSceneView scene = new();
        ViewportControlArbiter arbiter = new();
        using ViewportOverlayManager manager = new(scene, arbiter);
        ViewportToolContext context = new(scene, arbiter, manager);
        FrameStatisticsOverlay owner = new();
        Check(owner.DisplayMode == FrameStatisticsDisplayMode.Normal && !owner.IsDetailed,
            "The library overlay retains its Normal display default.");
        StatisticsSource model = new() { Mode = FrameStatisticsDisplayMode.Compact };
        owner.SetBinding(FrameStatisticsOverlay.DisplayModeProperty,
            static (StatisticsSource state) => state.Mode, source: model);
        using IDisposable attachment = owner.Attach(context);
        Grid root = (Grid)scene.Children.Single();
        Grid presenter = (Grid)root.Children.Single();
        ViewportOverlay host = presenter.Children.OfType<ViewportOverlay>().Single();
        Border chrome = (Border)host.Content;
        FrameStatisticsView view = (FrameStatisticsView)chrome.Content!;
        Check(host.InputMode == ViewportOverlayInputMode.Interactive && !presenter.InputTransparent &&
            view.DisplayMode == FrameStatisticsDisplayMode.Compact && scene.PresentationSubscriberCount == 1 &&
            scene.SessionSubscriberCount == 1 && !view.GestureRecognizers.OfType<TapGestureRecognizer>().Any(),
            "The actual statistics attachment exposes interactive content and one frame/session subscription.");
        PointerGestureRecognizer pointer = presenter.GestureRecognizers.OfType<PointerGestureRecognizer>().Single();
        MethodInfo press = typeof(PointerGestureRecognizer).GetMethod("SendPointerPressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        Func<IElement, MauiPoint?> getPosition = _ => new MauiPoint(0d, 0d);
        press.Invoke(pointer, [presenter, getPosition, null, ButtonsMask.Primary]);
        Check(arbiter.CurrentLease?.Priority == ViewportControlPriorities.OverlayUi,
            "The interactive indicator acquires the real OverlayUi lease instead of rotating the camera.");
        TapGestureRecognizer tap = chrome.GestureRecognizers.OfType<TapGestureRecognizer>().Single();
        Tap(tap, chrome);
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Normal && owner.DisplayMode == view.DisplayMode &&
            model.Mode == owner.DisplayMode,
            "A complete-chrome tap advances exactly once through the view-to-owner-to-INPC bindings.");
        model.Mode = FrameStatisticsDisplayMode.Detail;
        Check(owner.DisplayMode == model.Mode && owner.IsDetailed && view.DisplayMode == model.Mode && view.IsDetailed,
            "A source update after an overlay tap reaches both selectors and the attachment-owned view.");
        model.Mode = FrameStatisticsDisplayMode.Compact;
        Check(owner.DisplayMode == FrameStatisticsDisplayMode.Compact && !owner.IsDetailed &&
            view.DisplayMode == FrameStatisticsDisplayMode.Compact && !view.IsDetailed,
            "Both actual mode mirrors retain Compact rather than feeding false back as Normal.");
        owner.IsDetailed = false;
        Check(owner.DisplayMode == FrameStatisticsDisplayMode.Normal && view.DisplayMode == owner.DisplayMode &&
            model.Mode == owner.DisplayMode,
            "An explicit repeated legacy CLR false assignment also updates an attached overlay to Normal.");
        model.Mode = FrameStatisticsDisplayMode.Compact;
        attachment.Dispose();
        Check(arbiter.CurrentLease is null && root.Children.Count == 0 && view.Source is null &&
            scene.PresentationSubscriberCount == 0 && scene.SessionSubscriberCount == 0 &&
            !chrome.GestureRecognizers.Contains(tap),
            "Attachment disposal releases input, frame/session observers and the borrowed source.");
        view.DisplayMode = FrameStatisticsDisplayMode.Detail;
        Check(owner.DisplayMode == FrameStatisticsDisplayMode.Compact && model.Mode == owner.DisplayMode,
            "Removing the owned mode binding prevents a disposed view from writing back to the overlay.");
        Tap(tap, chrome);
        Check(view.DisplayMode == FrameStatisticsDisplayMode.Detail,
            "A queued tap through a disposed overlay view cannot mutate display state.");
    }

    private static void Tap(TapGestureRecognizer recognizer, View view) =>
        typeof(TapGestureRecognizer).GetMethod("SendTapped", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(recognizer, [view, null]);

    private static void Lifecycle(VisualElement view, string methodName) =>
        typeof(VisualElement).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: Type.EmptyTypes, modifiers: null)!
            .Invoke(view, null);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal sealed class StatisticsSource : INotifyPropertyChanged
    {
        private FrameStatisticsDisplayMode mode;
        private bool detailed;
        public event PropertyChangedEventHandler? PropertyChanged;
        public FrameStatisticsDisplayMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
            }
        }
        public bool Detailed
        {
            get => detailed;
            set
            {
                if (detailed == value) return;
                detailed = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detailed)));
            }
        }
    }
}

internal sealed class StatisticsTestDispatcherProvider : IDispatcherProvider
{
    private readonly IDispatcher dispatcher = new StatisticsTestDispatcher();
    public IDispatcher GetForCurrentThread() => dispatcher;
}

internal sealed class StatisticsTestDispatcher : IDispatcher
{
    public bool IsDispatchRequired => false;
    public bool Dispatch(Action action) { action(); return true; }
    public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException();
    public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
}
