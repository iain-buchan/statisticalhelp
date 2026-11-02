// Exercise rendered equation ink and scroll geometry, not CSS implementation strings.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),source=path.join(root,'HelpSource/Content'),web=path.join(root,'src/StatsDirectHelpEditor.App/Web'),evidence=path.join(root,'evidence');
const topics=[],checks=[],results=[],errors=[],external=new Set();
function walk(folder){for(const item of fs.readdirSync(folder,{withFileTypes:true})){const file=path.join(folder,item.name);if(item.isDirectory())walk(file);else if(/\.htm$/i.test(file)){const text=fs.readFileSync(file,'utf8'),count=(text.match(/<MadCap:equation\b/g)||[]).length;if(count)topics.push({url:path.relative(source,file).replaceAll('\\','/').toLowerCase(),file,count});}}}
walk(source);
const digest=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex'),before=new Map(topics.map(t=>[t.file,digest(t.file)]));
const mime={'.html':'text/html','.htm':'text/html','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.woff':'font/woff','.woff2':'font/woff2'};
const server=http.createServer((req,res)=>{const u=new URL(req.url,'http://localhost'),parts=decodeURIComponent(u.pathname).split('/').filter(Boolean);let base=web;
 if(parts[0]==='built'){parts.shift();const target=parts.shift();if(!['Desktop','Public'].includes(target)){res.writeHead(404).end();return;}base=path.join(root,'Build',target);}
 else if(parts[0]==='project'){parts.shift();base=source;}
 if(!parts.length)parts.push('index.html');const file=path.resolve(base,...parts);if(!file.startsWith(base+path.sep)){res.writeHead(403).end();return;}
 try{res.setHeader('Content-Type',(mime[path.extname(file)]||'application/octet-stream')+'; charset=utf-8');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}
});
function record(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);}
function inspectEquations(selector='.help-equation'){
 const scrollable=value=>value==='auto'||value==='scroll'||value==='overlay',clips=value=>scrollable(value)||value==='hidden'||value==='clip';
 const box=element=>{const r=element.getBoundingClientRect();return{left:r.left+element.clientLeft,right:r.left+element.clientLeft+element.clientWidth,top:r.top+element.clientTop,bottom:r.top+element.clientTop+element.clientHeight};};
 return [...document.querySelectorAll(selector)].map((equation,index)=>{
  const artwork=equation.querySelector(':scope>img.help-equation-original'),nodes=artwork?[equation,artwork]:[equation,...equation.querySelectorAll('mjx-container,svg')],svg=artwork?null:equation.querySelector('mjx-container[jax="SVG"]>svg'),assistive=equation.querySelector('mjx-assistive-mml'),e=(artwork||equation).getBoundingClientRect(),verticalScroll=[];
  for(const node of nodes){const s=getComputedStyle(node);if(scrollable(s.overflowY)&&node.scrollHeight>node.clientHeight)verticalScroll.push({tag:node.tagName,overflowY:s.overflowY,clientHeight:node.clientHeight,scrollHeight:node.scrollHeight});}
  let ink=null,verticalClip=[],horizontalClip=[],accessibleHorizontal=true;
  if(svg||artwork){
   if(artwork)ink={left:e.left,right:e.right,top:e.top,bottom:e.bottom};
   else{const b=svg.getBBox(),m=svg.getScreenCTM(),points=[[b.x,b.y],[b.x+b.width,b.y],[b.x,b.y+b.height],[b.x+b.width,b.y+b.height]].map(([x,y])=>new DOMPoint(x,y).matrixTransform(m));ink={left:Math.min(...points.map(p=>p.x)),right:Math.max(...points.map(p=>p.x)),top:Math.min(...points.map(p=>p.y)),bottom:Math.max(...points.map(p=>p.y))};}
   for(let node=artwork||svg;node;node=node.parentElement){const s=getComputedStyle(node),r=box(node);if(clips(s.overflowY)&&(ink.top<r.top-.75||ink.bottom>r.bottom+.75))verticalClip.push({tag:node.tagName,overflowY:s.overflowY,viewport:r});
    if(clips(s.overflowX)&&(ink.left<r.left-.75||ink.right>r.right+.75)){
     const canScroll=scrollable(s.overflowX)&&node.scrollWidth>node.clientWidth&&ink.left>=r.left-.75&&ink.right<=r.left+node.scrollWidth+.75;
     horizontalClip.push({tag:node.tagName,overflowX:s.overflowX,clientWidth:node.clientWidth,scrollWidth:node.scrollWidth,accessible:canScroll});if(!canScroll)accessibleHorizontal=false;
    }
    if(node===equation)break;
   }
  }
  const assistiveMathML=!!assistive?.querySelector('math')&&getComputedStyle(assistive).display!=='none'&&!assistive.closest('[hidden],[aria-hidden="true"]');
  return{index:index+1,width:e.width,height:e.height,svg:!!svg,cachedArtwork:!!artwork&&artwork.complete&&artwork.naturalWidth>0&&artwork.naturalHeight>0,assistiveMathML,ink,verticalScroll,verticalClip,horizontalClip,accessibleHorizontal,mathErrors:equation.querySelectorAll('[data-mml-node="merror"],mjx-merror,.MathJax_Error').length};
 });
}
function valid(row){return(row.svg||row.cachedArtwork)&&row.assistiveMathML&&row.ink&&row.ink.right>row.ink.left&&row.ink.bottom>row.ink.top&&!row.verticalScroll.length&&!row.verticalClip.length&&row.accessibleHorizontal&&!row.mathErrors;}
const longTerm='\\frac{(x_j-\\bar{x})^2}{1+\\sqrt{(y_j-\\bar{y})^2}}';
const syntheticSource='<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"><head><title>Equation overflow fixture</title></head><body><p>Before <MadCap:equation>$\\frac{a+b}{c+d}$</MadCap:equation> after.</p><p><MadCap:equation>$\\displaystyle \\left\\{\\begin{array}{l}'+Array.from({length:12},(_,i)=>'\\frac{x_'+i+'^2}{\\sqrt{y_'+i+'+1}}').join(' \\\\ ')+'\\end{array}\\right.$</MadCap:equation></p><p><MadCap:equation>$\\displaystyle '+Array.from({length:12},()=>longTerm).join('+')+'$</MadCap:equation></p><p><MadCap:equation><math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><msup><mi>x</mi><mn>2</mn></msup><mrow><mi>y</mi><mo>+</mo><mn>1</mn></mrow></mfrac></math></MadCap:equation></p></body></html>';
(async()=>{let browser;try{
 await new Promise(r=>server.listen(0,'127.0.0.1',r));const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:process.env.STATSDIRECT_BROWSER_CHANNEL||'msedge',headless:true});
 const context=await browser.newContext({viewport:{width:900,height:1100},deviceScaleFactor:2.5});await context.route('**/*',route=>{const u=route.request().url();if(u.startsWith(origin+'/')||/^(data|blob):/.test(u))return route.continue();external.add(u);return route.abort();});
 const jobs=[];for(const target of ['Desktop','Public'])for(const width of [900,390])for(const topic of topics)jobs.push({target,width,...topic});
 let cursor=0;await Promise.all(Array.from({length:4},async()=>{const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));while(cursor<jobs.length){const job=jobs[cursor++];try{
  await page.setViewportSize({width:job.width,height:1100});await page.goto(origin+'/built/'+job.target+'/'+job.url+'?content=1');await page.evaluate(()=>MathJax.startup.promise);await page.waitForFunction(()=>Array.from(document.images).every(i=>i.complete));
  const equations=await page.evaluate(inspectEquations);results.push({target:job.target,width:job.width,url:job.url,expected:job.count,equations,passed:equations.length===job.count&&equations.every(valid)});
 }catch(error){results.push({target:job.target,width:job.width,url:job.url,passed:false,error:error.stack});}}await page.close();}));
 for(const target of ['Desktop','Public'])for(const width of [900,390]){
  const selected=results.filter(r=>r.target===target&&r.width===width),total=selected.reduce((n,r)=>n+(r.equations?.length||0),0);
  record(target+' equations are fully visible without vertical scrollers at '+width+'px',selected.length===87&&total===258&&selected.every(r=>r.passed),{topics:selected.length,equations:total,failures:selected.filter(r=>!r.passed).map(r=>({url:r.url,error:r.error,equations:r.equations?.filter(e=>!valid(e))}))});
 }
 const editor=await context.newPage();editor.on('pageerror',e=>errors.push(e.message));await editor.goto(origin+'/');await editor.waitForFunction(()=>!!window.HelpEditor);
 const previewSamples=['basic_descriptive_statistics/time_series.htm','rates/incidence_and_prevalence.htm','sample_size/matched_case_control.htm'];
 for(const target of ['desktop','public'])for(const width of [900,390])for(const rel of [...previewSamples,'synthetic-equations.htm']){
  await editor.setViewportSize({width,height:1100});const text=rel==='synthetic-equations.htm'?syntheticSource:fs.readFileSync(path.join(source,rel),'utf8'),payload={path:rel,title:rel,source:text,baseUrl:origin+'/project/'+rel};
  await editor.evaluate(p=>{HelpEditor.load(p);HelpEditor.setWorkspaceMode(true);HelpEditor.setWorkspaceView(p.target);},{...payload,target});const frame=await(await editor.$('#preview-frame')).contentFrame();await frame.waitForFunction(()=>window.helpPreviewReady===true);await frame.waitForFunction(()=>Array.from(document.images).every(i=>i.complete));
  const equations=await frame.evaluate(inspectEquations),expected=rel==='synthetic-equations.htm'?4:(text.match(/<MadCap:equation\b/g)||[]).length;
  record(target+' draft preview '+rel+' fits equations at '+width+'px without source edits',equations.length===expected&&equations.every(valid)&&await editor.evaluate(p=>HelpEditor.getSource()===p.source,payload),{equations:equations.filter(e=>!valid(e)),count:equations.length,expected});
 }
 record('Source files are unchanged',Array.from(before).every(([file,value])=>digest(file)===value));
 record('No browser errors or external requests',errors.length===0&&external.size===0,{errors,external:[...external]});
 await context.close();
 }catch(error){record('Equation geometry checks completed',false,error.stack||String(error));}finally{
 if(browser)await browser.close();server.close();const equations=results.flatMap(r=>r.equations||[]),totals={scans:results.length,equationRenders:equations.length,verticalScrollbarCases:equations.filter(e=>e.verticalScroll.length).length,clippedVerticalInkCases:equations.filter(e=>e.verticalClip.length).length,inaccessibleHorizontalInkCases:equations.filter(e=>!e.accessibleHorizontal).length,missingAssistiveMathML:equations.filter(e=>!e.assistiveMathML).length};
 const report={checkedAtUtc:new Date().toISOString(),passed:checks.every(c=>c.passed),deviceScaleFactor:2.5,totals,checks,results,errors,externalRequests:[...external]};
 fs.writeFileSync(path.join(evidence,'equation-overflow-checks.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({passed:report.passed,checks:checks.length,...totals,failures:checks.filter(c=>!c.passed).map(c=>c.name)},null,2));if(!report.passed)process.exitCode=1;
 }})();
