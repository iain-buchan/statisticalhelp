// Read-only capture of the published Flare baseline and its local counterpart.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence','FlareLiveBaseline');
const local=path.resolve('C:/Develop/SD3Help/StatsDirect/Output/ieb21/Website');
const live='https://statsdirect.com/help/';
fs.mkdirSync(evidence,{recursive:true});
const mime={'.htm':'text/html','.html':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.woff':'font/woff','.woff2':'font/woff2','.ttf':'font/ttf','.xml':'application/xml','.ico':'image/x-icon'};
const server=http.createServer((req,res)=>{const name=decodeURIComponent(new URL(req.url,'http://localhost').pathname).replace(/^\//,'')||'Default.htm',file=path.resolve(local,name);if(!file.startsWith(local+path.sep)){res.writeHead(403).end();return;}try{res.setHeader('Content-Type',mime[path.extname(file).toLowerCase()]||'application/octet-stream');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}});
const measure=()=>{
 const rect=e=>{const r=e.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height,bottom:r.bottom,right:r.right};};
 const style=e=>{const s=getComputedStyle(e);return{...rect(e),tag:e.tagName,id:e.id,class:e.className,text:e.textContent.trim().slice(0,130),font:s.font,fontFamily:s.fontFamily,fontSize:s.fontSize,fontWeight:s.fontWeight,lineHeight:s.lineHeight,color:s.color,background:s.backgroundColor,padding:s.padding,margin:s.margin,border:s.border,display:s.display,position:s.position,overflowX:s.overflowX,overflowY:s.overflowY};};
 const selectors=['html','body','.title-bar','.title-bar-layout','a.logo','.search-bar','.search-field','.sidenav-layout','.sidenav-wrapper','.sidenav-container','.sidenav','.off-canvas-wrapper','.body-container','#mc-main-content','#navigation','#navigationResizeBar','#navigationButtons','.tabs','.tabs-panel','#toc','#search','h1','.toolbar','iframe'];
 const selected={};for(const selector of selectors){selected[selector]=Array.from(document.querySelectorAll(selector)).filter(e=>e.getBoundingClientRect().width&&e.getBoundingClientRect().height).slice(0,8).map(style);}
 return{url:location.href,viewport:{width:innerWidth,height:innerHeight,dpr:devicePixelRatio},title:document.title,selectors:selected,inputs:Array.from(document.querySelectorAll('input,button,[role=tab],a.logo')).filter(e=>e.getBoundingClientRect().width&&e.getBoundingClientRect().height).slice(0,35).map(style),scripts:Array.from(document.scripts).map(s=>s.src),styles:Array.from(document.querySelectorAll('link[rel=stylesheet]')).map(s=>s.href),frames:Array.from(document.querySelectorAll('iframe')).map(f=>({src:f.src,...rect(f)}))};
};
(async()=>{let browser;const pages=[],errors=[],responses=[];try{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:'msedge',headless:true});
 for(const [kind,base] of [['live',live],['local',origin+'/']]){
  const context=await browser.newContext({viewport:{width:1280,height:900}}),page=await context.newPage();page.on('pageerror',e=>errors.push({kind,error:e.message}));
  if(kind==='live')page.on('response',async response=>{const url=response.url();if(!url.startsWith(live)||response.status()!==200)return;try{const relative=decodeURIComponent(new URL(url).pathname.slice('/help/'.length));const bytes=await response.body(),file=path.resolve(local,relative||'Default.htm');responses.push({url,relative,status:response.status(),bytes:bytes.length,sha256:crypto.createHash('sha256').update(bytes).digest('hex'),localExists:fs.existsSync(file),sameAsLocal:fs.existsSync(file)&&crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')===crypto.createHash('sha256').update(bytes).digest('hex')});}catch{}});
  for(const width of [1280,900,390]){
   await page.setViewportSize({width,height:width===390?844:900});await page.goto(base,{waitUntil:'load',timeout:45000});await page.waitForTimeout(1200);
   await page.screenshot({path:path.join(evidence,kind+'-'+width+'.png')});
   fs.writeFileSync(path.join(evidence,kind+'-'+width+'.html'),await page.content());
   pages.push({kind,width,...await page.evaluate(measure)});
  }
  await context.close();
 }
}catch(error){errors.push({fatal:error.stack||String(error)});}finally{
 if(browser)await browser.close();server.close();
 const report={checkedAtUtc:new Date().toISOString(),live,local,pages,responses,errors};fs.writeFileSync(path.join(evidence,'baseline.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify({pages:pages.map(p=>({kind:p.kind,width:p.width,url:p.url,title:p.title,frames:p.frames,styles:p.styles})),responses:responses.length,matchingAssets:responses.filter(r=>r.sameAsLocal).length,errors},null,2));if(errors.some(e=>e.fatal))process.exitCode=1;
}})();
