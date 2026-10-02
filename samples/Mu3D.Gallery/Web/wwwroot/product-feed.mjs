import {createManagedViewportHandler} from './mu3d/viewport-handler.mjs';

// Layout, visibility and deadlines are application policy. Each visible Canvas retains one RAF.
export function create(root, managed, maximumDimension) {
  const canvases = [...root.querySelectorAll('canvas[data-product]')], cards = [];
  const events = new AbortController();
  let disposed = false, timer, scrollIdle, scrolling = false, latest, pending, disposal;
  for (const canvas of canvases) {
    const index = Number(canvas.dataset.product), transparent = canvas.dataset.transparent === 'true';
    let flight;
    const handler = createManagedViewportHandler({canvas, maxDimension:maximumDimension, properties:{ready:false},
      callbacks:{
        draw(size, {ready}) {
          if (!ready) return;
          flight = managed.invokeMethodAsync('DrawProduct', index, size.width, size.height, size.deltaSeconds, size.refreshSurface);
          return flight.finally(() => {flight = null;});
        },
      },
      onFrame(_result, size, {ready}) {
        if (!ready) return;
        const config = canvas.getContext('webgpu').getConfiguration?.();
        if (config?.format !== 'rgba16float' || config.colorSpace !== 'srgb' || config.toneMapping?.mode !== 'extended' ||
            config.alphaMode !== (transparent ? 'premultiplied' : 'opaque')) throw new Error('HDR product Canvas configuration was rejected.');
      },
      onError:error => {void managed.invokeMethodAsync('ProductFailed', index, error.message).catch(console.error);},
    });
    cards.push({handler, idle:() => flight});
  }
  async function dispatch() {
    while (latest && !disposed) {const update = latest; latest = null; await managed.invokeMethodAsync('UpdateLayout', update, scrolling);}
  }
  function measure() {
    if (disposed) return;
    latest = canvases.map(canvas => {
      const box = canvas.closest('article').getBoundingClientRect();
      return box.bottom < 0 ? box.bottom : box.top > innerHeight ? box.top - innerHeight : 0;
    });
    if (!pending) pending = dispatch().catch(console.error).finally(() => {pending = null; if (latest) measure();});
    clearTimeout(timer);
    timer = setTimeout(measure, 250); // Retention deadline, never submits a frame itself.
  }
  window.addEventListener('scroll', () => {
    scrolling = true; clearTimeout(scrollIdle); measure();
    scrollIdle = setTimeout(() => {scrolling = false; measure();}, 200);
  }, {passive:true, signal:events.signal});
  window.addEventListener('resize', measure, {signal:events.signal});
  const resize = new ResizeObserver(measure); resize.observe(root);
  measure();
  return {
    async setReady(index, ready, continuous) {
      if (disposed) return;
      const card = cards[index]; card.handler.setProperties({ready}); card.handler.setContinuous(ready && continuous);
      if (ready) card.handler.invalidate(); else await card.idle();
    },
    dispose() {
      return disposal ??= (async () => {
        disposed = true; events.abort(); resize.disconnect(); clearTimeout(timer); clearTimeout(scrollIdle); latest = null;
        await Promise.all(cards.map(card => card.handler.dispose()));
        if (pending) await pending;
      })();
    },
  };
}
