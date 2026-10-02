/**
 * Optional DOM diagnostics for the shared FrameStatisticsCollector. The host records successful
 * submissions and publishes camelCase FrameStatisticsSnapshot payloads at its configured interval.
 * This module owns no render clock, collector, GPU measurement or physical-presentation estimate.
 */
const countFields = ['sampleCount', 'presentationSampleCount', 'rendererSampleCount',
  'drawCallSampleCount', 'primitiveSampleCount'];
const numberFields = ['windowDurationMilliseconds', 'framesPerSecond', 'averageFrameMilliseconds',
  'minimumFrameMilliseconds', 'maximumFrameMilliseconds'];
const optionalFields = ['averageAcquireMilliseconds', 'averagePresentationRenderMilliseconds',
  'averagePresentMilliseconds', 'averagePresentationTotalMilliseconds',
  'averageRendererPrepareMilliseconds', 'averageRendererEncodeMilliseconds',
  'averageRendererSubmitMilliseconds', 'averageRendererCacheTrimMilliseconds',
  'averageRendererTotalMilliseconds', 'averageDrawCallCount', 'averagePrimitiveCount'];
const resourceFields = ['meshCount', 'vertexCount', 'materialCount', 'textureCount', 'bufferCount',
  'pipelineCount', 'estimatedCpuBytes', 'estimatedGpuBytes'];
const emptySnapshot = Object.freeze(Object.fromEntries([
  ...countFields.map(key => [key, 0]), ...numberFields.map(key => [key, 0]),
  ...optionalFields.map(key => [key, null]), ['resourceCounts', null],
]));
const defaults = Object.freeze({isDetailed: false, isGraphVisible: true, textColor: '#ffffff',
  graphColor: '#00ffff', graphBackgroundColor: 'rgb(0 3.5% 9%)'});
const placements = new Set(['top-left', 'top-center', 'top-right', 'center-left', 'center',
  'center-right', 'bottom-left', 'bottom-center', 'bottom-right']);
const decimals = [0, 1, 2].map(digits => new Intl.NumberFormat('en-US', {
  minimumFractionDigits: digits, maximumFractionDigits: digits, useGrouping: false,
}));
const groupedInteger = new Intl.NumberFormat('en-US', {maximumFractionDigits: 0});
const format = (value, digits) => decimals[digits].format(value);
const milliseconds = value => value == null ? '—' : `${format(value, 2)} ms`;
const average = value => value == null ? '—' : format(value, 1);

function nonnegative(value, name, integer = false) {
  if (!Number.isFinite(value) || value < 0 || (integer && !Number.isSafeInteger(value)))
    throw new TypeError(`${name} must be a finite non-negative${integer ? ' safe integer' : ' number'}.`);
  return value;
}
function interval(value) {
  nonnegative(value, 'snapshotIntervalMilliseconds');
  if (value > 922337203685477.6) throw new TypeError('snapshotIntervalMilliseconds exceeds the native TimeSpan range.');
  return value;
}
function nullableCount(value, name) { return value === null ? null : nonnegative(value, name, true); }
function collectionOption(key, value) {
  return key === 'isEnabled' ? boolean(value, key) : key === 'snapshotIntervalMilliseconds'
    ? interval(value) : nullableCount(value, key);
}
function boolean(value, name) {
  if (typeof value !== 'boolean') throw new TypeError(`${name} must be a boolean.`);
  return value;
}
function color(value, name) {
  if (typeof value !== 'string' || !value.trim()) throw new TypeError(`${name} must be a CSS color.`);
  return value;
}
function snapshotCopy(value) {
  if (value == null || typeof value !== 'object') throw new TypeError('A managed statistics snapshot is required.');
  const copy = {};
  for (const key of countFields) copy[key] = nonnegative(value[key], key, true);
  for (const key of numberFields) copy[key] = nonnegative(value[key], key);
  for (const key of optionalFields) copy[key] = value[key] == null ? null : nonnegative(value[key], key);
  copy.resourceCounts = null;
  if (value.resourceCounts != null) {
    const resources = {};
    for (const key of resourceFields)
      resources[key] = value.resourceCounts[key] == null ? null : nonnegative(value.resourceCounts[key], key, true);
    copy.resourceCounts = Object.freeze(resources);
  }
  return Object.freeze(copy);
}
function subscriptions() {
  const listeners = new Set();
  return {
    add(listener) {
      if (typeof listener !== 'function') throw new TypeError('A subscription requires a function.');
      const entry = value => listener(value);
      listeners.add(entry);
      return () => listeners.delete(entry);
    },
    notify(value) { for (const listener of [...listeners]) listener(value); },
    clear() { listeners.clear(); },
  };
}
function requireSource(value) {
  if (value != null && (typeof value.subscribe !== 'function' || !value.latestSnapshot))
    throw new TypeError('source must publish frame statistics, or be null.');
  return value;
}

