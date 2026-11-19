import { attachControls } from './player-presentation.mjs';
export { focusPlayerPanel, focusPlayerMenu, focusSourcePicker, revealPlayer } from './player-presentation.mjs';

const cueTiming = /^(\s*)((?:\d{2,}:)?\d{2}:\d{2}\.\d{3})(\s+-->\s+)((?:\d{2,}:)?\d{2}:\d{2}\.\d{3})(.*)$/;

function cueMilliseconds(stamp) {
  return stamp.split(':').reduce((time, part) => time * 60 + Number(part), 0) * 1000;
}

function cueTimestamp(time) {
  const milliseconds = Math.round(time);
  const parts = [Math.floor(milliseconds / 3600000), Math.floor(milliseconds / 60000) % 60,
    Math.floor(milliseconds / 1000) % 60];
  return parts.map(part => String(part).padStart(2, '0')).join(':') + '.' +
    String(milliseconds % 1000).padStart(3, '0');
}

export function placeSubtitles(cues, placement, offset = 0) {
  const line = placement === 'top' ? '10%' : placement === 'bottom' ? '90%' : null;
  if (!line && offset === 0) return cues;
  return cues.replace(/\r\n?/g, '\n').split(/\n[ \t]*\n/).flatMap(block => {
    const lines = block.split('\n');
    if (/^(?:WEBVTT|NOTE|STYLE|REGION)(?:\s|$)/.test(lines[0])) return [block];
    const index = cueTiming.test(lines[0]) ? 0 : 1;
    const timing = lines[index]?.match(cueTiming);
    if (!timing) return [block];
    const [, indent, start, arrow, end, settings] = timing;
    const shiftedEnd = cueMilliseconds(end) + offset;
    if (shiftedEnd <= 0) return [];
    const shiftedStart = Math.max(0, cueMilliseconds(start) + offset);
    const placementSettings = line ? settings.replace(/\s+line:[^\s]+/g, '') + ' line:' + line : settings;
    lines[index] = indent + (offset ? cueTimestamp(shiftedStart) : start) + arrow +
      (offset ? cueTimestamp(shiftedEnd) : end) + placementSettings;
    return [lines.join('\n')];
  }).join('\n\n');
}

let sessionOwner;

