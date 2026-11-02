// Read-only print-regression diagnostic. Browser-printed evidence is isolated;
// no production CSS, help source or published files are changed.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),os=require('node:os'),{spawnSync}=require('node:child_process');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence/FlarePrintProbe');
const mounts={flare:path.resolve('C:/Develop/SD3Help/StatsDirect/Output/ieb21/Website'),editor:path.join(root,'Build/Public')};
const poppler=path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/native/poppler/Library/bin');
const python=path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe');
const topics=['references/reference_list.htm','analysis_of_variance/one_way.htm'];
const mime={'.htm':'text/html','.html':'text/html','.js':'text/javascript','.css':'text/css','.xml':'application/xml','.json':'application/json','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.ico':'image/x-icon','.woff':'font/woff','.woff2':'font/woff2'};
const fix='@media print{html[data-help-skin="flare"] body{overflow:visible}html[data-help-skin="flare"] article#topic{height:auto;overflow:visible}}';
const server=http.createServer((req,res)=>{const parts=decodeURIComponent(new URL(req.url,'http://localhost').pathname).split('/').filter(Boolean),mount=mounts[parts.shift()],file=mount&&path.resolve(mount,...parts);if(!file||!file.startsWith(mount+path.sep)){res.writeHead(403).end();return;}try{res.setHeader('Content-Type',mime[path.extname(file)]||'application/octet-stream');res.setHeader('Cache-Control','public, max-age=3600');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}});
function command(exe,args){const result=spawnSync(path.join(poppler,exe+'.exe'),args,{encoding:'utf8',maxBuffer:8*1024*1024,windowsHide:true});if(result.error||result.status!==0)throw result.error||Error(result.stderr);return result.stdout;}
(async()=>{fs.mkdirSync(evidence,{recursive:true});await new Promise(r=>server.listen(0,'127.0.0.1',r));const origin='http://127.0.0.1:'+server.address().port,browser=await chromium.launch({channel:'msedge',headless:true}),context=await browser.newContext({viewport:{width:1280,height:900}}),page=await context.newPage(),rows=[],errors=[];page.on('pageerror',e=>errors.push(e.message));try{
 await context.route('**/*',route=>route.request().url().startsWith(origin+'/')||/^(data|blob):/.test(route.request().url())?route.continue():route.abort());
 for(const topic of topics)for(const variant of ['flare','editor','editor-print-fix']){
  await page.goto(origin+'/'+(variant==='flare'?'flare':'editor')+'/'+topic,{waitUntil:'load'});if(variant!=='flare')await page.waitForFunction(()=>!!window.StatsDirectHelp);else await page.waitForFunction(()=>!!document.querySelector('.sidenav a'));
  await page.waitForFunction(()=>[...document.images].every(i=>i.complete));if(await page.evaluate(()=>!!window.MathJax?.startup?.promise))await page.evaluate(()=>MathJax.startup.promise);await page.evaluate(()=>document.fonts.ready);
  if(variant==='editor-print-fix')await page.addStyleTag({content:fix});await page.emulateMedia({media:'print'});
  const metrics=await page.evaluate(()=>Object.fromEntries(['html','body','main','#topic','#help-topic-body','.body-container'].map(selector=>{const e=document.querySelector(selector);if(!e)return[selector,null];const s=getComputedStyle(e),r=e.getBoundingClientRect();return[selector,{clientHeight:e.clientHeight,scrollHeight:e.scrollHeight,height:r.height,overflow:s.overflow,overflowY:s.overflowY,display:s.display,position:s.position,textLength:e.innerText?.length}];})));
  const stem=topic.replace(/[/.]/g,'-')+'-'+variant,file=path.join(evidence,stem+'.pdf');await page.pdf({path:file,format:'A4',margin:{top:'12mm',bottom:'12mm',left:'12mm',right:'12mm'},printBackground:true});
  const info=command('pdfinfo',[file]),pages=Number(info.match(/^Pages:\s*(\d+)/m)?.[1]);
  const extraction=spawnSync(python,['-c',"import json,sys; from pypdf import PdfReader; r=PdfReader(sys.argv[1]); texts=[p.extract_text() or '' for p in r.pages]; text='\\n'.join(texts); print(json.dumps({'textLength':len(text),'footer':'Copyright' in text and '1987' in text,'lastTail':texts[-1][-900:]}))",file],{encoding:'utf8',windowsHide:true});if(extraction.status!==0)throw Error(extraction.stderr);const extracted=JSON.parse(extraction.stdout);
  command('pdftoppm',['-f',String(pages),'-l',String(pages),'-scale-to','1200','-singlefile','-png',file,path.join(evidence,stem+'-last')]);
  rows.push({topic,variant,pages,pdfTextLength:extracted.textLength,footerPrinted:extracted.footer,lastPageTail:extracted.lastTail,metrics,file});console.log(JSON.stringify({topic,variant,pages,textLength:extracted.textLength,footer:extracted.footer}));await page.emulateMedia({media:'screen'});
 }
 }finally{await browser.close();server.close();fs.writeFileSync(path.join(evidence,'print-probe.json'),JSON.stringify({checkedAtUtc:new Date().toISOString(),diagnostic:true,fix,rows,errors},null,2));}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
