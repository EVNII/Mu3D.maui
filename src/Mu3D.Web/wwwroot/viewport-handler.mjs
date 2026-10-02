/**
 * Optional browser host. Owns RAF/visibility/physical sizing, never the GPU or scene.
 * render({width,height,cssWidth,cssHeight,dpr,sizing,deltaSeconds,frameIntervalSeconds,refreshSurface})
 * may return a promise; at most one render is in flight. onError receives terminal frame errors.
 * invalidate() requests one frame; setContinuous(true) opts into visible RAF animation.
 * dispose() removes observers/listeners; the caller disposes its renderer after in-flight work.
 */
export function createViewportHandler({canvas, render, onError, maxDimension, continuous = false}) {
  const events = new AbortController();
  let disposed = false, running = false, visible = false, pending = true, refresh = true;
  let zeroSize = false;
  let raf = 0, previousTime = 0, deviceBox, boxDpr, dprMedia;
  const active = () => !disposed && visible && document.visibilityState !== 'hidden';
  function schedule() {
    if (active() && !zeroSize && !running && !raf && (pending || continuous)) raf = requestAnimationFrame(tick);
  }
  function invalidate(refreshSurface = false) {
    if (!continuous && !running && !raf) previousTime = 0;
    pending = true;
    zeroSize = false;
    refresh ||= refreshSurface;
    schedule();
  }
  function measure() {
    const rect = canvas.getBoundingClientRect(), dpr = devicePixelRatio || 1;
    const exact = deviceBox && boxDpr === dpr;
    return {width: exact ? deviceBox.inlineSize : Math.round(rect.width * dpr),
      height: exact ? deviceBox.blockSize : Math.round(rect.height * dpr),
      cssWidth: rect.width, cssHeight: rect.height, dpr,
      sizing: exact ? 'device-pixel-content-box' : 'css-times-dpr'};
  }
  async function tick(time) {
    raf = 0;
    if (!active()) { previousTime = 0; return; }
    running = true;
    pending = false;
    try {
      const size = measure();
      if (size.width < 1 || size.height < 1) { previousTime = 0; zeroSize = true; return; }
      if (size.width > maxDimension || size.height > maxDimension)
        throw new Error(`Native Canvas size ${size.width}×${size.height} exceeds GPU limit ${maxDimension}.`);
      const interval = previousTime ? Math.max(0, (time - previousTime) / 1000) : 0;
      previousTime = time;
      const refreshSurface = refresh;
      refresh = false;
      await render({...size, deltaSeconds: Math.min(interval, 0.1), frameIntervalSeconds: interval, refreshSurface});
    } catch (error) {
      continuous = false;
      // An explicit invalidation received during this frame still requests a retry.
      previousTime = 0;
      onError(error);
    } finally {
      running = false;
      schedule();
    }
  }
  function watchDpr() {
    dprMedia?.removeEventListener('change', onDpr);
    dprMedia = matchMedia(`(resolution: ${devicePixelRatio || 1}dppx)`);
    dprMedia.addEventListener('change', onDpr);
  }
  function onDpr() { deviceBox = null; watchDpr(); invalidate(true); }
  function visibilityChanged() {
    previousTime = 0;
    if (!active()) { cancelAnimationFrame(raf); raf = 0; }
    else invalidate(true);
  }
  const resize = new ResizeObserver(entries => {
    const entry = entries.find(value => value.target === canvas);
    deviceBox = entry?.devicePixelContentBoxSize?.[0];
    boxDpr = devicePixelRatio || 1;
    invalidate();
  });
  try { resize.observe(canvas, {box: 'device-pixel-content-box'}); }
  catch { resize.observe(canvas); }
  const intersection = new IntersectionObserver(entries => {
    visible = entries.some(entry => entry.target === canvas && entry.isIntersecting);
    visibilityChanged();
  });
  intersection.observe(canvas);
  watchDpr();
  document.addEventListener('visibilitychange', visibilityChanged, {signal: events.signal});
  window.addEventListener('focus', () => invalidate(true), {signal: events.signal});
  matchMedia('(dynamic-range: high)').addEventListener('change', () => invalidate(true), {signal: events.signal});
  return {
    invalidate,
    setContinuous(value) {
      continuous = Boolean(value);
      previousTime = 0;
      if (!continuous && !pending) { cancelAnimationFrame(raf); raf = 0; }
      schedule();
    },
    dispose() {
      disposed = true;
      cancelAnimationFrame(raf);
      events.abort();
      resize.disconnect();
      intersection.disconnect();
      dprMedia?.removeEventListener('change', onDpr);
    },
  };
}

