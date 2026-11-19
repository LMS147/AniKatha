import assert from 'node:assert/strict';
import { test } from 'node:test';
import { attachPlayer, placeSubtitles } from '../src/AniKatha.UI/wwwroot/episode-player.mjs';

class TestVideo extends EventTarget {
  currentTime = 0;
  duration = 33;
  ended = false;
  seeking = false;

  emit(name) { this.dispatchEvent(new Event(name)); }
  pause() { this.emit('pause'); }
  load() { this.currentTime = 0; }
  removeAttribute(name) { delete this[name]; }
}

test('seeks to the saved position and stores checkpoints, pauses, and seeks', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  const writes = [];
  const player = attachPlayer(video, {
    async invokeMethodAsync(method, seconds) {
      if (method === 'UpdateMediaTracks') return;
      assert.equal(method, 'SavePosition');
      writes.push(seconds);
      return true;
    },
  }, 'https://example.com/sample.mp4', 12.75);

  video.emit('loadedmetadata');
  assert.equal(video.currentTime, 12.75);

  video.currentTime = 13;
  video.emit('timeupdate');
  video.currentTime = 15;
  video.emit('timeupdate');
  video.currentTime = 15.5;
  video.pause();
  video.currentTime = 4;
  video.emit('seeked');

  await player.dispose();
  assert.deepEqual(writes, [15, 15.5, 4]);
  assert.equal(video.src, undefined);
});

test('retries from the last position, reports media failure, and resets when finished', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  const calls = [];
  const player = attachPlayer(video, {
    async invokeMethodAsync(...args) {
      if (args[0] !== 'UpdateMediaTracks') calls.push(args);
      return true;
    },
  }, 'https://example.com/sample.mp4', 0);

  video.emit('loadedmetadata');
  video.currentTime = 19;
  video.emit('error');
  player.retry();
  video.emit('loadedmetadata');
  assert.equal(video.currentTime, 19);
  video.currentTime = video.duration;
  video.ended = true;
  video.emit('ended');

  await player.dispose();
  assert.deepEqual(calls, [['SetPlaybackError'], ['SavePosition', 19], ['SavePosition', 0]]);
});

test('provider completion preserves the timestamp and is recorded once', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  const calls = [];
  const player = attachPlayer(video, {
    async invokeMethodAsync(...args) {
      if (args[0] !== 'UpdateMediaTracks') calls.push(args);
      return true;
    },
  }, 'https://example.com/sample.mp4', 12, true);

  video.emit('loadedmetadata');
  video.currentTime = video.duration;
  video.ended = true;
  video.emit('ended');
  video.emit('ended');
  await player.dispose();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls, [['SavePosition', video.duration], ['CompleteEpisode']]);
});

test('seeking directly to the end saves only the position', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  const calls = [];
  const player = attachPlayer(video, {
    async invokeMethodAsync(...args) { if (args[0] !== 'UpdateMediaTracks') calls.push(args); return true; },
  }, 'https://example.com/sample.mp4', 0, true);

  video.emit('loadedmetadata');
  player.seek(video.duration);
  video.emit('seeked');
  video.ended = true;
  video.emit('ended');
  await player.checkpoint();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls, [['SavePosition', video.duration]]);

  video.ended = false;
  player.seek(-5);
  video.emit('seeked');
  video.currentTime = video.duration;
  video.ended = true;
  video.emit('ended');
  await player.dispose();
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(calls.at(-1), ['CompleteEpisode']);
});

test('speed survives media load and seek clamps before saving', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  const saved = [];
  const player = attachPlayer(video, {
    async invokeMethodAsync(method, value) { if (method === 'SavePosition') saved.push(value); return true; },
  }, 'https://example.com/video.mp4', 5, true, 1.25);
  video.emit('loadedmetadata');
  assert.equal(video.playbackRate, 1.25);
  player.setSpeed(1.5);
  assert.equal(video.playbackRate, 1.5);
  player.seek(-15);
  assert.equal(video.currentTime, 0);
  video.emit('seeked');
  player.seek(1000);
  assert.equal(video.currentTime, video.duration);
  video.emit('seeked');
  await player.dispose();
  assert.deepEqual(saved, [0, video.duration]);
});

test('places subtitle cues and preserves the automatic track layout', () => {
  const cues = 'WEBVTT\n\n00:00:01.000 --> 00:00:03.000 align:start line:42%\nDialogue\n';
  assert.equal(placeSubtitles(cues, 'automatic'), cues);
  assert.match(placeSubtitles(cues, 'top'), /align:start line:10%/);
  assert.match(placeSubtitles(cues, 'bottom'), /line:90%/);
  assert.ok(!placeSubtitles(cues, 'bottom').includes('line:42%'));
});

test('Android offline playback loads a local Blob and revokes it when closed', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  window.location = { hostname: '0.0.0.1' };
  const video = new TestVideo();
  const originalFetch = globalThis.fetch;
  let localUrl;
  try {
    globalThis.fetch = async (url, { signal }) => {
      assert.equal(url, 'offline-media/example/video.mp4');
      assert.equal(signal.aborted, false);
      return new Response(new Uint8Array(32), { headers: { 'Content-Type': 'video/mp4' } });
    };
    const player = attachPlayer(video, { async invokeMethodAsync() { return true; } },
      'offline-media/example/video.mp4', 10, true, 1, false, true);
    await new Promise(resolve => setImmediate(resolve));
    localUrl = video.src;
    assert.match(localUrl, /^blob:/);
    video.emit('loadedmetadata');
    assert.equal(video.currentTime, 10);
    await player.dispose();
  } finally {
    globalThis.fetch = originalFetch;
  }
  await assert.rejects(globalThis.fetch(localUrl));
});

