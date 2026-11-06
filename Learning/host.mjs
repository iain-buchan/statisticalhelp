// How the Learning page finds its host and names the platform; shared with the Windows version (HOST-CONTRACT.md).
// The host registers window.statsDirectHost ({platform, postMessage}) before the page runs. The WebKit message
// handler is the fallback until every host registers it (the Windows host shims that handler on WebView2).
export function bridge(win){
  if(win.statsDirectHost)return win.statsDirectHost;
  return win.webkit?.messageHandlers?.statsDirectLearn?{postMessage:body=>win.webkit?.messageHandlers?.statsDirectLearn?.postMessage(body)}:null;
}
// settings.platform from the host wins, then the host object's platform, then the bridge in use.
export function platformOf(win,host,settings){
  return settings?.platform??host?.platform??(win.chrome?.webview?'windows':win.webkit?.messageHandlers?.statsDirectLearn?'mac':'web');
}
const wording={mac:{computer:'this Mac',app:'the Mac app',application:'the Mac application'},windows:{computer:'this PC',app:'the StatsDirect app',application:'the StatsDirect application'}};
const neutral={computer:'this computer',app:'the StatsDirect app',application:'the StatsDirect application'};
export function wordsFor(platform){return wording[platform]??neutral;}
// The class the page sets on the document element, so stylesheets can carry host variants.
export function hostClassName(platform){return 'host-'+(typeof platform==='string'&&/^[a-z]+$/.test(platform)?platform:'web');}
