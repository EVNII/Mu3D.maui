/**
 * Optional raw Pointer Events adapter. Register before camera adapters.
 * acceptContact(sample) synchronously claims a primary contact; false leaves it to the host.
 * onSamples(samples) receives actual (never predicted) samples, including unclaimed hover/moves.
 * Positions are Canvas-relative u/v and physical x/y, without clamping during capture.
 * Device values are browser reports, not proof of hardware capability (defaults may be synthetic).
 * Missing numeric fields are null. No brush, selection, undo or pen-button policy is imposed.
 * The host supplies touch-action CSS and cancels before changing its coordinate mapping.
 */
export function attachPointerInput(canvas, {acceptContact = () => false, onSamples}) {
  const events = new AbortController(), options = {signal: events.signal};
  let active = null, disposed = false;
  const number = value => Number.isFinite(value) ? value : null;
  function sample(event, phase, parent = event) {
    const rect = canvas.getBoundingClientRect();
    if (!(rect.width > 0 && rect.height > 0) ||
        !Number.isFinite(event.clientX) || !Number.isFinite(event.clientY)) return null;
    const u = (event.clientX - rect.left) / rect.width, v = (event.clientY - rect.top) / rect.height;
    const pointerType = event.pointerType || parent.pointerType || '';
    const buttons = number(event.buttons), button = number(event.button);
    return {pointerId: event.pointerId ?? parent.pointerId, pointerType,
      isPrimary: event.isPrimary ?? parent.isPrimary, phase, timeStamp: number(event.timeStamp),
      u, v, x: u * canvas.width, y: v * canvas.height,
      pressure: number(event.pressure), tangentialPressure: number(event.tangentialPressure),
      tiltX: number(event.tiltX), tiltY: number(event.tiltY), twist: number(event.twist),
      altitudeAngle: number(event.altitudeAngle), azimuthAngle: number(event.azimuthAngle),
      buttons, button, eraser: pointerType === 'pen' && ((buttons & 32) !== 0 || button === 5),
      barrel: pointerType === 'pen' && (buttons & 2) !== 0, captured: false};
  }
  function release() {
    const previous = active;
    active = null; // lostpointercapture can be synchronous in a host.
    if (previous && canvas.hasPointerCapture(previous.pointerId)) canvas.releasePointerCapture(previous.pointerId);
    return previous;
  }
  function cancel() {
    const previous = release();
    if (previous) onSamples([{...previous, phase: 'cancel', captured: true}]);
  }
  canvas.addEventListener('pointerdown', event => {
    const value = sample(event, 'down');
    if (!value) return;
    // While claimed, consume other contacts too so a second finger cannot start camera movement.
    if (active) { event.preventDefault(); return; }
    if (!event.defaultPrevented && event.isPrimary && acceptContact(value)) {
      active = {...value, captured: true};
      try { canvas.setPointerCapture(event.pointerId); }
      catch (error) { cancel(); throw error; }
      value.captured = true;
      canvas.focus({preventScroll: true});
      event.preventDefault();
    }
    onSamples([value]);
  }, options);
  canvas.addEventListener('pointermove', event => {
    if (active && active.pointerId !== event.pointerId) return;
    const coalesced = event.getCoalescedEvents?.() ?? [];
    const values = (coalesced.length ? coalesced : [event])
      .map(item => sample(item, event.buttons ? 'move' : 'hover', event)).filter(Boolean);
    if (!values.length) return;
    if (active) {
      for (const value of values) value.captured = true;
      active = values.at(-1);
      event.preventDefault();
    }
    onSamples(values);
  }, options);
  canvas.addEventListener('pointerup', event => {
    if (active && active.pointerId !== event.pointerId) return;
    const value = sample(event, 'up');
    if (active) {
      const previous = release();
      event.preventDefault();
      onSamples([{...(value ?? previous), phase: 'up', captured: true}]);
    } else if (value) onSamples([value]);
  }, options);
  for (const name of ['pointercancel', 'lostpointercapture'])
    canvas.addEventListener(name, event => { if (active?.pointerId === event.pointerId) cancel(); }, options);
  canvas.addEventListener('keydown', event => {
    if (event.key === 'Escape' && active) { event.preventDefault(); cancel(); }
  }, options);
  window.addEventListener('blur', cancel, options);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') cancel();
  }, options);
  return {cancel, get isActive() { return active !== null; },
    dispose() { if (!disposed) { disposed = true; try { cancel(); } finally { events.abort(); } } }};
}
