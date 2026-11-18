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
  } finally {
    socket.close();
    for (const {reject, timer} of pending.values()) {clearTimeout(timer); reject(new Error('Debug connection closed.'));}
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  checkWebView().catch(error => {console.error(`FAIL: ${error.message}`); process.exitCode = 1;});
}
