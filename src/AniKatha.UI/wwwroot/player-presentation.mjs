export function playbackTime(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00';
  const whole = Math.floor(seconds);
  const minutes = Math.floor(whole / 60);
  const two = part => String(part).padStart(2, '0');
  return minutes < 60 ? `${minutes}:${two(whole % 60)}` :
    `${Math.floor(minutes / 60)}:${two(minutes % 60)}:${two(whole % 60)}`;
}

export function attachControls(stage, video, seekTo, signal) {
  if (!stage) return;
  const toggle = stage.querySelector('[data-player-toggle]');
  const label = stage.querySelector('[data-player-play-label]');
  const slider = stage.querySelector('[data-player-scrubber]');
  const seekButtons = stage.querySelectorAll('[data-player-seek]');
  const elapsed = stage.querySelector('[data-player-elapsed]');
  const duration = stage.querySelector('[data-player-duration]');
  const options = { signal };
  const playing = () => !video.paused && !video.ended;
  const hasControlFocus = () => !!stage.querySelector(':focus-visible');
  const fullscreen = () => stage.classList.contains('player-fullscreen');
  let scrubbing = false;
  let started = false;
  let conceal;

  function showOverlay() {
    delete stage.dataset.controlsHidden;
    clearTimeout(conceal);
    if (playing() && !scrubbing && !hasControlFocus()) {
      conceal = setTimeout(() => { stage.dataset.controlsHidden = 'true'; }, 3000);
    }
  }

  function renderTime() {
    const known = Number.isFinite(video.duration) && video.duration > 0;
    slider.disabled = !known;
    for (const button of seekButtons) button.disabled = !known;
    duration.hidden = !known;
    if (known) {
      slider.max = video.duration;
      duration.textContent = playbackTime(video.duration);
    }
    if (!scrubbing) slider.value = known ? video.currentTime : 0;
    const time = playbackTime(scrubbing ? Number(slider.value) : video.currentTime);
    elapsed.textContent = time;
    slider.setAttribute('aria-valuetext', known ? `${time} of ${duration.textContent}` : time);
  }

  function renderTransport() {
    const action = playing() ? 'Pause' : 'Play';
    toggle.setAttribute('aria-label', action);
    label.textContent = action;
    stage.dataset.playing = String(playing());
    showOverlay();
  }

  video.addEventListener('loadstart', () => {
    started = false;
    stage.dataset.opening = 'true';
    delete stage.dataset.buffering;
    delete stage.dataset.mediaReady;
    renderTime();
    renderTransport();
  }, options);
  for (const event of ['loadedmetadata', 'durationchange', 'seeked', 'timeupdate']) {
    video.addEventListener(event, renderTime, options);
  }
  for (const event of ['play', 'pause', 'ended']) {
    video.addEventListener(event, renderTransport, options);
  }
  for (const event of ['playing', 'canplay', 'pause', 'error']) {
    video.addEventListener(event, () => {
      if (event === 'playing') started = true;
      if (event === 'canplay' || event === 'playing') stage.dataset.mediaReady = 'true';
      delete stage.dataset.opening;
      delete stage.dataset.buffering;
    }, options);
  }
  for (const event of ['waiting', 'stalled']) {
    video.addEventListener(event, () => {
      if (started && playing()) stage.dataset.buffering = 'true';
    }, options);
  }

  toggle.addEventListener('click', () => {
    if (playing()) video.pause();
    else video.play()?.catch(() => {});
  }, options);
  slider.addEventListener('input', () => { scrubbing = true; renderTime(); showOverlay(); }, options);
  slider.addEventListener('change', () => {
    const position = Number(slider.value);
    scrubbing = false;
    seekTo(position);
    renderTime();
    showOverlay();
  }, options);
  slider.addEventListener('pointercancel', () => { scrubbing = false; renderTime(); showOverlay(); }, options);
  video.addEventListener('click', () => {
    if (!fullscreen()) return;
    if (stage.dataset.controlsHidden) showOverlay();
    else if (playing() && !scrubbing && !hasControlFocus()) {
      clearTimeout(conceal);
      stage.dataset.controlsHidden = 'true';
    }
  }, options);
  stage.addEventListener('focusin', showOverlay, options);
  stage.addEventListener('focusout', () => queueMicrotask(() => { if (!signal.aborted) showOverlay(); }), options);
  stage.addEventListener('keydown', event => {
    if (event.key !== 'Tab') return;
    const dialog = stage.querySelector('.player-sheet');
    const controls = Array.from(dialog?.querySelectorAll('button:not(:disabled)') ?? []);
    if (!controls.length) return;
    const first = controls[0], last = controls.at(-1);
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }, options);
  const layout = new MutationObserver(showOverlay);
  layout.observe(stage, { attributes: true, attributeFilter: ['class', 'data-panel'] });
  signal.addEventListener('abort', () => {
    clearTimeout(conceal);
    layout.disconnect();
  }, { once: true });
  renderTime();
  renderTransport();
}

export function focusPlayerPanel(sheet) {
  sheet?.querySelector('[data-player-close]')?.focus({ preventScroll: true });
}

export function focusPlayerMenu(stage, menu) {
  stage?.querySelector(`[data-player-menu="${menu}"]`)?.focus({ preventScroll: true });
}

export function focusSourcePicker(id) {
  requestAnimationFrame(() => {
    const picker = document.getElementById(id);
    picker?.focus({ preventScroll: true });
    picker?.scrollIntoView({ block: 'start' });
  });
}

export function revealPlayer(stage) {
  stage?.scrollIntoView({ block: 'start' });
}
