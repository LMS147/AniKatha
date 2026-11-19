import { execFileSync } from 'node:child_process';
import { writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export function hasNavigation(snapshot) {
  const expected = ['Home', 'Anime', 'Manga', 'Library', 'Profile'];
  return !snapshot.errorVisible && expected.every(label => snapshot.links?.includes(label));
}

export async function checkWebView(endpoint = 'http://127.0.0.1:9222') {
  const mode = process.argv[2] ?? 'fresh';
  const aniListConfigured = process.env.ANIKATHA_TEST_ANILIST_CONFIGURED;
  const fixtureRef = process.env.ANIKATHA_TEST_FIXTURE_REF;
  const fixture = 'https://raw.githubusercontent.com/LMS147/AniKatha/' +
    (fixtureRef ?? '118ce2c1895db30ec2ef2dd7db50e952c4de1bff') + '/examples/addons/story-sampler/manifest.json';
  const testDownloads = mode === 'fresh' && Boolean(fixtureRef);
  const pages = await (await fetch(`${endpoint}/json/list`, {signal: AbortSignal.timeout(10000)})).json();
  const page = pages.find(p => p.type === 'page' && (p.title?.includes('AniKatha') || p.url?.startsWith('https://0.0.0.1')));
  if (!page?.webSocketDebuggerUrl) throw new Error('AniKatha WebView debugging target was not found.');
  const address = new URL(page.webSocketDebuggerUrl);
  // Android advertises the socket's device address; use the forwarded local port.
  address.host = new URL(endpoint).host;
  const socket = new WebSocket(address);
  const pending = new Map();
  let sequence = 0;
  socket.addEventListener('message', e => {
    const reply = JSON.parse(e.data);
    if (pending.has(reply.id)) {
      const {resolve, reject, timer} = pending.get(reply.id);
      clearTimeout(timer); pending.delete(reply.id);
      reply.error ? reject(new Error(JSON.stringify(reply.error))) : resolve(reply.result);
    }
  });
  const connected = new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('WebView debug connection timed out.')), 10000);
    socket.addEventListener('open', () => {clearTimeout(timer); resolve();}, {once:true});
    socket.addEventListener('error', () => {clearTimeout(timer); reject(new Error('WebView debug connection failed.'));}, {once:true});
  });
  const command = (method, params, timeout = 10000) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => {pending.delete(id); reject(new Error(`${method} timed out.`));}, timeout);
    pending.set(id, {resolve, reject, timer}); socket.send(JSON.stringify({id, method, params}));
  });
  try {
    await connected;
    const reply = await command('Runtime.evaluate', {
      awaitPromise: true,
      returnByValue: true,
      expression: `(async () => {
        const visible = element => !!element && element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
        let snapshot;
        for (let i = 0; i < 160; i++) {
          snapshot = {
            links: Array.from(document.querySelectorAll('.bottom-nav a')).filter(visible).map(a => a.textContent.trim()),
            errorVisible: visible(document.querySelector('#blazor-error-ui')),
            text: document.body?.innerText
          };
          if (snapshot.links.length >= 5 && !snapshot.errorVisible) return snapshot;
          await new Promise(resolve => setTimeout(resolve, 100));
        }
        return snapshot;
      })()`,
    }, 20000);
    if (reply.exceptionDetails) throw new Error('WebView DOM inspection failed.');
    const snapshot = reply.result?.value;
    if (!snapshot) throw new Error('WebView returned no DOM snapshot.');
    await writeFile('artifacts/startup/webview-' + mode + '.json', JSON.stringify(snapshot, null, 2));
    if (!hasNavigation(snapshot)) throw new Error('App navigation did not render or the Blazor error UI is visible.');
    console.log('PASS: all five navigation tabs rendered in the installed APK WebView.');

    if (mode === 'upgrade') {
      const upgraded = await command('Runtime.evaluate', {
        awaitPromise: true, returnByValue: true,
        expression: `(async () => {
          async function wait(find, label) {
            for (let i = 0; i < 250; i++) {
              const result = find();
              if (result) return result;
              await new Promise(resolve => setTimeout(resolve, 100));
            }
            throw new Error(label);
          }
          const button = name => [...document.querySelectorAll('button')].find(x => x.textContent.trim() === name && !x.disabled);
          const tab = name => document.querySelector('.bottom-nav a[href="/' + name + '"]').click();
          tab('profile');
          (await wait(() => document.querySelector('a[href="/profile/sources"]'), 'Sources link')).click();
          await wait(() => document.querySelector('.addon-row strong')?.textContent === 'AniKatha Story Sampler' && button('Disable'), 'Installed source did not survive upgrade');
          tab('manga');
          for (const name of ['The Midnight Lighthouse', 'Chapter 1', 'Read Chapter 1'])
            (await wait(() => button(name), name)).click();
          await wait(() => {
            const image = document.querySelector('.reader-page');
            return image?.naturalWidth > 0 && image.src.endsWith('-2.webp');
          }, 'Reading position lost during upgrade');
          return true;
        })()`,
      }, 90000);
      if (upgraded.result?.value !== true) throw new Error(upgraded.exceptionDetails?.exception?.description ?? 'Upgrade check failed');
      console.log('PASS: APK upgrade preserved the enabled provider and existing chapter page.');
      return;
    }

    for (const tab of ['Anime', 'Manga', 'Anime']) {
      const navigation = await command('Runtime.evaluate', {
        awaitPromise: true,
        returnByValue: true,
        expression: `(async () => {
          const tab = ${JSON.stringify(tab)};
          const path = '/' + tab.toLowerCase();
          const link = document.querySelector('.bottom-nav a[href="' + path + '"]');
          if (!link) return false;
          link.click();
          for (let attempt = 0; attempt < 60; attempt++) {
            const heading = document.querySelector('h1')?.textContent.trim();
            const search = document.querySelector('input[aria-label="Search titles"]');
            if (location.pathname === path && heading === tab &&
                search?.placeholder === 'Search ' + tab.toLowerCase() + ' titles…') return true;
            await new Promise(resolve => setTimeout(resolve, 100));
          }
          return false;
        })()`,
      });
      if (navigation.result?.value !== true) throw new Error(`${tab} opened the wrong catalogue.`);
    }
    console.log('PASS: Anime → Manga → Anime opens the correct catalogue on each tap.');

    const player = await command('Runtime.evaluate', {
      awaitPromise: true,
      returnByValue: true,
      expression: `(async () => {
        async function waitFor(selector) {
          for (let attempt = 0; attempt < 25; attempt++) {
            const element = document.querySelector(selector);
            if (element) return element;
            await new Promise(resolve => setTimeout(resolve, 100));
          }
          throw new Error('Missing ' + selector);
        }
        document.querySelector('.bottom-nav a[href="/"]').click();
        (await waitFor('a[href="/anime/preview"]')).click();
        (await waitFor('a[href="/anime/preview/episode-1"]')).click();
        const video = await waitFor('video[src]');
        return location.pathname === '/anime/preview/episode-1' && video.controls &&
          video.src === 'https://media.w3.org/2010/05/bunny/trailer.mp4';
      })()`,
    });
    if (player.result?.value !== true) throw new Error('Sample episode player did not mount in the APK WebView.');
    console.log('PASS: Home → sample → Episode 1 mounts the in-app video player.');

    const reader = await command('Runtime.evaluate', {
      awaitPromise: true,
      returnByValue: true,
      expression: `(async () => {
        async function waitFor(find, description) {
          for (let attempt = 0; attempt < 60; attempt++) {
            const result = find();
            if (result) return result;
            await new Promise(resolve => setTimeout(resolve, 100));
          }
          throw new Error('Timed out waiting for ' + description);
        }
        const find = selector => document.querySelector(selector);
        const openChapter = async () => {
          const link = await waitFor(() => find('a[href="/manga/preview/chapter-1"]'), 'chapter link');
          link.click();
        };
        const loadedPage = number => {
          const image = find('.reader-page');
          return image?.complete && image.naturalWidth > 0 && image.src.endsWith('-' + number + '.webp');
        };

        document.querySelector('.bottom-nav a[href="/manga"]').click();
        const sample = await waitFor(() => find('a[href="/manga/preview"]'), 'manga sample');
        sample.click();
        await openChapter();
        await waitFor(() => loadedPage(1), 'bundled page 1');
        (await waitFor(() => find('.reader-controls button.primary:not(:disabled)'), 'Next page')).click();
        await waitFor(() => loadedPage(2) && find('.reader-controls button.primary:not(:disabled)'), 'saved page 2');
        find('.reader-header a').click();
        await waitFor(() => find('a[href="/manga/preview/chapter-1"]')?.textContent.includes('Resume at page 2'), 'resume link');
        await openChapter();
        await waitFor(() => loadedPage(2), 'resumed page 2');
        (await waitFor(() => find('.reader-controls button.primary:not(:disabled)'), 'Next page from page 2')).click();
        await waitFor(() => loadedPage(3) && find('.reader-controls button.secondary:not(:disabled)'), 'last bundled page');
        (await waitFor(() => [...document.querySelectorAll('.reader-controls button.secondary:not(:disabled)')]
          .find(button => button.textContent.trim() === '← Previous'), 'previous-page control')).click();
        await waitFor(() => loadedPage(2), 'Previous page');
        return true;
      })()`,
    });
    if (reader.result?.value !== true) {
      throw new Error(reader.exceptionDetails?.exception?.description ?? 'Sample manga chapter did not load or resume.');
    }
    console.log('PASS: installed APK renders all three bundled manga pages, turns pages, and restores the saved page.');


    const httpAddon = await command('Runtime.evaluate', {
      awaitPromise: true, returnByValue: true,
      expression: `(async () => {
        async function wait(find, description) {
          for (let i = 0; i < 300; i++) {
            const found = find();
            if (found) return found;
            await new Promise(resolve => setTimeout(resolve, 100));
          }
          throw new Error('Timed out waiting for ' + description);
        }
        const button = name => Array.from(document.querySelectorAll('button')).find(b => (b.textContent.trim() === name || b.getAttribute('aria-label') === name) && !b.disabled);
        const click = async name => (await wait(() => button(name), name)).click();
        const tab = name => document.querySelector('.bottom-nav a[href="/' + name + '"]').click();
        const open = async path => (await wait(() => document.querySelector('a[href="' + path + '"]'), path)).click();
        const oldProfile = ${mode === 'seed'};
        const sources = async () => {
          tab('profile');
          if (!oldProfile) await open('/profile/sources');
        };
        tab('profile');
        const expectedAniList = ${JSON.stringify(mode === 'fresh' ? aniListConfigured ?? '' : '')};
        if (!oldProfile && expectedAniList) await open('/profile/anilist');
        if (expectedAniList === 'true') {
          await wait(() => button('Connect AniList'), 'AniList sign-in in the configured APK');
        } else if (expectedAniList === 'false') {
          await wait(() => document.querySelector('.anilist-card')?.textContent.includes('AniList sign-in is unavailable for this build.'),
            'AniList availability notice in the unconfigured APK');
        }
        await sources();
        const input = await wait(() => document.querySelector('input[inputmode="url"]'), 'manifest input');
        input.value = ${JSON.stringify(fixture)};
        input.dispatchEvent(new Event('input', {bubbles:true}));
        if (!oldProfile) await click('Check manifest');
        await click('Add source');
        await wait(() => document.querySelector('.addon-row strong')?.textContent === 'AniKatha Story Sampler', 'installed sampler');
        tab('anime');
        await click('Big Buck Bunny');
        await click('Sample trailer');
        await click('Play trailer');
        await wait(() => document.querySelector('video')?.src === 'https://media.w3.org/2010/05/bunny/trailer.mp4', 'resolved anime video');
        if (${testDownloads}) await click('Download Compatibility clip');
        tab('manga');
        await click('The Midnight Lighthouse');
        await click('Chapter 1');
        await click('Read Chapter 1');
        const imageAt = number => {
          const image = document.querySelector('.reader-page');
          return image?.src.includes('raw.githubusercontent.com') && image.src.endsWith('-' + number + '.webp') && image.naturalWidth > 0;
        };
        await wait(() => imageAt(1), 'resolved manga page');
        await click('Next page →');
        await wait(() => imageAt(2), 'second manga page');
        await sources();
        await click('Disable');
        await wait(() => button('Enable'), 'disabled source');
        tab('manga');
        await wait(() => document.querySelector('h1')?.textContent === 'Manga', 'manga tab');
        if (button('The Midnight Lighthouse')) throw new Error('Disabled provider still contributes results');
        await sources();
        await click('Enable');
        await wait(() => button('Disable'), 'enabled source');
        tab('manga');
        await click('The Midnight Lighthouse');
        await click('Chapter 1');
        await click('Read Chapter 1');
        await wait(() => imageAt(2), 'restored reading position');
        if (${testDownloads}) {
          await click('Download chapter');
          tab('library');
          await click('Downloads');
          await wait(() => [...document.querySelectorAll('a')].filter(x => x.textContent.trim() === 'Open offline').length === 2, 'completed downloads');
        }
        return true;
      })()`,
    }, 180000);
    if (httpAddon.result?.value !== true)
      throw new Error(httpAddon.exceptionDetails?.exception?.description ?? 'Installed HTTP add-on workflow failed.');
    console.log('PASS: installed HTTP add-on supplies anime and manga resources and preserves reading progress across disable/enable.');
    if (mode === 'fresh' && aniListConfigured) {
      console.log('PASS: Profile shows the AniList sign-in state expected for this APK.');
    }
    if (testDownloads) {
      const adb = (...args) => execFileSync('adb', ['shell', ...args], { encoding: 'utf8' }).trim();
      adb('cmd', 'connectivity', 'airplane-mode', 'enable');
      try {
        if (adb('settings', 'get', 'global', 'airplane_mode_on') !== '1') throw new Error('Airplane mode did not enable.');
        const offline = await command('Runtime.evaluate', {
          awaitPromise: true, returnByValue: true,
          expression: `(async () => {
            async function wait(find, label) {
              for (let i = 0; i < 300; i++) {
                const result = find();
                if (result) return result;
                await new Promise(resolve => setTimeout(resolve, 100));
              }
              throw new Error(label);
            }
            const open = title => [...document.querySelectorAll('.library-item')].find(x => x.querySelector('h3')?.textContent.includes(title))?.querySelector('a');
            (await wait(() => open('Midnight Lighthouse'), 'Downloaded chapter was not listed')).click();
            await wait(() => {
              const image = document.querySelector('.reader-page');
              return image?.naturalWidth > 0 && image.src.includes('/offline-media/') && /page-0002/.test(image.src);
            }, 'Downloaded chapter did not load its saved page in airplane mode');
            (await wait(() => document.querySelector('a[href="/library?view=downloads"]'), 'Downloads link')).click();
            (await wait(() => open('Big Buck Bunny'), 'Downloaded video was not listed')).click();
            const video = await wait(() => {
              const media = document.querySelector('video');
              return media?.dataset.offlineSource?.includes('offline-media/') && media.readyState >= 2 && media;
            }, 'Video frame did not load in airplane mode');
            return { duration: video.duration, readyState: video.readyState };

          })()`,
        }, 115000);
        if (!offline.result?.value) throw new Error(offline.exceptionDetails?.exception?.description ?? 'Offline media failed.');
        console.log('PASS: downloaded manga restored its exact page and a downloaded video frame loaded in airplane mode.');
        const seek = await command('Runtime.evaluate', {
          awaitPromise: true, returnByValue: true, userGesture: true,
          expression: `(async () => {
            const video = document.querySelector('video');
            video.muted = true;
            const speed = [...document.querySelectorAll('label')].find(x => x.textContent.trim().startsWith('Playback speed'))?.querySelector('select');
            speed.value = '1.25';
            speed.dispatchEvent(new Event('change', { bubbles: true }));
            for (let i = 0; i < 50 && video.playbackRate !== 1.25; i++) await new Promise(resolve => setTimeout(resolve, 100));
            if (video.playbackRate !== 1.25) throw new Error('Speed control did not change the offline video rate');
            await video.play();
            const checkpoint = Math.min(6, video.duration / 2);
            video.currentTime = checkpoint;
            for (let i = 0; i < 150; i++) {
              if (video.error) throw new Error('Media error ' + video.error.code);
              if (!video.seeking && video.currentTime >= checkpoint - 0.1) {
                video.pause();
                return video.currentTime;
              }
              await new Promise(resolve => setTimeout(resolve, 100));
            }
            throw new Error('Offline seek stalled at ' + video.currentTime + ' of ' + video.duration +
              ', ready state ' + video.readyState + ', seekable ranges ' + video.seekable.length);
          })()`,
        }, 22000);
        if (!Number.isFinite(seek.result?.value)) throw new Error(seek.exceptionDetails?.exception?.description ?? 'Offline video seek failed.');
        const resume = await command('Runtime.evaluate', {
          awaitPromise: true, returnByValue: true,
          expression: `(async () => {
            const pause = () => new Promise(resolve => setTimeout(resolve, 100));
            document.querySelector('a[href="/library?view=downloads"]').click();
            const open = () => [...document.querySelectorAll('.library-item')].find(x => x.querySelector('h3')?.textContent.includes('Big Buck Bunny'))?.querySelector('a');
            for (let i = 0; i < 100 && !open(); i++) await pause();
            open()?.click();
            const expected = ${seek.result.value};
            for (let i = 0; i < 150; i++) {
              const video = document.querySelector('video');
              if (video?.readyState >= 2 && !video.seeking && Math.abs(video.currentTime - expected) < 0.25) return true;
              await pause();
            }
            return false;
          })()`,
        }, 28000);
        if (resume.result?.value !== true) throw new Error('Downloaded episode did not reopen at the saved timestamp.');
        console.log('PASS: downloaded video played at the chosen speed, sought, and reopened at its saved timestamp in airplane mode.');
      } finally {
        adb('cmd', 'connectivity', 'airplane-mode', 'disable');
      }
    }
    if (mode === 'fresh') {
      for (const [scene, label] of [
        ['hub', 'profile-hub'], ['switcher', 'profile-switcher'],
        ['appearance', 'appearance'], ['sources', 'sources'],
        ['anilist', 'anilist-account-android'], ['tracking-editor', 'anilist-editor-android'],
        ['reader-feedback', 'anilist-reader-feedback-android'],
      ]) {
        const result = await command('Runtime.evaluate', {
          expression: '(' + verifyProfileScene.toString() + ')(' + JSON.stringify(scene) + ')',
          awaitPromise: true, returnByValue: true,
        }, 45000);
        if (result.result?.value !== true)
          throw new Error(result.exceptionDetails?.exception?.description ?? 'Could not verify ' + label);
        const image = execFileSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 12 * 1024 * 1024 });
        await writeFile('artifacts/startup/' + label + '.png', image);
      }
      console.log('PASS: created and switched profiles, verified source and appearance isolation, and captured the profile and tracking screens.');
    }
  } finally {
    socket.close();
    for (const {reject, timer} of pending.values()) {clearTimeout(timer); reject(new Error('Debug connection closed.'));}
  }
}

