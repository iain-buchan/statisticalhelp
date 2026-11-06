import test from 'node:test';
import assert from 'node:assert/strict';
import {bridge,platformOf,wordsFor,hostClassName} from './host.mjs';

const handlerWindow=(posted)=>({webkit:{messageHandlers:{statsDirectLearn:{postMessage:body=>posted.push(body)}}}});
test('the host object is the bridge when registered, the WebKit handler otherwise, and nothing in a browser',()=>{
  const posted=[];const registered={platform:'mac',postMessage:body=>posted.push(['host',body])};
  assert.equal(bridge({statsDirectHost:registered}),registered);
  const fallback=bridge(handlerWindow(posted));fallback.postMessage({action:'ready'});
  assert.deepEqual(posted,[{action:'ready'}]);
  assert.equal(bridge({}),null);
});
test('a fallback bridge survives the handler being removed',()=>{
  const win=handlerWindow([]);const fallback=bridge(win);delete win.webkit.messageHandlers.statsDirectLearn;
  assert.equal(fallback.postMessage({action:'save'}),undefined);
});
test('the platform follows settings, then the host object, then the bridge in use',()=>{
  const mac={webkit:{messageHandlers:{statsDirectLearn:{}}}},windows={chrome:{webview:{}},...mac},web={};
  assert.equal(platformOf(mac,{platform:'mac'},{}),'mac');
  assert.equal(platformOf(mac,bridge(mac),{}),'mac');
  assert.equal(platformOf(windows,bridge(windows),{}),'windows');
  assert.equal(platformOf(web,null,{}),'web');
  assert.equal(platformOf(mac,{platform:'mac'},{platform:'windows'}),'windows');
  assert.equal(platformOf(mac,null,undefined),'mac');
});
test('wording and the document class follow the platform, with a neutral default',()=>{
  assert.equal(wordsFor('mac').computer,'this Mac');assert.equal(wordsFor('windows').computer,'this PC');
  assert.equal(wordsFor('web').computer,'this computer');assert.equal(wordsFor(undefined).app,'the StatsDirect app');
  assert.equal(hostClassName('mac'),'host-mac');assert.equal(hostClassName('windows'),'host-windows');
  assert.equal(hostClassName('x y'),'host-web');assert.equal(hostClassName(undefined),'host-web');
});