/**
 * Observable JS binding boundary for a borrowed managed collector. Subscribe handlers run only
 * on subsequent changes; read latestSnapshot/options for initial state. publish is explicit even
 * while disabled, matching native manual publication. Option changes notify the host to configure
 * its collector. reset publishes the empty state and notifies subscribeReset to reset that collector.
 * publishSnapshot notifies subscribePublish to request immediate host capture without a new sample.
 */
export function createFrameStatisticsSource({isEnabled = true, snapshotIntervalMilliseconds = 500,
  drawCallCount = null, primitiveCount = null} = {}) {
  let options = Object.freeze({isEnabled: boolean(isEnabled, 'isEnabled'),
    snapshotIntervalMilliseconds: interval(snapshotIntervalMilliseconds),
    drawCallCount: nullableCount(drawCallCount, 'drawCallCount'), primitiveCount: nullableCount(primitiveCount, 'primitiveCount')});
  let latest = emptySnapshot, disposed = false;
  const snapshots = subscriptions(), changes = subscriptions(), resets = subscriptions(), captures = subscriptions();
  function live() { if (disposed) throw new Error('The frame statistics source is disposed.'); }
  function update(key, value) {
    live();
    if (options[key] === value) return;
    options = Object.freeze({...options, [key]: value}); changes.notify(options);
  }
  return {
    get isEnabled() { return options.isEnabled; },
    set isEnabled(value) { update('isEnabled', boolean(value, 'isEnabled')); },
    get snapshotIntervalMilliseconds() { return options.snapshotIntervalMilliseconds; },
    set snapshotIntervalMilliseconds(value) {
      update('snapshotIntervalMilliseconds', interval(value));
    },
    get drawCallCount() { return options.drawCallCount; },
    set drawCallCount(value) { update('drawCallCount', nullableCount(value, 'drawCallCount')); },
    get primitiveCount() { return options.primitiveCount; },
    set primitiveCount(value) { update('primitiveCount', nullableCount(value, 'primitiveCount')); },
    get options() { return options; },
    get latestSnapshot() { return latest; },
    subscribe(listener) { live(); return snapshots.add(listener); },
    subscribeOptions(listener) { live(); return changes.add(listener); },
    subscribeReset(listener) { live(); return resets.add(listener); },
    subscribePublish(listener) { live(); return captures.add(listener); },
    publish(snapshot) { live(); latest = snapshotCopy(snapshot); snapshots.notify(latest); return latest; },
    publishSnapshot() { live(); captures.notify(); return latest; },
    reset() { live(); latest = emptySnapshot; snapshots.notify(latest); resets.notify(); return latest; },
    dispose() {
      if (disposed) return;
      disposed = true; snapshots.clear(); changes.clear(); resets.clear(); captures.clear();
    },
  };
}

