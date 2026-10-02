// Diagnostic control only. Not a Mu3D renderer/backend or a proposed product implementation.
const canvas = document.querySelector('#canvas');
const status = document.querySelector('#status');
const logElement = document.querySelector('#log');
const log = value => { logElement.textContent = (logElement.textContent + value + '\n').slice(-16000); };
const boundary = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
const query = new URLSearchParams(location.search);
const viewportLayout = query.get('layout') === 'viewport';
document.documentElement.classList.toggle('viewport-canvas',viewportLayout);
const alphaMode = query.get('alpha') === 'premultiplied' ? 'premultiplied' : 'opaque';
const initialReadback = query.get('readback') !== '0';
let looping = query.get('loop') === '1', loopRaf = 0;
const loopButton = document.querySelector('#loop');
const updateLoopButton = () => { loopButton.textContent = looping ? '停止连续提交并检查像素' : '开始连续提交'; };
document.querySelector('#variant').textContent = `本次对照：alphaMode = ${alphaMode}；初始${looping ? '连续提交' : '只画一帧'}；${initialReadback ? '首帧检查 GPU 像素' : '首帧不回读 GPU'}；${viewportLayout ? '画布铺满视口，文字覆盖其上' : '嵌入式画布'}。所有像素 alpha 仍为 1。`;
updateLoopButton();
let busy = false, frame = 0, configurations = 0, clicks = 0;
document.querySelector('#noop').onclick = () => { document.querySelector('#clicks').textContent = `点击 ${++clicks} 次`; };
// Deliberately no GPU or focus/visibility handler on the unrelated native select.
function half(bits) {
  const sign = bits & 0x8000 ? -1 : 1, exponent = (bits >> 10) & 31, mantissa = bits & 1023;
  return sign * (exponent === 0 ? mantissa * 2 ** -24 : exponent === 31 ? (mantissa ? NaN : Infinity) : (1 + mantissa / 1024) * 2 ** (exponent - 15));
}
try {
  if (!navigator.gpu) throw new Error('浏览器未提供 WebGPU。');
  const adapter = await navigator.gpu.requestAdapter();
  if (!adapter) throw new Error('没有可用 GPU。');
  const device = await adapter.requestDevice();
  let gpuError;
  const fail = error => {
    gpuError = error; looping = false; cancelAnimationFrame(loopRaf); loopRaf = 0;
    updateLoopButton(); status.textContent = 'GPU 错误'; log(error.message ?? String(error));
  };
  device.addEventListener('uncapturederror', event => fail(event.error));
  device.lost.then(info => { if (info.reason !== 'destroyed') fail(new Error(`GPU 设备丢失：${info.message}`)); });
  const context = canvas.getContext('webgpu');
  if (canvas.getBoundingClientRect().width <= 0 || canvas.getBoundingClientRect().height <= 0) {
    status.textContent = '等待 Canvas 获得可见布局尺寸…';
    await new Promise(resolve => {
      const observer = new ResizeObserver(() => {
        const rect = canvas.getBoundingClientRect();
        if (rect.width > 0 && rect.height > 0) { observer.disconnect(); resolve(); }
      });
      observer.observe(canvas);
    });
  }
  const css = canvas.getBoundingClientRect(), dpr = devicePixelRatio || 1;
  canvas.width = Math.round(css.width * dpr); canvas.height = Math.round(css.height * dpr);
  const shader = device.createShaderModule({code: `
    @group(0) @binding(0) var<uniform> width: vec4f;
    @vertex fn vs(@builtin(vertex_index) i:u32) -> @builtin(position) vec4f {
      let p = array<vec2f,3>(vec2f(-1,-1),vec2f(3,-1),vec2f(-1,3)); return vec4f(p[i],0,1);
    }
    @fragment fn fs(@builtin(position) p:vec4f) -> @location(0) vec4f {
      let v = array<f32,4>(0.25,1,2,4)[min(u32(p.x / width.x * 4),3u)];
      let e = 1.055 * pow(v,1.0/2.4) - 0.055; return vec4f(e,e,e,1);
    }`});
  const uniform = device.createBuffer({size:16,usage:GPUBufferUsage.UNIFORM|GPUBufferUsage.COPY_DST});
  device.queue.writeBuffer(uniform,0,new Float32Array([canvas.width,0,0,0]));
  const pipelines = new Map();
  for (const format of ['rgba16float','bgra8unorm']) {
    const pipeline = device.createRenderPipeline({layout:'auto',vertex:{module:shader,entryPoint:'vs'},fragment:{module:shader,entryPoint:'fs',targets:[{format}]}});
    pipelines.set(format,{pipeline,group:device.createBindGroup({layout:pipeline.getBindGroupLayout(0),entries:[{binding:0,resource:{buffer:uniform}}]})});
  }
  const buffer = device.createBuffer({size:1024,usage:GPUBufferUsage.COPY_DST|GPUBufferUsage.MAP_READ});
  let format = 'rgba16float', range = 'extended';
  function configure(nextFormat = 'rgba16float', nextRange = 'extended') {
    format = nextFormat; range = nextRange;
    context.configure({device,format,usage:GPUTextureUsage.RENDER_ATTACHMENT|GPUTextureUsage.COPY_SRC,colorSpace:'srgb',alphaMode,toneMapping:{mode:range}});
    configurations++;
  }
  function submitFrame(readback) {
    if (gpuError) throw gpuError;
    const texture = context.getCurrentTexture(), encoder = device.createCommandEncoder();
    const pass = encoder.beginRenderPass({colorAttachments:[{view:texture.createView(),loadOp:'clear',storeOp:'store',clearValue:[0,0,0,1]}]});
    const {pipeline,group} = pipelines.get(format);
    pass.setPipeline(pipeline); pass.setBindGroup(0,group); pass.draw(3); pass.end();
    if (readback) for (let i=0;i<4;i++) encoder.copyTextureToBuffer({texture,origin:[Math.floor(canvas.width*(i+0.5)/4),Math.floor(canvas.height/2)]},{buffer,offset:i*256,bytesPerRow:256},[1,1]);
    device.queue.submit([encoder.finish()]);
    status.dataset.submittedFrames = String(++frame);
  }
  async function draw(reason, readback = true) {
    submitFrame(readback);
    let patches = null, alphas = null;
    if (readback) {
      await buffer.mapAsync(GPUMapMode.READ);
      const view = new DataView(buffer.getMappedRange());
      patches = Array.from({length:4},(_,i) => format === 'rgba16float' ? half(view.getUint16(i*256,true)) : view.getUint8(i*256)/255);
      alphas = Array.from({length:4},(_,i) => format === 'rgba16float' ? half(view.getUint16(i*256+6,true)) : view.getUint8(i*256+3)/255);
      buffer.unmap();
    }
    await boundary();
    if (gpuError) throw gpuError;
    const config = context.getConfiguration();
    const expected = format === 'rgba16float' ? [0.5371,1,1.3533,1.8248] : [0.5371,1,1,1];
    if (config.format !== format || config.toneMapping.mode !== range || config.alphaMode !== alphaMode || (readback && (patches.some((v,i)=>!Number.isFinite(v)||Math.abs(v-expected[i])>0.007) || alphas.some(v=>v!==1)))) throw new Error('配置或 GPU 像素不符合预期。');
    const result = {frame,configurations,reason,format,range,alphaMode,looping,pixelsChecked:readback,patches,alphas,size:[canvas.width,canvas.height]};
    status.textContent = `${readback ? 'GPU 像素通过' : '配置通过（未回读像素）'} · 检查时已绘制 ${frame} 次 · 配置 ${configurations} 次 · ${range} / ${alphaMode}${looping ? ' · 连续提交中' : ''}`;
    status.dataset.result = JSON.stringify(result); log(JSON.stringify(result));
  }
  function scheduleLoop() {
    if (!looping || loopRaf || busy || gpuError) return;
    loopRaf = requestAnimationFrame(() => {
      loopRaf = 0;
      try { submitFrame(false); scheduleLoop(); } catch (error) { fail(error); }
    });
  }
  async function run(action) {
    if (busy) return; busy=true;
    cancelAnimationFrame(loopRaf); loopRaf = 0;
    try { await action(); } catch (error) { status.textContent='验证失败';log(error.stack ?? String(error)); }
    finally {busy=false;scheduleLoop();}
  }
  document.querySelector('#redraw').onclick = () => run(()=>draw('redraw only'));
  document.querySelector('#reconfigure').onclick = () => run(async()=>{configure();await draw('HDR reconfigure');});
  document.querySelector('#range-cycle').onclick = () => run(async()=>{configure('rgba16float','standard');await draw('standard, still FP16');configure();await draw('extended, still FP16');});
  document.querySelector('#format-cycle').onclick = () => run(async()=>{configure('bgra8unorm','standard');await draw('SDR format');configure();await draw('HDR format');});
  loopButton.onclick = () => {
    if (busy) return;
    looping = !looping; updateLoopButton(); cancelAnimationFrame(loopRaf); loopRaf = 0;
    if (looping) scheduleLoop(); else void run(()=>draw('loop stopped; same pixels'));
  };
  window.addEventListener('pagehide',()=>{looping=false;cancelAnimationFrame(loopRaf);device.destroy();},{once:true});
  log(JSON.stringify({userAgent:navigator.userAgent,reportedHdr:matchMedia('(dynamic-range: high)').matches,css:[css.width,css.height],dpr,alphaMode,initialLoop:looping,initialReadback}));
  configure(); await run(()=>draw('initial, no warmup',initialReadback));
} catch (error) { status.textContent='启动失败';log(error.stack ?? String(error)); }
