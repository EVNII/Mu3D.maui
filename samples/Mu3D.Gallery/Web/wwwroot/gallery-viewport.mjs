import {createManagedViewportHandler} from './mu3d/viewport-handler.mjs';
import {attachOrbitInput} from './mu3d/orbit-input.mjs';
import {attachPointerInput} from './mu3d/pointer-input.mjs';

export function createById(id, managed, maxDimension) {
  const canvas = document.getElementById(id);
  if (!canvas) throw new Error(`Gallery canvas missing: ${id}`);
  return create(canvas, managed, maxDimension);
}

// Razor owns sample state. This adapter supplies browser input, physical size and lifecycle only.
export function create(canvas, managed, maxDimension, alphaMode = 'opaque') {
  let interactive = false, gizmo = false, dragging = false, continuous = false, scheduling = false, disposal;
  let rotateX = 0, rotateY = 0, dolly = 0, dimensions = '';
  const events = new AbortController();
  const handler = createManagedViewportHandler({canvas, maxDimension, properties: {hdr: true},
    callbacks: {
      async draw(size, {hdr}) {
        const next = `${size.width}:${size.height}:${size.cssWidth}:${size.cssHeight}`;
        if (dimensions && dimensions !== next) cancel();
        dimensions = next;
        const commands = [rotateX, rotateY, dolly];
        rotateX = rotateY = dolly = 0;
        await managed.invokeMethodAsync('DrawFrame', size.width, size.height, size.deltaSeconds,
          size.frameIntervalSeconds, size.width / size.cssWidth, size.cssWidth, size.cssHeight,
          ...commands, hdr, size.refreshSurface);
      },
      disconnect: () => managed.invokeMethodAsync('Disconnect'),
    },
    onFrame(_result, size, {hdr}) {
      const config = canvas.getContext('webgpu').getConfiguration?.();
      if (canvas.width !== size.width || canvas.height !== size.height ||
          config?.format !== (hdr ? 'rgba16float' : 'bgra8unorm') || config.colorSpace !== 'srgb' ||
          config.alphaMode !== alphaMode || config.toneMapping?.mode !== (hdr ? 'extended' : 'standard'))
        throw new Error('浏览器未确认请求的画布尺寸或 HDR/SDR 配置。');
    },
    onError: error => { void managed.invokeMethodAsync('RenderFailed', error.message).catch(console.error); },
  });
  const pointer = attachPointerInput(canvas, {
    acceptContact(sample) {
      if (!gizmo || sample.button !== 0 || !managed.invokeMethod('BeginGizmoDrag', sample.u, sample.v)) return false;
      dragging = true;
      orbit.cancel(); rotateX = rotateY = dolly = 0;
      schedule(); handler.invalidate();
      return true;
    },
    onSamples(samples) {
      const sample = samples.at(-1);
      if (!sample.captured || !dragging) return;
      if (sample.phase !== 'cancel') managed.invokeMethod('UpdateGizmoDrag', sample.u, sample.v);
      if (sample.phase === 'up' || sample.phase === 'cancel') {
        managed.invokeMethod('EndGizmoDrag', sample.phase === 'cancel');
        dragging = false; schedule();
      }
      handler.invalidate();
    },
  });
  const orbit = attachOrbitInput(canvas, command => {
    rotateX += command.rotateX; rotateY += command.rotateY; dolly += command.dolly;
    handler.invalidate();
  }, {isEnabled: () => interactive && !dragging, dragThreshold: 6});
  function cancel() { pointer.cancel(); orbit.cancel(); rotateX = rotateY = dolly = 0; }
  function schedule() {
    const next = dragging || continuous;
    if (next !== scheduling) { scheduling = next; handler.setContinuous(next); }
  }
  function dispose() {
    if (!disposal) {
      events.abort(); pointer.dispose(); orbit.dispose();
      disposal = handler.dispose();
    }
    return disposal;
  }
  window.addEventListener('pagehide', () => { void dispose().catch(console.error); }, {signal: events.signal});
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
  return {
    configure(enableOrbit, enableGizmo, hdr, enableContinuous = false) {
      if (interactive !== enableOrbit || gizmo !== enableGizmo) cancel();
      interactive = enableOrbit; gizmo = enableGizmo;
      continuous = enableContinuous; schedule();
      handler.setProperties({hdr}); handler.invalidate();
    },
    dispose,
  };
}
