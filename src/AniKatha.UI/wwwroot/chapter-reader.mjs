export function watchPages(container, callback, page, startAtTop = false) {
  const abort = new AbortController();
  const options = { passive: true, signal: abort.signal };
  let scheduled = 0;
  let last = page;

  function show(pageNumber) {
    last = pageNumber;
    container.querySelector('[data-reader-page="' + pageNumber + '"]')?.scrollIntoView({ block: 'start' });
  }

  function update() {
    scheduled = 0;
    const anchor = window.innerHeight * 0.4;
    let nearest = null, distance = Infinity;
    for (const image of container.querySelectorAll('[data-reader-page]')) {
      const rect = image.getBoundingClientRect();
      if (rect.bottom <= 0 || rect.top >= window.innerHeight) continue;
      const delta = Math.max(rect.top - anchor, anchor - rect.bottom, 0);
      if (delta < distance) { nearest = image; distance = delta; }
    }
    const observed = Number(nearest?.dataset.readerPage);
    if (!observed || observed === last) return;
    last = observed;
    callback.invokeMethodAsync('ObservePage', observed).catch(() => {});
  }

  function schedule() {
    if (!scheduled) scheduled = window.requestAnimationFrame(update);
  }

  if (startAtTop) window.scrollTo(0, 0);
  else show(page);
  window.addEventListener('scroll', schedule, options);
  window.addEventListener('resize', schedule, options);
  container.setAttribute('data-reader-ready', '');
  return {
    show,
    dispose() {
      abort.abort();
      container.removeAttribute('data-reader-ready');
      if (scheduled) window.cancelAnimationFrame(scheduled);
    }
  };
}