test('sidecar offset moves known cues both ways and preserves cue settings', () => {
  const source = 'WEBVTT\n\nNOTE untouched\n00:00:01.000 --> 00:00:02.000\n\n' +
    'opening\n00:00:00.100 --> 00:00:00.200\nFirst\n\n00:01.000 --> 00:03.000 align:start line:45%\nLater';
  const later = placeSubtitles(source, 'top', 250);
  assert.match(later, /00:00:01\.250 --> 00:00:03\.250 align:start line:10%/);
  assert.match(later, /NOTE untouched\n00:00:01\.000 --> 00:00:02\.000/);
  const earlier = placeSubtitles(source, 'automatic', -1250);
  assert.doesNotMatch(earlier, /opening|First/);
  assert.match(earlier, /00:00:00\.000 --> 00:00:01\.750 align:start line:45%/);
  assert.equal(placeSubtitles(source, 'automatic', 0), source);
});

test('embedded track inventory and selection follow the media engine', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const video = new TestVideo();
  class TrackList extends EventTarget {
    items = [];
    [Symbol.iterator]() { return this.items.values(); }
    add(track) { this.items.push(track); this.dispatchEvent(new Event('addtrack')); }
  }
  video.textTracks = new TrackList();
  video.audioTracks = new TrackList();
  const english = { language: 'en', label: 'SDH', kind: 'captions', mode: 'disabled' };
  const japanese = { language: 'ja', label: 'Original', kind: 'main', enabled: false };
  const dub = { language: 'en', label: 'Dub', kind: 'main', enabled: true };
  video.textTracks.add(english);
  video.audioTracks.add(dub);
  video.audioTracks.add(japanese);
  let inventory;
  const player = attachPlayer(video, {
    async invokeMethodAsync(method, subtitles, audio) {
      if (method === 'UpdateMediaTracks') inventory = { subtitles, audio };
      return true;
    },
  }, 'https://example.com/episode.mp4', 0);
  video.emit('loadedmetadata');
  assert.equal(inventory.subtitles[0].role, 'captions');
  player.selectEmbeddedSubtitle(inventory.subtitles[0].id);
  assert.equal(english.mode, 'showing');
  player.selectAudio(inventory.audio[1].id);
  assert.equal(japanese.enabled, true);
  assert.equal(dub.enabled, false);
  player.selectEmbeddedSubtitle('');
  const late = { language: 'ja', label: '', kind: 'forced', mode: 'showing' };
  video.textTracks.add(late);
  assert.equal(late.mode, 'disabled');
  assert.equal(english.mode, 'disabled');
  assert.equal(inventory.subtitles.length, 2);
  await player.dispose();
});

test('media actions follow the active video and clear when playback ends', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  const originalNavigator = Object.getOwnPropertyDescriptor(globalThis, 'navigator');
  const originalMetadata = globalThis.MediaMetadata;
  const actions = new Map();
  const session = { setActionHandler(action, handler) { actions.set(action, handler); } };
  Object.defineProperty(globalThis, 'navigator', { configurable: true, value: { mediaSession: session } });
  globalThis.MediaMetadata = class { constructor(metadata) { Object.assign(this, metadata); } };
  const callbacks = { async invokeMethodAsync() { return true; } };
  try {
    const oldVideo = new TestVideo();
    const old = attachPlayer(oldVideo, callbacks, 'https://example.com/1.mp4', 0, false, 1, false, false, 'Series', 'Episode 1');
    oldVideo.emit('playing');
    assert.equal(session.playbackState, 'playing');
    const video = new TestVideo();
    const player = attachPlayer(video, callbacks, 'https://example.com/2.mp4', 0, false, 1, false, false, 'Series', 'Episode 2');
    video.play = async () => video.emit('playing');
    video.emit('playing');
    await old.dispose();
    assert.equal(session.metadata.artist, 'Episode 2');
    actions.get('pause')();
    assert.equal(session.playbackState, 'paused');
    await actions.get('play')();
    assert.equal(session.playbackState, 'playing');
    video.emit('ended');
    assert.equal(session.playbackState, 'none');
    assert.equal(actions.get('pause'), null);
    await player.dispose();
  } finally {
    if (originalNavigator) Object.defineProperty(globalThis, 'navigator', originalNavigator);
    else delete globalThis.navigator;
    globalThis.MediaMetadata = originalMetadata;
  }
});

test('picture-in-picture toggles the same video where the runtime supports it', async () => {
  globalThis.document = new EventTarget();
  globalThis.window = new EventTarget();
  document.pictureInPictureEnabled = true;
  const video = new TestVideo();
  const button = new EventTarget();
  button.hidden = true;
  const callbacks = { async invokeMethodAsync() { return true; } };
  video.requestPictureInPicture = async () => {
    document.pictureInPictureElement = video;
    video.emit('enterpictureinpicture');
  };
  document.exitPictureInPicture = async () => {
    document.pictureInPictureElement = null;
    video.emit('leavepictureinpicture');
  };
  const player = attachPlayer(video, callbacks, 'https://example.com/1.mp4', 8, false, 1, false, false, '', '', button);
  video.emit('loadedmetadata');
  assert.equal(button.hidden, false);
  assert.equal(button.disabled, false);
  button.dispatchEvent(new Event('click'));
  assert.equal(document.pictureInPictureElement, video);
  assert.equal(button.textContent, 'Exit picture-in-picture');
  button.dispatchEvent(new Event('click'));
  assert.equal(document.pictureInPictureElement, null);
  assert.equal(video.currentTime, 8);
  await player.dispose();
});
