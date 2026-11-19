import assert from 'node:assert/strict';
import test from 'node:test';
import { attachControls, playbackTime } from '../src/AniKatha.UI/wwwroot/player-presentation.mjs';

class Element extends EventTarget {
  dataset = {};
  attributes = new Map();
  setAttribute(name, value) { this.attributes.set(name, value); }
  getAttribute(name) { return this.attributes.get(name); }
  emit(name) { this.dispatchEvent(new Event(name)); }
}

test('transport reports real state, keeps the scrub preview, and separates opening from buffering', t => {
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const video = Object.assign(new Element(), {
    paused: true,
    ended: false,
    duration: NaN,
    currentTime: 0,
    play() { this.paused = false; this.emit('play'); return Promise.resolve(); },
    pause() { this.paused = true; this.emit('pause'); },
  });
  const stage = new Element();
  stage.classList = new Set(['player-fullscreen']);
  stage.classList.contains = stage.classList.has.bind(stage.classList);
  const toggle = new Element(), playLabel = new Element(), slider = new Element(), elapsed = new Element(), duration = new Element();
  const elements = new Map([
    ['[data-player-toggle]', toggle], ['[data-player-play-label]',playLabel],['[data-player-scrubber]', slider],
    ['[data-player-elapsed]', elapsed],['[data-player-duration]', duration],
  ]);
  stage.querySelector = selector => elements.get(selector);
  const seek = new Element();
  stage.querySelectorAll = () => [seek];
  const oldObserver = globalThis.MutationObserver;
  globalThis.MutationObserver = class { observe() {} disconnect() {} };
  t.after(() => { globalThis.MutationObserver = oldObserver; });
  const abort = new AbortController();
  let sought;
  attachControls(stage, video, time => { sought = time; video.currentTime = time; }, abort.signal);

  assert.equal(slider.disabled, true);
  assert.equal(seek.disabled, true);
  assert.equal(duration.hidden, true);
  video.emit('loadstart');
  assert.equal(stage.dataset.opening, 'true');
  video.duration = 73.3;
  video.emit('durationchange');
  assert.equal(duration.textContent, '1:13');
  assert.equal(slider.disabled, false);
  assert.equal(seek.disabled, false);
  video.emit('canplay');
  assert.equal(stage.dataset.opening, undefined);
  toggle.emit('click');
  assert.equal(playLabel.textContent, 'Pause');
  video.emit('playing');
  video.emit('waiting');
  assert.equal(stage.dataset.buffering, 'true');
  video.emit('playing');
  assert.equal(stage.dataset.buffering, undefined);
  t.mock.timers.tick(3000);
  assert.equal(stage.dataset.controlsHidden, 'true');
  video.emit('click');
  assert.equal(stage.dataset.controlsHidden, undefined);
  slider.value = '14.6';
  slider.emit('input');
  video.currentTime = 3;
  video.emit('timeupdate');
  assert.equal(elapsed.textContent, '0:14');
  assert.equal(sought, undefined);
  slider.emit('change');
  assert.equal(sought, 14.6);
  assert.equal(slider.getAttribute('aria-valuetext'), '0:14 of 1:13');
  toggle.emit('click');
  t.mock.timers.tick(3000);
  assert.equal(stage.dataset.controlsHidden, undefined);
  assert.equal(playLabel.textContent, 'Play');
  abort.abort();
  assert.equal(playbackTime(3670), '1:01:10');
});
