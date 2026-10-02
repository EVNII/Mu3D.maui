using Microsoft.Maui;
using Microsoft.Maui.Dispatching;
using Mu3D.GalleryApp.Pages;

internal static class FurnacePickerChecks
{
    internal static void Run()
    {
        Picker mode = new();
        foreach (string text in new[] { "Reference", "Hybrid", "Interactive", "Raster" }) mode.Items.Add(text);
        mode.SelectedIndex = 0;
        QueuedDispatcher dispatcher = new();
        int preset = 4, applied = -1, updates = 0, selectionEvents = 0;
        bool updating = false, nativeSelection = false;
        FurnaceSettingsQueue settings = new(dispatcher, () =>
        {
            if (nativeSelection) throw new Exception("Settings ran inside the native selection transaction.");
            updating = true;
            try
            {
                if (mode.SelectedIndex == 3 && preset is 4 or 5) mode.SelectedIndex = 0;
                applied = mode.SelectedIndex;
                updates++;
            }
            finally { updating = false; }
        });
        mode.SelectedIndexChanged += (_, _) =>
        {
            if (++selectionEvents > 32) throw new Exception("Furnace Picker selection feedback loop.");
            if (!updating) settings.Request();
        };
        // MAUI 10.0.90 Element maps to its handler AFTER SelectedIndexChanged. Apple's
        // MapSelectedIndex -> UpdatePicker writes the current value back through IPicker.
        // A PropertyChanged-only echo runs too early and does not reproduce this regression.
        mode.SelectedIndexChanged += (_, _) => ((IPicker)mode).SelectedIndex = mode.SelectedIndex;

        foreach (int material in new[] { 4, 5 })
        {
            preset = material;
            Select(3);
            Require(mode.SelectedIndex == 3 && dispatcher.Count == 1, "Selection must return before correction.");
            dispatcher.Drain();
            Require(mode.SelectedIndex == 0 && applied == 0 && dispatcher.Count == 0,
                "Unsupported glass/SSS Raster selection must settle on Reference.");
        }
        Require(updates == 2 && selectionEvents == 4, "Fallback applied repeatedly.");

        preset = 0;
        Select(1); Select(2); Select(3);
        Require(dispatcher.Count == 1, "Rapid choices should coalesce.");
        dispatcher.Drain();
        Require(applied == 3 && updates == 3, "Latest supported mode was lost.");

        Select(1); settings.Cancel(); // Page disappears before queued work executes.
        dispatcher.Drain();
        Require(updates == 3, "Hidden page settings were applied.");
        Select(2); settings.Cancel();
        Select(0); // Re-entry must not be canceled by the old callback.
        dispatcher.Drain();
        Require(applied == 0 && updates == 4, "Re-entry lost its new settings snapshot.");

        dispatcher.Accept = false; settings.Request();
        dispatcher.Accept = true; settings.Request(); dispatcher.Drain();
        Require(updates == 5, "Rejected dispatch left settings permanently pending.");
        Console.WriteLine("Furnace Picker: glass/SSS fallback, native echo, coalescing, cancel/re-entry and dispatch retry passed.");

        void Select(int index)
        {
            nativeSelection = true;
            try { ((IPicker)mode).SelectedIndex = index; }
            finally { nativeSelection = false; }
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private sealed class QueuedDispatcher : IDispatcher
    {
        private readonly Queue<Action> actions = new();
        internal int Count => actions.Count;
        internal bool Accept { get; set; } = true;
        public bool IsDispatchRequired => false;
        public bool Dispatch(Action action)
        {
            if (!Accept) return false;
            actions.Enqueue(action); return true;
        }
        public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException();
        public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
        internal void Drain()
        {
            int remaining = 16;
            while (actions.TryDequeue(out Action? action))
            {
                if (--remaining < 0) throw new Exception("Unbounded settings dispatch.");
                action();
            }
        }
    }
}
