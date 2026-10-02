// The renderer owns one successful-frame boundary; consumers bind real DOM to opaque node handles.
// This demo bridge adapts its WASM exports once, without a per-control render or projection loop.
export function createViewerNodeAnchorBridge(renderer, createSource, invalidate = () => {}) {
  const catalog = JSON.parse(renderer.GetViewerNodeCatalog()), listeners = new Set();
  let disposed = false;
  const source = createSource({sourceId: catalog.SourceId,
    configureAnchor({id, target, localPosition}) {
      if (disposed) throw new Error('The node anchor bridge is disposed.');
      const registration = JSON.parse(renderer.ConfigureViewerNodeAnchor(id ?? '', target ?? '', ...localPosition));
      invalidate(); return registration;
    },
    removeAnchor(id) {
      if (disposed) return;
      const revision = renderer.RemoveViewerNodeAnchor(id);
      invalidate(); return revision;
    },
    subscribeFrames(listener) { listeners.add(listener); return () => listeners.delete(listener); },
  });
  const nodes = Object.freeze(Object.fromEntries(catalog.Nodes.map(node => [node.Key, source.node(node.Token)])));
  return {source, nodes,
    publishFrame(frame) {
      if (disposed) return;
      for (const listener of [...listeners]) if (!disposed && listeners.has(listener)) listener(frame);
    },
    dispose() {
      if (disposed) return;
      // Unregister while exports are live, before marking the bridge disposed or shutting down WASM.
      source.dispose(); disposed = true; listeners.clear();
    },
  };
}
