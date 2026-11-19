export function placeSubtitles(cues, placement) {
  if (placement !== 'top' && placement !== 'bottom') return cues;
  const line = placement === 'top' ? '10%' : '90%';
  return cues.split(/\r?\n/).map(text => {
    if (!/^\s*(?:\d+:)?\d{2}:\d{2}\.\d{3}\s+-->\s+(?:\d+:)?\d{2}:\d{2}\.\d{3}/.test(text)) return text;
    return text.replace(/\s+line:[^\s]+/g, '') + ' line:' + line;
  }).join('\n');
}

export function attachPlayer(video, callbacks, url, resumePosition, trackCompletion = false, speed = 1, startPlaying = false, offlineFile = false) {
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  let ready = false;
  let position = resumePosition;
  let savedPosition = resumePosition;
  let completed = false;
  let soughtToEnd = false;
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
    video.playbackRate = speed;
    ready = true;
    if (startPlaying) {
      startPlaying = false;
      video.play()?.catch(() => {});
    }
  }, options);
  video.addEventListener('timeupdate', () => save(false), options);
  video.addEventListener('pause', () => save(true), options);
  video.addEventListener('seeked', () => {
    soughtToEnd = Number.isFinite(video.duration) && video.currentTime >= video.duration;
    save(true);
  }, options);
  video.addEventListener('ended', async () => {
    const eligible = trackCompletion && !video.seeking && !soughtToEnd;
    await save(true);
    if (!eligible || completed || soughtToEnd) return;
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

  const useLocalBlob = offlineFile && window.location?.hostname === '0.0.0.1';
  let mediaRequest;
  let mediaBlobUrl;

  function clearMediaBlob() {
    mediaRequest?.abort();
    mediaRequest = undefined;
    if (mediaBlobUrl) URL.revokeObjectURL(mediaBlobUrl);
    mediaBlobUrl = undefined;
  }

  async function loadMediaBlob() {
    clearMediaBlob();
    const request = new AbortController();
    mediaRequest = request;
    try {
      // Android's static WebView provider serves whole files. A Blob lets the native
      // media engine seek without HTTP Range support on that origin.
      const response = await fetch(url, { signal: request.signal });
      if (!response.ok) throw new Error('Local media unavailable');
      const body = await response.blob();
      if (request.signal.aborted) return;
      mediaBlobUrl = URL.createObjectURL(body.type === 'video/mp4' ? body : new Blob([body], { type: 'video/mp4' }));
      video.src = mediaBlobUrl;
    } catch {
      if (!request.signal.aborted) callbacks.invokeMethodAsync('SetPlaybackError').catch(() => {});
    }
  }

  if (useLocalBlob) void loadMediaBlob();
  else video.src = url;

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
    checkpoint: () => save(true),
    seek(seconds) {
      if (!ready || !Number.isFinite(seconds) || !Number.isFinite(video.duration)) return;
      video.currentTime = Math.max(0, Math.min(video.duration, video.currentTime + seconds));
    },
    setSpeed(value) {
      if (!Number.isFinite(value) || value < 0.5 || value > 2) return;
      speed = value;
      video.playbackRate = value;
    },
    setSubtitle(language, cues, placement) {
      clearSubtitle();
      track = document.createElement('track');
      track.kind = 'subtitles';
      track.label = language || 'Subtitles';
      track.srclang = language || 'und';
      subtitleUrl = URL.createObjectURL(new Blob([placeSubtitles(cues, placement)], {type: 'text/vtt'}));
      track.src = subtitleUrl;
      video.appendChild(track);
      track.track.mode = 'showing';
    },
    retry() {
      save(true);
      ready = false;
      if (useLocalBlob) void loadMediaBlob();
      else video.load();
    },
    async dispose() {
      const saving = save(true);
      listeners.abort();
      clearSubtitle();
      video.pause();
      video.removeAttribute('src');
      video.load();
      clearMediaBlob();
      await saving;
    }
  };
}
