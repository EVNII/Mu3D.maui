/**
 * Optional observable selection and completed-tap adapters. Geometry stays in the host's shared
 * SceneRaycaster. Selection is application state: no camera, Gizmo, undo or rendering is owned.
 */
const sources = new WeakSet();
function callback(value, name) {
  if (typeof value !== 'function') throw new TypeError(`${name} must be a function.`);
  return value;
}
function boolean(value, name) {
  if (typeof value !== 'boolean') throw new TypeError(`${name} must be a boolean.`);
  return value;
}
function mask(value) {
  if (!Number.isInteger(value) || value < 0 || value > 0xffffffff)
    throw new TypeError('selectionMask must be an unsigned 32-bit integer.');
  return value;
}
function position(value) {
  if (!value || !Number.isFinite(value.x) || !Number.isFinite(value.y))
    throw new TypeError('A finite physical-pixel position {x, y} is required.');
  return Object.freeze({x: value.x, y: value.y});
}
function evidence(value, ancestors = new Set()) {
  if (value === null || typeof value !== 'object') {
    if (typeof value === 'number' && !Number.isFinite(value))
      throw new TypeError('Hit evidence must contain finite numbers.');
    if (['function', 'symbol', 'bigint', 'undefined'].includes(typeof value))
      throw new TypeError('Hit evidence must contain data values.');
    return value;
  }
  if (ancestors.has(value)) throw new TypeError('Hit evidence must not contain cycles.');
  if (!Array.isArray(value) && Object.getPrototypeOf(value) !== Object.prototype &&
      Object.getPrototypeOf(value) !== null)
    throw new TypeError('Hit evidence must contain plain objects or arrays.');
  ancestors.add(value);
  const copy = Array.isArray(value) ? value.map(item => evidence(item, ancestors))
    : Object.fromEntries(Object.entries(value).map(([key, item]) => [key, evidence(item, ancestors)]));
  ancestors.delete(value);
  return Object.freeze(copy);
}
function subscriptions() {
  const entries = new Set();
  return {
    add(listener) {
      callback(listener, 'listener');
      const entry = value => listener(value);
      entries.add(entry);
      return () => entries.delete(entry);
    },
    notify(value, current) {
      for (const entry of [...entries]) {
        if (!current()) break;
        if (entries.has(entry)) entry(value);
      }
    },
    clear() { entries.clear(); },
  };
}

/**
 * Normal application-owned selection state. hitTest({x,y}, selectionMask) synchronously returns
 * ordered {node,hit} candidates mapped by the host from SceneRaycaster hits. validateNode checks
 * borrowed source-scoped node handles (null is always valid). commitSelection(node) runs before
 * state publication, including the initial selectedNode; a failure preserves the previous state.
 * Requested subscribers may replace request.selectedNode or set request.cancel before commit.
 * Changing options never implicitly clears selection. No subscription replays initial state.
 */
