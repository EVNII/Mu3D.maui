// Owned-node/event fixture only; real browser layout and hit testing are separate checks.
export class TestNode extends EventTarget {
  constructor(document, tag = 'div') {
    super(); Object.assign(this, {ownerDocument: document, tag, children: [], parentNode: null,
      style: {}, dataset: {}, attributes: {}, textContent: '', value: '', checked: false,
      hidden: false, inert: false});
  }
  appendChild(node) { node.remove(); this.children.push(node); node.parentNode = this; return node; }
  insertBefore(node, before) {
    node.remove(); this.children.splice(this.children.indexOf(before), 0, node); node.parentNode = this;
  }
  replaceChild(node, old) { this.insertBefore(node, old); old.remove(); }
  replaceChildren() { for (const child of [...this.children]) child.remove(); }
  remove() {
    if (this.parentNode) this.parentNode.children.splice(this.parentNode.children.indexOf(this), 1);
    this.parentNode = null;
  }
  contains(node) { return this === node || this.children.some(child => child.contains(node)); }
  blur() { if (this.ownerDocument.activeElement === this) this.ownerDocument.activeElement = null; }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  getBoundingClientRect() { return this.rect ?? {width: this.width, height: this.height}; }
}

export function createTestDocument({width, height, rect} = {}) {
  const document = Object.assign(new EventTarget(), {defaultView: new EventTarget(), visibilityState: 'visible'});
  document.elements = new Map();
  document.createElement = tag => Object.assign(new TestNode(document, tag), {width, height, rect});
  document.createElementNS = (_, tag) => document.createElement(tag);
  document.createComment = () => document.createElement('comment');
  document.getElementById = id => {
    if (!document.elements.has(id)) document.elements.set(id, document.createElement('div'));
    return document.elements.get(id);
  };
  return document;
}

export function createOverlayDom() {
  const document = createTestDocument();
  const container = new TestNode(document), origin = new TestNode(document);
  const label = new TestNode(document), button = new TestNode(document);
  origin.appendChild(label); origin.appendChild(button);
  return {document, container, origin, label, button};
}

export const anchorPoint = (Id, X = 250, Y = 75, extra = {}) =>
  ({Id, Projected: true, InsideViewport: true, X, Y, Depth: 0.5, ...extra});

export function sendTargetedEvent(node, type, target, properties = {}) {
  const event = Object.assign(new Event(type, {cancelable: true}), properties);
  Object.defineProperty(event, 'target', {value: target}); node.dispatchEvent(event); return event;
}