function resourcesText(counts) {
  if (!counts) return 'Resources  —';
  const values = [];
  for (const [key, label] of [['meshCount', 'meshes'], ['vertexCount', 'vertices'],
    ['materialCount', 'materials'], ['textureCount', 'textures'], ['bufferCount', 'buffers'],
    ['pipelineCount', 'pipelines']])
    if (counts[key] != null) values.push(`${label} ${groupedInteger.format(counts[key])}`);
  for (const [key, label] of [['estimatedCpuBytes', 'CPU'], ['estimatedGpuBytes', 'GPU']]) {
    const bytes = counts[key];
    if (bytes != null) values.push(`${label} ${bytes >= 1048576 ? `${format(bytes / 1048576, 1)} MiB`
      : bytes >= 1024 ? `${format(bytes / 1024, 1)} KiB` : `${bytes} B`}`);
  }
  return values.length ? `Resources  ${values.join('  ·  ')}` : 'Resources  —';
}
function text(snapshot, detailed, history) {
  if (snapshot.sampleCount === 0) return ['Waiting for frame samples…', snapshot.resourceCounts
    ? resourcesText(snapshot.resourceCounts) : 'No completed frame interval has been sampled.'];
  const headline = history.length
    ? `${format(snapshot.framesPerSecond, 0)} FPS (${format(Math.min(...history), 0)}–${format(Math.max(...history), 0)})  ·  ${format(snapshot.averageFrameMilliseconds, 2)} ms`
    : `${format(snapshot.framesPerSecond, 1)} FPS  ·  ${format(snapshot.averageFrameMilliseconds, 2)} ms`;
  const summary = `Render ${milliseconds(snapshot.averageRendererTotalMilliseconds)}  ·  Present ${milliseconds(snapshot.averagePresentationTotalMilliseconds)}\nDraws ${average(snapshot.averageDrawCallCount)}  ·  Primitives ${average(snapshot.averagePrimitiveCount)}`;
  if (!detailed) return [headline, summary];
  return [headline,
    `Frame  min ${format(snapshot.minimumFrameMilliseconds, 2)} ms  ·  avg ${format(snapshot.averageFrameMilliseconds, 2)} ms  ·  max ${format(snapshot.maximumFrameMilliseconds, 2)} ms  ·  samples ${snapshot.sampleCount}\n` +
    `Presentation  acquire ${milliseconds(snapshot.averageAcquireMilliseconds)}  ·  render ${milliseconds(snapshot.averagePresentationRenderMilliseconds)}  ·  present ${milliseconds(snapshot.averagePresentMilliseconds)}  ·  total ${milliseconds(snapshot.averagePresentationTotalMilliseconds)}\n` +
    `Renderer  prepare ${milliseconds(snapshot.averageRendererPrepareMilliseconds)}  ·  encode ${milliseconds(snapshot.averageRendererEncodeMilliseconds)}  ·  submit ${milliseconds(snapshot.averageRendererSubmitMilliseconds)}  ·  trim ${milliseconds(snapshot.averageRendererCacheTrimMilliseconds)}  ·  total ${milliseconds(snapshot.averageRendererTotalMilliseconds)}\n` +
    `${summary}\n${resourcesText(snapshot.resourceCounts)}`];
}

/**
 * Creates a real DOM view, borrowing its source. 120 published snapshots feed a 52 CSS-pixel
 * graph, independently of renderer cadence. append element to mount it; detach/attach suspend and
 * resume source observation while retaining displayed history. dispose removes owned DOM only.
 */
