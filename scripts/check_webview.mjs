import { writeFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';

export function hasNavigation(snapshot) {
  const expected = ['Home', 'Anime', 'Manga', 'Library', 'Profile'];
  return !snapshot.errorVisible && expected.every(label => snapshot.links?.includes(label));
}

export async function checkWebView(endpoint = 'http://127.0.0.1:9222') {
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
  const command = (method, params) => new Promise((resolve, reject) => {
    const id = ++sequence;
    const timer = setTimeout(() => {pending.delete(id); reject(new Error(`${method} timed out.`));}, 10000);
    pending.set(id, {resolve, reject, timer}); socket.send(JSON.stringify({id, method, params}));
  });
  try {
    await connected;
    const reply = await command('Runtime.evaluate', {returnByValue: true, expression: `(() => {
      const visible = element => !!element && element.getBoundingClientRect().width > 0 && element.getBoundingClientRect().height > 0;
      return {
        links: Array.from(document.querySelectorAll('.bottom-nav a')).filter(visible).map(a => a.textContent.trim()),
        errorVisible: visible(document.querySelector('#blazor-error-ui')),
        text: document.body.innerText
      };
    })()`});
    if (reply.exceptionDetails) throw new Error('WebView DOM inspection failed.');
    const snapshot = reply.result?.value;
    if (!snapshot) throw new Error('WebView returned no DOM snapshot.');
    await writeFile('artifacts/startup/webview.json', JSON.stringify(snapshot, null, 2));
    if (!hasNavigation(snapshot)) throw new Error('App navigation did not render or the Blazor error UI is visible.');
    console.log('PASS: all five navigation tabs rendered in the installed APK WebView.');

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
        find('.reader-controls button.secondary').click();
        await waitFor(() => loadedPage(2), 'Previous page');
        return true;
      })()`,
    });
    if (reader.result?.value !== true) {
      throw new Error(reader.exceptionDetails?.exception?.description ?? 'Sample manga chapter did not load or resume.');
    }
    console.log('PASS: installed APK renders all three bundled manga pages, turns pages, and restores the saved page.');


  } finally {
    socket.close();
    for (const {reject, timer} of pending.values()) {clearTimeout(timer); reject(new Error('Debug connection closed.'));}
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  checkWebView().catch(error => {console.error(`FAIL: ${error.message}`); process.exitCode = 1;});
}
