const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),web=path.join(root,'src/StatsDirectHelpEditor.App/Web'),evidence=path.join(root,'evidence');
const checks=[],errors=[];let browser;
const check=(name,passed,detail)=>checks.push({name,passed:!!passed,detail});
const server=http.createServer((req,res)=>{
 const u=new URL(req.url,'http://localhost');
 if(u.pathname.startsWith('/generated/')){
  res.setHeader('Content-Type','text/html');const body='<!doctype html><html><head><title>Generated</title></head><body><nav>Contents</nav><h1>Generated '+u.pathname.split('/').pop()+'</h1></body></html>';
  setTimeout(()=>res.end(body),Math.min(1500,Math.max(0,Number(u.searchParams.get('delay'))||0)));return;
 }
 const target=path.resolve(web,u.pathname==='/'?'index.html':u.pathname.slice(1));
 if(!target.startsWith(web+path.sep)){res.writeHead(403);res.end();return;}
 try{res.setHeader('Content-Type',({'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.woff':'font/woff','.woff2':'font/woff2','.svg':'image/svg+xml'})[path.extname(target)]||'application/octet-stream');res.end(fs.readFileSync(target));}
 catch{res.writeHead(404);res.end();}
});
(async()=>{await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));try{
 const origin='http://127.0.0.1:'+server.address().port;
 const payload=n=>({path:'basics/'+n+'.htm',title:n,baseUrl:origin+'/project/basics/'+n+'.htm',source:'<?xml version="1.0"?><html><head><title>'+n+'</title></head><body><h1>'+n+'</h1><p id="body">Original &amp; α '+n+'</p></body></html>'});
 browser=await chromium.launch({channel:'msedge',headless:true});
 const page=await browser.newPage({viewport:{width:1250,height:850}});page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{window.messages=[];window.views=[];window.chrome={webview:{postMessage:m=>{messages.push(m);if(m.action==='view')views.push({...m,visualHidden:document.getElementById('visual-panel').hidden,sourceHidden:document.getElementById('source-panel').hidden,previewHidden:document.getElementById('preview-panel').hidden,loadingHidden:document.getElementById('preview-loading').hidden});}}};});
 await page.goto(origin+'/');await page.waitForFunction(()=>!!window.HelpEditor);
 await page.evaluate(p=>{HelpEditor.load(p);HelpEditor.setWorkspaceMode(true);HelpEditor.setUnifiedViews(true);HelpEditor.setOutputView('browser');},payload('first'));
 check('Entering Browser initially still shows its preparing overlay',await page.evaluate(()=>!document.getElementById('preview-loading').hidden));
 await page.evaluate(url=>HelpEditor.setGeneratedPreview(url),origin+'/generated/first.htm');
 let generated=await(await page.$('#preview-frame')).contentFrame();await generated.waitForSelector('h1');await page.waitForFunction(()=>document.getElementById('preview-loading').hidden);
 const handoff=await page.evaluate(p=>{window.oldPreviewWindow=document.getElementById('preview-frame').contentWindow;window.oldGeneration=HelpEditor.getState().previewGeneration;window.beforeFrame=document.getElementById('preview-frame');messages=[];views=[];HelpEditor.load(p);return{state:HelpEditor.getState(),views,source:HelpEditor.getSource(),sameFrame:beforeFrame===document.getElementById('preview-frame'),overlayHidden:document.getElementById('preview-loading').hidden};},payload('second'));
 check('Browser load commits only Browser notifications and never exposes formatted/source panels',handoff.views.length>0&&handoff.views.every(v=>v.pageView==='browser'&&v.workspaceView==='public'&&v.tab==='preview'&&v.visualHidden&&v.sourceHidden&&!v.previewHidden),handoff.views);
 check('Browser load retains accepted state, exact new source and existing generated document',handoff.state.pageView==='browser'&&handoff.state.tab==='preview'&&handoff.state.buildTarget==='Public'&&handoff.source===payload('second').source&&handoff.sameFrame&&handoff.overlayHidden,handoff.state);
 const accepted=await page.evaluate(()=>{HelpEditor.setOutputView('browser');return{sameFrame:beforeFrame===document.getElementById('preview-frame'),hidden:document.getElementById('preview-loading').hidden};});
 check('Repeated native Browser acceptance does not flash a loading overlay',accepted.sameFrame&&accepted.hidden,accepted);
 const pending=await page.evaluate(url=>{HelpEditor.setGeneratedPreview(url);return{hidden:document.getElementById('preview-loading').hidden,state:HelpEditor.getState()};},origin+'/generated/second.htm?delay=350');
 check('Cached generated-page handoff leaves the accepted Browser chrome and loading state stable',pending.hidden&&pending.state.pageView==='browser'&&pending.state.workspaceView==='public',pending);
 generated=await(await page.$('#preview-frame')).contentFrame();await generated.waitForSelector('h1');
 const stale=await page.evaluate(()=>{const before=messages.filter(m=>m.action==='open-link').length,current=document.getElementById('preview-frame').contentWindow;window.dispatchEvent(new MessageEvent('message',{source:oldPreviewWindow,data:{type:'help-editor-preview-link',generation:oldGeneration,href:'https://example.org/obsolete'}}));window.dispatchEvent(new MessageEvent('message',{source:current,data:{type:'help-editor-preview-link',generation:oldGeneration,href:'https://example.org/stale'}}));return messages.filter(m=>m.action==='open-link').length===before;});
 check('Obsolete iframe and stale generation messages still cannot navigate',stale);
 const current=await page.evaluate(()=>{const before=messages.filter(m=>m.action==='open-link').length;window.dispatchEvent(new MessageEvent('message',{source:document.getElementById('preview-frame').contentWindow,data:{type:'help-editor-preview-link',generation:HelpEditor.getState().previewGeneration,href:'https://example.org/current'}}));return messages.filter(m=>m.action==='open-link').length===before+1;});
 check('Current iframe generation can still request host navigation',current);
 const source=await page.evaluate(p=>{HelpEditor.setOutputView('source');messages=[];views=[];HelpEditor.load(p);return{state:HelpEditor.getState(),views,source:HelpEditor.getSource()};},payload('source-next'));
 check('HTML-source load never publishes a temporary Formatted view',source.views.length>0&&source.views.every(v=>v.pageView==='source'&&v.tab==='source'&&v.visualHidden&&!v.sourceHidden)&&source.source===payload('source-next').source,source.views);
 const appSource=await page.evaluate(p=>{HelpEditor.setOutputView('app');messages=[];views=[];HelpEditor.load(p);return{state:HelpEditor.getState(),views,source:HelpEditor.getSource()};},payload('app-source-next'));
 check('App load preserves its source buffer and never switches the editing view',appSource.state.pageView==='app'&&appSource.state.tab==='source'&&appSource.views.length>0&&appSource.views.every(v=>v.pageView==='app'&&v.workspaceView==='desktop'&&v.tab==='source'&&v.visualHidden&&!v.sourceHidden)&&appSource.source===payload('app-source-next').source,appSource.views);
 const appVisual=await page.evaluate(p=>{HelpEditor.setOutputView('formatted');HelpEditor.setOutputView('app');messages=[];views=[];HelpEditor.load(p);return{state:HelpEditor.getState(),views};},payload('app-visual-next'));
 check('App formatted load commits only the accepted App workspace',appVisual.state.pageView==='app'&&appVisual.state.tab==='visual'&&appVisual.views.length>0&&appVisual.views.every(v=>v.pageView==='app'&&v.workspaceView==='desktop'&&v.tab==='visual'&&!v.visualHidden),appVisual.views);
 check('Accepted new-page loads keep a clean single-entry Undo history',appVisual.state.historyIndex===0&&!appVisual.state.dirty,appVisual.state);
 for(const view of ['browser','app']){
  const bad=await page.evaluate(({p,view})=>{HelpEditor.load(p);HelpEditor.setOutputView(view);messages=[];HelpEditor.load({...p,path:'basics/broken.htm',source:'<html><body>unfinished'});let rejected=false;try{HelpEditor.getSource();}catch{rejected=true;}return{state:HelpEditor.getState(),source:document.getElementById('source-editor').value,error:!document.getElementById('error').hidden,rejected,requests:messages.filter(m=>m.action==='preview-request').length};},{p:payload('valid-again'),view});
  check('Malformed page loaded from '+view+' remains recoverable source and cannot request output',bad.state.pageView==='source'&&bad.state.tab==='source'&&bad.source==='<html><body>unfinished'&&bad.error&&bad.rejected&&bad.requests===0,bad);
 }
 const empty=await page.evaluate(p=>{HelpEditor.load(p);HelpEditor.setOutputView('browser');HelpEditor.load({...p,path:''});return HelpEditor.getState();},payload('no-selection'));
 check('An empty page selection returns to Formatted without inventing a preview request',empty.pageView==='formatted'&&empty.tab==='visual'&&!empty.path,empty);
 check('No unhandled browser errors',errors.length===0,errors);
}catch(error){check('Handoff test completed',false,error.stack||String(error));}
finally{if(browser)await browser.close();server.close();const result={checkedAtUtc:new Date().toISOString(),passed:checks.every(row=>row.passed),checks,errors};fs.writeFileSync(path.join(evidence,'browser-handoff-checks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));if(!result.passed)process.exitCode=1;}})();
