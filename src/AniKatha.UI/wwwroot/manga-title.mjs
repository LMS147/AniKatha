export async function shareTitle(title, url, copy) {
  if (!copy && typeof navigator.share === 'function') {
    try {
      await navigator.share({ title, url });
      return '';
    } catch (error) {
      if (error.name === 'AbortError') return '';
    }
  }
  await navigator.clipboard.writeText(url);
  return 'Link copied.';
}

export function openDetails(details) {
  details.open = true;
  details.scrollIntoView({ block: 'start' });
}
