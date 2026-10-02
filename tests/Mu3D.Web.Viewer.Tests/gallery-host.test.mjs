import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {setMaxListeners} from 'node:events';
import {runInNewContext} from 'node:vm';
import {createViewportEnvironment} from './support/viewport-fixture.mjs';
import {sendEvent, primaryPointer} from './support/pointer-fixture.mjs';
setMaxListeners(0);
let source = await readFile(new URL('../../samples/Mu3D.Gallery/Web/wwwroot/gallery-viewport.mjs', import.meta.url), 'utf8');
for (const [name, folder] of [['viewport-handler','Mu3D.Web'],['orbit-input','Mu3D.Web.Toolkit'],['pointer-input','Mu3D.Web.Toolkit']])
  source = source.replace(`'./mu3d/${name}.mjs'`, JSON.stringify(new URL(`../../src/${folder}/wwwroot/${name}.mjs`, import.meta.url).href));
const {create} = await import(`data:text/javascript;base64,${Buffer.from(source).toString('base64')}`);
const flush = async () => { for (let i = 0; i < 16; i++) await Promise.resolve(); };
function open({draw = () => {}, hit = true, format = 'rgba16float', alphaMode = 'opaque'} = {}) {
  const env = createViewportEnvironment(), calls = [];
  env.canvas.getContext = () => ({getConfiguration: () => ({format, colorSpace:'srgb', alphaMode, toneMapping:{mode:'extended'}})});
  const managed = {
    invokeMethod(method, ...args) { calls.push([method,...args]); return method === 'BeginGizmoDrag' && hit; },
    async invokeMethodAsync(method, ...args) {
      calls.push([method,...args]);
      if (method === 'DrawFrame') { env.canvas.width = args[0]; env.canvas.height = args[1]; await draw(); }
    },
  };
  const host = create(env.canvas, managed, 4096, alphaMode);
  env.resize(); env.show();
  return {...env, host, calls};
}
test('Gallery forwards actual physical/CSS sizing and controls without another WASM runtime', async () => {
  const env = open();
  try {
    env.host.configure(true, false, true); env.resize({inlineSize:300,blockSize:180});
    await env.next(100); await flush();
    const first = env.calls.find(call => call[0] === 'DrawFrame');
    assert.deepEqual(first.slice(1,8), [300,180,0,0,3,100,60]);
    sendEvent(env.canvas,'wheel',{deltaY:50,deltaMode:0});
    await env.next(200); await flush();
    assert.equal(env.calls.filter(call => call[0] === 'DrawFrame').at(-1)[10], -0.05);
  } finally { await env.host.dispose(); }
});
test('Gallery Gizmo capture keeps RAF active, blocks Orbit and cancels on a mode change', async () => {
  const env = open();
  try {
    env.host.configure(true,true,true); await env.next(0); await flush();
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    sendEvent(env.canvas,'pointermove',{...primaryPointer,clientX:80});
    await env.next(16); await flush();
    assert.equal(env.frames.size,1,'Paused Gizmo capture retains the existing render clock');
    assert.equal(env.calls.some(call => call[0] === 'UpdateGizmoDrag'),true);
    assert.deepEqual(env.calls.filter(call => call[0] === 'DrawFrame').at(-1).slice(8,11),[0,0,0]);
    env.host.configure(false,false,true);
    assert.deepEqual(env.calls.filter(call => call[0] === 'EndGizmoDrag'),[['EndGizmoDrag',true]]);
    assert.equal(env.canvas.capture,null);
  } finally { await env.host.dispose(); }
});
test('Overlapping Gallery disposal waits for an active frame before releasing the managed backend', async () => {
  let release; const pending = new Promise(resolve => {release = resolve;});
  const env = open({draw: () => pending});
  env.next(0); await flush();
  const first = env.host.dispose(), second = env.host.dispose();
  assert.equal(first,second,'All owners await the same teardown');
  assert.equal(env.calls.some(call => call[0] === 'Disconnect'),false);
  release(); await Promise.all([first,second]);
  assert.equal(env.calls.filter(call => call[0] === 'Disconnect').length,1);
  assert.equal(env.frames.size,0); assert.equal(env.disconnected(),true);
  assert.equal(env.canvas.style.touchAction,'pan-y');
});
test('Gallery reports rejected HDR configuration without silently switching output', async () => {
  const env = open({format:'bgra8unorm'});
  try {
    await env.next(0); await flush();
    assert.equal(env.calls.filter(call => call[0] === 'RenderFailed').length,1);
    assert.equal(env.calls.filter(call => call[0] === 'DrawFrame').length,1);
    assert.equal(env.calls.find(call => call[0] === 'DrawFrame')[11],true);
    assert.equal(env.frames.size,0);
  } finally { await env.host.dispose(); }
});
test('Repeated live settings retain the animation clock and Gizmo release restores requested playback', async () => {
  const env = open();
  try {
    env.host.configure(true,true,true,true); await env.next(100); await flush();
    env.host.configure(true,true,true,true); await env.next(200); await flush();
    assert.equal(env.calls.filter(call => call[0] === 'DrawFrame').at(-1)[3],0.1);
    sendEvent(env.canvas,'pointerdown',primaryPointer);
    sendEvent(env.canvas,'pointerup',primaryPointer);
    await env.next(300); await flush();
    assert.equal(env.frames.size,1,'Playback continues after a captured drag');
    assert.equal(env.calls.filter(call => call[0] === 'DrawFrame').at(-1)[3],0.1);
  } finally { await env.host.dispose(); }
});
test('A requested premultiplied HDR Canvas retains its alpha mode', async () => {
  const env = open({alphaMode:'premultiplied'});
  try { await env.next(0); await flush(); assert.equal(env.calls.some(call => call[0] === 'RenderFailed'),false); }
  finally { await env.host.dispose(); }
});

