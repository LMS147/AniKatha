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
    const probeHome = () => command('Runtime.evaluate', {
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
    let reply;
    for (let attempt = 0; attempt < 5; attempt++) {
      try {
        reply = await probeHome();
        break;
      } catch (error) {
        if (!/Execution context was destroyed|Cannot find default execution context/.test(error.message) || attempt === 4) throw error;
        await new Promise(resolve => setTimeout(resolve, 400));
      }
    }
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
        document.querySelector('.bottom-nav a[href="${mode === 'seed' ? '/' : '/anime'}"]').click();
        (await waitFor('a[href="/anime/preview"]')).click();
        (await waitFor('a[href="/anime/preview/episode-1"]')).click();
        const video = await waitFor('video[src]');
        const controlsReady = ${mode === 'seed'} ? video.controls : !!document.querySelector('[data-player-toggle]');
        return location.pathname === '/anime/preview/episode-1' && controlsReady &&
          video.src === 'https://media.w3.org/2010/05/bunny/trailer.mp4';
      })()`,
    });
    if (player.result?.value !== true) throw new Error('Sample episode player did not mount in the APK WebView.');
    console.log('PASS: Sample Episode 1 mounts the in-app video player.');

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
            const button = name => [...document.querySelectorAll('button')].find(b => b.textContent.trim() === name && !b.disabled);
            (await wait(() => button('Next page →'), 'Next downloaded page')).click();
            await wait(() => document.querySelector('.reader-page')?.naturalWidth > 0 &&
              document.querySelector('.reader-position')?.textContent === 'Page 3 of 3', 'Last downloaded page');
            (await wait(() => button('Mark chapter read'), 'Complete downloaded chapter')).click();
            const feedback = await wait(() => {
              const message = document.querySelector('.tracking-feedback');
              return message?.textContent.includes('no verified AniList match') && message;
            }, 'Local-only completion feedback');
            feedback.scrollIntoView({block:'center'});
            return true;
          })()`,
        }, 115000);
        if (offline.result?.value !== true) throw new Error(offline.exceptionDetails?.exception?.description ?? 'Offline manga failed.');
        const readerScreenshot = execFileSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 12 * 1024 * 1024 });
        await writeFile('artifacts/startup/anilist-reader-feedback-android.png', readerScreenshot);
        console.log('PASS: downloaded manga restored its exact page and explains local-only completion in airplane mode.');

        const offlineVideo = await command('Runtime.evaluate', {
          awaitPromise: true, returnByValue: true,
          expression: `(async () => {
            async function wait(find, label) {
              for (let i = 0; i < 200; i++) {
                const value = find();
                if (value) return value;
                await new Promise(resolve => setTimeout(resolve, 100));
              }
              throw new Error(label);
            }
            (await wait(() => document.querySelector('a[href="/library?view=downloads"]'), 'Downloads link')).click();
            const open = await wait(() => [...document.querySelectorAll('.library-item')].find(x =>
              x.querySelector('h3')?.textContent.includes('Big Buck Bunny'))?.querySelector('a'), 'Downloaded video');
            open.click();
            await wait(() => {
              const media = document.querySelector('video');
              return media?.dataset.offlineSource?.includes('offline-media/') && media.readyState >= 2;
            }, 'Video frame did not load in airplane mode');
            return true;
          })()`,
        }, 65000);
        if (offlineVideo.result?.value !== true) throw new Error(offlineVideo.exceptionDetails?.exception?.description ?? 'Offline video failed.');
        const seek = await command('Runtime.evaluate', {
          awaitPromise: true, returnByValue: true, userGesture: true,
          expression: `(async () => {
            const video = document.querySelector('video');
            video.muted = true;
            const wait = async find => {
              for (let i = 0; i < 45; i++) {
                const item = find();
                if (item) return item;
                await new Promise(resolve => setTimeout(resolve, 100));
              }
              throw new Error('Playback control did not update');
            };
            document.querySelector('[data-player-menu="speed"]').click();
            (await wait(() => document.querySelector('[data-player-rate="1.25"]'))).click();
            await wait(() => video.playbackRate === 1.25);
            document.querySelector('[data-player-close]').click();
            await wait(() => !document.querySelector('.player-sheet'));
            document.querySelector('[data-player-toggle]').click();
            await wait(() => !video.paused);
            const checkpoint = Math.min(6, video.duration / 2);
            const scrubber = document.querySelector('[data-player-scrubber]');
            scrubber.value = checkpoint;
            scrubber.dispatchEvent(new Event('input', {bubbles:true}));
            scrubber.dispatchEvent(new Event('change', {bubbles:true}));
            for (let i = 0; i < 150; i++) {
              if (video.error) throw new Error('Media error ' + video.error.code);
              if (!video.seeking && video.currentTime >= checkpoint - 0.1) {
                document.querySelector('[data-player-toggle]').click();
                await wait(() => video.paused);
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
            document.querySelector('.player-back').click();
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
        await checkHomeAnimeResume(command, seek.result.value);
        await checkPlayerScreens(command, adb);
      } finally {
        adb('cmd', 'connectivity', 'airplane-mode', 'disable');
      }
    }
    if (mode === 'fresh') {
      await checkHomeViews(command);
      for (const [scene, label] of [
        ['hub', 'profile-account'], ['preferences', 'profile-preferences'],
        ['sync', 'profile-sync'], ['security', 'profile-security'], ['switcher', 'profile-switcher'],
        ['appearance', 'appearance'], ['sources', 'sources'],
        ['anilist', 'anilist-account-android'], ['tracking-editor', 'anilist-editor-android'],
      ]) {
        const result = await command('Runtime.evaluate', {
          expression: '(' + verifyProfileScene.toString() + ')(' + JSON.stringify(scene) + ')',
          awaitPromise: true, returnByValue: true,
        }, 45000);
        if (result.result?.value !== true)
          throw new Error(result.exceptionDetails?.exception?.description ?? 'Could not verify ' + label);
        await new Promise(resolve => setTimeout(resolve, 500));
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
    await wait(() => document.querySelector('.profile-summary h3')?.textContent === name, 'Active profile ' + name);
  };

  if (scene === 'hub') {
    document.querySelector('.bottom-nav a[href="/profile"]').click();
    await heading('Profile');
    await wait(() => document.querySelector('.profile-summary h3')?.textContent === 'Story explorer', 'Default profile');
    const username = document.querySelector('#profile-username');
    username.value = 'TestUser';
    username.dispatchEvent(new Event('input', { bubbles: true }));
    await click('Preferences');
    await wait(() => document.querySelector('.profile-panel:not([hidden]) h2')?.textContent === 'Preferences', 'Preferences tab');
    await click('Account');
    if (username.value !== 'TestUser') throw new Error('Account draft lost during tab switch');
    const input = document.querySelector('.profile-photo-edit input');
    const files = new DataTransfer();
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = 32;
    const art = canvas.getContext('2d');
    art.fillStyle = '#1763c6';
    art.fillRect(0, 0, 32, 32);
    art.fillStyle = '#f3f7ff';
    art.fillRect(8, 8, 16, 16);
    const image = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
    files.items.add(new File([image], 'avatar.png', { type: 'image/png' }));
    input.files = files.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await wait(() => document.querySelector('.profile-summary img')?.naturalWidth === 32, 'Local avatar decoded');
    await click('Save changes');
    await wait(() => document.querySelector('.profile-summary')?.textContent.includes('TestUser'), 'Saved username');
  } else if (scene === 'preferences') {
    await click('Preferences');
    const toggle = await wait(() => document.querySelector('.profile-panel:not([hidden]) .profile-switch input'), 'Playback switch');
    toggle.click();
    await click('Account');
    await click('Preferences');
    await wait(() => document.querySelector('.profile-panel:not([hidden]) .profile-switch input')?.checked,
      'Playback preference persisted');
  } else if (scene === 'sync') {
    await click('Sync');
    await wait(() => document.querySelector('.profile-panel:not([hidden]) .tracking-account h2')?.textContent === 'AniList account', 'Sync tab');
    await click('Import');
    const input = await wait(() => document.querySelector('.profile-panel:not([hidden]) input[type="file"]'), 'Backup picker');
    const file = new DataTransfer();
    file.items.add(new File(['{"Version":1,"Library":[],"History":[]}'], 'anikatha-test.json', { type: 'application/json' }));
    input.files = file.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
    await click('Merge backup');
    await wait(() => document.querySelector('.profile-backup-card')?.textContent.includes('Backup merged.'), 'Backup merge');
    await click('Export');
  } else if (scene === 'security') {
    await click('Security');
    await wait(() => document.querySelector('.profile-panel:not([hidden]) input[autocomplete="new-password"]'), 'Local PIN form');
    document.querySelector('.profile-panel:not([hidden]) .profile-switch input').click();
    await wait(() => document.querySelector('.profile-panel:not([hidden]) input[autocomplete="new-password"]')?.type === 'text', 'Show PIN');
    document.querySelector('.profile-panel:not([hidden]) .profile-switch input').click();
    const setPin = (field, value) => {
      field.value = value;
      field.dispatchEvent(new Event('change', { bubbles: true }));
    };
    for (const input of document.querySelectorAll('.profile-panel:not([hidden]) input[inputmode="numeric"]')) setPin(input, '4275');
    await click('Set PIN');
    await click('Done');
    const current = await wait(() => document.querySelector('.profile-panel:not([hidden]) input[autocomplete="off"][inputmode="numeric"]'), 'Current PIN');
    setPin(current, '4275');
    await click('Remove PIN');
    await wait(() => document.querySelector('.profile-panel:not([hidden])')?.textContent.includes('PIN removed.'), 'Removed profile PIN');
    await click('Account');
    await click('Security');
  } else if (scene === 'switcher') {
    await click('Account');
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

  }
  return true;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  checkWebView().catch(error => {console.error(`FAIL: ${error.message}`); process.exitCode = 1;});
}

async function checkPlayerScreens(command, adb) {
  async function scene(action) {
    const result = await command('Runtime.evaluate', {
      expression: '(' + exercisePlayerScene.toString() + ')(' + JSON.stringify(action) + ')',
      awaitPromise: true, returnByValue: true, userGesture: true,
    }, 35000);
    if (result.result?.value !== true)
      throw new Error(result.exceptionDetails?.exception?.description ?? 'Player screen check failed: ' + action);
  }
  async function screenshot(name) {
    // SurfaceFlinger may still display the previous frame after WebView reports the new viewport.
    await new Promise(resolve => setTimeout(resolve, 750));
    const image = execFileSync('adb', ['exec-out', 'screencap', '-p'], { maxBuffer: 12 * 1024 * 1024 });
    await writeFile('artifacts/startup/' + name + '.png', image);
  }
  await scene('portrait');
  await screenshot('player-portrait');
  await scene('subtitles');
  await screenshot('player-subtitle-picker');
  adb('input', 'keyevent', '4');
  await scene('sheet dismissed');
  await scene('fullscreen');
  try {
    adb('settings', 'put', 'system', 'accelerometer_rotation', '0');
    adb('settings', 'put', 'system', 'user_rotation', '1');
    await scene('landscape');
    await screenshot('player-fullscreen');
    adb('input', 'keyevent', '4');
    await scene('fullscreen dismissed');
  } finally {
    adb('settings', 'put', 'system', 'user_rotation', '0');
  }
  await scene('recovery');
  await screenshot('player-recovery');
  await scene('retry');
  await scene('settings');
  await screenshot('player-subtitle-appearance');
  console.log('PASS: Android player sheets, embedded caption selection, system Back, fullscreen rotation, retry, and subtitle appearance preview.');
}

async function exercisePlayerScene(action) {
  const query = selector => document.querySelector(selector);
  async function wait(find, label) {
    for (let attempt = 0; attempt < 210; attempt++) {
      const value = find();
      if (value) return value;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Player: ' + label);
  }
  async function click(selector) {
    (await wait(() => query(selector), selector)).click();
  }
  const video = query('video');
  if (action === 'portrait') {
    await wait(() => innerWidth < innerHeight && video?.readyState >= 2 && !video.seeking, 'portrait and saved frame');
    scrollTo(0, 0);
  } else if (action === 'subtitles') {
    // Feed a local caption into Android WebView's track inventory without network access.
    for (const [label, kind] of [['Full dialogue', 'subtitles'], ['English SDH', 'captions']]) {
      const track = document.createElement('track');
      track.kind = kind;
      track.srclang = 'en';
      track.label = label;
      track.src = URL.createObjectURL(new Blob(['WEBVTT\n\n00:00:00.000 --> 00:00:15.000\nThe story continues.\n'], {type:'text/vtt'}));
      video.append(track);
    }
    await click('[data-player-menu="subtitles"]');
    const track = await wait(() => [...document.querySelectorAll('.player-option')].find(option => option.textContent.includes('English SDH')), 'captions listed');
    track.click();
    await wait(() => track.getAttribute('aria-pressed') === 'true' && [...video.textTracks].some(track => track.label === 'English SDH' && track.mode === 'showing'), 'captions selected');
  } else if (action === 'sheet dismissed') {
    await wait(() => !query('.player-sheet') && query('.player-stage'), 'Back dismisses sheet');
  } else if (action === 'fullscreen') {
    window.playerRotationCheck = { video, time: video.currentTime };
    await click('.player-heading button:last-child');
    await wait(() => query('.player-fullscreen'), 'fullscreen stage');
  } else if (action === 'landscape') {
    await wait(() => {
      const stage = query('.player-fullscreen')?.getBoundingClientRect();
      const transport = query('.player-transport')?.getBoundingClientRect();
      return innerWidth > innerHeight && stage?.width >= innerWidth * 0.95 && stage.height >= innerHeight * 0.95 &&
        transport?.bottom > 0 && transport.bottom < innerHeight;
    }, 'fullscreen occupies the landscape viewport');
    if (video !== window.playerRotationCheck.video || Math.abs(video.currentTime - window.playerRotationCheck.time) > 0.25)
      throw new Error('Rotation changed the playback session or its checkpoint.');
  } else if (action === 'fullscreen dismissed') {
    await wait(() => !query('.player-fullscreen') && query('.player-stage'), 'Back exits fullscreen');
  } else if (action === 'recovery') {
    await wait(() => innerWidth < innerHeight && video?.readyState >= 2, 'return to portrait');
    video.dispatchEvent(new Event('error'));
    const panel = await wait(() => query('.player-recovery'), 'video recovery action');
    panel.scrollIntoView({block:'center', behavior:'instant'});
    window.playerRotationCheck.source = video.src;
  } else if (action === 'retry') {
    await click('.player-recovery button');
    await wait(() => !query('.player-recovery') && video.src !== window.playerRotationCheck.source && video.readyState >= 2 && !video.seeking, 'video recovered');
    if (Math.abs(video.currentTime - window.playerRotationCheck.time) > 0.25)
      throw new Error('Retry changed the saved position.');
  } else if (action === 'settings') {
    await click('.bottom-nav a[href="/profile"]');
    await click('a[href="/profile/playback"]');
    const preview = await wait(() => query('.subtitle-preview'), 'subtitle appearance');
    await wait(() => {
      const box = preview.getBoundingClientRect();
      if (box.top >= 0 && box.bottom < innerHeight - 80) return true;
      preview.scrollIntoView({block:'center', behavior:'instant'});
    }, 'subtitle preview is visible');
  }
  return true;
}

async function runHomeCheck(command, action, expected = null) {
  const reply = await command('Runtime.evaluate', {
    expression: '(' + exerciseHome.toString() + ')(' + JSON.stringify(action) + ', ' + JSON.stringify(expected) + ')',
    awaitPromise: true, returnByValue: true,
  }, 90000);
  if (reply.result?.value !== true)
    throw new Error(reply.exceptionDetails?.exception?.description ?? 'Home check failed: ' + action);
}

async function checkHomeAnimeResume(command, expected) {
  await runHomeCheck(command, 'resume anime', expected);
  console.log('PASS: Home → Continue Watching → downloaded episode restored the exact timestamp in airplane mode.');
}

async function checkHomeViews(command) {
  for (const [action, name] of [
    ['anime top', 'home-anime-top'], ['anime latest', 'home-anime-latest'],
    ['anime upcoming', 'home-anime-upcoming'], ['anime below', 'home-anime-releases-schedule'],
    ['manga top', 'home-manga-history'], ['manga below', 'home-manga-discovery'],
    ['manga updates', 'home-manga-updates']
  ]) {
    await runHomeCheck(command, action);
    await new Promise(resolve => setTimeout(resolve, 750));
    const image = execFileSync('adb', ['exec-out', 'screencap', '-p'], {maxBuffer: 12 * 1024 * 1024});
    await writeFile('artifacts/startup/' + name + '.png', image);
  }
  await runHomeCheck(command, 'verify memory');
  await runHomeCheck(command, 'resume manga');
  console.log('PASS: Home opens on Anime, keeps all sections in order, preserves both scroll positions and Anime filters, and opens the saved manga page.');
}

async function exerciseHome(action, expected) {
  const find = selector => document.querySelector(selector);
  async function wait(condition, description) {
    for (let retry = 0; retry < 420; retry++) {
      const value = condition();
      if (value) return value;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error('Home: ' + description);
  }
  const home = async () => {
    find('.bottom-nav a[href="/"]').click();
    await wait(() => find('.home-landing') && find('.bottom-nav a[href="/"].active'), 'Home navigation');
  };
  const click = async selector => (await wait(() => find(selector), selector)).click();
  const active = view => find('#home-' + view + '[aria-hidden="false"]');
  const format = async (view) => {
    await click('#home-tab-' + view);
    await wait(() => active(view), 'active ' + view);
  };
  function sections(view, order) {
    const actual = [...active(view).querySelectorAll('.home-section > header h2')].map(heading => heading.textContent.trim());
    if (JSON.stringify(actual) !== JSON.stringify(order)) throw new Error('Unexpected Home section order: ' + actual);
  }
  const selectButton = (selector, label) => [...document.querySelectorAll(selector)].find(button => button.textContent.trim() === label);
  const scroll = selector => {
    const section = find(selector);
    section.scrollIntoView({block:'start', behavior:'instant'});
    if (section.getBoundingClientRect().bottom <= 0) throw new Error('Home section did not scroll into view');
  };
  const settle = selector => wait(() => !find(selector + ' .home-loading'), selector + ' settles');
  if (action === 'resume anime') {
    await home();
    await wait(() => active('anime'), 'Anime is default');
    await click('#home-anime .home-section:first-child a[href^="/resume/"]');
    (await wait(() => [...document.querySelectorAll('a')].find(link => link.textContent.trim() === 'Open downloaded copy'), 'downloaded resume option')).click();
    await wait(() => {
      const video = find('video');
      return video?.readyState >= 2 && !video.seeking && Math.abs(video.currentTime - expected) < 0.25;
    }, 'exact anime checkpoint');
  } else if (action === 'anime top') {
    await home();
    await wait(() => active('anime'), 'Anime is default');
    const children = [...find('.home-landing').children];
    if (!children[0]?.matches('.home-header') || !children[1]?.matches('.home-spotlight') ||
        !children[2]?.matches('.home-mode') || find('.home-mode').getBoundingClientRect().bottom >= innerHeight)
      throw new Error('Spotlight is out of order or obscures the tabs');
    if (document.querySelectorAll('.home-spotlight').length !== 1) throw new Error('Home should have one shared spotlight');
    const next = find('.home-spotlight [aria-label="Next spotlight"]');
    if (next) {
      const current = find('.home-spotlight-controls span').textContent;
      const hero = find('.home-spotlight');
      hero.dispatchEvent(new TouchEvent('touchstart', { bubbles: true, touches: [
        new Touch({ identifier: 1, target: hero, clientX: 200, clientY: 160 })
      ] }));
      hero.dispatchEvent(new TouchEvent('touchend', { bubbles: true, changedTouches: [
        new Touch({ identifier: 1, target: hero, clientX: 100, clientY: 170 })
      ] }));
      window.spotlightSlide = await wait(() => {
        const slide = find('.home-spotlight-controls span').textContent;
        return slide !== current && slide;
      }, 'spotlight swipe changes the slide');
      console.log('Spotlight swipe verified');
    }
    sections('anime', ['Continue Watching', 'Latest Episodes', 'Upcoming Anime', 'Release lists', 'Estimated Schedule']);
    await wait(() => find('#home-anime a[href^="/resume/"]'), 'watch history is shown');
    await settle('#home-anime .home-section:nth-child(2)');
    const grid = find('#home-anime .home-grid');
    if (getComputedStyle(grid).gridTemplateColumns.split(' ').length !== 2) throw new Error('Phone Home should show two poster columns');
    active('anime').scrollTop = 0;
  } else if (action === 'anime latest' || action === 'anime upcoming') {
    await settle('#home-anime');
    scroll(action === 'anime latest' ? '[aria-labelledby="home-latest-title"]' : '[aria-labelledby="home-upcoming-title"]');
  } else if (action === 'anime below') {
    selectButton('#home-anime button', 'Trending').click();
    await wait(() => selectButton('#home-anime button', 'Trending').getAttribute('aria-pressed') === 'true', 'Trending filter');
    selectButton('#home-anime button', 'Just Completed').click();
    await wait(() => selectButton('#home-anime button', 'Just Completed').getAttribute('aria-pressed') === 'true', 'completion filter');
    const selectedDay = () => find('#home-anime .home-day[aria-pressed="true"]')?.textContent;
    const previousDay = selectedDay();
    find('#home-anime [aria-label="Next day"]').click();
    await wait(() => selectedDay() && selectedDay() !== previousDay, 'new schedule day is selected');
    await settle('#home-anime');
    scroll('#home-anime .home-section:nth-child(4)');
    window.aniKathaHome = { anime: active('anime').scrollTop,
      date: find('#home-anime .home-day[aria-pressed="true"]').textContent };
  } else if (action === 'manga top') {
    await format('manga');
    if (window.spotlightSlide && find('.home-spotlight-controls span')?.textContent !== window.spotlightSlide)
      throw new Error('Changing to Manga reset the shared spotlight');
    sections('manga', ['New Chapters from Followed Comics', 'Reading History', 'Discover Manga', 'Most Recent Popular', 'New Comics', 'Latest Updates']);
    await wait(() => find('#home-manga a[href^="/resume/"]'), 'reading history is shown');
    active('manga').scrollTop = 0;
  } else if (action === 'manga below') {
    await settle('#home-manga .home-section:nth-child(3)');
    scroll('#home-manga .home-section:nth-child(3)');
  } else if (action === 'manga updates') {
    await settle('#home-manga');
    scroll('#home-manga .home-section:last-child');
    window.aniKathaHome.manga = active('manga').scrollTop;
  } else if (action === 'verify memory') {
    await format('anime');
    if (window.spotlightSlide && find('.home-spotlight-controls span')?.textContent !== window.spotlightSlide)
      throw new Error('Returning to Anime reset the shared spotlight');
    const memory = window.aniKathaHome;
    if (Math.abs(active('anime').scrollTop - memory.anime) > 5)
      throw new Error('Anime scroll changed from ' + memory.anime + ' to ' + active('anime').scrollTop);
    if (find('#home-anime .home-day[aria-pressed="true"]')?.textContent !== memory.date)
      throw new Error('Anime schedule day was not restored');
    for (const label of ['Trending', 'Just Completed']) {
      if (selectButton('#home-anime button', label).getAttribute('aria-pressed') !== 'true')
        throw new Error(label + ' filter was not restored');
    }
    await format('manga');
    if (Math.abs(active('manga').scrollTop - memory.manga) > 5) throw new Error('Manga position was lost');
  } else if (action === 'resume manga') {
    await click('#home-manga a[href^="/resume/"]');
    (await wait(() => selectButton('button', 'Read Chapter 1'), 'saved chapter action')).click();
    await wait(() => {
      const page = find('.reader-page');
      return page?.naturalWidth > 0 && page.src.endsWith('-3.webp');
    }, 'exact manga page');
  }
  return true;
}
