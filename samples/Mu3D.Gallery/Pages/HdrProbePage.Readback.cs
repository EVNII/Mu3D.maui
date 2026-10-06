using Mu3D.Graphics;

namespace Mu3D.Gallery.Pages;

public partial class HdrProbePage
{
    private CancellationTokenSource? readbackCancellation;
    private TaskCompletionSource<string>? readbackRequest;
    private string? readbackDetails;
    private string readbackFrameStatus = "not observed";
    private bool readbackAwaitingPresentation;

    private async void OnReadbackClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (!OperatingSystem.IsAndroid() || session is not { } current || !navigationActive) return;
        CancelReferenceReadback();
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));
        readbackCancellation = cancellation;
        ReadbackButton.IsEnabled = false;
        try
        {
            if (!current.TryEnableReadback())
            {
                readbackDetails = "Presentation pixel readback unavailable: surface does not support CopySource.\n";
                return;
            }
            TaskCompletionSource<string> request = new(TaskCreationOptions.RunContinuationsAsynchronously);
            readbackRequest = request;
            readbackDetails = "Presentation pixel readback: waiting for the next reference frame.\n";
            readbackFrameStatus = "not observed";
            referencePatternPresented = true;
            RefreshReport(current, "before readback");
            SurfaceView.InvalidateSurface();
            string result = await request.Task.WaitAsync(cancellation.Token);
            if (ReferenceEquals(readbackCancellation, cancellation) &&
                !cancellation.IsCancellationRequested && ReferenceEquals(session, current) && navigationActive)
                readbackDetails = result;
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(readbackCancellation, cancellation))
                readbackDetails = "Presentation pixel readback cancelled or timed out; no pixel result.\n";
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(readbackCancellation, cancellation))
                readbackDetails = $"Presentation pixel readback failed: {exception.Message}\n";
        }
        finally
        {
            if (ReferenceEquals(readbackCancellation, cancellation))
            {
                readbackCancellation = null;
                readbackRequest = null;
                ReadbackButton.IsEnabled = session is not null;
                if (ReferenceEquals(session, current) && navigationActive)
                    RefreshReport(current, "after readback");
            }
        }
    }

    private void SubmitReferenceReadback(GraphicsDevice device, GraphicsTexture target)
    {
        if (readbackRequest is not { } request || readbackCancellation is not { } cancellation) return;
        readbackRequest = null;
        readbackAwaitingPresentation = true;
        CancellationToken token = cancellation.Token;
        try
        {
            // Begin submits the copies synchronously while the control-owned target is alive.
            // Only the staging buffer is retained by its asynchronous completion.
            Task<string> pending = SurfaceReferenceReadback.Begin(device, target, token);
            _ = CompleteReferenceReadbackAsync(pending, request, token);
        }
        catch (Exception exception)
        {
            request.TrySetException(exception);
        }
    }

    private static async Task CompleteReferenceReadbackAsync(Task<string> pending,
        TaskCompletionSource<string> request, CancellationToken token)
    {
        try
        {
            string result = await pending.ConfigureAwait(false);
            if (token.IsCancellationRequested) request.TrySetCanceled(token);
            else request.TrySetResult(result);
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested) request.TrySetCanceled(token);
            else request.TrySetException(exception);
        }
    }

    private void RecordReferenceReadbackFrameStatus(string status)
    {
        if (readbackCancellation is not null && readbackAwaitingPresentation)
        {
            readbackFrameStatus = status;
            readbackAwaitingPresentation = false;
        }
    }

    private void FailReferenceReadback(Exception exception, string status)
    {
        RecordReferenceReadbackFrameStatus(status);
        readbackRequest?.TrySetException(exception);
        readbackRequest = null;
    }

    private void CancelReferenceReadback()
    {
        readbackCancellation?.Cancel();
        readbackCancellation = null;
        readbackRequest = null;
        readbackDetails = null;
        readbackAwaitingPresentation = false;
        ReadbackButton.IsEnabled = session is not null && navigationActive;
    }

    private string FormatReferenceReadback() => readbackDetails is null ? string.Empty :
        $"\nReadback frame presentation status: {readbackFrameStatus}\n{readbackDetails}";
}