export function createSceneSelectionSource({hitTest, commitSelection = () => {},
  validateNode = () => {}, selectedNode = null, isEnabled = true,
  clearSelectionOnMiss = true, selectionMask = 0xffffffff} = {}) {
  let hit = callback(hitTest, 'hitTest'), commit = callback(commitSelection, 'commitSelection');
  let validate = callback(validateNode, 'validateNode');
  let options = Object.freeze({isEnabled: boolean(isEnabled, 'isEnabled'),
    clearSelectionOnMiss: boolean(clearSelectionOnMiss, 'clearSelectionOnMiss'), selectionMask: mask(selectionMask)});
  let selected = null, disposed = false, committing = false, generation = 0, selectionVersion = 0;
  const selections = subscriptions(), changes = subscriptions(), requests = subscriptions();
  function live() { if (disposed) throw new Error('The scene selection source is disposed.'); }
  function mutable() {
    live();
    if (committing) throw new Error('Selection cannot be mutated during its synchronous commit callback.');
  }
  function node(value) {
    if (value !== null) {
      if (typeof value !== 'object') throw new TypeError('selectedNode must be a borrowed node handle or null.');
      validate(value);
    }
    return value;
  }
  function updateSelection(value) {
    mutable();
    const beforeValidation = generation;
    value = node(value);
    if (disposed || generation !== beforeValidation) return selected;
    if (value === selected) return selected;
    const oldNode = selected;
    committing = true;
    try { commit(value); } finally { committing = false; }
    if (disposed) return null;
    selected = value;
    ++generation;
    const version = ++selectionVersion;
    selections.notify(Object.freeze({oldNode, newNode: value}), () => !disposed && selectionVersion === version);
    return selected;
  }
  function updateOption(key, value) {
    mutable();
    if (options[key] === value) return;
    options = Object.freeze({...options, [key]: value});
    ++generation;
    const published = options;
    changes.notify(published, () => !disposed && options === published);
  }
  // Initial composition is transactional, before the source or subscriptions can escape.
  selected = node(selectedNode);
  commit(selected);
  const source = {
    get selectedNode() { return selected; }, set selectedNode(value) { updateSelection(value); },
    get isEnabled() { return options.isEnabled; },
    set isEnabled(value) { updateOption('isEnabled', boolean(value, 'isEnabled')); },
    get clearSelectionOnMiss() { return options.clearSelectionOnMiss; },
    set clearSelectionOnMiss(value) { updateOption('clearSelectionOnMiss', boolean(value, 'clearSelectionOnMiss')); },
    get selectionMask() { return options.selectionMask; },
    set selectionMask(value) { updateOption('selectionMask', mask(value)); },
    get options() { return options; }, get isDisposed() { return disposed; },
    subscribeSelection(listener) { live(); return selections.add(listener); },
    subscribeOptions(listener) { live(); return changes.add(listener); },
    subscribeRequested(listener) { live(); return requests.add(listener); },
    requestSelection(value) {
      mutable();
      const viewportPositionPixels = position(value);
      if (!options.isEnabled) return selected;
      const version = ++generation;
      const current = () => !disposed && generation === version && options.isEnabled;
      const values = hit(viewportPositionPixels, options.selectionMask);
      if (!current()) return selected;
      if (!Array.isArray(values)) throw new TypeError('hitTest must return an ordered candidate array.');
      const candidates = [];
      for (const value of values) {
        if (!value || value.node == null || value.hit == null)
          throw new TypeError('Each candidate requires a borrowed node handle and hit evidence.');
        const selectedNode = node(value.node);
        if (!current()) return selected;
        candidates.push(Object.freeze({node: selectedNode, hit: evidence(value.hit)}));
      }
      if (!current()) return selected;
      const request = {selectedNode: candidates[0]?.node ?? (options.clearSelectionOnMiss ? null : selected), cancel: false};
      Object.defineProperties(request, {
        viewportPositionPixels: {value: viewportPositionPixels, enumerable: true},
        candidates: {value: Object.freeze(candidates), enumerable: true},
      });
      Object.seal(request);
      requests.notify(request, current);
      if (!current() || boolean(request.cancel, 'request.cancel')) return selected;
      return updateSelection(request.selectedNode);
    },
    dispose() {
      if (disposed) return;
      disposed = true; ++generation; selected = null;
      selections.clear(); changes.clear(); requests.clear();
      hit = commit = validate = null;
    },
  };
  sources.add(source);
  return source;
}

function requireSource(value) {
  if (!sources.has(value) || value.isDisposed)
    throw new TypeError('source must be a live createSceneSelectionSource result.');
  return value;
}

/**
 * Observes only completed primary left taps on a borrowed Canvas. Register after raw/Gizmo
 * pointer input and before Orbit. It neither consumes events nor captures pointers. Recognized
 * camera commands should call cancel(), preserving camera/Gizmo precedence even below tolerance.
 * tapTolerance is in CSS pixels; the completed position maps to physical Canvas pixels. A source
 * replacement cancels the contact and detaches old subscriptions; sources/Canvas stay borrowed.
 */
