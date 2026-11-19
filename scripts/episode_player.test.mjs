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
      calls.push(args);
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
      calls.push(args);
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
