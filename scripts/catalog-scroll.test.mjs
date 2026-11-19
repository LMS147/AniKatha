import assert from 'node:assert/strict';
import test from 'node:test';
import { activate } from '../src/AniKatha.UI/wwwroot/catalog-scroll.mjs';

test('restores each catalogue position and ignores a detached page', () => {
    const view = new EventTarget();
    view.scrollY = 0;
    view.scrollTo = (_x, y) => { view.scrollY = y; };
    globalThis.window = view;
    const anime = { isConnected: true };
    const manga = { isConnected: true };
    activate(anime, 'ANIME', false);
    view.scrollTo(0, 320);
    view.dispatchEvent(new Event('scroll'));
    anime.isConnected = false;
    view.scrollTo(0, 0);
    view.dispatchEvent(new Event('scroll'));
    activate(manga, 'MANGA', false);
    assert.equal(view.scrollY, 0);
    view.scrollTo(0, 180);
    view.dispatchEvent(new Event('scroll'));
    activate({ isConnected: true }, 'ANIME', true);
    assert.equal(view.scrollY, 320);
    activate(manga, 'MANGA', true);
    assert.equal(view.scrollY, 180);
});