export function attachSceneSelectionInput(canvas, initialSource, {isInputEnabled = () => true,
  tapTolerance = 6, onError = error => { throw error; }} = {}) {
  callback(isInputEnabled, 'isInputEnabled'); callback(onError, 'onError');
  if (!Number.isFinite(tapTolerance) || tapTolerance < 0)
    throw new TypeError('tapTolerance must be a finite non-negative CSS-pixel distance.');
  if (!canvas?.addEventListener || typeof canvas.getBoundingClientRect !== 'function')
    throw new TypeError('A borrowed Canvas is required.');
  let source = requireSource(initialSource), disposed = false, active = null, stopOptions, stopSelection;
  const contacts = new Set(), events = new AbortController(), options = {signal: events.signal};
  const document = canvas.ownerDocument ?? globalThis.document;
  const window = document?.defaultView ?? globalThis.window;
  const cancel = () => { active = null; };
  const reset = () => { cancel(); contacts.clear(); };
  function observe() {
    stopOptions = source.subscribeOptions(cancel); stopSelection = source.subscribeSelection(cancel);
  }
  function enabled() {
    return !disposed && !source.isDisposed && source.isEnabled && document?.visibilityState !== 'hidden' && isInputEnabled();
  }
  function mapped(event) {
    const rect = canvas.getBoundingClientRect();
    if (![rect.left, rect.top, rect.width, rect.height, canvas.width, canvas.height,
      event.clientX, event.clientY].every(Number.isFinite) ||
      !(rect.width > 0 && rect.height > 0 && canvas.width > 0 && canvas.height > 0)) return null;
    const u = (event.clientX - rect.left) / rect.width, v = (event.clientY - rect.top) / rect.height;
    if (u < 0 || u > 1 || v < 0 || v > 1) return null;
    return {x: u * canvas.width, y: v * canvas.height};
  }
  function within(event) {
    return active && Number.isFinite(event.clientX) && Number.isFinite(event.clientY) &&
      Math.hypot(event.clientX - active.x, event.clientY - active.y) <= tapTolerance;
  }
  const safely = action => event => {
    try { action(event); } catch (error) { cancel(); onError(error); }
  };
  observe();
  canvas.addEventListener('pointerdown', safely(event => {
    if (contacts.has(event.pointerId)) { cancel(); return; }
    contacts.add(event.pointerId);
    if (contacts.size !== 1) { cancel(); return; }
    if (event.defaultPrevented || !event.isPrimary || event.button !== 0 || !enabled() || !mapped(event)) return;
    active = {id: event.pointerId, x: event.clientX, y: event.clientY};
  }), options);
  canvas.addEventListener('pointermove', safely(event => {
    if (!active || active.id !== event.pointerId) return;
    if (event.defaultPrevented || !enabled() || !mapped(event) || !within(event)) { cancel(); return; }
    for (const sample of event.getCoalescedEvents?.() ?? [])
      if (!within(sample) || !mapped(sample)) { cancel(); break; }
  }), options);
  canvas.addEventListener('pointerup', safely(event => {
    contacts.delete(event.pointerId);
    if (!active || active.id !== event.pointerId) return;
    const accepted = !event.defaultPrevented && event.button === 0 && enabled() && within(event);
    const value = accepted ? mapped(event) : null;
    cancel();
    if (value) source.requestSelection(value);
  }), options);
  canvas.addEventListener('pointercancel', event => {
    contacts.delete(event.pointerId);
    if (active?.id === event.pointerId) cancel();
  }, options);
  canvas.addEventListener('lostpointercapture', event => {
    if (active?.id === event.pointerId) cancel();
  }, options);
  canvas.addEventListener('pointerleave', cancel, options);
  canvas.addEventListener('keydown', event => { if (event.key === 'Escape') reset(); }, options);
  window?.addEventListener('blur', reset, options);
  window?.addEventListener('pagehide', reset, options);
  // A Canvas without another tool's capture may end outside it. Global ends only cancel/clean up;
  // they never propose a selection or observe clicks on other HTML controls.
  for (const name of ['pointerup', 'pointercancel'])
    window?.addEventListener(name, event => {
      contacts.delete(event.pointerId);
      if (active?.id === event.pointerId) cancel();
    }, options);
  document?.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') reset();
  }, options);
  return {
    get source() { return source; },
    set source(value) {
      if (disposed) throw new Error('The scene selection input adapter is disposed.');
      value = requireSource(value);
      if (source === value) return;
      cancel(); stopOptions(); stopSelection(); source = value; observe();
    },
    get isActive() { return active !== null; }, cancel,
    dispose() {
      if (disposed) return;
      disposed = true; reset(); events.abort(); stopOptions(); stopSelection(); source = null;
    },
  };
}
