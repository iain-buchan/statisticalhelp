// Print the actual built help; never inject fixes or change source/output.
// Developer-only dependencies: Playwright (browser-runtime.cjs), Python+pypdf
// and Poppler pdfinfo/pdftoppm. Codex's bundled runtimes are the defaults;
// STATSDIRECT_PYTHON and STATSDIRECT_POPPLER_PATH can select local installations.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),os=require('node:os'),{spawnSync}=require('node:child_process');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence/FlarePrintChecks');
const mounts={flare:path.resolve(process.env.STATSDIRECT_FLARE_WEBSITE||'C:/Develop/SD3Help/StatsDirect/Output/ieb21/Website'),editor:path.resolve(process.env.STATSDIRECT_HELP_OUTPUT||path.join(root,'Build/Public'))};
const runtime=path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies');
const python=process.env.STATSDIRECT_PYTHON||path.join(runtime,'python/python.exe'),poppler=process.env.STATSDIRECT_POPPLER_PATH||path.join(runtime,'native/poppler/Library/bin');
const topics=['references/reference_list.htm','analysis_of_variance/one_way.htm'];
const mime={'.htm':'text/html','.html':'text/html','.js':'text/javascript','.css':'text/css','.xml':'application/xml','.json':'application/json','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.ico':'image/x-icon','.woff':'font/woff','.woff2':'font/woff2'};
const server=http.createServer((req,res)=>{const parts=decodeURIComponent(new URL(req.url,'http://localhost').pathname).split('/').filter(Boolean),mount=mounts[parts.shift()],file=mount&&path.resolve(mount,...parts);if(!file||!file.startsWith(mount+path.sep)){res.writeHead(403).end();return;}try{res.setHeader('Content-Type',mime[path.extname(file)]||'application/octet-stream');res.setHeader('Cache-Control','public, max-age=3600');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}});
const compact=text=>text.normalize('NFKC').replace(/[\s\u00ad]/g,'');
const checks=[],rows=[],errors=[];
function check(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);}
function run(executable,args){const r=spawnSync(executable,args,{encoding:'utf8',maxBuffer:8*1024*1024,windowsHide:true});if(r.error||r.status!==0)throw r.error||Error(r.stderr);return r.stdout;}
function extract(file){return JSON.parse(run(python,['-c',"import json,sys;from pypdf import PdfReader;r=PdfReader(sys.argv[1]);print(json.dumps({'pages':len(r.pages),'text':'\\n'.join(p.extract_text() or '' for p in r.pages)}))",file]));}
(async()=>{fs.mkdirSync(evidence,{recursive:true});await new Promise(r=>server.listen(0,'127.0.0.1',r));const origin='http://127.0.0.1:'+server.address().port,browser=await chromium.launch({channel:'msedge',headless:true}),context=await browser.newContext(),page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));try{
 await context.route('**/*',route=>route.request().url().startsWith(origin+'/')||/^(data|blob):/.test(route.request().url())?route.continue():route.abort());
 for(const topic of topics){const topicRows=[];
  for(const variant of ['flare','editor','editor-open-navigation']){
   await page.setViewportSize({width:variant==='editor-open-navigation'?390:1280,height:900});await page.emulateMedia({media:'screen'});
   await page.goto(origin+'/'+(variant==='flare'?'flare':'editor')+'/'+topic,{waitUntil:'load'});
   if(variant==='flare')await page.waitForFunction(()=>!!document.querySelector('.sidenav a'));else await page.waitForFunction(()=>!!window.StatsDirectHelp&&!!document.querySelector('article#topic'));
   await page.waitForFunction(()=>[...document.images].every(i=>i.complete));if(await page.evaluate(()=>!!window.MathJax?.startup?.promise))await page.evaluate(()=>MathJax.startup.promise);await page.evaluate(()=>document.fonts.ready);
   if(variant==='editor-open-navigation')await page.locator('#navigation-toggle').click();
   const screenOverflow=variant==='flare'?null:await page.locator('article#topic').evaluate(e=>getComputedStyle(e).overflowY);
   await page.emulateMedia({media:'print'});
   const expected=await page.evaluate(()=>{const body=document.querySelector('#help-topic-body')||document.querySelector('.body-container')||document.body,paras=[...body.querySelectorAll('p')].filter(p=>p.checkVisibility()&&!p.closest('footer')&&!p.textContent.trim().startsWith('Copyright'));return{lastParagraph:paras.findLast(p=>p.textContent.trim())?.textContent||'',footer:[...body.querySelectorAll('p')].find(p=>p.textContent.trim().startsWith('Copyright'))?.textContent||''};});
   const metrics=await page.evaluate(()=>{const article=document.querySelector('article#topic'),main=document.querySelector('main');return{bodyOverflow:getComputedStyle(document.body).overflowY,mainTransform:main?getComputedStyle(main).transform:null,article:article?{height:article.clientHeight,scrollHeight:article.scrollHeight,overflow:getComputedStyle(article).overflowY,x:article.getBoundingClientRect().x}:null,headerHidden:[...document.querySelectorAll('header,aside,.navigation-shade')].every(e=>getComputedStyle(e).display==='none')};});
   const stem=topic.replace(/[/.]/g,'-')+'-'+variant,file=path.join(evidence,stem+'.pdf');await page.pdf({path:file,format:'A4',margin:{top:'12mm',bottom:'12mm',left:'12mm',right:'12mm'},printBackground:true});
   const pdf=extract(file),info=run(path.join(poppler,'pdfinfo.exe'),[file]),infoPages=Number(info.match(/^Pages:\s*(\d+)/m)?.[1]),text=compact(pdf.text),lastPrinted=!!expected.lastParagraph&&text.includes(compact(expected.lastParagraph)),footerPrinted=!!expected.footer&&text.includes(compact(expected.footer));
   run(path.join(poppler,'pdftoppm.exe'),['-f',String(pdf.pages),'-l',String(pdf.pages),'-scale-to','1200','-singlefile','-png',file,path.join(evidence,stem+'-last')]);
   check(topic+' '+variant+' prints more than one complete page',pdf.pages>1&&pdf.pages===infoPages,{pages:pdf.pages,pdfinfoPages:infoPages});
   check(topic+' '+variant+' prints the last visible source paragraph',lastPrinted,expected.lastParagraph);
   check(topic+' '+variant+' prints the complete source footer',footerPrinted,expected.footer);
   if(variant!=='flare'){
    check(topic+' '+variant+' keeps screen scrolling and removes print clipping',screenOverflow==='auto'&&metrics.bodyOverflow==='visible'&&metrics.article.overflow==='visible'&&metrics.article.height>=metrics.article.scrollHeight-1,metrics);
    check(topic+' '+variant+' hides navigation and prints the whole article width',metrics.headerHidden&&metrics.mainTransform==='none'&&Math.abs(metrics.article.x)<.1,metrics);
   }
   const row={topic,variant,pages:pdf.pages,textLength:pdf.text.length,lastPrinted,footerPrinted,lastParagraph:expected.lastParagraph,metrics,file};rows.push(row);topicRows.push({...row,compactText:text});
  }
  const [baseline,normal,opened]=topicRows;
  check(topic+' keeps the expected multi-page count',topic.startsWith('references/')?normal.pages>=baseline.pages:normal.pages===baseline.pages,{baseline:baseline.pages,actual:normal.pages,knownReferenceSourceEdits:topic.startsWith('references/')});
  check(topic+' opening Contents before Print preserves every printed character',opened.pages===normal.pages&&opened.compactText===normal.compactText,{closedPages:normal.pages,openedPages:opened.pages,closedCharacters:normal.textLength,openedCharacters:opened.textLength});
 }
 check('No browser errors during printing',errors.length===0,errors);
 }catch(error){check('Print checks completed',false,error.stack||String(error));}finally{await browser.close();server.close();const result={checkedAtUtc:new Date().toISOString(),passed:checks.every(c=>c.passed),checks,rows,errors};fs.writeFileSync(path.join(evidence,'print-checks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({passed:result.passed,total:checks.length,failures:checks.filter(c=>!c.passed)},null,2));if(!result.passed)process.exitCode=1;}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