async function verifyProfileScene(scene) {
  async function wait(find, label) {
    for (let i = 0; i < 130; i++) {
      const found = find();
      if (found) return found;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(label);
  }
  const button = name => Array.from(document.querySelectorAll('button')).find(b => (b.textContent.trim() === name || b.getAttribute('aria-label') === name) && !b.disabled);
  const click = async name => (await wait(() => button(name), name)).click();
  const open = async path => (await wait(() => document.querySelector('a[href="' + path + '"]'), path)).click();
  const heading = name => wait(() => document.querySelector('h1')?.textContent === name, name);
  const activate = async name => {
    (await wait(() => [...document.querySelectorAll('.settings-row')].find(row => row.textContent.includes(name)), name)).click();
    await wait(() => document.querySelector('.profile-card h2')?.textContent === name, 'Active profile ' + name);
  };

  if (scene === 'hub') {
    document.querySelector('.bottom-nav a[href="/profile"]').click();
    await heading('Profile');
    await wait(() => document.querySelector('.profile-card h2')?.textContent === 'Story explorer', 'Default profile');
  } else if (scene === 'switcher') {
    await open('/profile/profiles');
    await open('/profile/profiles/new');
    const input = await wait(() => document.querySelector('input[maxlength="32"]'), 'Profile name');
    input.value = 'Night reader';
    input.dispatchEvent(new Event('input', { bubbles: true }));
    await click('Moon');
    await click('Save profile');
    await heading('Switch profile');
    await wait(() => document.querySelector('.profile-switcher')?.textContent.includes('Night reader'), 'Created profile');
  } else if (scene === 'appearance') {
    await activate('Night reader');
    await open('/profile/sources');
    await wait(() => document.body.innerText.includes('No sources installed'), 'Isolated sources');
    await open('/profile');
    await open('/profile/appearance');
    await click('Light');
    await click('Compact');
    await click('Save appearance');
    await wait(() => document.querySelector('.app-shell')?.dataset.theme === 'light' &&
      document.querySelector('.app-shell')?.dataset.density === 'compact', 'Saved profile appearance');
  } else if (scene === 'sources') {
    await open('/profile');
    await open('/profile/profiles');
    await activate('Story explorer');
    await wait(() => document.querySelector('.app-shell')?.dataset.theme === 'system', 'Original theme');
    await open('/profile/sources');
    const row = await wait(() => {
      const card = document.querySelector('.source-card');
      return card?.querySelector('strong')?.textContent === 'AniKatha Story Sampler' && card;
    }, 'Restored original sources');
    row.scrollIntoView({ block: 'center' });
  } else if (scene === 'anilist') {
    document.querySelector('.bottom-nav a[href="/profile"]').click();
    await open('/profile/anilist');
    await heading('AniList');
    await wait(() => document.querySelector('.tracking-account h2')?.textContent === 'AniList account', 'Disconnected AniList account');
  } else if (scene === 'tracking-editor') {
    document.querySelector('.bottom-nav a[href="/anime"]').click();
    await click('Big Buck Bunny');
    await click('Save to library');
    await wait(() => [...document.querySelectorAll('button')].some(button =>
      button.disabled && button.textContent.trim() === 'Saved to library'), 'Saved title confirmation');
    document.querySelector('.bottom-nav a[href="/library"]').click();
    const editor = await wait(() => [...document.querySelectorAll('.tracking-library-card')].find(card =>
      card.querySelector('h3')?.textContent.includes('Big Buck Bunny'))?.querySelector('a[href$="/tracking"]'), 'Library tracking editor link');
    editor.click();
    await heading('Edit tracking');
    await wait(() => document.querySelector('.tracking-editor input[inputmode="numeric"]'), 'Completed count editor');
    const status = document.querySelector('.tracking-editor select');
    status.value = 'CURRENT';
    status.dispatchEvent(new Event('change', { bubbles:true }));
    const count = document.querySelector('.tracking-editor input[inputmode="numeric"]');
    count.value = '1';
    count.dispatchEvent(new Event('input', { bubbles:true }));
    await click('Save tracking');
    await wait(() => document.querySelector('.tracking-editor')?.textContent.includes('Saved on this device.'), 'Local save confirmation');
  } else if (scene === 'reader-feedback') {
    document.querySelector('.bottom-nav a[href="/manga"]').click();
    await click('The Midnight Lighthouse');
    await click('Chapter 1');
    await click('Read Chapter 1');
    const getPage = () => Number(document.querySelector('.reader-position')?.textContent.match(/Page ([0-9]+)/)?.[1]);
    let page = await wait(getPage, 'Saved chapter page');
    while (page < 3) {
      await click('Next page →');
      const next = page + 1;
      page = await wait(() => getPage() === next && next, 'Chapter page ' + next);
    }
    await wait(() => document.querySelector('.reader-page')?.naturalWidth > 0, 'Last chapter image');
    await click('Mark chapter read');
    const feedback = await wait(() => {
      const message = document.querySelector('.tracking-feedback');
      return message?.textContent.includes('no verified AniList match') && message;
    }, 'Local-only completion feedback');
    feedback.scrollIntoView({ block: 'end' });
  }
  return true;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  checkWebView().catch(error => {console.error(`FAIL: ${error.message}`); process.exitCode = 1;});
}
