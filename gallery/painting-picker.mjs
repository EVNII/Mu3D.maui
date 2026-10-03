// This Gallery adapter owns one captured contact; color coordinates and hit regions stay in C#.
export function attach(root, managed) {
  const canvas = root.querySelector('canvas');
  if (!canvas) throw new Error('Painting picker canvas is missing.');
  const events = new AbortController();
  let pointer = null, region = 0, pending, flight, disposed = false;
  function sample(event) {
    const box = canvas.getBoundingClientRect();
    return [(event.clientX - box.left) / box.width, (event.clientY - box.top) / box.height, box.width, box.height];
  }
  function offer(contact) {
    pending = contact;
    if (!flight) flight = pump();
  }
  async function pump() {
    try {
      while (pending && !disposed) {
        const next = pending; pending = null;
        await managed.invokeMethodAsync('Pick', ...next);
      }
    } catch (error) { cancel(); console.error(error); }
    finally { flight = null; }
  }
  function cancel() {
    const active = pointer; pointer = null; region = 0; pending = null;
    if (active !== null && canvas.hasPointerCapture(active)) canvas.releasePointerCapture(active);
  }
  canvas.addEventListener('pointerdown', event => {
    if (disposed || pointer !== null || event.button !== 0 || event.isPrimary === false) return;
    const position = sample(event);
    if (position[2] <= 0 || position[3] <= 0) return;
    const hit = managed.invokeMethod('BeginPick', ...position);
    if (!hit) return;
    event.preventDefault();
    offer([hit, ...position]);
    if (hit === 3) return;
    region = hit; pointer = event.pointerId; canvas.setPointerCapture(pointer);
  }, {signal: events.signal});
  canvas.addEventListener('pointermove', event => {
    if (event.pointerId === pointer) offer([region, ...sample(event)]);
  }, {signal: events.signal});
  canvas.addEventListener('pointerup', event => {
    if (event.pointerId !== pointer) return;
    offer([region, ...sample(event)]);
    const active = pointer; pointer = null; region = 0;
    if (canvas.hasPointerCapture(active)) canvas.releasePointerCapture(active);
  }, {signal: events.signal});
  canvas.addEventListener('pointercancel', cancel, {signal: events.signal});
  canvas.addEventListener('lostpointercapture', () => { if (pointer !== null) cancel(); }, {signal: events.signal});
  window.addEventListener('blur', cancel, {signal: events.signal});
  const resize = new ResizeObserver(cancel); resize.observe(canvas);
  return {
    async cancel() { cancel(); if (flight) await flight; },
    async dispose() { disposed = true; cancel(); events.abort(); resize.disconnect(); if (flight) await flight; },
  };
}
