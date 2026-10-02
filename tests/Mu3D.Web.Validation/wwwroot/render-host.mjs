import {createManagedViewportHandler} from './mu3d/viewport-handler.mjs';

// Demo display intent and diagnostic UI only. The shared Handler owns browser lifecycle/sizing.
export async function startRenderHost(runtime, log, setStatus) {
  const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
  const renderer = exports.Mu3D.Web.Validation.RenderProbe;
  const canvas = document.querySelector('#mu3d-canvas');
  const exposure = document.querySelector('#exposure');
  const output = document.querySelector('#output-mode');
  const resolution = document.querySelector('#resolution');
  const displayState = document.querySelector('#display-state');
  const report = document.querySelector('#pixel-report');
  const query = new URLSearchParams(location.search);
  exposure.value = query.get('exposure') === '-2' ? '-2' : '0';
  output.value = query.get('output') === 'sdr' ? 'sdr' : 'hdr';
  const hdrMedia = matchMedia('(dynamic-range: high)');
  const events = new AbortController(), options = {signal: events.signal};
  let lastKey = '';
  let firstResolve, firstReject;
  const firstFrame = new Promise((resolve, reject) => { firstResolve = resolve; firstReject = reject; });

  const handler = createManagedViewportHandler({canvas, maxDimension: renderer.GetMaximumDimension(),
    properties: {hdr: output.value === 'hdr', exposure: Number(exposure.value)},
    callbacks: {
      async draw(size, {hdr, exposure: ev}) {
        const key = [size.width, size.height, hdr, ev, size.dpr, hdrMedia.matches].join(':');
        if (key === lastKey && !size.refreshSurface) return null;
        setStatus('running', '正在绘制并验证像素…');
        const result = JSON.parse(await renderer.RenderFrame(size.width, size.height, ev, hdr, size.refreshSurface));
        return {result, key};
      },
      disconnect() { renderer.Shutdown(); },
    },
    onFrame(packet, size, {hdr, exposure: ev}) {
      if (packet === null) return;
      const {result, key} = packet;
      const config = canvas.getContext('webgpu').getConfiguration?.();
      const format = hdr ? 'rgba16float' : 'bgra8unorm';
      const toneMapping = hdr ? 'extended' : 'standard';
      if (!config || config.format !== format || config.colorSpace !== 'srgb' ||
          config.alphaMode !== 'opaque' || config.toneMapping?.mode !== toneMapping)
        throw new Error('浏览器没有确认请求的画布格式和动态范围。可以明确切换普通屏幕预览；不会自动伪装成 HDR。');
      if (canvas.width !== size.width || canvas.height !== size.height)
        throw new Error('画布实际像素尺寸与屏幕原生分辨率不一致。');
      lastKey = key;
      log(`Frame ${result.Frame}: ${size.width}×${size.height}, ${format}/${toneMapping}, ${ev} EV; source=${result.ScenePatches}; canvas=${result.CanvasPatches}; refresh=${size.refreshSurface}.`);
      // The exact physical box can differ from the reported DPR under browser emulation.
      resolution.textContent = `原生分辨率 ${size.width} × ${size.height} · 页面尺寸 ${size.cssWidth.toFixed(1)} × ${size.cssHeight.toFixed(1)} · 像素密度 ${(size.width / size.cssWidth).toFixed(2)}×`;
      resolution.dataset.width = String(size.width);
      resolution.dataset.height = String(size.height);
      resolution.dataset.dpr = String(size.dpr);
      resolution.dataset.sizing = size.sizing;
      displayState.textContent = hdr
        ? `HDR 画布已启用 · ${hdrMedia.matches ? '屏幕报告支持 HDR' : '屏幕未报告 HDR 能力'} · 实际亮度由浏览器、系统与屏幕决定`
        : '普通屏幕预览 · 明确裁切到 SDR，原始 HDR 数据保留';
      displayState.dataset.format = config.format;
      displayState.dataset.range = config.toneMapping.mode;
      displayState.dataset.displayHdr = String(hdrMedia.matches);
      report.textContent = `原始亮度：${result.ScenePatches.join(' / ')}；画布编码值：${result.CanvasPatches.map(v => v.toFixed(4)).join(' / ')}。两段 GPU 像素检查通过。`;
      report.dataset.frame = String(result.Frame);
      report.dataset.result = JSON.stringify(result);
      setStatus('passed', hdr ? 'HDR 配置、像素与原生分辨率检查通过' : 'SDR 预览与原生分辨率检查通过');
      firstResolve();
    },
    onError(error) {
      lastKey = ''; // Returning to the preceding display intent must also recover its status.
      log(error?.stack ?? String(error));
      setStatus('failed', '验证失败，请查看日志；可选择普通屏幕预览。');
      firstReject(error);
    },
  });

  function onIntentChange() {
    const url = new URL(location.href);
    url.searchParams.set('exposure', exposure.value);
    url.searchParams.set('output', output.value);
    history.replaceState(null, '', url);
    handler.setProperties({hdr: output.value === 'hdr', exposure: Number(exposure.value)});
    handler.invalidate();
  }
  exposure.addEventListener('change', onIntentChange, options);
  output.addEventListener('change', onIntentChange, options);
  window.addEventListener('pageshow', event => {
    // pagehide releases the WASM session; history restoration needs a fresh device/session.
    if (event.persisted) location.reload();
  });
  window.addEventListener('pagehide', () => {
    events.abort();
    void handler.dispose().catch(error => log(error?.stack ?? String(error)));
  }, {once: true});
  // ResizeObserver supplies the initial exact physical size; no continuous redraw loop.
  return firstFrame;
}