/**
 * Connects explicit managed callbacks to the existing Canvas lifecycle/scheduler.
 * callbacks: connect(properties), resize(frame), setProperties(properties), draw(frame, properties),
 * disconnect(). Only draw is required; callbacks may be asynchronous and choose their own interop.
 * Properties are shallow immutable snapshots, coalesced before the next draw. No export names,
 * scene mathematics, GPU/backend choice, JSON encoding or automatic display fallback are supplied.
 * A failed connect disconnects once and closes this handler. Other frame failures stop continuous
 * scheduling and can recover on an explicit invalidation, as with createViewportHandler.
 * dispose() detaches immediately, suppresses late publication, and returns the same promise until
 * all active callbacks (including onFrame) finish and disconnect completes. Callbacks must not
 * await disposal of their own handler. The caller owns borrowed Canvas, scene and input bindings.
 */
export function createManagedViewportHandler({canvas, callbacks, properties: initialProperties = {},
  onFrame, onError, maxDimension, continuous = false}) {
  if (!callbacks || typeof callbacks.draw !== 'function')
    throw new TypeError('Explicit managed draw callbacks are required.');
  for (const name of ['connect', 'resize', 'setProperties', 'disconnect'])
    if (callbacks[name] !== undefined && typeof callbacks[name] !== 'function')
      throw new TypeError(`The managed ${name} callback must be a function.`);
  for (const [name, callback] of [['onFrame', onFrame], ['onError', onError]])
    if (callback !== undefined && typeof callback !== 'function')
      throw new TypeError(`${name} must be a function.`);
  function snapshot(value) {
    if (!value || typeof value !== 'object' || Array.isArray(value))
      throw new TypeError('Managed properties must be an object.');
    return Object.freeze({...value});
  }
  let properties = snapshot(initialProperties), version = 0, appliedVersion = -1;
  let connected = false, disposed = false, failedConnect = false, disposeRequested = false, sizeKey = '';
  let flight, disposePromise, disconnectPromise;
  const disconnectOnce = () => disconnectPromise ??= Promise.resolve().then(() => callbacks.disconnect?.());

  async function draw(frame) {
    if (disposed) return;
    const current = properties, currentVersion = version;
    if (!connected) {
      try {
        await callbacks.connect?.(current);
        connected = true;
      } catch (error) {
        failedConnect = true;
        disposed = true;
        viewport.dispose();
        try { await disconnectOnce(); }
        catch (cleanupError) {
          throw new AggregateError([error, cleanupError], 'Managed connection and cleanup failed.');
        }
        throw error;
      }
    }
    if (disposed) return;
    const nextSize = [frame.width, frame.height, frame.cssWidth, frame.cssHeight, frame.dpr].join(':');
    if (frame.refreshSurface || nextSize !== sizeKey) {
      // A failed same-size reconfiguration must be retried on the next explicit invalidation.
      sizeKey = '';
      await callbacks.resize?.(frame);
      sizeKey = nextSize;
    }
    if (disposed) return;
    if (currentVersion !== appliedVersion) {
      await callbacks.setProperties?.(current);
      appliedVersion = currentVersion;
    }
    if (disposed) return;
    const result = await callbacks.draw(frame, current);
    if (!disposed) await onFrame?.(result, frame, current);
  }

  const viewport = createViewportHandler({canvas, maxDimension, continuous,
    render(frame) {
      // Assign the active promise before invoking callbacks, including synchronous reentrancy.
      const current = Promise.resolve().then(() => draw(frame));
      flight = current;
      return current.finally(() => { if (flight === current) flight = undefined; });
    },
    onError(error) {
      if (!disposed || (failedConnect && !disposeRequested)) onError?.(error);
    },
  });

  return {
    get properties() { return properties; },
    get isDisposed() { return disposed; },
    setProperties(patch) {
      if (disposed) throw new Error('The managed viewport handler is disposed.');
      const changes = snapshot(patch);
      if (!Object.keys(changes).some(key => !Object.hasOwn(properties, key) || !Object.is(properties[key], changes[key])))
        return false;
      properties = Object.freeze({...properties, ...changes});
      version++;
      viewport.invalidate();
      return true;
    },
    invalidate(refreshSurface = false) { if (!disposed) viewport.invalidate(refreshSurface); },
    setContinuous(value) { if (!disposed) viewport.setContinuous(value); },
    dispose() {
      disposeRequested = true;
      if (!disposed) { disposed = true; viewport.dispose(); }
      // Defer even when idle so reentrant disconnect cannot create a second teardown promise.
      return disposePromise ??= Promise.resolve().then(async () => {
        if (flight) { try { await flight; } catch {} }
        await disconnectOnce();
      });
    },
  };
}
