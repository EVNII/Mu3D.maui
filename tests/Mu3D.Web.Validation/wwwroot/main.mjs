const inBrowser = typeof document !== 'undefined';
const suites = {
  Render: ['实际渲染', '共享 Mu3D 场景 → FP16 GPU 纹理 → 浏览器 Canvas，并读回真实 GPU 像素验证。'],
  RenderPerf: ['解释器对照', '与 AOT 相同源码的独立性能对照构建。'],
  RenderAot: ['AOT 渲染对照', '共享 Mu3D 场景与 Toolkit 的 AOT 构建。'],
  Smoke: ['基础运行', '检查场景、网格、相机、FP16 HDR 数据及异步 C 回调。'],
  Creative: ['HDR 画布逻辑', '执行现有绘画核心的 215 项检查。'],
  Graphics: ['图形接口', '检查资源、命令与设备丢失契约；这里使用测试设备。'],
  HostContracts: ['宿主契约', '复用桌面测试源码，检查共享逻辑与 Canvas Handler 边界。'],
  HostContractsAot: ['AOT 宿主契约', '同一套宿主与共享契约的裁剪 AOT 构建。'],
  SmokeAot: ['AOT 构建', '执行预先编译为 WASM 的基础检查；构建保留了完整 Core。'],
  WgpuAbi: ['WGPU 接口', '检查 wasm32 布局和通过 C 适配展开的回调参数。'],
  Emdawn: ['WebGPU 接入', '通过 .NET → C → Emdawn 请求当前浏览器的 GPU 适配器。'],
};
const suite = inBrowser ? location.pathname.split('/').find(part => part in suites) ?? 'Smoke' : null;
const isRender = ['Render','RenderPerf','RenderAot'].includes(suite);
const params = inBrowser ? new URLSearchParams(location.search) : null;
const args = inBrowser ? params.getAll('arg') : process.argv.slice(2);
if (suite === 'WgpuAbi' && !params.has('raw')) args.push('--flat-callbacks');
const log = message => {
  console.log(message);
  if (inBrowser) {
    const element = document.querySelector('#log');
    element.textContent = (element.textContent + `${message}\n`).slice(-16000);
  }
};
const setStatus = (state, text) => {
  if (!inBrowser) return;
  const element = document.querySelector('#status');
  element.dataset.state = state;
  element.textContent = text;
};
if (inBrowser) {
  document.querySelector('#suite').textContent = suites[suite][0];
  document.querySelector('#description').textContent = suites[suite][1];
  document.querySelector('#environment').textContent =
    `安全上下文：${isSecureContext ? '是' : '否'} · WebGPU API：${navigator.gpu ? '可用' : '不可用'}`;
  document.querySelector(`nav a[href="/${suite}/"]`)?.setAttribute('aria-current', 'page');
  document.querySelector('#rerun').addEventListener('click', () => location.reload());
  if (isRender) {
    document.querySelector('#render-panel').hidden = false;
    if (params.get('demo') === 'viewer') document.querySelector('#viewer-controls').hidden = false;
    if (params.get('layout') === 'bare' && params.get('demo') !== 'viewer') {
      // Change DOM containment before GPU initialization; keep the existing Canvas and renderer.
      document.documentElement.classList.add('bare-render');
      document.body.prepend(document.querySelector('#canvas-frame'));
      const info = document.querySelector('#bare-render-info');
      info.hidden = false;
      for (const id of ['status', 'resolution', 'pixel-report', 'log'])
        info.append(document.getElementById(id));
    }
  }
}

try {
  const { dotnet } = await import('./_framework/dotnet.js');
  if (inBrowser) dotnet.withModuleConfig({ print: log, printErr: log });
  const runtime = await dotnet.create();
  setStatus('running', '正在执行验证…');
  const exitCode = await runtime.runMain(runtime.getConfig().mainAssemblyName, args);
  if (!inBrowser) process.exitCode = exitCode;
  else if (isRender && exitCode === 0) {
    if (params.get('demo') === 'viewer') {
      const { startViewerHost } = await import('./viewer-host.mjs');
      await startViewerHost(runtime, log, setStatus);
    } else {
      const { startRenderHost } = await import('./render-host.mjs');
      await startRenderHost(runtime, log, setStatus);
    }
  } else {
    const unavailable = suite === 'Emdawn' && !navigator.gpu;
    setStatus(exitCode === 0 ? 'passed' : 'failed', exitCode === 0
      ? unavailable ? '无 GPU 的错误回调验证通过；本浏览器没有提供 WebGPU。' : '验证通过'
      : `验证失败（退出码 ${exitCode}），请查看日志。`);
  }
} catch (error) {
  log(error?.stack ?? String(error));
  setStatus('failed', '验证失败，请查看日志。');
  if (!inBrowser) process.exitCode = 1;
}
