import {createManagedViewportHandler} from './mu3d/viewport-handler.mjs';
import {createFrameStatisticsSource, createFrameStatisticsOverlay} from './mu3d/frame-statistics.mjs';
import {createSceneNodeAnchorSource, createSceneNodeOverlay} from './mu3d/scene-node-overlay.mjs';

// Sample-owned input policy; all camera/gizmo/projection mathematics remain in shared Toolkit.
export function create(canvas, container, managed, maxDimension, id, cyanLabel, magentaLabel) {
  const events = new AbortController(), options = {signal: events.signal}, pointers = new Map(), held = new Set();
  const previousTouchAction = canvas.style.touchAction;
  canvas.style.touchAction = 'none';
  let enabled = false, continuous = false, scheduling = false, disposal, sizeKey = '', pair = null;
  let rotateX = 0, rotateY = 0, panX = 0, panY = 0, dolly = 0;
  let mapKeys = ['A', 'D', 'W', 'S', 'E', 'Q'];
  const statistics = createFrameStatisticsSource({snapshotIntervalMilliseconds: 100});
  const panel = createFrameStatisticsOverlay({container, source: statistics, isVisible: false,
    placement: id === 'declarative-tools' ? 'top-left' : 'top-right', graphColor: '#42E8FF'});
  const frameListeners = new Set();
  let anchorSource, anchorOverlay;
  if (id === 'ui-anchors') {
    anchorSource = createSceneNodeAnchorSource({sourceId: managed.invokeMethod('AnchorSourceId'),
      configureAnchor: ({id: registration, target, localPosition}) => JSON.parse(managed.invokeMethod('ConfigureAnchor', registration ?? '', target ?? '', ...localPosition)),
      removeAnchor: registration => managed.invokeMethod('RemoveAnchor', registration),
      subscribeFrames(listener) { frameListeners.add(listener); return () => frameListeners.delete(listener); },
    });
    anchorOverlay = createSceneNodeOverlay({container, source: anchorSource});
    anchorOverlay.bind(cyanLabel, {target: anchorSource.node('cyan'), localPosition: [0, .5, 0], interactive: false});
    anchorOverlay.bind(magentaLabel, {target: anchorSource.node('magenta'), localPosition: [0, .5, 0], interactive: false});
  }
  const handler = createManagedViewportHandler({canvas, maxDimension, properties: {hdr: true}, callbacks: {
    async draw(size, {hdr}) {
      const next = [size.width, size.height, size.cssWidth, size.cssHeight].join(':');
      if (sizeKey && sizeKey !== next) cancel(); sizeKey = next;
      const commands = [rotateX, rotateY, panX, panY, dolly]; clearCommands();
      return managed.invokeMethodAsync('DrawFrame', size.width, size.height, size.deltaSeconds, size.frameIntervalSeconds,
        size.width / size.cssWidth, size.cssWidth, size.cssHeight, ...commands, keyMask(), hdr, size.refreshSurface);
    },
    disconnect: () => managed.invokeMethodAsync('Disconnect'),
  }, onFrame(result, size, {hdr}) {
    const config = canvas.getContext('webgpu').getConfiguration?.();
    if (canvas.width !== size.width || canvas.height !== size.height || config?.format !== (hdr ? 'rgba16float' : 'bgra8unorm') ||
      config.colorSpace !== 'srgb' || config.alphaMode !== 'opaque' || config.toneMapping?.mode !== (hdr ? 'extended' : 'standard'))
      throw new Error('浏览器未确认请求的画布尺寸或 HDR/SDR 配置。');
    const report = JSON.parse(result);
    if (report.statistics) statistics.publish(JSON.parse(report.statistics));
    if (report.anchors) for (const listener of [...frameListeners]) listener(JSON.parse(report.anchors));
    schedule(report.continuous);
  }, onError(error) {
    cancel(); anchorSource?.clear();
    void managed.invokeMethodAsync('RenderFailed', error.message).catch(console.error);
  }});
  function clearCommands() { rotateX = rotateY = panX = panY = dolly = 0; }
  function schedule(pending = false) {
    const next = continuous || pending || keyMask() !== 0 || [...pointers.values()].some(pointer => pointer.claim === 1);
    // Reapplying setContinuous resets the RAF clock; only transition when its value changes.
    if (next !== scheduling) { scheduling = next; handler.setContinuous(next); }
  }
  const coordinates = event => {
    const rect = canvas.getBoundingClientRect();
    return {u: (event.clientX - rect.left) / rect.width, v: (event.clientY - rect.top) / rect.height};
  };
  function drag(dx, dy, action) {
    const rect = canvas.getBoundingClientRect(); if (!(rect.width > 0 && rect.height > 0)) return;
    const fly = id === 'fly-controls', referenceX = fly ? Math.min(rect.width, rect.height) : rect.width;
    const referenceY = fly ? Math.min(rect.width, rect.height) : rect.height;
    if (action === 'rotate') {
      rotateX += dx / referenceX * Math.PI * (fly ? 1 : -2);
      rotateY -= dy / referenceY * Math.PI * (fly ? 1 : 2);
    } else { panX -= dx / referenceX; panY += dy / referenceY; }
    handler.invalidate();
  }
  function pairGeometry() {
    const first = [...pointers.values()].slice(0, 2);
    if (first.length < 2) return null;
    return {x: (first[0].x + first[1].x) / 2, y: (first[0].y + first[1].y) / 2,
      distance: Math.hypot(first[0].x - first[1].x, first[0].y - first[1].y)};
  }
  canvas.addEventListener('pointerdown', event => {
    if (event.defaultPrevented || event.button !== 0 && event.button !== 2) return;
    if ([...pointers.values()].some(pointer => pointer.claim === 1)) { event.preventDefault(); return; }
    const contact = coordinates(event);
    const claim = event.button === 0 && pointers.size === 0 ? managed.invokeMethod('BeginContact', contact.u, contact.v) : 0;
    if (!claim && !enabled && id !== 'declarative-tools') return;
    pointers.set(event.pointerId, {id: event.pointerId, x: event.clientX, y: event.clientY, startX: event.clientX, startY: event.clientY,
      claim, button: event.button, dragging: false, maySelect: id === 'declarative-tools' && event.button === 0});
    canvas.setPointerCapture(event.pointerId); canvas.focus({preventScroll: true}); event.preventDefault();
    if (pointers.size > 1) { for (const pointer of pointers.values()) { pointer.dragging = true; pointer.maySelect = false; } pair = pairGeometry(); }
    if (claim) { clearCommands(); schedule(); handler.invalidate(); }
  }, options);
  canvas.addEventListener('pointermove', event => {
    const pointer = pointers.get(event.pointerId); if (!pointer) return;
    event.preventDefault();
    const samples = event.getCoalescedEvents?.() ?? [];
    for (const sample of [...samples, event]) {
      if (!Number.isFinite(sample.clientX) || !Number.isFinite(sample.clientY)) continue;
      if (pointer.claim === 1) { const point = coordinates(sample); managed.invokeMethod('UpdateContact', point.u, point.v); handler.invalidate(); continue; }
      if (pointer.claim === 2) continue;
      if (pointers.size > 1) {
        pointer.x = sample.clientX; pointer.y = sample.clientY;
        const current = pairGeometry();
        if (enabled && pair && current) {
          drag(current.x - pair.x, current.y - pair.y, id === 'map-controls' ? 'rotate' : 'pan');
          if (pair.distance > 0 && current.distance > 0) dolly += Math.log(current.distance / pair.distance) * (id === 'fly-controls' ? 6 : 1.6);
          handler.invalidate();
        }
        pair = current; continue;
      }
      if (!pointer.dragging) {
        if (Math.hypot(sample.clientX - pointer.startX, sample.clientY - pointer.startY) <= 6) continue;
        pointer.dragging = true; pointer.maySelect = false;
      }
      if (enabled) drag(sample.clientX - pointer.x, sample.clientY - pointer.y,
        id === 'map-controls' ? pointer.button === 0 ? 'pan' : 'rotate' : pointer.button === 0 ? 'rotate' : 'pan');
      pointer.x = sample.clientX; pointer.y = sample.clientY;
    }
  }, options);
  function end(event, canceled) {
    const pointer = pointers.get(event.pointerId); if (!pointer) return;
    // Consume terminal movement, so a quick press/up cannot incorrectly become a stationary tap.
    const moved = Math.hypot(event.clientX - pointer.startX, event.clientY - pointer.startY) > 6;
    if (!canceled && !pointer.claim && enabled && pointers.size === 1 && (pointer.dragging || moved))
      drag(event.clientX - pointer.x, event.clientY - pointer.y,
        id === 'map-controls' ? pointer.button === 0 ? 'pan' : 'rotate' : pointer.button === 0 ? 'rotate' : 'pan');
    pointers.delete(event.pointerId);
    if (canvas.hasPointerCapture(event.pointerId)) canvas.releasePointerCapture(event.pointerId);
    if (pointer.claim === 1) {
      if (!canceled) { const point = coordinates(event); managed.invokeMethod('UpdateContact', point.u, point.v); }
      managed.invokeMethod('EndContact', canceled);
    } else if (!canceled && pointer.maySelect && !moved) { const point = coordinates(event); managed.invokeMethod('SelectAt', point.u, point.v); }
    for (const remaining of pointers.values()) { remaining.startX = remaining.x; remaining.startY = remaining.y; remaining.dragging = true; remaining.maySelect = false; }
    pair = pairGeometry(); schedule(); handler.invalidate();
  }
  canvas.addEventListener('pointerup', event => end(event, false), options);
  canvas.addEventListener('pointercancel', event => end(event, true), options);
  canvas.addEventListener('lostpointercapture', event => end(event, true), options);
  canvas.addEventListener('contextmenu', event => { if (enabled) event.preventDefault(); }, options);
  canvas.addEventListener('wheel', event => {
    if (!enabled || event.defaultPrevented || event.metaKey) return;
    event.preventDefault();
    const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? canvas.clientHeight : 1;
    if (event.ctrlKey) dolly -= event.deltaY * unit * .01 * (id === 'fly-controls' ? 6 : 1.6);
    else if (event.shiftKey || Math.abs(event.deltaX) > 0) drag(event.deltaX * unit, event.deltaY * unit, id === 'map-controls' ? 'rotate' : 'pan');
    else dolly += Math.max(-1, Math.min(1, -event.deltaY * unit / 120)) * (id === 'fly-controls' ? 1 : .12);
    handler.invalidate();
  }, {...options, passive: false});
  const normalizeKey = key => key.length === 1 ? key.toUpperCase() : key;
  function bindings() { return id === 'fly-controls' ? ['W', 'S', 'A', 'D', 'E', 'Q', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']
    : id === 'map-controls' ? mapKeys : ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', '+', '-']; }
  function keyMask() {
    if (!enabled) return 0;
    let result = 0;
    for (const key of held) {
      if (key === 'None') continue;
      let index = bindings().indexOf(key);
      if (index < 0 && id === 'map-controls') index = key === 'PageUp' ? 4 : key === 'PageDown' ? 5 : -1;
      if (index >= 0) result |= 1 << index;
    }
    return result;
  }
  window.addEventListener('keydown', event => {
    if (event.key === 'Escape') { cancel(); handler.invalidate(); return; }
    if (!enabled || event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey || event.target?.closest?.('input,select,textarea,[contenteditable]')) return;
    const key = normalizeKey(event.key);
    if (!bindings().includes(key) && !(id === 'map-controls' && ['PageUp', 'PageDown'].includes(key))) return;
    held.add(key); event.preventDefault(); schedule(); handler.invalidate();
  }, options);
  window.addEventListener('keyup', event => { if (held.delete(normalizeKey(event.key))) { schedule(); handler.invalidate(); } }, options);
  function cancel() {
    const active = [...pointers.values()]; pointers.clear(); pair = null;
    if (active.some(pointer => pointer.claim === 1)) managed.invokeMethod('EndContact', true);
    for (const pointer of active) if (canvas.hasPointerCapture(pointer.id)) canvas.releasePointerCapture(pointer.id);
    held.clear(); clearCommands(); schedule();
  }
  window.addEventListener('blur', () => { cancel(); anchorSource?.clear(); }, options);
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') { cancel(); anchorSource?.clear(); } }, options);
  function dispose() {
    if (!disposal) {
      cancel(); events.abort(); canvas.style.touchAction = previousTouchAction;
      anchorOverlay?.dispose(); anchorSource?.dispose(); panel.dispose(); statistics.dispose();
      disposal = handler.dispose();
    }
    return disposal;
  }
  window.addEventListener('pagehide', () => { void dispose().catch(console.error); }, options);
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); }, options);
  return {
    configure(enableNavigation, hdr, animate, showStatistics, detailed, interval, keys, enableGizmo = true) {
      if (enabled !== enableNavigation || keys.some((key, index) => key !== mapKeys[index]) ||
          !enableGizmo && [...pointers.values()].some(pointer => pointer.claim === 1)) cancel();
      enabled = enableNavigation; mapKeys = [...keys]; continuous = animate;
      panel.isVisible = showStatistics; panel.isDetailed = detailed; statistics.snapshotIntervalMilliseconds = interval;
      schedule(); handler.setProperties({hdr}); handler.invalidate();
    },
    resetStatistics() { statistics.reset(); },
    dispose,
  };
}