export function attachPlayer(video, callbacks, url, resumePosition, trackCompletion = false, speed = 1,
  startPlaying = false, offlineFile = false, title = '', episode = '', pipButton = null, stage = null) {
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  let ready = false;
  let position = resumePosition;
  let savedPosition = resumePosition;
  let completed = false;
  let soughtToEnd = false;
  let checkpoint = Promise.resolve();
  const mediaSession = globalThis.navigator?.mediaSession;

  function mediaAction(name, action) {
    try { mediaSession.setActionHandler(name, action); } catch { /* Browser may omit actions. */ }
  }

  function startMediaSession() {
    if (!mediaSession || !title) return;
    sessionOwner = video;
    try {
      if (typeof MediaMetadata === 'function') mediaSession.metadata = new MediaMetadata({ title, artist: episode });
      mediaSession.playbackState = 'playing';
      mediaAction('play', () => video.play()?.catch(() => {}));
      mediaAction('pause', () => video.pause());
    } catch { /* Video remains playable without system integration. */ }
  }

  function clearMediaSession() {
    if (sessionOwner !== video) return;
    sessionOwner = undefined;
    try {
      mediaAction('play', null);
      mediaAction('pause', null);
      mediaSession.metadata = null;
      mediaSession.playbackState = 'none';
    } catch { /* This runtime does not expose writable media-session state. */ }
  }

  if (pipButton && document.pictureInPictureEnabled && typeof video.requestPictureInPicture === 'function') {
    pipButton.hidden = false;
    pipButton.disabled = true;
    pipButton.addEventListener('click', async () => {
      try {
        if (document.pictureInPictureElement === video) await document.exitPictureInPicture();
        else await video.requestPictureInPicture();
      } catch {
        callbacks.invokeMethodAsync('SetPictureInPictureError').catch(() => {});
      }
    }, options);
    video.addEventListener('enterpictureinpicture', () => { pipButton.textContent = 'Exit picture-in-picture'; }, options);
    video.addEventListener('leavepictureinpicture', () => {
      pipButton.textContent = 'Picture-in-picture';
      save(true);
    }, options);
  }

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

  video.addEventListener('durationchange', () => {
    if (trackCompletion && Number.isFinite(video.duration) && video.duration > 0)
      callbacks.invokeMethodAsync('SaveVideoLength', video.duration).catch(() => {});
  }, options);
  video.addEventListener('loadedmetadata', () => {
    if (position > 0 && position < video.duration) video.currentTime = position;
    video.playbackRate = speed;
    ready = true;
    if (pipButton && !pipButton.hidden) pipButton.disabled = false;
    publishTracks();
    if (startPlaying) {
      startPlaying = false;
      video.play()?.catch(() => {});
    }
  }, options);
  video.addEventListener('timeupdate', () => save(false), options);
  video.addEventListener('playing', startMediaSession, options);
  video.addEventListener('pause', () => {
    if (sessionOwner === video) {
      try { mediaSession.playbackState = 'paused'; } catch { /* Unavailable system state. */ }
    }
    save(true);
  }, options);
  video.addEventListener('seeked', () => {
    soughtToEnd = Number.isFinite(video.duration) && video.currentTime >= video.duration;
    save(true);
  }, options);
  video.addEventListener('ended', async () => {
    clearMediaSession();
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
    clearMediaSession();
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

  let sidecar;
  let subtitleUrl;
  let selectedSubtitle = null;
  const trackIds = new WeakMap();
  let nextId = 0;
  const trackId = track => {
    if (!trackIds.has(track)) trackIds.set(track, String(++nextId));
    return trackIds.get(track);
  };
  const subtitleTracks = () => Array.from(video.textTracks ?? []).filter(track =>
    track !== sidecar?.track && ['subtitles', 'captions', 'forced'].includes(track.kind));
  const availableAudio = () => Array.from(video.audioTracks ?? []);

  function applyEmbeddedSelection() {
    if (selectedSubtitle === null) return;
    for (const track of subtitleTracks()) {
      const mode = trackId(track) === selectedSubtitle ? 'showing' : 'disabled';
      if (track.mode !== mode) track.mode = mode;
    }
  }

  function publishTracks() {
    if (listeners.signal.aborted) return;
    applyEmbeddedSelection();
    const describe = (track, selected) => ({ id: trackId(track), language: track.language || '',
      label: track.label || '', role: track.kind || '', selected });
    callbacks.invokeMethodAsync('UpdateMediaTracks',
      subtitleTracks().map(track => describe(track, track.mode === 'showing')),
      availableAudio().map(track => describe(track, track.enabled))).catch(() => {});
  }

  for (const tracks of [video.textTracks, video.audioTracks]) {
    for (const event of ['addtrack', 'removetrack']) tracks?.addEventListener(event, publishTracks, options);
  }

  function clearSubtitle() {
    const previous = sidecar;
    previous?.remove();
    if (subtitleUrl) URL.revokeObjectURL(subtitleUrl);
    sidecar = undefined;
    subtitleUrl = undefined;
  }
  function seekTo(seconds) {
    if (!ready || !Number.isFinite(seconds) || !Number.isFinite(video.duration)) return;
    const next = Math.max(0, Math.min(video.duration, seconds));
    soughtToEnd = next >= video.duration;
    video.currentTime = next;
  }

  attachControls(stage, video, seekTo, listeners.signal);
  if (useLocalBlob) void loadMediaBlob();
  else video.src = url;

  return {
    checkpoint: () => save(true),
    pauseAndCheckpoint() {
      video.pause();
      return save(true);
    },
    seek(seconds) {
      seekTo(video.currentTime + seconds);
    },
    setSpeed(value) {
      if (!Number.isFinite(value) || value < 0.5 || value > 2) return;
      speed = value;
      video.playbackRate = value;
    },
    selectEmbeddedSubtitle(id) {
      selectedSubtitle = id;
      applyEmbeddedSelection();
      clearSubtitle();
    },
    selectAudio(id) {
      const tracks = availableAudio();
      const selected = tracks.find(track => trackId(track) === id);
      if (!selected) return false;
      for (const track of tracks) track.enabled = track === selected;
      return selected.enabled;
    },
    setSubtitle(language, cues, placement, offset = 0, version = 0) {
      selectedSubtitle = '';
      applyEmbeddedSelection();
      clearSubtitle();
      const element = document.createElement('track');
      sidecar = element;
      element.kind = 'subtitles';
      element.label = language || 'Subtitles';
      element.srclang = language || 'und';
      subtitleUrl = URL.createObjectURL(new Blob([placeSubtitles(cues, placement, offset)], {type: 'text/vtt'}));
      element.src = subtitleUrl;
      element.addEventListener('error', () => {
        if (sidecar === element && !listeners.signal.aborted)
          callbacks.invokeMethodAsync('SetSubtitleError', version).catch(() => {});
      }, options);
      video.appendChild(element);
      element.track.mode = 'showing';
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
      clearMediaSession();
      if (document.pictureInPictureElement === video)
        await document.exitPictureInPicture().catch(() => {});
      clearSubtitle();
      video.pause();
      video.removeAttribute('src');
      video.load();
      clearMediaBlob();
      await saving;
    }
  };
}