const galleryHtml = await readFile(new URL('../../samples/Mu3D.Gallery/Web/wwwroot/index.html', import.meta.url), 'utf8');
function bootstrap(path, baseHref = '/') {
  let pageUrl = new URL(path, 'http://127.0.0.1:8765');
  const requests = [], replacements = [], events = [];
  let baseUri = new URL(baseHref, pageUrl).href;
  const base = {
    getAttribute(name) { assert.equal(name, 'href'); return baseHref; },
    get href() { return baseUri; },
    set href(value) { baseHref = value; baseUri = new URL(value, pageUrl).href; },
  };
  const document = {
    get baseURI() { return baseUri; },
    querySelector(selector) { assert.equal(selector, 'base'); return base; },
    createElement(tag) { return {tag}; },
    head: {append(element) { events.push('style'); requests.push([element.tag, element.rel, String(element.href)]); }},
    body: {append(element) { events.push('boot'); requests.push([element.tag, String(element.src)]); }},
  };
  const location = {get pathname() { return pageUrl.pathname; }, get href() { return pageUrl.href; }};
  const history = {replaceState(_state, _title, relative) {
    events.push('restore');
    replacements.push(relative); pageUrl = new URL(relative, pageUrl);
  }};
  const scripts = [...galleryHtml.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/gi)].map(match => match[1]);
  const context = {document, location, history, URL};
  for (const script of scripts) runInNewContext(script, context);
  return {baseUri, requests, replacements, pageUrl, events};
}
function assertBootstrapRequests(result, prefix) {
  assert.equal(result.baseUri, new URL(prefix, result.pageUrl).href);
  assert.deepEqual(result.requests, [
    ['link', 'stylesheet', new URL(`${prefix}gallery.css`, result.pageUrl).href],
    ['script', new URL(`${prefix}_framework/blazor.webassembly.js`, result.pageUrl).href],
  ]);
}

test('Gallery bootstrap resolves CSS and runtime after selecting the root, interpreted or AOT deployment base', () => {
  assert.doesNotMatch(galleryHtml, /<script\b[^>]*\bsrc\s*=/i, 'The preload scanner must not request boot before the base script runs');
  assert.doesNotMatch(galleryHtml, /<link\b[^>]*\bhref\s*=/i, 'The preload scanner must not request CSS before the base script runs');
  for (const [path, prefix] of [['/', '/'], ['/Gallery/', '/Gallery/'], ['/GalleryAot/examples/shadows-ao', '/GalleryAot/']]) {
    const result = bootstrap(path);
    assertBootstrapRequests(result, prefix);
    assert.deepEqual(result.replacements, [], path);
  }
});

test('Gallery bootstrap preserves an explicit static deployment base, including on deep routes', () => {
  for (const [path, prefix] of [
    ['/Mu3d/gallery/', '/Mu3d/gallery/'],
    ['/Mu3d/gallery/examples/shadows-ao', '/Mu3d/gallery/'],
    ['/showcase/wasm/examples/openpbr-materialx', '/showcase/wasm/'],
    ['/GalleryAot/examples/shadows-ao', '/custom/'],
  ]) {
    const result = bootstrap(path, prefix);
    assertBootstrapRequests(result, prefix);
    assert.deepEqual(result.replacements, [], path);
  }
});

test('Gallery bootstrap restores a same-base Pages fallback route before boot, preserving query and fragment', () => {
  const route = '/Mu3d/gallery/examples/openpbr-materialx?view=AgX%20HDR&asset=a%2Fb#mu3d-canvas';
  const result = bootstrap(`/Mu3d/gallery/?mu3d-route=${encodeURIComponent(route)}`, '/Mu3d/gallery/');
  assert.deepEqual(result.replacements, [route]);
  assert.deepEqual(result.events, ['restore', 'style', 'boot']);
  assert.equal(result.pageUrl.pathname + result.pageUrl.search + result.pageUrl.hash, route);
  assertBootstrapRequests(result, '/Mu3d/gallery/');
  const local = '/GalleryAot/examples/shadows-ao?output=hdr#mu3d-canvas';
  const localResult = bootstrap(`/GalleryAot/?mu3d-route=${encodeURIComponent(local)}`);
  assert.deepEqual(localResult.replacements, [local]);
  assertBootstrapRequests(localResult, '/GalleryAot/');
});

test('Gallery bootstrap rejects external, sibling-prefix and escaped Pages fallback routes', () => {
  for (const route of [
    'https://example.com/Mu3d/gallery/examples/shadows-ao',
    '//example.com/Mu3d/gallery/examples/shadows-ao',
    '/Mu3d/gallery-other/examples/shadows-ao',
    '/Mu3d/gallery/../private',
    '/Mu3d/gallery/%2e%2e/private',
    'http://[invalid',
  ]) {
    const result = bootstrap(`/Mu3d/gallery/?mu3d-route=${encodeURIComponent(route)}&keep=1#top`, '/Mu3d/gallery/');
    assert.deepEqual(result.replacements, ['/Mu3d/gallery/?keep=1#top'], route);
    assertBootstrapRequests(result, '/Mu3d/gallery/');
  }
});
