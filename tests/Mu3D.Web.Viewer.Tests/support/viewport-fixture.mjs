import assert from 'node:assert/strict';
import {createPointerEnvironment, sendEvent} from './pointer-fixture.mjs';

// The existing scheduler is exercised against explicit RAF/visibility/size reports.
export function createViewportEnvironment({canvas, dpr = 2} = {}) {
  canvas ??= Object.assign(createPointerEnvironment({clientHeight: 60}).canvas, {width: 200, height: 120});
  const document = canvas.ownerDocument, frames = new Map(), media = [];
  let sequence = 0, resize, intersection, now = 0;
  canvas.releasePointerCapture = () => { canvas.capture = null; };
  globalThis.document = document; globalThis.window = document.defaultView;
  globalThis.devicePixelRatio = dpr;
  globalThis.performance = {now: () => now};
  globalThis.requestAnimationFrame = callback => { frames.set(++sequence, callback); return sequence; };
  globalThis.cancelAnimationFrame = id => frames.delete(id);
  globalThis.matchMedia = query => {
    const value = Object.assign(new EventTarget(), {media: query}); media.push(value); return value;
  };
  globalThis.ResizeObserver = class {
    constructor(callback) { this.callback = callback; }
    observe(node) { if (node === canvas) resize = this; }
    disconnect() { this.disconnected = true; }
  };
  document.defaultView.ResizeObserver = globalThis.ResizeObserver;
  globalThis.IntersectionObserver = class {
    constructor(callback) { this.callback = callback; intersection = this; }
    observe() {} disconnect() { this.disconnected = true; }
  };
  return {canvas, document, frames, media,
    get now() { return now; },
    show(value = true) { intersection.callback([{target: canvas, isIntersecting: value}]); },
    resize(box) { resize.callback([{target: canvas, devicePixelContentBoxSize: box ? [box] : []}]); },
    hidden(value) { document.visibilityState = value ? 'hidden' : 'visible'; sendEvent(document, 'visibilitychange'); },
    next(time) {
      assert.equal(frames.size, 1, 'Only one RAF should be scheduled');
      const [id, callback] = frames.entries().next().value; frames.delete(id); now = time; return callback(time);
    },
    disconnected() { return resize.disconnected && intersection.disconnected; },
  };
}
