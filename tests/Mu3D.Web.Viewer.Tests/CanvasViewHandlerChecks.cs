using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Web;
using static ViewerTestChecks;

internal static class CanvasViewHandlerChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        check(Throws<ArgumentNullException>(() => _ = new CanvasViewHandler(null!)),
            "A Canvas handler requires an explicit presentation boundary.");
        using (FakeSession session = new())
        using (CanvasViewHandler handler = new(session))
        {
            check(ReferenceEquals(handler.PresentationSession, session), "The handler exposes its borrowed session.");
            PresentationSurfaceFrameStatus result = handler.RenderAndPresent(320, 180, target =>
            {
                session.Operations.Add("draw");
                check(target.Descriptor.Size.Width == 320 && target.Descriptor.Size.Height == 180 && ReferenceEquals(target.Device, session.Device),
                    "Draw receives the current acquired texture and compatible device after physical resize.");
            });
            check(result == PresentationSurfaceFrameStatus.PresentedOptimal &&
                session.Operations.SequenceEqual(["resize", "acquire", "draw", "release"]),
                "Physical resize precedes acquisition and drawing, with the backend result preserved.");
            handler.RenderAndPresent(320, 180, _ => { });
            check(session.ResizeCount == 1, "An unchanged physical extent skips redundant resize.");
            handler.RenderAndPresent(320, 180, _ => { }, refreshSurface: true);
            check(session.ResizeCount == 2, "Same-size display/resume refresh still reconfigures the surface.");
            session.OnResize = () => check(Throws<InvalidOperationException>(() =>
                handler.RenderAndPresent(320, 180, _ => { })), "Resize callbacks cannot recursively start another frame.");
            handler.RenderAndPresent(320, 180, _ => { }, refreshSurface: true);
            session.OnResize = null;
            session.FailNextResize = true;
            int framesBeforeRefreshFailure = session.FrameCount;
            check(Throws<ProbeException>(() => handler.RenderAndPresent(320, 180, _ => { }, refreshSurface: true)) &&
                session.FrameCount == framesBeforeRefreshFailure, "A failed same-size refresh cannot acquire an outdated surface.");
            int failedRefreshResizeCount = session.ResizeCount;
            handler.RenderAndPresent(320, 180, _ => { });
            check(session.ResizeCount == failedRefreshResizeCount + 1 && session.FrameCount == framesBeforeRefreshFailure + 1,
                "A failed refresh remains pending and retries resize on the next ordinary same-size frame.");
            int draws = session.FrameCount;
            check(Throws<ArgumentOutOfRangeException>(() => handler.RenderAndPresent(0, 180, _ => { })) &&
                Throws<ArgumentOutOfRangeException>(() => handler.RenderAndPresent(320, 0, _ => { })) &&
                Throws<ArgumentNullException>(() => handler.RenderAndPresent(320, 180, null!)) &&
                session.FrameCount == draws, "Invalid dimensions or callbacks never enter the presentation session.");
            session.FailNextResize = true;
            check(Throws<ProbeException>(() => handler.RenderAndPresent(640, 360, _ => { })) &&
                session.Width == 320 && session.FrameCount == draws, "Resize failure prevents acquisition.");
            handler.RenderAndPresent(640, 360, _ => { });
            check(session.Width == 640 && session.FrameCount == draws + 1, "Resize failure releases the frame guard for recovery.");
            check(Throws<ProbeException>(() => handler.RenderAndPresent(640, 360, _ => throw new ProbeException())),
                "A real drawing failure propagates to the caller.");
            int framesBeforeReentry = session.FrameCount, acquisitionsBeforeReentry = session.AcquireCount;
            handler.RenderAndPresent(640, 360, _ =>
            {
                int activeFrames = session.FrameCount, activeAcquisitions = session.AcquireCount;
                check(Throws<InvalidOperationException>(() => handler.RenderAndPresent(640, 360, _ => { })),
                    "A draw callback cannot recursively acquire another frame.");
                check(session.FrameCount == activeFrames && session.AcquireCount == activeAcquisitions,
                    "Rejected reentry never reaches backend frame or acquisition operations.");
            });
            check(session.FrameCount == framesBeforeReentry + 1 && session.AcquireCount == acquisitionsBeforeReentry + 1,
                "Recovery after a drawing failure completes exactly one new frame and acquisition.");
            foreach (PresentationSurfaceFrameStatus status in Enum.GetValues<PresentationSurfaceFrameStatus>())
            {
                session.Status = status;
                int callbacks = 0;
                check(handler.RenderAndPresent(640, 360, _ => callbacks++) == status &&
                    callbacks == (status is PresentationSurfaceFrameStatus.PresentedOptimal or
                        PresentationSurfaceFrameStatus.PresentedSuboptimal ? 1 : 0),
                    $"The handler preserves {status} without inventing a retry or successful draw.");
            }
        }

        foreach (bool disposeDuringResize in new[] { false, true })
        {
            using FakeSession session = new();
            CanvasViewHandler handler = new(session);
            bool disposedInside = false;
            void Stop()
            {
                handler.Dispose(); handler.Dispose();
                disposedInside = true;
                check(ReferenceEquals(handler.PresentationSession, session),
                    "Disposal retains the borrowed boundary until the active synchronous frame unwinds.");
                check(Throws<ObjectDisposedException>(() => handler.RenderAndPresent(80, 60, _ => { })),
                    "Stopping immediately rejects further frame requests.");
            }
            if (disposeDuringResize) session.OnResize = Stop;
            int callbacks = 0;
            PresentationSurfaceFrameStatus result = handler.RenderAndPresent(80, 60, _ =>
            {
                callbacks++;
                if (!disposeDuringResize) Stop();
            });
            check(disposedInside && callbacks == 1 && result == PresentationSurfaceFrameStatus.PresentedOptimal &&
                handler.PresentationSession is null && session.DisposeCount == 0,
                "Disposal during resize or draw lets the current frame finish, then clears only the borrowed reference.");
            handler.Dispose();
            using GraphicsTexture stillUsable = session.Device.CreateTexture(new(new(1, 1), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment));
            check(session.DisposeCount == 0 && stillUsable.Descriptor.Size.Width == 1,
                "Repeated handler disposal never releases the application's session or device.");
        }

        using (FakeSession session = new())
        {
            CanvasViewHandler handler = new(session);
            check(Throws<ProbeException>(() => handler.RenderAndPresent(40, 30, _ =>
            {
                handler.Dispose();
                throw new ProbeException();
            })) && handler.PresentationSession is null && session.DisposeCount == 0,
                "A throw after reentrant disposal still clears the borrowed reference without owning backend cleanup.");
            handler.Dispose();
        }
        using (FakeSession session = new())
        {
            CanvasViewHandler handler = new(session);
            handler.Dispose(); handler.Dispose();
            check(handler.PresentationSession is null && session.DisposeCount == 0 &&
                Throws<ObjectDisposedException>(() => handler.RenderAndPresent(1, 1, _ => { })),
                "Idle disposal is idempotent and stops frames without disposing borrowed resources.");
        }
    }

    private sealed class ProbeException : Exception { }

    private sealed class FakeSession : IPresentationSurfaceSession
    {
        private readonly RecordingGraphicsDevice device = new();
        internal List<string> Operations { get; } = [];
        internal int ResizeCount { get; private set; }
        internal int FrameCount { get; private set; }
        internal int AcquireCount { get; private set; }
        internal int DisposeCount { get; private set; }
        internal bool FailNextResize { get; set; }
        internal Action? OnResize { get; set; }
        internal PresentationSurfaceFrameStatus Status { get; set; } = PresentationSurfaceFrameStatus.PresentedOptimal;
        public GraphicsDevice Device => device;
        public uint Width { get; private set; } = 1;
        public uint Height { get; private set; } = 1;
        public SurfaceCapabilities Capabilities { get; } = new([PresentationFormat.Rgba16Float],
            [SurfacePresentMode.Fifo], [SurfaceAlphaMode.Opaque], true, []);
        public SurfaceOutputPlan OutputPlan { get; } = new(new(PresentationFormat.Rgba16Float,
            OutputDynamicRange.Hdr, ColorEncoding.ExtendedSrgb, null, null), false);
        public PresentationSurfaceFrameTimings LastFrameTimings => default;
        public void Resize(uint width, uint height)
        {
            ResizeCount++; Operations.Add("resize");
            if (FailNextResize) { FailNextResize = false; throw new ProbeException(); }
            OnResize?.Invoke();
            Width = width; Height = height;
        }
        public PresentationSurfaceFrameStatus RenderAndPresent(Action<GraphicsTexture> render)
        {
            FrameCount++;
            if (Status is not (PresentationSurfaceFrameStatus.PresentedOptimal or PresentationSurfaceFrameStatus.PresentedSuboptimal))
                return Status;
            using GraphicsTexture target = device.CreateTexture(new(new(Width, Height), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment));
            AcquireCount++;
            Operations.Add("acquire");
            try { render(target); return Status; }
            finally { Operations.Add("release"); }
        }
        public void Dispose() { DisposeCount++; device.Dispose(); }
    }
}
