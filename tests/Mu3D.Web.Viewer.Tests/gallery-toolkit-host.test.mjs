import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {setMaxListeners} from 'node:events';
import {createViewportEnvironment} from './support/viewport-fixture.mjs';
import {createTestDocument} from './support/dom-fixture.mjs';
import {sendEvent, primaryPointer} from './support/pointer-fixture.mjs';
setMaxListeners(0);
async function hostModule(name) {
  let source = await readFile(new URL(`../../samples/GalleryApp/Web/wwwroot/${name}.mjs`, import.meta.url), 'utf8');
  for (const [module, folder] of [['viewport-handler','Mu3D.Web'], ['frame-statistics','Mu3D.Web.Toolkit'],
    ['scene-node-overlay','Mu3D.Web.Toolkit'], ['pointer-input','Mu3D.Web.Toolkit']])
    source = source.replace(`'./mu3d/${module}.mjs'`, JSON.stringify(new URL(`../../src/${folder}/wwwroot/${module}.mjs`, import.meta.url).href));
  return import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
}
const toolkit = await hostModule('toolkit-viewport'), pen = await hostModule('pointer-pen-viewport');
const flush = async () => { for (let i = 0; i < 18; i++) await Promise.resolve(); };
function canvasEnvironment() {
  const env = createViewportEnvironment();
  env.canvas.getContext = () => ({getConfiguration: () => ({format:'rgba16float',colorSpace:'srgb',alphaMode:'opaque',toneMapping:{mode:'extended'}})});
  return env;
}
function openToolkit(id = 'map-controls', {claim = 0, draw} = {}) {
  const env = canvasEnvironment(), calls = [], document = createTestDocument({width: 238, height: 52});
  document.defaultView.ResizeObserver = class {observe() {} disconnect() {}};
  const container = document.createElement('div');
  let dragging = false;
  const managed = {
    invokeMethod(method, ...args) { calls.push([method,...args]); if (method === 'BeginContact') { dragging = claim === 1; return claim; } if (method === 'EndContact') dragging = false; },
    async invokeMethodAsync(method, ...args) {
      calls.push([method,...args]);
      if (method === 'DrawFrame') {
        env.canvas.width = args[0]; env.canvas.height = args[1]; await draw?.();
        return JSON.stringify({continuous: dragging});
      }
    },
  };
  const host = toolkit.create(env.canvas, container, managed, 4096, id);
  host.configure(true,true,false,false,false,100,['A','D','W','S','E','Q']); env.resize(); env.show();
  return {...env,host,calls,container};
}
test('Map native left-pan/right-rotate routes remain distinct; quick taps do not orbit', async () => {
  const env = openToolkit();
  try {
    await env.next(100); await flush();
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    sendEvent(env.canvas,'pointermove',{...primaryPointer,clientX:90,clientY:76});
    await env.next(200); await flush();
    let draw = env.calls.filter(call => call[0] === 'DrawFrame').at(-1);
    assert.deepEqual(draw.slice(8,10),[0,0]); assert.equal(draw[10],-.2); assert.equal(draw[11],.1);
    sendEvent(env.canvas,'pointerup',{...primaryPointer,clientX:90,clientY:76});
    sendEvent(env.canvas,'pointerdown',{...primaryPointer,button:2,buttons:2});
    sendEvent(env.canvas,'pointermove',{...primaryPointer,button:2,buttons:2,clientX:90,clientY:76});
    await env.next(300); await flush();
    draw = env.calls.filter(call => call[0] === 'DrawFrame').at(-1);
    assert.ok(draw[8]<0 && draw[9]<0); assert.deepEqual(draw.slice(10,12),[0,0]);
  } finally { await env.host.dispose(); }
});
test('Fly held composite keys advance the single RAF and release even after focus moves to a control', async () => {
  const env = openToolkit('fly-controls');
  try {
    await env.next(100); await flush();
    sendEvent(window,'keydown',{key:'w'}); sendEvent(window,'keydown',{key:'d'});
    await env.next(200); await flush();
    await env.next(300); await flush();
    const draw = env.calls.filter(call => call[0] === 'DrawFrame').at(-1);
    assert.equal(draw[3],.1,'continuous key updates keep a real elapsed frame interval');
    assert.equal(draw[13],(1<<0)|(1<<3),'both held axes reach the FP32 controller together');
    sendEvent(window,'keyup',{key:'w'}); sendEvent(window,'keyup',{key:'d'});
    await env.next(400); await flush(); assert.equal(env.frames.size,0);
  } finally { await env.host.dispose(); }
});
test('Map key conflicts follow native first-match priority, including PageUp alternate bindings', async () => {
  const env = openToolkit();
  try {
    env.host.configure(true,true,false,false,false,100,['PageUp','D','W','S','PageUp','Q']);
    await env.next(100); await flush(); sendEvent(window,'keydown',{key:'PageUp'});
    await env.next(200); await flush();
    assert.equal(env.calls.filter(call=>call[0]==='DrawFrame').at(-1)[13],1,'first rotate binding wins over duplicate and alternate dolly');
  } finally { await env.host.dispose(); }
});
test('Declarative selection accepts a jittered tap but rejects a coalesced excursion that returns to its origin', async () => {
  const env = openToolkit('declarative-tools');
  try {
    await env.next(100); await flush();
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    sendEvent(env.canvas,'pointerup',{...primaryPointer,clientX:72});
    assert.equal(env.calls.filter(call=>call[0]==='SelectAt').length,1);
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    sendEvent(env.canvas,'pointermove',{...primaryPointer,getCoalescedEvents:()=>[
      {...primaryPointer,clientX:80},{...primaryPointer,clientX:70}]});
    sendEvent(env.canvas,'pointerup',primaryPointer);
    assert.equal(env.calls.filter(call=>call[0]==='SelectAt').length,1,'backtracking movement remains a drag');
  } finally { await env.host.dispose(); }
});
test('Live diagnostics changes retain a captured Gizmo clock; cancellation restores the initial transform once', async () => {
  const env = openToolkit('transform-gizmo',{claim:1});
  try {
    await env.next(100); await flush(); sendEvent(env.canvas,'pointerdown',primaryPointer);
    await env.next(200); await flush();
    env.host.configure(true,true,false,true,true,50,['A','D','W','S','E','Q']);
    await env.next(300); await flush();
    assert.equal(env.calls.filter(call => call[0] === 'DrawFrame').at(-1)[3],.1);
    assert.equal(env.calls.filter(call => call[0] === 'EndContact').length,0);
    sendEvent(window,'blur');
    assert.deepEqual(env.calls.filter(call => call[0] === 'EndContact'),[['EndContact',true]]);
    assert.equal(env.canvas.capture,null);
  } finally { await env.host.dispose(); }
});
test('Paused settings invalidate one frame; disabling Gizmo releases capture while keeping Orbit enabled', async () => {
  const env = openToolkit('transform-gizmo',{claim:1});
  try {
    await env.next(100); await flush(); assert.equal(env.frames.size,0);
    env.host.configure(true,true,false,false,false,100,['A','D','W','S','E','Q']);
    await env.next(200); await flush(); assert.equal(env.calls.filter(call=>call[0]==='DrawFrame').length,2);
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    env.host.configure(true,true,false,false,false,100,['A','D','W','S','E','Q'],false);
    assert.equal(env.canvas.capture,null);
    assert.deepEqual(env.calls.filter(call=>call[0]==='EndContact'),[['EndContact',true]]);
    await env.next(300); await flush(); assert.equal(env.frames.size,0);
  } finally { await env.host.dispose(); }
});
test('Toolkit overlapping disposal waits for submission, suppresses late publication and releases its overlay', async () => {
  let release; const active = new Promise(resolve => {release=resolve;});
  const env = openToolkit('frame-statistics',{draw:()=>active});
  env.next(100); await flush();
  const a = env.host.dispose(), b = env.host.dispose(); assert.equal(a,b);
  assert.equal(env.calls.some(call => call[0]==='Disconnect'),false);
  release(); await a;
  assert.equal(env.calls.filter(call => call[0]==='Disconnect').length,1);
  assert.equal(env.container.children.length,0); assert.equal(env.frames.size,0);
});
test('Pen actual coalesced pressure/tilt survive unchanged; pen proximity suppresses direct-touch contact', async () => {
  const env = canvasEnvironment(), calls = [], cursor = {style:{},hidden:true}, line={style:{}};
  const managed = {
    invokeMethod(method,json) { const samples=JSON.parse(json); calls.push(samples); return JSON.stringify({visible:true,x:0,y:0,rotation:10,red:0,green:1,blue:1}); },
    async invokeMethodAsync(method,...args) { if (method==='DrawFrame') {env.canvas.width=args[0];env.canvas.height=args[1];} },
  };
  const host = pen.create(env.canvas,cursor,line,managed,4096); env.resize();env.show();
  try {
    await env.next(100);await flush();
    sendEvent(env.canvas,'pointermove',{...primaryPointer,pointerType:'pen',buttons:0,pressure:0,tiltX:22,tiltY:-9});
    sendEvent(env.canvas,'pointerdown',{...primaryPointer,pointerId:8,pointerType:'touch'});
    assert.equal(env.canvas.capture,null,'suppressed touch must never retain the primary capture');
    sendEvent(env.canvas,'pointerdown',{...primaryPointer,pointerType:'pen',pressure:.7,tiltX:22,tiltY:-9});
    const samples=[{...primaryPointer,pointerType:'pen',pressure:.2,tiltX:23,tiltY:-8,clientX:75},
      {...primaryPointer,pointerType:'pen',pressure:.9,tiltX:25,tiltY:-6,clientX:80}];
    sendEvent(env.canvas,'pointermove',{...primaryPointer,pointerType:'pen',getCoalescedEvents:()=>samples});
    assert.deepEqual(calls.at(-1).map(sample=>[sample.pressure,sample.tiltX,sample.tiltY]),[[.2,23,-8],[.9,25,-6]]);
    assert.equal(calls.flat().some(sample=>sample.pointerType==='touch'),false);
    assert.equal(line.style.transform,'rotate(10deg)');
  } finally {await host.dispose();}
});
