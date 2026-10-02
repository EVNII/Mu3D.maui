import {createSceneAnchorOverlay} from './scene-anchor-overlay.mjs';

// Handles carry no application-readable IDs. Registrations and the projection stream belong to
// their explicit host source, rather than to a global node registry or an additional render loop.
const sources = new WeakMap(), nodes = new WeakMap();
let nextUnboundId = 0;
const vector = (value, length, name) => {
  if (!Array.isArray(value) || value.length !== length || !value.every(Number.isFinite))
    throw new TypeError(`${name} requires ${length} finite coordinates.`);
  if (name === 'localPosition' && !value.every(coordinate => Number.isFinite(Math.fround(coordinate))))
    throw new TypeError('localPosition must fit finite FP32 scene coordinates.');
  return Object.freeze(name === 'localPosition' ? value.map(Math.fround) : [...value]);
};
const boolean = (value, name) => {
  if (typeof value !== 'boolean') throw new TypeError(`${name} must be a boolean.`);
  return value;
};
const subscribe = (listeners, listener) => {
  if (typeof listener !== 'function') throw new TypeError('A listener function is required.');
  const entry = {listener, active: true}; listeners.add(entry);
  return () => { entry.active = false; listeners.delete(entry); };
};
const notify = (listeners, value, current = () => true) => {
  for (const entry of [...listeners]) if (entry.active && current()) entry.listener(value);
};
const sameVector = (a, b) => a.length === b.length && a.every((value, index) => value === b[index]);

/**
 * Adapt one host-owned scene's node registrations and submitted-frame stream. Host callbacks are
 * borrowed and synchronous, except for the delivered frames; this source owns no renderer/clock.
 * configureAnchor({id, target, localPosition}) returns {id, revision, sourceId?}; removeAnchor(id)
 * returns a new revision. subscribeFrames(listener) returns an unsubscribe function. Frame batches
 * have SourceId/Revision plus the existing scene-anchor overlay's PascalCase projection fields.
 */