export function createFrameStatisticsView({source = null, document = globalThis.document, ...display} = {}) {
  if (!document?.createElement || !document?.createElementNS) throw new TypeError('A DOM document is required.');
  source = requireSource(source);
  const options = {...defaults};
  for (const key of Object.keys(defaults)) if (display[key] !== undefined)
    options[key] = key.startsWith('is') ? boolean(display[key], key) : color(display[key], key);
  const element = document.createElement('div'), headline = document.createElement('div'), details = document.createElement('div');
  const graph = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  element.dataset.mu3dFrameStatistics = '';
  headline.dataset.mu3dStatisticsHeadline = ''; details.dataset.mu3dStatisticsDetails = '';
  graph.dataset.mu3dStatisticsGraph = ''; graph.setAttribute('aria-hidden', 'true');
  Object.assign(element.style, {display: 'flex', flexDirection: 'column', gap: '3px', padding: '10px',
    boxSizing: 'border-box', minWidth: '0', fontFamily: 'system-ui, sans-serif'});
  Object.assign(headline.style, {fontWeight: 'bold', fontSize: '16px', whiteSpace: 'nowrap'});
  Object.assign(details.style, {fontSize: '12px', whiteSpace: 'pre-wrap', overflowWrap: 'break-word'});
  Object.assign(graph.style, {width: '100%', height: '52px', flexShrink: '0', pointerEvents: 'none'});
  graph.setAttribute('preserveAspectRatio', 'none');
  element.appendChild(headline); element.appendChild(graph); element.appendChild(details);
  const snapshots = subscriptions(), changes = subscriptions();
  let history = [], latest = emptySnapshot, attached = true, disposed = false, unsubscribe = null, subscriptionVersion = 0;
  const ResizeObserver = document.defaultView?.ResizeObserver;
  const observer = ResizeObserver ? new ResizeObserver(() => { if (attached && !disposed) refreshGraph(); }) : null;
  function live() { if (disposed) throw new Error('The frame statistics view is disposed.'); }
  function refreshGraph() {
    graph.replaceChildren();
    graph.hidden = !options.isGraphVisible; graph.style.display = options.isGraphVisible ? 'block' : 'none';
    graph.style.backgroundColor = options.graphBackgroundColor;
    const width = graph.getBoundingClientRect?.().width || 119, height = 52;
    graph.setAttribute('viewBox', `0 0 ${width} ${height}`);
    if (!history.length) return;
    function shape(tag, attributes) {
      const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
      for (const [key, value] of Object.entries(attributes)) node.setAttribute(key, String(value));
      graph.appendChild(node);
    }
    for (let row = 1; row < 4; row++) shape('line', {x1: 0, x2: width, y1: height * row / 4,
      y2: height * row / 4, stroke: options.graphColor, 'stroke-width': 1, opacity: 0.18,
      'vector-effect': 'non-scaling-stroke'});
    const scale = Math.max(60, ...history), step = width / 119, barWidth = Math.max(1, step * 0.72);
    const firstX = width - (history.length - 1) * step;
    let previous = null;
    for (let index = 0; index < history.length; index++) {
      const x = firstX + index * step, y = height - Math.min(1, history[index] / scale) * height;
      shape('rect', {x: x - barWidth / 2, y, width: barWidth, height: height - y,
        fill: options.graphColor, opacity: 0.22});
      if (previous) shape('line', {x1: previous.x, y1: previous.y, x2: x, y2: y,
        stroke: options.graphColor, 'stroke-width': 1.5, 'vector-effect': 'non-scaling-stroke'});
      previous = {x, y};
    }
  }
  function refresh() {
    [headline.textContent, details.textContent] = text(latest, options.isDetailed, history);
    headline.style.color = details.style.color = options.textColor; refreshGraph();
  }
  function apply(snapshot, append) {
    latest = snapshot;
    if (snapshot.sampleCount === 0) history = [];
    else if (append) { history.push(snapshot.framesPerSecond); if (history.length > 120) history.shift(); }
    refresh(); snapshots.notify(latest);
  }
  function observeSource() {
    const version = ++subscriptionVersion, observedSource = source;
    unsubscribe?.(); unsubscribe = null;
    if (!attached || !source) return;
    let initializing = true, delivered = false;
    const stop = observedSource.subscribe(snapshot => {
      if (!disposed && attached && source === observedSource && subscriptionVersion === version) {
        delivered = true; apply(snapshot, initializing ? history.length === 0 : true);
      }
    });
    initializing = false;
    if (disposed || !attached || source !== observedSource || subscriptionVersion !== version) { stop(); return; }
    unsubscribe = stop;
    if (!delivered) apply(observedSource.latestSnapshot, history.length === 0);
  }
  const view = {
    element,
    get source() { return source; },
    set source(value) {
      live(); value = requireSource(value); if (source === value) return;
      unsubscribe?.(); unsubscribe = null; source = value; history = []; apply(emptySnapshot, false);
      observeSource(); changes.notify(view.options);
    },
    get options() { return Object.freeze({source, ...options}); },
    get latestSnapshot() { return latest; },
    get history() { return Object.freeze([...history]); },
    get isAttached() { return attached; },
    subscribe(listener) { live(); return snapshots.add(listener); },
    subscribeOptions(listener) { live(); return changes.add(listener); },
    attach() { live(); if (attached) return; attached = true; observer?.observe(graph); observeSource(); },
    detach() { live(); attached = false; subscriptionVersion++; unsubscribe?.(); unsubscribe = null; observer?.disconnect(); },
    dispose() {
      if (disposed) return;
      disposed = true; attached = false; unsubscribe?.(); unsubscribe = null;
      observer?.disconnect(); snapshots.clear(); changes.clear(); element.remove(); source = null;
    },
  };
  for (const key of Object.keys(defaults)) Object.defineProperty(view, key, {
    enumerable: true, get: () => options[key], set(value) {
      live(); value = key.startsWith('is') ? boolean(value, key) : color(value, key);
      if (options[key] === value) return;
      options[key] = value; refresh(); changes.notify(view.options);
    },
  });
  try { refresh(); observer?.observe(graph); observeSource(); }
  catch (error) { view.dispose(); throw error; }
  return view;
}

