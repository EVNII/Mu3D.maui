import { createManagedViewportHandler } from './mu3d/viewport-handler.mjs';
import { attachOrbitInput } from './mu3d/orbit-input.mjs';
import { attachPointerInput } from './mu3d/pointer-input.mjs';
import { createSceneNodeAnchorSource, createSceneNodeOverlay } from './mu3d/scene-node-overlay.mjs';
import { createFrameStatisticsSource, createFrameStatisticsOverlay } from './mu3d/frame-statistics.mjs';
import { createViewerStatisticsBridge } from './viewer-statistics-bridge.mjs';
import { createViewerNodeAnchorBridge } from './viewer-node-anchors-bridge.mjs';
import { createSceneSelectionSource, attachSceneSelectionInput } from './mu3d/scene-selection.mjs';
import { createViewerSelectionBridge } from './viewer-selection-bridge.mjs';

// Application/demo policy only. Browser lifecycle and input live in optional host packages.
export async function startViewerHost(runtime, log, setStatus) {
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const renderer = exports.Mu3D.Web.Validation.RenderProbe;
  const element = id => document.getElementById(id);
  const canvas = element('mu3d-canvas'), exposure = element('exposure'), output = element('output-mode');
  const metallic = element('metallic'), roughness = element('roughness'), toggle = element('animation-toggle');
  const events = new AbortController(), options = {signal: events.signal};
  const query = new URLSearchParams(location.search);
  // One CSS-pixel gesture boundary: ordinary tap jitter must not become a camera drag.
  const tapTolerance = 6;
  exposure.value = query.get('exposure') === '-2' ? '-2' : '0';
  output.value = query.get('output') === 'sdr' ? 'sdr' : 'hdr';
  canvas.tabIndex = 0;
  canvas.setAttribute('aria-describedby', 'viewer-instructions');
  let initialized = false, resetTiming = false, stopped = false, playing = query.get('animate') !== '0';
  let rotateX = 0, rotateY = 0, dolly = 0, resetCamera = false, lastUi = -Infinity;
  let lastConfig = '', lastDisplay = '';
  let interactionOwner = null, pointerInput, orbitInput, selectionInput, previousSize = '', lastPointerUi = -Infinity;
  let handler;
  const nodeBridge = createViewerNodeAnchorBridge(renderer, createSceneNodeAnchorSource, () => handler?.invalidate());
  const overlays = createSceneNodeOverlay({container: element('canvas-frame'), source: nodeBridge.source, onInteraction(active) {
    if (active) { cancelInteraction(); rotateX = rotateY = dolly = 0; }
  }});
  overlays.bind(element('blue-anchor-label'), {target: nodeBridge.nodes.blue, localPosition: [0, 0.8, 0],
    offset: [0, -6], pivot: [0.5, 1], interactive: false});
  const card = overlays.bind(element('tracked-anchor-card'), {target: nodeBridge.nodes.green, localPosition: [0, 0.8, 0]});
  const selectionBridge = createViewerSelectionBridge(renderer, nodeBridge.nodes, createSceneSelectionSource,
    () => handler?.invalidate(), cancelInteraction);
  const selectionSource = selectionBridge.source;
  function syncSelection(node) {
    card.target = node;
    const key = Object.keys(nodeBridge.nodes).find(key => nodeBridge.nodes[key] === node);
    element('overlay-target').value = key ?? 'none';
    element('overlay-title').textContent = {blue: '蓝球', cone: '圆锥', green: '绿球', model: '整组'}[key] ?? '未选中';
  }
  const stopSelection = selectionSource.subscribeSelection(change => syncSelection(change.newNode));
  syncSelection(selectionSource.selectedNode);
  const statisticsSource = createFrameStatisticsSource();
  const statisticsOverlay = createFrameStatisticsOverlay({container: element('canvas-frame'), source: statisticsSource});
  const statisticsBridge = createViewerStatisticsBridge(renderer, statisticsSource);
  const stopStatisticsOptions = statisticsSource.subscribeOptions(syncStatisticsControls);
  function syncStatisticsControls() {
    element('statistics-enabled').checked = statisticsSource.isEnabled;
    element('statistics-interval').value = String(statisticsSource.snapshotIntervalMilliseconds);
  }
  syncStatisticsControls();
  let resolveReady, rejectReady;
  const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
  handler = createManagedViewportHandler({canvas, maxDimension: renderer.GetMaximumDimension(), continuous: playing,
    properties: {hdr: output.value === 'hdr', exposure: Number(exposure.value)},
    onError(error) {
      nodeBridge.source.clear();
      playing = false; updateToggle();
      log(error?.stack ?? String(error));
      setStatus('failed', '渲染失败，动画已暂停，请查看日志。');
      rejectReady(error);
    },
    callbacks: {
      async draw(size, {hdr, exposure: ev}) {
        const dimensions = `${size.width}:${size.height}:${size.cssWidth}:${size.cssHeight}`;
        if (previousSize && previousSize !== dimensions) cancelInteraction();
        previousSize = dimensions;
        const firstFrame = !initialized;
        if (!initialized) {
          const result = JSON.parse(await renderer.RenderFrame(size.width, size.height, ev, hdr, size.refreshSurface));
          if (stopped) return;
          log(`Startup GPU check: source=${result.ScenePatches}; canvas=${result.CanvasPatches}.`);
          element('pixel-report').textContent = `启动时 GPU 像素检查通过：原始亮度 ${result.ScenePatches.join(' / ')}。动画期间不逐帧回读。`;
          initialized = true;
          resetTiming = true;
        }
        const interval = resetTiming ? 0 : size.frameIntervalSeconds;
        // Exclude startup validation/readback latency from the next animation interval too.
        resetTiming = firstFrame;
        const input = {DeltaSeconds: Math.min(interval, 0.1), FrameIntervalSeconds: interval, Playing: playing,
          RotateX: rotateX, RotateY: rotateY, Dolly: dolly,
          Metallic: Number(metallic.value), Roughness: Number(roughness.value), ResetCamera: resetCamera,
          PixelRatio: size.width / size.cssWidth, LogicalWidth: size.cssWidth, LogicalHeight: size.cssHeight};
        rotateX = rotateY = dolly = 0; resetCamera = false;
        const result = JSON.parse(await renderer.RenderViewerFrame(size.width, size.height, ev, hdr,
          !firstFrame && size.refreshSurface, JSON.stringify(input)));
        if (stopped) return;
        return result;
      },
      disconnect() { renderer.Shutdown(); },
    },
    onFrame(result, size, {hdr, exposure: ev}) {
      const configKey = `${size.width}:${size.height}:${hdr}`;
      if (configKey !== lastConfig || size.refreshSurface) {
        const config = canvas.getContext('webgpu').getConfiguration?.();
        if (config?.format !== (hdr ? 'rgba16float' : 'bgra8unorm') || config.colorSpace !== 'srgb' ||
            config.alphaMode !== 'opaque' || config.toneMapping?.mode !== (hdr ? 'extended' : 'standard'))
          throw new Error('Canvas 未确认请求的 HDR/SDR 配置。');
        if (canvas.width !== size.width || canvas.height !== size.height) throw new Error('Canvas 像素尺寸不匹配。');
        lastConfig = configKey;
        element('resolution').textContent = `原生分辨率 ${size.width} × ${size.height} · 页面尺寸 ${size.cssWidth.toFixed(1)} × ${size.cssHeight.toFixed(1)}`;
        setStatus('passed', '共享 Mu3D PBR 渲染与 Toolkit 已运行');
      }
      const display = hdr ? `HDR 画布 · FP16 / extended · ${ev} EV` : `普通屏幕预览 · 明确裁切到 SDR · ${ev} EV`;
      if (display !== lastDisplay) { element('display-state').textContent = display; lastDisplay = display; }
      nodeBridge.publishFrame(result.NodeAnchors);
      statisticsBridge.update(result.StatisticsVersion);
      const now = performance.now();
      if (!playing || now - lastUi >= 250) {
        const stats = element('viewer-statistics');
        stats.textContent = `${playing ? '旋转中' : '已暂停'} · 第 ${result.Frame} 帧 · 角度 ${(result.Angle * 180 / Math.PI).toFixed(1)}°`;
        // Explicit inspectable state; no timing claims from screenshots or native presentation.
        stats.dataset.result = JSON.stringify(result);
        stats.dataset.playing = String(playing);
        element('model-pose').textContent = result.Selection?.Token === null ? '未选中物体' :
          `${result.Dragging ? `编辑 ${result.ActiveAxis} 轴 · ` : ''}位置 ${(result.Selection?.Position ?? result.ModelPosition).map(v => v.toFixed(2)).join(' / ')} · 缩放 ${(result.Selection?.Scale ?? result.ModelScale).map(v => v.toFixed(2)).join(' / ')}`;
        lastUi = now;
      }
      resolveReady();
    },
  });
  function updateToggle() {
    toggle.textContent = playing ? '暂停旋转' : '继续旋转';
    toggle.setAttribute('aria-pressed', String(playing));
  }
  updateToggle();
  pointerInput = attachPointerInput(canvas, {
    acceptContact(sample) {
      if (!initialized || overlays.hasActivePointer) return false;
      if (sample.pointerType === 'pen' && element('pen-inspect').checked) {
        interactionOwner = 'pen'; orbitInput.cancel(); return true;
      }
      if (sample.button !== 0 || !renderer.BeginViewerDrag(sample.u, sample.v)) return false;
      interactionOwner = 'gizmo';
      orbitInput.cancel(); rotateX = rotateY = dolly = 0;
      // Pause model animation, while the active drag retains the existing viewport render clock.
      // Independent on-demand requests intentionally reset cadence and cannot sample drag FPS.
      playing = false; updateToggle(); handler.setContinuous(true); handler.invalidate();
      return true;
    },
    onSamples(samples) {
      const sample = samples.at(-1);
      if (sample.captured && interactionOwner === 'gizmo') {
        if (sample.phase !== 'cancel') renderer.UpdateViewerDrag(sample.u, sample.v);
        if (sample.phase === 'up' || sample.phase === 'cancel') {
          renderer.EndViewerDrag(sample.phase === 'cancel');
          interactionOwner = null;
          handler.setContinuous(playing);
        }
        handler.invalidate();
      }
      if (sample.captured && (sample.phase === 'up' || sample.phase === 'cancel')) interactionOwner = null;
      const now = performance.now();
      if (now - lastPointerUi < 50 && (sample.phase === 'move' || sample.phase === 'hover')) return;
      lastPointerUi = now;
      const value = field => sample[field] === null ? '未提供' : sample[field].toFixed(2);
      element('pointer-diagnostics').textContent = `${sample.pointerType || '未知设备'} · ${sample.phase} · 压力 ${value('pressure')} · 倾斜 ${value('tiltX')}° / ${value('tiltY')}° · 笔身旋转 ${value('twist')}° · buttons ${sample.buttons ?? '未提供'}${sample.eraser ? ' · 橡皮端' : ''}${sample.barrel ? ' · 侧键' : ''} · 本批 ${samples.length} 个采样`;
      element('pointer-diagnostics').dataset.sample = JSON.stringify(sample);
    },
  });
  // Observe completed taps after Gizmo arbitration, before Orbit's capture handlers.
  selectionInput = attachSceneSelectionInput(canvas, selectionSource, {
    tapTolerance,
    isInputEnabled: () => initialized && interactionOwner === null && !overlays.hasActivePointer,
    onError: error => log(error?.stack ?? String(error)),
  });
  orbitInput = attachOrbitInput(canvas, delta => {
    selectionInput.cancel();
    rotateX += delta.rotateX; rotateY += delta.rotateY; dolly += delta.dolly;
    handler.invalidate();
  }, {isEnabled: () => interactionOwner === null && !overlays.hasActivePointer, dragThreshold: tapTolerance});
  function cancelInteraction() { selectionInput?.cancel(); pointerInput?.cancel(); orbitInput?.cancel(); }
  function configureGizmo(resetModel = false) {
    cancelInteraction();
    renderer.ConfigureViewerGizmo(Number(element('gizmo-mode').value), element('gizmo-space').value === 'local',
      element('gizmo-enabled').checked, resetModel);
    handler.invalidate();
  }
  for (const id of ['gizmo-mode', 'gizmo-space', 'gizmo-enabled'])
    element(id).addEventListener('change', () => configureGizmo(), options);
  element('model-reset').addEventListener('click', () => configureGizmo(true), options);
  element('pen-inspect').addEventListener('change', cancelInteraction, options);
  toggle.addEventListener('click', () => {
    cancelInteraction();
    playing = !playing; updateToggle(); resetTiming = true;
    handler.setContinuous(playing); handler.invalidate();
  }, options);
  element('camera-reset').addEventListener('click', () => { cancelInteraction(); resetCamera = true; handler.invalidate(); }, options);
  function updateMaterial() {
    element('metallic-value').value = Number(metallic.value).toFixed(2);
    element('roughness-value').value = Number(roughness.value).toFixed(2);
    handler.invalidate();
  }
  for (const control of [metallic, roughness]) control.addEventListener('input', () => {
    element('material-preset').value = 'custom'; updateMaterial();
  }, options);
  element('material-preset').addEventListener('change', event => {
    const preset = {satin: [0.35, 0.28], metal: [1, 0.12], matte: [0, 0.85]}[event.target.value];
    if (preset) { [metallic.value, roughness.value] = preset; updateMaterial(); }
  }, options);
  element('overlay-enabled').addEventListener('change', event => overlays.isEnabled = event.target.checked, options);
  for (const [id, property] of [['statistics-visible', 'isVisible'], ['statistics-detailed', 'isDetailed'],
    ['statistics-graph', 'isGraphVisible'], ['statistics-enabled', 'isEnabled']])
    element(id).addEventListener('change', event => { statisticsOverlay[property] = event.target.checked; }, options);
  element('statistics-interval').addEventListener('change', event => {
    statisticsSource.snapshotIntervalMilliseconds = Number(event.target.value);
  }, options);
  element('statistics-placement').addEventListener('change', event => {
    statisticsOverlay.placement = event.target.value;
  }, options);
  element('statistics-reset').addEventListener('click', () => statisticsSource.reset(), options);
  element('overlay-target').addEventListener('change', event => {
    selectionSource.selectedNode = nodeBridge.nodes[event.target.value] ?? null;
  }, options);
  element('selection-enabled').addEventListener('change', event => selectionSource.isEnabled = event.target.checked, options);
  element('selection-clear-miss').addEventListener('change', event => selectionSource.clearSelectionOnMiss = event.target.checked, options);
  element('highlight-enabled').addEventListener('change', event => selectionBridge.configureHighlight(event.target.checked), options);
  element('overlay-height').addEventListener('input', event => {
    card.localPosition = [0, Number(event.target.value), 0];
  }, options);
  const stopCardOptions = card.subscribeOptions(() => {
    selectionSource.selectedNode = card.target;
    syncSelection(selectionSource.selectedNode);
    element('overlay-height').value = String(card.localPosition[1]);
  });
  element('overlay-material').addEventListener('click', () => {
    const metal = Number(metallic.value) < 1;
    metallic.value = metal ? '1' : '0.35'; roughness.value = metal ? '0.12' : '0.28';
    element('material-preset').value = metal ? 'metal' : 'satin';
    updateMaterial();
  }, options);
  for (const control of [exposure, output]) control.addEventListener('change', () => {
    handler.setProperties({hdr: output.value === 'hdr', exposure: Number(exposure.value)});
    handler.invalidate();
  }, options);
  // Keep this restoration hook after pagehide aborts input/UI listeners and releases the session.
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
  window.addEventListener('pagehide', () => {
    stopped = true;
    void handler.dispose().catch(error => log(error?.stack ?? String(error)));
    selectionInput.dispose(); orbitInput.dispose(); pointerInput.dispose();
    stopCardOptions(); stopSelection(); selectionBridge.dispose(); overlays.dispose(); nodeBridge.dispose();
    statisticsOverlay.dispose(); stopStatisticsOptions(); statisticsBridge.dispose(); statisticsSource.dispose();
    events.abort();
  }, {once: true});
  return ready;
}