export function createSceneNodeAnchorSource({sourceId, configureAnchor, removeAnchor, subscribeFrames}) {
  if (typeof sourceId !== 'string' || !sourceId ||
      ![configureAnchor, removeAnchor, subscribeFrames].every(value => typeof value === 'function'))
    throw new TypeError('A nonempty scene source ID and three host callbacks are required.');
  const listeners = new Set(), registrations = new Map(), handles = new Map();
  let disposed = false, disposing = false, revision = 0, latestFrame = null, generation = 0, stopFrames = null;
  let changing = false, lastFrameId = -1;
  const requireLive = () => { if (disposed || disposing) throw new Error('The scene node anchor source is disposed.'); };
  function checkRevision(value) {
    if (!Number.isSafeInteger(value) || value <= revision)
      throw new TypeError('An anchor mutation must return a newer nonnegative safe-integer revision.');
  }
  function clearFrame() {
    latestFrame = null;
    if (!disposing) notify(listeners, null, () => latestFrame === null);
  }
  function invalidate(value) { revision = value; clearFrame(); }
  function receive(value, expectedGeneration) {
    if (disposed || disposing || generation !== expectedGeneration) return;
    if (value === null) { clearFrame(); return; }
    if (!value || value.SourceId !== sourceId || value.Revision !== revision) return;
    if (!Number.isSafeInteger(value.FrameId) || value.FrameId < 0 ||
        !Number.isFinite(value.Width) || value.Width <= 0 ||
        !Number.isFinite(value.Height) || value.Height <= 0 || !Array.isArray(value.Points))
      throw new TypeError('A node projection batch requires a frame ID and positive logical extents.');
    if (value.FrameId <= lastFrameId) return;
    const ids = new Set();
    const points = value.Points.map(point => {
      if (!point || typeof point.Id !== 'string' || !point.Id || ids.has(point.Id) ||
          typeof point.Projected !== 'boolean' || typeof point.InsideViewport !== 'boolean' ||
          (point.Projected && ![point.X, point.Y, point.Depth].every(Number.isFinite)))
        throw new TypeError('Projection points must be unique and finite when projected.');
      ids.add(point.Id); return Object.freeze({...point});
    });
    latestFrame = Object.freeze({SourceId: sourceId, Revision: revision, FrameId: value.FrameId,
      Width: value.Width, Height: value.Height, Points: Object.freeze(points)});
    lastFrameId = value.FrameId;
    const published = latestFrame;
    notify(listeners, published, () => !disposed && generation === expectedGeneration && latestFrame === published);
  }
  function disconnect() {
    generation++; const stop = stopFrames; stopFrames = null;
    // Without observation there is no evidence that the last frame still describes this scene.
    latestFrame = null; if (stop) stop();
  }
  function change(action, allowDisposing = false) {
    if (disposed || (disposing && !allowDisposing)) requireLive();
    if (changing) throw new Error('Scene anchor host mutations cannot be reentered.');
    changing = true;
    try { return action(); } finally { changing = false; }
  }
  function configure(registration, target, localPosition) {
    const result = change(() => configureAnchor({id: registration?.id ?? null,
      target: nodes.get(target).token, localPosition: [...localPosition]}));
    if (!result || typeof result.id !== 'string' || !result.id ||
        (result.sourceId !== undefined && result.sourceId !== sourceId) ||
        (registration && result.id !== registration.id) ||
        (!registration && registrations.has(result.id)))
      throw new TypeError('The host must return a unique anchor ID from the same scene source.');
    checkRevision(result.revision);
    const current = registration ?? {id: result.id, active: true};
    registrations.set(current.id, current);
    try {
      invalidate(result.revision); requireLive();
      if (!current.active) throw new Error('The scene anchor was released during its registration.');
    } catch (error) {
      // Creation can fail in an application observer as well as in the bridge. Undo a newly owned
      // registration even when its DOM binding has not received the returned registration yet.
      if (!registration && current.active) {
        try { release(current); } catch { /* Preserve the first error; release already clears ownership. */ }
      }
      throw error;
    }
    return current;
  }
  function release(registration) {
    if (!registration?.active) return;
    const value = change(() => removeAnchor(registration.id), true); checkRevision(value);
    registration.active = false; registrations.delete(registration.id); invalidate(value);
  }
  const source = {
    get sourceId() { return sourceId; },
    get revision() { return revision; },
    get latestFrame() { return latestFrame; },
    /** Hide every borrowing view after host suspension/failure, without changing registrations. */
    clear() { requireLive(); clearFrame(); },
    /** Return a stable, frozen opaque handle; the host decides which token names a real node. */
    node(token) {
      requireLive();
      if (typeof token !== 'string' || !token) throw new TypeError('A nonempty host node token is required.');
      if (!handles.has(token)) {
        const handle = Object.freeze({}); nodes.set(handle, {source, token}); handles.set(token, handle);
      }
      return handles.get(token);
    },
    /** Observe immutable matching frames and null invalidations. The host stream is shared/lazy. */
    subscribe(listener) {
      requireLive(); const stop = subscribe(listeners, listener);
      if (listeners.size === 1) {
        const expected = ++generation;
        try {
          const upstream = subscribeFrames(value => receive(value, expected));
          if (typeof upstream !== 'function') throw new TypeError('subscribeFrames must return an unsubscribe function.');
          if (disposed || generation !== expected || listeners.size === 0) upstream();
          else stopFrames = upstream;
        } catch (error) { stop(); disconnect(); throw error; }
      }
      return () => { stop(); if (!listeners.size) disconnect(); };
    },
    /** Unregister owned anchors/observers; never dispose host scene nodes or rendering resources. */
    dispose() {
      if (disposed || disposing) return;
      if (changing) throw new Error('Scene anchor host mutations cannot be reentered by disposal.');
      disposing = true;
      try {
        disconnect();
        // No intermediate invalidation is observable while teardown is in progress. A disposal
        // listener may dispose its overlay/source again without unregistering an anchor twice.
        for (const registration of [...registrations.values()]) release(registration);
        disposed = true;
      } finally { disposing = false; }
      try { notify(listeners, null); }
      finally {
        for (const entry of listeners) entry.active = false;
        listeners.clear(); handles.clear();
      }
    },
  };
  sources.set(source, {requireLive, configure, release}); return source;
}

