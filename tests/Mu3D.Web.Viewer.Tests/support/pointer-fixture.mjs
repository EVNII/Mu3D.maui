// CSS/physical sizing and capture reports, without a render loop or browser emulation.
export function createPointerEnvironment({clientHeight} = {}) {
  const document = Object.assign(new EventTarget(), {visibilityState: 'visible', defaultView: new EventTarget()});
  const canvas = Object.assign(new EventTarget(), {ownerDocument: document,
    width: 300, height: 180, rect: {left: 20, top: 40, width: 100, height: 60},
    style: {touchAction: 'pan-y'}, capture: null, focuses: 0});
  if (clientHeight !== undefined) canvas.clientHeight = clientHeight;
  canvas.getBoundingClientRect = () => canvas.rect;
  canvas.setPointerCapture = id => { canvas.capture = id; };
  canvas.hasPointerCapture = id => canvas.capture === id;
  canvas.releasePointerCapture = id => {
    canvas.capture = null; sendEvent(canvas, 'lostpointercapture', {pointerId: id});
  };
  canvas.focus = () => canvas.focuses++;
  globalThis.document = document; globalThis.window = document.defaultView;
  return {canvas, document, window: document.defaultView};
}

export const primaryPointer = {pointerId: 7, isPrimary: true, pointerType: 'mouse', button: 0, buttons: 1,
  clientX: 70, clientY: 70};

export function sendEvent(target, type, properties = {}) {
  const event = Object.assign(new Event(type, {cancelable: true}), properties);
  target.dispatchEvent(event); return event;
}
