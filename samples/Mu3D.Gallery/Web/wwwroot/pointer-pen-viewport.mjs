import {createManagedViewportHandler} from './mu3d/viewport-handler.mjs';
import {attachPointerInput} from './mu3d/pointer-input.mjs';

export function create(canvas, cursor, tiltLine, managed, maxDimension) {
  const events = new AbortController(), options = {signal: events.signal};
  const previousTouchAction = canvas.style.touchAction;
  canvas.style.touchAction = 'none';
  let penPresent = false, pointer, disposal, sizeKey = '';
  function publish(samples) {
    const accepted = samples.filter(sample => !(penPresent && sample.pointerType === 'touch'));
    if (!accepted.length) return;
    for (const sample of accepted) if (sample.pointerType === 'pen') penPresent = sample.phase !== 'exit';
    const report = JSON.parse(managed.invokeMethod('PointerSamples', JSON.stringify(accepted)));
    cursor.hidden = !report.visible;
    cursor.style.transform = `translate(${report.x}px,${report.y}px)`;
    cursor.style.color = `rgb(${report.red * 100}% ${report.green * 100}% ${report.blue * 100}%)`;
    tiltLine.style.transform = `rotate(${report.rotation}deg)`;
    handler.invalidate();
  }
  const handler = createManagedViewportHandler({canvas, maxDimension, callbacks: {
    draw(size) {
      const next = [size.width, size.height, size.cssWidth, size.cssHeight].join(':');
      if (sizeKey && sizeKey !== next) { pointer.cancel(); cursor.hidden = true; }
      sizeKey = next;
      return managed.invokeMethodAsync('DrawFrame', size.width, size.height, size.cssWidth, size.cssHeight, size.refreshSurface);
    }, disconnect: () => managed.invokeMethodAsync('Disconnect'),
  }, onFrame(_result, size) {
    const config = canvas.getContext('webgpu').getConfiguration?.();
    if (canvas.width !== size.width || canvas.height !== size.height || config?.format !== 'rgba16float' ||
      config.colorSpace !== 'srgb' || config.alphaMode !== 'opaque' || config.toneMapping?.mode !== 'extended')
      throw new Error('浏览器未确认请求的画布尺寸或 HDR 配置。');
  }, onError(error) { cursor.hidden = true; void managed.invokeMethodAsync('RenderFailed', error.message).catch(console.error); }});
  pointer = attachPointerInput(canvas, {acceptContact: sample => !(penPresent && sample.pointerType === 'touch') &&
    (sample.button === 0 || sample.pointerType === 'pen'), onSamples: publish});
  canvas.addEventListener('pointerleave', event => {
    if (pointer.isActive) return;
    penPresent = false;
    publish([{pointerType: event.pointerType || '', phase: 'exit', u: 0, v: 0, pressure: null, tiltX: null, tiltY: null, eraser: false, barrel: false}]);
  }, options);
  window.addEventListener('blur', () => { penPresent = false; cursor.hidden = true; }, options);
  function dispose() {
    if (!disposal) { pointer.dispose(); events.abort(); cursor.hidden = true; canvas.style.touchAction = previousTouchAction; disposal = handler.dispose(); }
    return disposal;
  }
  window.addEventListener('pagehide', () => { void dispose().catch(console.error); }, options);
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); }, options);
  return {invalidate: () => handler.invalidate(), dispose};
}