const requireSource = value => {
  if (value !== null) {
    if (!sources.has(value)) throw new TypeError('A scene node anchor source or null is required.');
    sources.get(value).requireLive();
  }
  return value;
};
const requireTarget = (value, source) => {
  if (value !== null && (!nodes.has(value) || nodes.get(value).source !== source))
    throw new TypeError('The target must be an opaque node handle from the overlay source, or null.');
  return value;
};
function bindingOptions(value, source) {
  if (!value || typeof value !== 'object') throw new TypeError('Scene node binding options must be an object.');
  const option = (name, fallback) => value[name] === undefined ? fallback : value[name];
  return Object.freeze({target: requireTarget(option('target', null), source),
    localPosition: vector(option('localPosition', [0, 0, 0]), 3, 'localPosition'),
    offset: vector(option('offset', [8, -28]), 2, 'offset'),
    pivot: vector(option('pivot', [0, 0]), 2, 'pivot'),
    interactive: boolean(option('interactive', true), 'interactive'),
    hideWhenOutsideViewport: boolean(option('hideWhenOutsideViewport', true), 'hideWhenOutsideViewport')});
}

/**
 * Bind real HTML controls to opaque scene nodes/local points. This normal facade owns registration,
 * observation and placement; createSceneAnchorOverlay remains the manual projection escape hatch.
 * Source/controls are borrowed. Detach restores their DOM slots and retains options for reattach.
 * Replacing the source clears targets; retarget explicitly with handles from the replacement source.
 */