/**
 * Creates an owned, pass-through statistics panel in a borrowed positioned Canvas container.
 * Placement/margin/width use CSS logical pixels. source is borrowed, never reset/disposed on
 * replacement or teardown. isEnabled/interval proxy that source; visibility affects display only.
 * detach removes subscriptions/DOM; attach restores them. reset explicitly resets the bound source.
 */
export function createFrameStatisticsOverlay({container, source = null, ...settings} = {}) {
  if (!container?.ownerDocument?.createElement) throw new TypeError('An overlay container is required.');
  source = requireSource(source);
  const options = {isVisible: settings.isVisible ?? true, placement: settings.placement ?? 'top-right',
    margin: settings.margin ?? 12, maximumWidth: settings.maximumWidth ?? 520,
    backgroundColor: settings.backgroundColor ?? 'rgba(25, 36, 56, 0.8509803921568627)'};
  boolean(options.isVisible, 'isVisible'); color(options.backgroundColor, 'backgroundColor');
  nonnegative(options.margin, 'margin'); nonnegative(options.maximumWidth, 'maximumWidth');
  if (!placements.has(options.placement) || options.maximumWidth === 0) throw new TypeError('Invalid overlay placement or width.');
  const collection = {isEnabled: source?.isEnabled ?? true,
    snapshotIntervalMilliseconds: source?.snapshotIntervalMilliseconds ?? 500,
    drawCallCount: source?.drawCallCount ?? null, primitiveCount: source?.primitiveCount ?? null};
  for (const key of Object.keys(collection)) if (settings[key] !== undefined)
    collection[key] = collectionOption(key, settings[key]);
  const document = container.ownerDocument;
  const element = document.createElement('div'), panel = document.createElement('div');
  element.dataset.mu3dStatisticsOverlay = '';
  Object.assign(element.style, {position: 'absolute', inset: '0', display: 'flex', overflow: 'hidden',
    pointerEvents: 'none', boxSizing: 'border-box'});
  Object.assign(panel.style, {boxSizing: 'border-box', minWidth: '0', border: '1px solid #53647d',
    borderRadius: '7px', pointerEvents: 'none'});
  const view = createFrameStatisticsView({document, ...settings});
  if (!options.isVisible) view.detach();
  try { view.source = source; }
  catch (error) { view.dispose(); throw error; }
  panel.appendChild(view.element); element.appendChild(panel);
  try {
    for (const key of Object.keys(collection)) if (settings[key] !== undefined && source) source[key] = collection[key];
  } catch (error) { view.dispose(); element.remove(); throw error; }
  let attached = true, disposed = false, unsubscribe = null, unsubscribeOptions = null, subscriptionVersion = 0,
    latest = source?.latestSnapshot ?? emptySnapshot;
  const snapshots = subscriptions(), changes = subscriptions();
  function live() { if (disposed) throw new Error('The frame statistics overlay is disposed.'); }
  function refresh() {
    element.hidden = !options.isVisible; element.style.display = options.isVisible ? 'flex' : 'none';
    element.style.padding = `${options.margin}px`;
    const [row, column] = options.placement === 'center' ? ['center', 'center'] : options.placement.split('-');
    element.style.alignItems = row === 'top' ? 'flex-start' : row === 'bottom' ? 'flex-end' : 'center';
    element.style.justifyContent = column === 'left' ? 'flex-start' : column === 'right' ? 'flex-end' : 'center';
    panel.style.maxWidth = `${options.maximumWidth}px`; panel.style.width = 'max-content';
    panel.style.maxHeight = '100%'; panel.style.backgroundColor = options.backgroundColor;
    if (attached && options.isVisible) view.attach(); else view.detach();
  }
  function observeSource() {
    const version = ++subscriptionVersion;
    unsubscribe?.(); unsubscribeOptions?.(); unsubscribe = unsubscribeOptions = null;
    if (!attached || !source) return;
    for (const key of Object.keys(collection)) collection[key] = source[key];
    const observedSource = source;
    unsubscribe = source.subscribe(snapshot => {
      if (!disposed && attached && source === observedSource && subscriptionVersion === version) {
        latest = snapshot; snapshots.notify(snapshot);
      }
    });
    unsubscribeOptions = source.subscribeOptions?.(() => {
      if (disposed || !attached || source !== observedSource || subscriptionVersion !== version) return;
      for (const key of Object.keys(collection)) collection[key] = source[key];
      changes.notify(overlay.options);
    }) ?? null;
    latest = source.latestSnapshot;
  }
  const overlay = {
    element, view,
    get source() { return source; },
    set source(value) {
      live(); value = requireSource(value); if (value === source) return;
      unsubscribe?.(); unsubscribeOptions?.(); unsubscribe = unsubscribeOptions = null;
      source = value; view.source = value; latest = value?.latestSnapshot ?? emptySnapshot;
      if (source) for (const key of Object.keys(collection)) collection[key] = source[key];
      observeSource(); snapshots.notify(latest); changes.notify(overlay.options);
    },
    get options() { return Object.freeze({...view.options, source, ...options, ...collection,
      ...(source ? Object.fromEntries(Object.keys(collection).map(key => [key, source[key]])) : {})}); },
    get latestSnapshot() { return latest; },
    get isAttached() { return attached; },
    subscribe(listener) { live(); return snapshots.add(listener); },
    subscribeOptions(listener) { live(); return changes.add(listener); },
    reset() { live(); return source?.reset(); },
    publishSnapshot() { live(); return source?.publishSnapshot?.() ?? latest; },
    attach() {
      live(); if (attached) return;
      attached = true; container.appendChild(element); view.source = source; observeSource(); refresh();
    },
    detach() {
      live(); attached = false; subscriptionVersion++;
      unsubscribe?.(); unsubscribeOptions?.(); unsubscribe = unsubscribeOptions = null;
      view.detach(); element.remove();
    },
    dispose() {
      if (disposed) return;
      overlay.detach(); disposed = true; view.dispose(); snapshots.clear(); changes.clear(); source = null;
    },
  };
  for (const key of Object.keys(defaults)) Object.defineProperty(overlay, key, {
    enumerable: true, get: () => view[key], set(value) { live(); view[key] = value; },
  });
  for (const key of Object.keys(options)) Object.defineProperty(overlay, key, {
    enumerable: true, get: () => options[key], set(value) {
      live();
      if (key === 'isVisible') boolean(value, key);
      else if (key === 'backgroundColor') color(value, key);
      else if (key === 'placement') { if (!placements.has(value)) throw new TypeError('Invalid overlay placement.'); }
      else { nonnegative(value, key); if (key === 'maximumWidth' && value === 0) throw new TypeError('maximumWidth must be positive.'); }
      if (value === options[key]) return;
      options[key] = value; refresh(); changes.notify(overlay.options);
    },
  });
  for (const key of Object.keys(collection)) Object.defineProperty(overlay, key, {
    enumerable: true, get: () => source ? source[key] : collection[key], set(value) {
      live(); value = collectionOption(key, value);
      if ((source ? source[key] : collection[key]) === value) return;
      collection[key] = value;
      if (source) source[key] = value;
      if (!attached || !source) changes.notify(overlay.options);
    },
  });
  let displayOptions = Object.fromEntries(Object.keys(defaults).map(key => [key, view[key]]));
  view.subscribeOptions(() => {
    if (!disposed && Object.keys(defaults).some(key => displayOptions[key] !== view[key])) {
      displayOptions = Object.fromEntries(Object.keys(defaults).map(key => [key, view[key]]));
      changes.notify(overlay.options);
    }
  });
  try { container.appendChild(element); observeSource(); refresh(); }
  catch (error) { overlay.dispose(); throw error; }
  return overlay;
}
