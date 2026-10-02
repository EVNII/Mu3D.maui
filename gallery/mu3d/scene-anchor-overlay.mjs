/**
 * Opt-in HTML overlays driven by a host's submitted-frame projection batch, without a second clock.
 * container must share the Canvas content bounds and establish a CSS positioning context.
 * update({FrameId, Width, Height, Points:[{Id, Projected, InsideViewport, X, Y, Depth}]})
 * accepts logical, top-left-origin coordinates from Mu3D ViewportFrameSnapshot.TryProject.
 * The host owns node IDs, local anchor points, rendering and content. No depth occlusion is guessed.
 */
export function createSceneAnchorOverlay({container, onInteraction = () => {}}) {
  const document = container.ownerDocument, window = document.defaultView;
  const layer = document.createElement('div'), events = new AbortController();
  const options = {signal: events.signal}, entries = new Map(), pointers = new Map();
  Object.assign(layer.style, {position: 'absolute', inset: '0', overflow: 'hidden', pointerEvents: 'none'});
  layer.dataset.mu3dOverlay = '';
  container.appendChild(layer);
  let disposed = false, enabled = true, frame = null, interacting = false;
  function interactionChanged() {
    const next = !disposed && (pointers.size > 0 || [...entries.values()].some(entry =>
      entry.interactive && !entry.wrapper.hidden && entry.wrapper.contains(document.activeElement)));
    if (next !== interacting) { interacting = next; onInteraction(next); }
  }
  function hide(entry) {
    if (entry.wrapper.contains(document.activeElement)) document.activeElement.blur();
    for (const [id, owner] of pointers) if (owner === entry) pointers.delete(id);
    entry.wrapper.hidden = true;
    entry.wrapper.style.display = 'none';
    entry.wrapper.inert = true;
  }
  function place(entry) {
    const point = frame?.points.get(entry.anchor);
    if (!enabled || !point?.Projected || (entry.hideOutside && !point.InsideViewport)) {
      hide(entry); return;
    }
    entry.wrapper.style.left = `calc(${point.X / frame.Width * 100}% + ${entry.offset[0]}px)`;
    entry.wrapper.style.top = `calc(${point.Y / frame.Height * 100}% + ${entry.offset[1]}px)`;
    entry.wrapper.hidden = false;
    entry.wrapper.style.display = 'block';
    // Pass-through entries are decorative. Interactive entries retain native focus/accessibility.
    entry.wrapper.inert = !entry.interactive;
    entry.wrapper.dataset.frameId = String(frame.FrameId);
  }
  function refresh() { for (const entry of entries.values()) place(entry); interactionChanged(); }
  const ownerOf = target => [...entries.values()].find(entry => entry.interactive &&
    !entry.wrapper.hidden && entry.wrapper.contains(target));
  layer.addEventListener('pointerdown', event => {
    const owner = ownerOf(event.target);
    if (owner) { pointers.set(event.pointerId, owner); interactionChanged(); }
  }, {...options, capture: true});
  // Preserve the controls' default actions, but don't bubble their commands into viewport tools.
  for (const name of ['pointerdown', 'pointermove', 'pointerup', 'click', 'dblclick', 'keydown', 'keyup', 'wheel'])
    layer.addEventListener(name, event => event.stopPropagation(), options);
  for (const name of ['pointerup', 'pointercancel'])
    window.addEventListener(name, event => { pointers.delete(event.pointerId); interactionChanged(); }, {...options, capture: true});
  layer.addEventListener('focusin', interactionChanged, options);
  layer.addEventListener('focusout', () => queueMicrotask(() => { if (!disposed) interactionChanged(); }), options);
  window.addEventListener('blur', () => { pointers.clear(); interactionChanged(); }, options);
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') clear();
  }, options);
  function clear() { frame = null; pointers.clear(); refresh(); }
  function requireLive() { if (disposed) throw new Error('The scene anchor overlay is disposed.'); }
  function requireAnchor(anchor) {
    if (typeof anchor !== 'string' || !anchor) throw new TypeError('A nonempty host anchor ID is required.');
  }
  return {
    get isInteracting() { return interacting; },
    get hasActivePointer() { return pointers.size > 0; },
    /** Borrow an existing element. Dispose restores its original DOM slot and preserves its styles/listeners. */
    bind(element, {anchor, offset = [8, -8], pivot = [0, 1], interactive = false, hideWhenOutsideViewport = true}) {
      requireLive(); requireAnchor(anchor);
      if (entries.has(element) || element === container || element.contains(container))
        throw new Error('An overlay element must have a single non-cyclic binding.');
      if (![offset, pivot].every(pair => pair.length === 2 && pair.every(Number.isFinite)))
        throw new TypeError('Offset and pivot require two finite coordinates.');
      const placeholder = document.createComment('Mu3D overlay return position');
      const wrapper = document.createElement('div');
      Object.assign(wrapper.style, {position: 'absolute', width: 'max-content',
        transform: `translate(${-pivot[0] * 100}%, ${-pivot[1] * 100}%)`,
        pointerEvents: interactive ? 'auto' : 'none'});
      wrapper.dataset.mu3dAnchor = anchor;
      try {
        if (element.parentNode) element.parentNode.insertBefore(placeholder, element);
        wrapper.appendChild(element); layer.appendChild(wrapper);
      } catch (error) {
        if (element.parentNode === wrapper) {
          if (placeholder.parentNode) placeholder.parentNode.replaceChild(element, placeholder);
          else element.remove();
        }
        placeholder.remove(); wrapper.remove(); throw error;
      }
      const entry = {anchor, wrapper, placeholder, interactive, offset: [...offset], pivot: [...pivot], hideOutside: hideWhenOutsideViewport};
      entries.set(element, entry); place(entry);
      let detached = false;
      const binding = {
        setAnchor(value) {
          requireLive(); requireAnchor(value);
          if (detached) throw new Error('The anchor binding is disposed.');
          entry.anchor = value; wrapper.dataset.mu3dAnchor = value; place(entry); interactionChanged();
        },
        /** Update logical placement/input options atomically without reparenting the borrowed element. */
        setOptions(value) {
          requireLive();
          if (detached) throw new Error('The anchor binding is disposed.');
          const offset = value.offset ?? entry.offset, pivot = value.pivot ?? entry.pivot;
          const interactive = value.interactive ?? entry.interactive;
          const outside = value.hideWhenOutsideViewport ?? entry.hideOutside;
          if (![offset, pivot].every(pair => Array.isArray(pair) && pair.length === 2 && pair.every(Number.isFinite)) ||
              typeof interactive !== 'boolean' || typeof outside !== 'boolean')
            throw new TypeError('Anchor options require finite coordinate pairs and boolean flags.');
          if (entry.interactive && !interactive) hide(entry);
          entry.offset = [...offset]; entry.pivot = [...pivot]; entry.interactive = interactive; entry.hideOutside = outside;
          wrapper.style.transform = `translate(${-pivot[0] * 100}%, ${-pivot[1] * 100}%)`;
          wrapper.style.pointerEvents = interactive ? 'auto' : 'none';
          place(entry); interactionChanged();
        },
        dispose() {
          if (detached) return;
          detached = true; hide(entry); entries.delete(element);
          if (element.parentNode === wrapper) {
            if (placeholder.parentNode) placeholder.parentNode.replaceChild(element, placeholder);
            else element.remove();
          }
          placeholder.remove(); wrapper.remove(); interactionChanged();
        },
      };
      entry.dispose = binding.dispose;
      return binding;
    },
    /** Apply one complete frame atomically. Older batches are ignored; malformed batches are rejected. */
    update(value) {
      requireLive();
      if (!Number.isSafeInteger(value.FrameId) || value.FrameId < 0 ||
          !Number.isFinite(value.Width) || value.Width <= 0 || !Number.isFinite(value.Height) || value.Height <= 0)
        throw new TypeError('A projection batch requires a frame ID and positive logical extents.');
      if (frame && value.FrameId < frame.FrameId) return false;
      const points = new Map();
      for (const point of value.Points) {
        requireAnchor(point.Id);
        if (points.has(point.Id) || typeof point.Projected !== 'boolean' || typeof point.InsideViewport !== 'boolean' ||
            (point.Projected && ![point.X, point.Y, point.Depth].every(Number.isFinite)))
          throw new TypeError('Projection points must be unique and finite when projected.');
        points.set(point.Id, {...point});
      }
      frame = {FrameId: value.FrameId, Width: value.Width, Height: value.Height, points};
      refresh(); return true;
    },
    setEnabled(value) { requireLive(); enabled = Boolean(value); refresh(); },
    clear,
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const entry of [...entries.values()]) entry.dispose();
      events.abort(); layer.remove(); frame = null; pointers.clear(); interactionChanged();
    },
  };
}