export function createSceneNodeOverlay({container, source = null, onInteraction = () => {}, isEnabled = true}) {
  requireSource(source); boolean(isEnabled, 'isEnabled');
  if (typeof onInteraction !== 'function') throw new TypeError('onInteraction must be a function.');
  const bindings = new Set(), optionsListeners = new Set();
  let disposed = false, attached = false, low = null, stopSource = null, connection = 0;
  let options = Object.freeze({source, isEnabled});
  const requireLive = () => { if (disposed) throw new Error('The scene node overlay is disposed.'); };
  function connect() {
    const expected = ++connection, observedSource = source;
    if (!source) return;
    const stop = source.subscribe(batch => {
      if (disposed || !attached || connection !== expected || source !== observedSource) return;
      if (batch) low.update(batch); else low.clear();
    });
    if (disposed || !attached || connection !== expected || source !== observedSource) { stop(); return; }
    stopSource = stop;
    if (source.latestFrame) low.update(source.latestFrame);
  }
  function disconnect() { connection++; const stop = stopSource; stopSource = null; if (stop) stop(); }
  function release(entry) {
    if (entry.registration) {
      sources.get(entry.registrationSource).release(entry.registration);
      entry.registration = null; entry.registrationSource = null;
    }
  }
  function mount(entry) {
    const anchor = `mu3d-unbound-${++nextUnboundId}`;
    entry.low = low.bind(entry.element, {...entry.options, anchor});
    try {
      if (entry.options.target) {
        entry.registration = sources.get(source).configure(null, entry.options.target, entry.options.localPosition);
        entry.registrationSource = source; entry.low.setAnchor(entry.registration.id);
      }
    } catch (error) { release(entry); entry.low.dispose(); entry.low = null; throw error; }
  }
  function unmount(entry) { release(entry); entry.low?.dispose(); entry.low = null; }
  function setBinding(entry, name, value) {
    requireLive(); if (entry.disposed) throw new Error('The scene node binding is disposed.');
    let normalized;
    if (name === 'target') normalized = requireTarget(value, source);
    else if (name === 'localPosition') normalized = vector(value, 3, name);
    else if (name === 'offset' || name === 'pivot') normalized = vector(value, 2, name);
    else normalized = boolean(value, name);
    if (Array.isArray(normalized) ? sameVector(entry.options[name], normalized) : entry.options[name] === normalized) return;
    if (name === 'target' || name === 'localPosition') {
      // The previous batch contains the old registration revision even when the ID is retained.
      // Hide before calling the bridge; it must never flash the former target's position.
      low?.clear();
      if (attached) {
        const target = name === 'target' ? normalized : entry.options.target;
        const position = name === 'localPosition' ? normalized : entry.options.localPosition;
        if (target) {
          entry.registration = sources.get(source).configure(entry.registration, target, position);
          entry.registrationSource = source; entry.low.setAnchor(entry.registration.id);
        } else {
          release(entry); entry.low.setAnchor(`mu3d-unbound-${++nextUnboundId}`);
        }
      }
    } else entry.low?.setOptions({[name]: normalized});
    entry.options = Object.freeze({...entry.options, [name]: normalized}); notify(entry.listeners, entry.options);
  }
  const overlay = {
    get source() { return source; },
    set source(value) {
      requireLive(); requireSource(value); if (source === value) return;
      disconnect(); low?.clear();
      for (const entry of [...bindings]) release(entry);
      source = value; options = Object.freeze({...options, source});
      const changed = [];
      for (const entry of bindings) if (entry.options.target !== null) {
        entry.options = Object.freeze({...entry.options, target: null});
        entry.low?.setAnchor(`mu3d-unbound-${++nextUnboundId}`); changed.push(entry);
      }
      if (attached) connect();
      for (const entry of changed) if (!entry.disposed) notify(entry.listeners, entry.options);
      if (!disposed) notify(optionsListeners, options);
    },
    get isEnabled() { return options.isEnabled; },
    set isEnabled(value) {
      requireLive(); boolean(value, 'isEnabled'); if (value === options.isEnabled) return;
      options = Object.freeze({...options, isEnabled: value}); low?.setEnabled(value); notify(optionsListeners, options);
    },
    get options() { return options; },
    get isAttached() { return attached; },
    get isInteracting() { return low?.isInteracting ?? false; },
    get hasActivePointer() { return low?.hasActivePointer ?? false; },
    subscribeOptions(listener) { requireLive(); return subscribe(optionsListeners, listener); },
    /** Clear only this overlay's current placement; source.clear() also invalidates other borrowers. */
    clear() { requireLive(); low?.clear(); },
    /** Borrow an element, preserving its native styles/listeners and original DOM slot. */
    bind(element, value = {}) {
      requireLive(); const normalized = bindingOptions(value, source);
      if ([...bindings].some(entry => entry.element === element))
        throw new Error('An HTML element must have a single scene node binding.');
      const entry = {element, options: normalized, listeners: new Set(), low: null,
        registration: null, registrationSource: null, disposed: false};
      const binding = {
        get options() { return entry.options; },
        subscribeOptions(listener) {
          requireLive(); if (entry.disposed) throw new Error('The scene node binding is disposed.');
          return subscribe(entry.listeners, listener);
        },
        dispose() {
          if (entry.disposed) return; entry.disposed = true;
          unmount(entry); bindings.delete(entry);
          for (const listener of entry.listeners) listener.active = false;
          entry.listeners.clear();
        },
      };
      for (const name of Object.keys(normalized)) Object.defineProperty(binding, name,
        {enumerable: true, get: () => entry.options[name], set: value => setBinding(entry, name, value)});
      entry.binding = binding; bindings.add(entry);
      try { if (attached) mount(entry); }
      catch (error) { binding.dispose(); throw error; }
      return binding;
    },
    attach() {
      requireLive(); if (attached) return; if (source) sources.get(source).requireLive();
      low = createSceneAnchorOverlay({container, onInteraction}); attached = true;
      try {
        low.setEnabled(options.isEnabled); connect();
        for (const entry of bindings) mount(entry);
      } catch (error) {
        disconnect(); for (const entry of bindings) unmount(entry);
        low.dispose(); low = null; attached = false; throw error;
      }
    },
    detach() {
      requireLive(); if (!attached) return;
      attached = false; disconnect();
      for (const entry of bindings) unmount(entry);
      low.dispose(); low = null;
    },
    dispose() {
      if (disposed) return; overlay.detach(); disposed = true;
      for (const entry of [...bindings]) entry.binding.dispose();
      for (const listener of optionsListeners) listener.active = false;
      optionsListeners.clear();
    },
  };
  overlay.attach(); return overlay;
}
