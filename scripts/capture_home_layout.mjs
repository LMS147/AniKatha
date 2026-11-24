import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { setTimeout as delay } from 'node:timers/promises';

const [fixtures, output, mode] = process.argv.slice(2);
const baseline = mode === 'baseline';
const profile = await mkdtemp(join(tmpdir(), 'anikatha-home-chrome-'));
const chrome = spawn(process.env.CHROME_BIN ?? 'google-chrome', [
  '--headless', '--no-sandbox', '--disable-gpu', '--no-first-run',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank',
], { stdio: 'ignore' });

let socket;
try {
  await mkdir(output, { recursive: true });
  const address = await poll(async () => {
    const port = (await readFile(join(profile, 'DevToolsActivePort'), 'utf8').catch(() => '')).split('\n')[0];
    if (!port) return;
    const pages = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
    return pages.find(page => page.type === 'page')?.webSocketDebuggerUrl;
  });
  socket = new WebSocket(address);
  await new Promise((resolve, reject) => {
    socket.addEventListener('open', resolve, { once: true });
    socket.addEventListener('error', reject, { once: true });
  });
  let id = 0;
  async function command(method, params = {}) {
    const requestId = ++id;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        socket.removeEventListener('message', receive);
        reject(new Error('Chrome did not answer ' + method));
      }, 8000);
      function receive(event) {
        const result = JSON.parse(event.data);
        if (result.id !== requestId) return;
        socket.removeEventListener('message', receive);
        clearTimeout(timer);
        result.error ? reject(new Error(result.error.message)) : resolve(result.result);
      }
      socket.addEventListener('message', receive);
      socket.send(JSON.stringify({ id: requestId, method, params }));
    });
  }
  async function evaluate(fn, arg) {
    const result = await command('Runtime.evaluate', {
      expression: '(' + fn.toString() + ')(' + JSON.stringify(arg) + ')',
      returnByValue: true, awaitPromise: true,
    });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description);
    return result.result.value;
  }

  for (const width of [360, 390, 412]) {
    await command('Emulation.setDeviceMetricsOverride', { width, height: 844, mobile: true, deviceScaleFactor: 1 });
    await command('Emulation.setTouchEmulationEnabled', { enabled: true, maxTouchPoints: 5 });
    for (const name of ['anime', 'manga', 'empty']) {
      const url = pathToFileURL(resolve(fixtures, name + '.html')).href;
      await command('Page.navigate', { url });
      await poll(() => evaluate(url => location.href === url && document.readyState === 'complete', url));
      const active = name === 'manga' ? '#home-manga' : '#home-anime';
      const measurements = await evaluate(measure, { active, width, enlarged: false });
      assert.equal(measurements.width, width);
      assert.equal(measurements.touch, 5);
      if (!baseline) {
        assert.deepEqual(measurements.issues, []);
        const enlarged = await evaluate(measure, { active, width, enlarged: true });
        assert.deepEqual(enlarged.issues, []);
        await evaluate(() => { document.documentElement.style.fontSize = ''; });
      }
      if (width === 390 || name !== 'empty') {
        const sections = width === 390 && name !== 'empty' ?
          (name === 'anime' ? ['top','latest','upcoming','releases','schedule'] : ['top','read','discover','popular','new','updates']) : ['top'];
        for (const section of sections) {
          await evaluate(async ({ active, section }) => {
            const view = document.querySelector(active);
            const target = document.getElementById('home-' + section + '-title');
            view.scrollTop = section === 'top' ? 0 : view.scrollTop + target.parentElement.parentElement.getBoundingClientRect().top - view.getBoundingClientRect().top - 7;
            const images = [...view.querySelectorAll('img')];
            for (const image of images) image.loading = 'eager';
            await Promise.all(images.map(image => image.decode().catch(() => {})));
            await new Promise(requestAnimationFrame);
          }, { active, section });
          const { data } = await command('Page.captureScreenshot', { format: 'png' });
          await writeFile(join(output, width + '-' + name + '-' + section + '.png'), Buffer.from(data, 'base64'));
        }
      }
      console.log(mode ?? 'updated', width, name, JSON.stringify(measurements));
    }
  }
} finally {
  socket?.close();
  chrome.kill();
  await delay(300);
  await rm(profile, { recursive: true, force: true });
}

