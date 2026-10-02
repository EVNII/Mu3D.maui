using Microsoft.Maui.Dispatching;

namespace Mu3D.GalleryApp.Pages;

// Apple Picker maps its value back after SelectedIndexChanged. Defer corrections until that
// transaction has returned, and coalesce slider/selection changes into one settings snapshot.
internal sealed class FurnaceSettingsQueue(IDispatcher dispatcher, Action apply)
{
    private bool pending;
    private int generation;

    internal void Request()
    {
        if (pending) return;
        pending = true;
        int requested = generation;
        if (!dispatcher.Dispatch(() =>
        {
            if (requested != generation) return;
            pending = false;
            apply();
        })) pending = false;
    }

    internal void Cancel() { generation++; pending = false; }
}
