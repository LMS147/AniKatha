export function attachPlayer(video, callbacks, url, resumePosition, trackCompletion = false) {
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  let ready = false;
  let position = resumePosition;
  let savedPosition = resumePosition;
  let completed = false;
  let checkpoint = Promise.resolve();

  function save(force) {
    if (!ready || video.seeking || !Number.isFinite(video.currentTime)) return checkpoint;

    const next = video.ended && !trackCompletion ? 0 : video.currentTime;
    if (!force && Math.abs(next - position) < 2) return checkpoint;
    if (next === position && next === savedPosition) return checkpoint;

    position = next;
    checkpoint = checkpoint.then(async () => {
      if (next === savedPosition) return;
      if (await callbacks.invokeMethodAsync('SavePosition', next)) savedPosition = next;
    }).catch(() => {});
    return checkpoint;
  }

  video.addEventListener('loadedmetadata', () => {
    if (position > 0 && position < video.duration) video.currentTime = position;
    ready = true;
  }, options);
  video.addEventListener('timeupdate', () => save(false), options);
  for (const event of ['pause', 'seeked']) {
    video.addEventListener(event, () => save(true), options);
  }
  video.addEventListener('ended', async () => {
    await save(true);
    if (!trackCompletion || completed) return;
    completed = true;
    try { completed = !!(await callbacks.invokeMethodAsync('CompleteEpisode')); }
    catch { completed = false; }
  }, options);
  document.addEventListener('visibilitychange', () => {
    if (document.hidden) save(true);
  }, options);
  window.addEventListener('pagehide', () => save(true), options);
  video.addEventListener('error', () => {
    callbacks.invokeMethodAsync('SetPlaybackError').catch(() => {});
  }, options);

  video.src = url;

  let track;
  let subtitleUrl;
  function clearSubtitle() {
    track?.remove();
    if (subtitleUrl) URL.revokeObjectURL(subtitleUrl);
    track = undefined;
    subtitleUrl = undefined;
  }
  return {
    clearSubtitle,
    setSubtitle(language, cues) {
      clearSubtitle();
      track = document.createElement('track');
      track.kind = 'subtitles';
      track.label = language || 'Subtitles';
      track.srclang = language || 'und';
      subtitleUrl = URL.createObjectURL(new Blob([cues], {type: 'text/vtt'}));
      track.src = subtitleUrl;
      video.appendChild(track);
      track.track.mode = 'showing';
    },
    retry() {
      save(true);
      ready = false;
      video.load();
    },
    async dispose() {
      const saving = save(true);
      listeners.abort();
      clearSubtitle();
      video.pause();
      video.removeAttribute('src');
      video.load();
      await saving;
    }
  };
}