async function poll(find) {
  for (let attempt = 0; attempt < 60; attempt++) {
    const value = await find();
    if (value) return value;
    await delay(150);
  }
  throw new Error('Browser layout did not become available.');
}

function measure({ active, width, enlarged }) {
  document.documentElement.style.fontSize = enlarged ? '150%' : '';
  const view = document.querySelector(active);
  const issues = [];
  const bounds = element => element.getBoundingClientRect();
  if (view.scrollWidth > view.clientWidth + 1 || document.documentElement.scrollWidth > width + 1) issues.push('horizontal overflow');
  const viewBox = bounds(view);
  const viewStyle = getComputedStyle(view);
  const nav = document.querySelector('.bottom-nav');
  if (viewStyle.paddingLeft !== '16px' || viewStyle.paddingRight !== '16px') issues.push('page gutter');
  if (viewStyle.paddingBottom !== '112px' || getComputedStyle(nav).borderRadius !== '9999px') issues.push('navigation size or clearance');
  const rootChildren = [...document.querySelector('.home-landing').children];
  const hero = view.closest('.home-landing').querySelector('.home-spotlight');
  if (hero) {
    const tabs = view.closest('.home-landing').querySelector('.home-mode');
    if (rootChildren.indexOf(hero) !== 1 || rootChildren.indexOf(tabs) !== 2 || bounds(tabs).bottom >= innerHeight / 1.4)
      issues.push('spotlight placement or tab visibility');
    const action = hero.querySelector('.home-spotlight-action');
    if (action && (bounds(action).bottom > bounds(hero).bottom || bounds(action).right > width))
      issues.push('spotlight action clipped');
  }
  for (const control of document.querySelectorAll('.home-mode button,' + active + ' .home-segment button')) {
    const box = bounds(control);
    if (box.left < viewBox.left - 1 || box.right > viewBox.right + 1 || control.scrollWidth > control.clientWidth + 1) issues.push('clipped control');
  }
  for (const grid of view.querySelectorAll('.home-grid')) {
    if (!grid.children.length) continue;
    const gridStyle = getComputedStyle(grid);
    const columns = gridStyle.gridTemplateColumns.split(' ').map(parseFloat);
    if (gridStyle.columnGap !== '12px') issues.push('poster gutter');
    if (columns.length !== 2 || Math.abs(columns[0] - columns[1]) > 1) issues.push('unequal poster columns');
    const art = grid.querySelector('.home-art');
    if (!art) continue;
    const poster = bounds(art);
    if (Math.abs(poster.width / poster.height - 2 / 3) > 0.012) issues.push('poster ratio');
    const band = grid.querySelector('.home-card-meta');
    if (band && active === '#home-anime' && grid.querySelector('.home-card:first-child .home-card-meta') &&
        Math.abs(bounds(band).top - poster.bottom) > 1) issues.push('detached anime strip');
  }
  const calendar = view.querySelector('.home-calendar');
  if (calendar) {
    const heading = bounds(calendar.closest('header') ?? calendar.closest('section'));
    if (bounds(calendar).width < heading.width - 28 || calendar.querySelectorAll('.home-day').length !== 3)
      issues.push('schedule dates do not fill their panel');
  }
  const release = view.querySelector('.home-schedule button');
  if (release) {
    const time = bounds(release.querySelector('time'));
    const episode = bounds(release.querySelector('small'));
    const title = bounds(release.querySelector('strong'));
    if (episode.left <= time.left || Math.abs(episode.top - time.top) > 3 || title.top < Math.max(time.bottom, episode.bottom))
      issues.push('schedule row alignment');
  }
  const before = view.scrollTop;
  view.scrollTop = view.scrollHeight;
  if (bounds(view.lastElementChild).bottom > bounds(nav).top) issues.push('last section under navigation');
  view.scrollTop = before;
  return { issues, width: innerWidth, touch: navigator.maxTouchPoints };
}
