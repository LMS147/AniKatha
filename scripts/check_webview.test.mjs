import {test} from 'node:test';
import assert from 'node:assert/strict';
import {hasNavigation} from './check_webview.mjs';
const links = ['Home', 'Anime', 'Manga', 'Library', 'Profile'];
test('accepts rendered five-tab app', () => assert.equal(hasNavigation({links,errorVisible:false}), true));
test('rejects native shell without rendered navigation', () => assert.equal(hasNavigation({links:[],errorVisible:false}), false));
test('rejects partially rendered app', () => assert.equal(hasNavigation({links:links.slice(1),errorVisible:false}), false));
test('rejects Blazor error even if old navigation remains', () => assert.equal(hasNavigation({links,errorVisible:true}), false));
