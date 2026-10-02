import {createSceneAnchorOverlay} from './mu3d/scene-anchor-overlay.mjs';
import {summarize} from './benchmark-summary.mjs';

const element = id => document.getElementById(id), canvas = element('mu3d-canvas');
const status = (state, text) => { element('status').dataset.state = state; element('status').textContent = text; };
const log = text => { console.log(text); element('log').textContent += `${text}\n`; };
const start = performance.now(), width = 960, height = 576;
const warmup = 120, baseline = 180, samples = 300;
const overlays = createSceneAnchorOverlay({container: element('canvas-frame')});
overlays.bind(element('blue-label'), {anchor:'blue', offset:[0,-6], pivot:[0.5,1]});
overlays.bind(element('green-label'), {anchor:'green'});
let renderer, invalidated = false, running = false;
function fail(error) { log(error?.stack ?? String(error)); status('failed', '测量未完成，请重新加载后重试。'); }
document.addEventListener('visibilitychange', () => { if (running) invalidated = true; });
window.addEventListener('resize', () => { if (running) invalidated = true; });
window.addEventListener('pagehide', () => { invalidated = true; overlays.dispose(); renderer?.Shutdown(); });
element('reload').addEventListener('click', () => location.reload());

try {
  const {dotnet} = await import('./_framework/dotnet.js');
  const runtime = await dotnet.withModuleConfig({print:log, printErr:log}).create();
  if (await runtime.runMain(runtime.getConfig().mainAssemblyName, []) !== 0) throw new Error('Runtime initialization failed.');
  renderer = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Mu3D.Web.Validation.RenderProbe;
  const build = renderer.GetBenchmarkBuild();
  element('build').textContent = build;
  const startup = JSON.parse(await renderer.RenderFrame(width,height,0,true,true));
  const readyMilliseconds = performance.now() - start;
  log(`GPU 检查：原始亮度 ${startup.ScenePatches.join(' / ')}；输出 ${startup.CanvasPatches.join(' / ')}。`);
  status('ready', 'HDR 像素检查通过，可以开始测量。');
  element('start').disabled = false;
  element('start').addEventListener('click', async () => {
    element('start').disabled = true;
    running = true;
    try {
      if (document.hidden) throw new Error('Benchmark needs a visible document.');
      const bounds = canvas.getBoundingClientRect();
      const input = {DeltaSeconds:1/60,FrameIntervalSeconds:1/60,Playing:true,
        RotateX:0,RotateY:0,Dolly:0,Metallic:0.35,Roughness:0.28,ResetCamera:false,
        PixelRatio:width/bounds.width,LogicalWidth:bounds.width,LogicalHeight:bounds.height};
      const periods = {}, finalStates = {};
      let managed;
      for (const [name,count] of [['warmup',warmup],['baseline',baseline],['profile',samples]]) {
        status('running', {warmup:'预热中…',baseline:'测量普通调用…',profile:'测量各阶段耗时与分配…'}[name]);
        const data = {roundTripMs:[],inputJsonMs:[],outputJsonMs:[],overlayMs:[],rafIntervalMs:[]};
        let previousRaf = null;
        if (name === 'profile') renderer.BeginBenchmark(count);
        for (let i=0;i<count;i++) {
          const raf = await new Promise(resolve => requestAnimationFrame(resolve));
          if (invalidated || document.hidden) throw new Error('Visibility or window size changed during measurement.');
          if (previousRaf !== null) data.rafIntervalMs.push(raf-previousRaf);
          previousRaf = raf;
          let t = performance.now();
          const inputJson = JSON.stringify(input);
          data.inputJsonMs.push(performance.now()-t);
          t = performance.now();
          const json = await renderer.RenderViewerFrame(width,height,0,true,false,inputJson);
          data.roundTripMs.push(performance.now()-t);
          t = performance.now();
          const report = JSON.parse(json);
          data.outputJsonMs.push(performance.now()-t);
          t = performance.now(); overlays.update(report.Anchors);
          data.overlayMs.push(performance.now()-t);
          if (i === count-1) finalStates[name] = report;
        }
        periods[name] = data;
        if (name === 'profile') managed = JSON.parse(renderer.FinishBenchmark());
      }
      const config = canvas.getContext('webgpu').getConfiguration();
      if (config.format !== 'rgba16float' || config.toneMapping.mode !== 'extended') throw new Error('HDR configuration changed.');
      // Batch synchronous echo provides a lower-bound interop comparison, not inferred frame overhead.
      const echoPayload = JSON.stringify(finalStates.profile), echoBatches = [];
      for (let b=0;b<12;b++) {
        await new Promise(resolve => requestAnimationFrame(resolve));
        if (invalidated || document.hidden) throw new Error('Benchmark lost visibility.');
        const t = performance.now();
        for (let i=0;i<200;i++) {
          if (renderer.BenchmarkEcho(echoPayload) !== echoPayload) throw new Error('Interop echo changed its payload.');
        }
        echoBatches.push((performance.now()-t)/200);
      }
      const summary = {
        baselineRoundTrip:summarize(periods.baseline.roundTripMs), profileRoundTrip:summarize(periods.profile.roundTripMs),
        managedTotal:summarize(managed.TotalMilliseconds), allocatedBytes:summarize(managed.TotalAllocatedBytes),
        stages:managed.Stages.map((name,i) => ({name,time:summarize(managed.StageMilliseconds[i]),bytes:summarize(managed.StageAllocatedBytes[i])})),
        renderer:Object.fromEntries(['PrepareMilliseconds','EncodeMilliseconds','SubmitMilliseconds','CacheTrimMilliseconds','TotalMilliseconds']
          .map(key => [key,summarize(managed.RendererCpuTimings.map(frame => frame[key]))])),
        js:Object.fromEntries(Object.entries(periods.profile).map(([key,values]) => [key,summarize(values)])),
        synchronousEcho:summarize(echoBatches),
      };
      const result = {schema:1,build,source:location.pathname,userAgent:navigator.userAgent,width,height,
        logicalWidth:bounds.width,logicalHeight:bounds.height,devicePixelRatio:devicePixelRatio,
        readyMilliseconds,warmup,baseline,samples,stepSeconds:1/60,startup,summary,managed,periods,finalStates,
        echoPayloadCharacters:echoPayload.length,echoBatches,
        note:'Loopback load time is cache/environment dependent, not cold network startup. Managed current-thread allocations exclude JS/native/GPU. CPU wall time includes probe overhead/back-pressure, not GPU execution or presentation. RAF cadence is not throughput.'};
      element('benchmark-json').value = JSON.stringify(result);
      const body = element('results').querySelector('tbody');
      const rows = [['普通调用往返',summary.baselineRoundTrip,null],['分段统计调用往返',summary.profileRoundTrip,null],
        ['C# 总计',summary.managedTotal,summary.allocatedBytes],
        ...summary.stages.map(stage => [stage.name,stage.time,stage.bytes]),
        ['JS 输入 JSON',summary.js.inputJsonMs,null],['JS 输出 JSON',summary.js.outputJsonMs,null],
        ['HTML 跟踪更新',summary.js.overlayMs,null],['同步字符串往返（单次批均值）',summary.synchronousEcho,null]];
      for (const [label,time,bytes] of rows) {
        const row=document.createElement('tr');
        for (const text of [label,time.median.toFixed(4),time.p95.toFixed(4),bytes?.mean.toFixed(0) ?? '—']) {
          const cell=document.createElement('td');cell.textContent=text;row.appendChild(cell);
        }
        body.appendChild(row);
      }
      element('results').hidden = false;
      status('passed', '测量完成。普通调用与分段结果已分别记录；重新加载可重复测试。');
    } catch (error) { fail(error); }
    finally { running = false; }
  }, {once:true});
} catch (error) { fail(error); }
