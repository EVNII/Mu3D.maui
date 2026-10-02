// Application-owned selection shared by the managed Gizmo/outline and borrowed HTML card.
// Hit evidence comes from the shared SceneRaycaster, not browser-side 3D mathematics.
export function createViewerSelectionBridge(renderer, nodes, createSource,
  invalidate = () => {}, cancelInteraction = () => {}) {
  const catalog = JSON.parse(renderer.GetViewerNodeCatalog());
  const tokens = new Map(), handles = new Map();
  for (const entry of catalog.Nodes) {
    const node = nodes[entry.Key];
    if (!node) throw new TypeError('Selection and node bindings require the same scene catalog.');
    tokens.set(node, entry.Token); handles.set(entry.Token, node);
  }
  let disposed = false;
  const source = createSource({selectedNode: nodes.model,
    validateNode(node) {
      if (node !== null && !tokens.has(node)) throw new TypeError('The selected handle belongs to another scene source.');
    },
    hitTest(position, mask) {
      if (disposed) throw new Error('The selection bridge is disposed.');
      const report = JSON.parse(renderer.HitTestViewerSelection(position.x, position.y, mask));
      if (report.SourceId !== catalog.SourceId) throw new Error('The selection candidates belong to another scene source.');
      return report.Candidates.map(candidate => {
        const node = handles.get(candidate.Token);
        if (!node) throw new Error('The selection candidate is not in this scene catalog.');
        return {node, hit: candidate};
      });
    },
    commitSelection(node) {
      if (disposed) throw new Error('The selection bridge is disposed.');
      cancelInteraction();
      renderer.SelectViewerNode(node === null ? '' : tokens.get(node));
      invalidate();
    },
  });
  return {source,
    configureHighlight(enabled) {
      if (disposed) throw new Error('The selection bridge is disposed.');
      renderer.ConfigureViewerHighlight(enabled); invalidate();
    },
    dispose() { if (disposed) return; disposed = true; source.dispose(); tokens.clear(); handles.clear(); },
  };
}
