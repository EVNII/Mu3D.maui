export function attach(element, managed) {
  const events = new AbortController();
  element.addEventListener('scroll', () => {
    const range = Math.max(1, element.scrollWidth - element.clientWidth);
    void managed.invokeMethodAsync('ScrollProgress', element.scrollLeft / range).catch(console.error);
  }, {passive: true, signal: events.signal});
  return {dispose() {events.abort();}};
}
