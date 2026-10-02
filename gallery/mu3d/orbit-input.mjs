/**
 * Opt-in browser input adapter for Mu3D.Toolkit.Controls.OrbitController.
 * onInput({rotateX,rotateY,dolly}) receives radians and logarithmic dolly, not raw events.
 * Owns primary-pointer capture and plain wheel/arrow/+/- input on this Canvas only.
 * Ctrl-wheel (browser pinch/page zoom) is left to the browser. Dispose releases capture and styles.
 * dragThreshold is a non-negative CSS-pixel distance from pointerdown (default 0). Camera
 * movement starts only after exceeding it; the first command retains the full displacement.
 * Set it to the completed-tap tolerance when sharing this Canvas with selection. Actual
 * coalesced positions preserve excursions/backtracking; stationary input emits no command.
 */
export function attachOrbitInput(canvas, onInput, {isEnabled = () => true, dragThreshold = 0} = {}) {
  if (!Number.isFinite(dragThreshold) || dragThreshold < 0)
    throw new TypeError('dragThreshold must be a finite non-negative CSS-pixel distance.');
  const events = new AbortController(), options = {signal: events.signal};
  const previousTouchAction = canvas.style.touchAction;
  let pointer = null;
  canvas.style.touchAction = 'none';
  const emit = (rotateX = 0, rotateY = 0, dolly = 0) => {
    if (rotateX !== 0 || rotateY !== 0 || dolly !== 0) onInput({rotateX, rotateY, dolly});
  };
  function release() {
    const previous = pointer;
    pointer = null;
    if (previous && canvas.hasPointerCapture(previous.id)) canvas.releasePointerCapture(previous.id);
  }
  canvas.addEventListener('pointerdown', event => {
    if (event.defaultPrevented || !isEnabled() || pointer || !event.isPrimary || event.button !== 0) return;
    pointer = {id: event.pointerId, x: event.clientX, y: event.clientY,
      startX: event.clientX, startY: event.clientY, dragging: false};
    canvas.setPointerCapture(event.pointerId);
    canvas.focus({preventScroll: true});
    event.preventDefault();
  }, options);
  canvas.addEventListener('pointermove', event => {
    if (!isEnabled()) { release(); return; }
    if (pointer?.id !== event.pointerId) return;
    const rect = canvas.getBoundingClientRect();
    function move(sample) {
      if (!pointer || !Number.isFinite(sample.clientX) || !Number.isFinite(sample.clientY)) return;
      if (!pointer.dragging) {
        if (Math.hypot(sample.clientX - pointer.startX, sample.clientY - pointer.startY) <= dragThreshold) return;
        pointer.dragging = true;
      }
      const x = sample.clientX - pointer.x, y = sample.clientY - pointer.y;
      pointer.x = sample.clientX; pointer.y = sample.clientY;
      if (rect.width > 0 && rect.height > 0)
        emit(-x / rect.width * Math.PI * 2, -y / rect.height * Math.PI * 2);
    }
    for (const sample of event.getCoalescedEvents?.() ?? []) move(sample);
    move(event);
  }, options);
  const endPointer = event => { if (pointer?.id === event.pointerId) release(); };
  canvas.addEventListener('pointerup', endPointer, options);
  canvas.addEventListener('pointercancel', endPointer, options);
  canvas.addEventListener('lostpointercapture', event => {
    if (pointer?.id === event.pointerId) pointer = null;
  }, options);
  window.addEventListener('blur', release, options);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') release();
  }, options);
  canvas.addEventListener('wheel', event => {
    if (!isEnabled() || event.defaultPrevented || event.ctrlKey || event.metaKey) return;
    event.preventDefault();
    const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? canvas.clientHeight : 1;
    emit(0, 0, Math.max(-0.5, Math.min(0.5, -event.deltaY * unit * 0.001)));
  }, {...options, passive: false});
  canvas.addEventListener('keydown', event => {
    if (!isEnabled() || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey) return;
    const command = {ArrowLeft: [-0.1, 0, 0], ArrowRight: [0.1, 0, 0],
      ArrowUp: [0, -0.1, 0], ArrowDown: [0, 0.1, 0], '+': [0, 0, 0.1],
      '=': [0, 0, 0.1], '-': [0, 0, -0.1]}[event.key];
    if (command) { event.preventDefault(); emit(...command); }
  }, options);
  return {cancel: release, dispose() { release(); events.abort(); canvas.style.touchAction = previousTouchAction; }};
}
