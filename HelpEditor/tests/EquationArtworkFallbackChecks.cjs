// Real MathLive editing/insertion -> preview -> saved XML -> CLI builds.
// All authored changes and outputs live in an isolated temporary fixture.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto'),os=require('node:os');
const {spawn}=require('node:child_process');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),web=path.join(root,'src/StatsDirectHelpEditor.App/Web'),realSource=path.join(root,'HelpSource'),evidence=path.join(root,'evidence');
const fixture=fs.mkdtempSync(path.join(os.tmpdir(),'StatsDirectHelp-EquationFallback-')),project=path.join(fixture,'ProjectSource'),content=path.join(project,'Content');
const catalog=JSON.parse(fs.readFileSync(path.join(web,'equation-artwork/catalog.json'),'utf8')),sample=catalog.entries[0];
const checks=[],errors=[],external=[],results=[],builds=[];
const digest=bytes=>crypto.createHash('sha256').update(bytes).digest('hex');
function treeDigests(folder){const files=new Map();function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const file=path.join(dir,e.name);if(e.isSymbolicLink())throw new Error('Source tree contains a link: '+file);if(e.isDirectory())walk(file);else files.set(file,digest(fs.readFileSync(file)));}}walk(folder);return files;}
const sourceBefore=treeDigests(realSource);
function record(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);}
const mime={'.html':'text/html','.htm':'text/html','.js':'text/javascript','.mjs':'text/javascript','.json':'application/json','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.woff':'font/woff','.woff2':'font/woff2'};
const server=http.createServer((req,res)=>{
 const parts=decodeURIComponent(new URL(req.url,'http://localhost').pathname).split('/').filter(Boolean);let mount=web;
 if(parts[0]==='project'){parts.shift();mount=content;}
 else if(parts[0]==='built'){parts.shift();const target=parts.shift();if(!['Desktop','Public'].includes(target)){res.writeHead(404).end();return;}mount=path.join(fixture,target);}
 if(!parts.length)parts.push('index.html');const file=path.resolve(mount,...parts);
 if(!file.startsWith(mount+path.sep)){res.writeHead(403).end();return;}
 try{res.setHeader('Content-Type',(mime[path.extname(file).toLowerCase()]||'application/octet-stream')+'; charset=utf-8');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}
});
function run(command,args){return new Promise((resolve,reject)=>{const child=spawn(command,args,{cwd:root,windowsHide:true,shell:false});let stdout='',stderr='';child.stdout.on('data',b=>stdout+=b);child.stderr.on('data',b=>stderr+=b);child.on('error',reject);child.on('close',code=>code===0?resolve({stdout,stderr}):reject(new Error(command+' exited '+code+'\n'+stdout+'\n'+stderr)));});}
function inspect(){
 const cases={};
 for(const id of ['current','edited','new']){
  const equation=document.querySelector('#'+id+' .help-equation'),image=equation?.querySelector(':scope>img.help-equation-original'),container=equation?.querySelector('mjx-container'),math=equation?.querySelector('mjx-assistive-mml math'),svg=container?.querySelector('svg'),r=(image||container||equation)?.getBoundingClientRect();
  const fraction=math?.querySelector('mfrac');
  cases[id]={exists:!!equation,cached:!!equation?.classList.contains('help-equation-artwork'),images:equation?.querySelectorAll('img.help-equation-original').length||0,sourceKey:equation?.dataset.equationSourceKey||null,mathML:math?.outerHTML||null,mathText:math?.textContent.replace(/\s/g,'')||'',visibleMathJax:!!container&&!container.closest('.help-equation-accessible')&&!!svg&&r.width>0&&r.height>0,visibleImage:!!image&&image.complete&&image.naturalWidth>0&&r.width>0&&r.height>0,width:r?.width,height:r?.height,errors:equation?.querySelectorAll('[data-mml-node="merror"],mjx-merror,.MathJax_Error').length||0,hasSevenThirds:!!fraction&&fraction.children[0]?.textContent==='7'&&fraction.children[1]?.textContent==='3',hasAlpha:!!math&&Array.from(math.querySelectorAll('mi')).some(e=>e.textContent==='α')};
 }
 return cases;
}
(async()=>{let browser,successful=false;try{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:process.env.STATSDIRECT_BROWSER_CHANNEL||'msedge',headless:true});const context=await browser.newContext({viewport:{width:1000,height:1100}});
 await context.route('**/*',route=>{const url=route.request().url();if(url.startsWith(origin+'/')||/^(data|blob):/.test(url))return route.continue();external.push(url);return route.abort();});
 const editor=await context.newPage();editor.on('pageerror',e=>errors.push(e.message));await editor.goto(origin+'/');await editor.waitForFunction(()=>!!window.HelpEditor);
 const originalTopic=fs.readFileSync(path.join(realSource,'Content',sample.topic),'utf8');
 const initialSource=await editor.evaluate(({originalTopic,index})=>{
  const parsed=new DOMParser().parseFromString(originalTopic,'application/xml'),equation=parsed.getElementsByTagNameNS('http://www.madcapsoftware.com/Schemas/MadCap.xsd','equation')[index-1],xml=new XMLSerializer().serializeToString(equation);
  return '<?xml version="1.0" encoding="utf-8"?>\n<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"><head><title>Equation fallback fixture</title><link rel="stylesheet" href="resources/stylesheets/statsdirect.css" /></head><body><h1>Equation fallback fixture</h1><p id="current">'+xml+'</p><p id="edited">'+xml+'</p><p id="new"></p></body></html>';
 },{originalTopic,index:sample.index});
 const payload={path:'fixture.htm',title:'Equation fallback fixture',source:initialSource,baseUrl:origin+'/project/fixture.htm'};
 const editedSource=await editor.evaluate(payload=>{
  HelpEditor.load(payload);const currentBefore=document.querySelector('#current .equation-widget').dataset.protectedId,edited=document.querySelector('#edited .equation-widget');
  HelpEditor.openEquation(edited.dataset.protectedId);const field=document.getElementById('equation-field');field.value=field.value+'+12345';document.getElementById('equation-apply').click();
  if(document.getElementById('equation-dialog').open)throw new Error('Editing the existing equation was rejected.');
  const visual=document.getElementById('visual-editor'),target=visual.querySelector('#new'),range=document.createRange();range.selectNodeContents(target);range.collapse(false);visual.focus();getSelection().removeAllRanges();getSelection().addRange(range);document.dispatchEvent(new Event('selectionchange'));
  HelpEditor.openEquation();document.getElementById('equation-field').value='\\frac{7}{3}+\\alpha';document.getElementById('equation-apply').click();
  if(document.getElementById('equation-dialog').open)throw new Error('Inserting the new equation was rejected.');
  const source=HelpEditor.getSource(),doc=new DOMParser().parseFromString(source,'application/xml');HelpEditor.markSaved();
  return {source,valid:!doc.querySelector('parsererror'),equations:doc.getElementsByTagNameNS('http://www.madcapsoftware.com/Schemas/MadCap.xsd','equation').length,unchangedWidget:document.querySelector('#current .equation-widget').dataset.protectedId===currentBefore,edited:doc.querySelector('#edited').textContent.includes('12345'),new:doc.querySelector('#new').textContent.includes('\\frac')&&doc.querySelector('#new').textContent.includes('\\alpha')};
 },payload);
 record('MathLive edits an existing equation and inserts a new one while keeping the original',editedSource.valid&&editedSource.equations===3&&editedSource.unchangedWidget&&editedSource.edited&&editedSource.new,editedSource);
 fs.mkdirSync(path.join(content,'resources/stylesheets'),{recursive:true});fs.mkdirSync(path.join(project,'Project/TOCs'),{recursive:true});
 fs.writeFileSync(path.join(project,'.statsdirect-help-editor.json'),'{"prototypeWorkingCopy":true}');fs.writeFileSync(path.join(project,'StatsDirect.flprj'),'<CatapultProject MasterToc="/Project/TOCs/STATSDIRECT.fltoc" />');
 fs.writeFileSync(path.join(project,'Project/TOCs/STATSDIRECT.fltoc'),'<CatapultToc Version="1"><TocEntry Title="Equation fallback fixture" Link="/Content/fixture.htm" /></CatapultToc>');
 fs.copyFileSync(path.join(realSource,'Content/resources/Stylesheets/statsdirect.css'),path.join(content,'resources/stylesheets/statsdirect.css'));fs.writeFileSync(path.join(content,'fixture.htm'),editedSource.source);
 const fixtureBefore=treeDigests(project),builder=path.join(root,'src/StatsDirectHelpEditor.Cli/bin/Release/net10.0/StatsDirectHelpBuilder.dll');
 if(!fs.existsSync(builder))throw new Error('Build the Release solution before running the end-to-end equation fallback check.');
 for(const target of ['Desktop','Public']){
  const built=await run('dotnet',[builder,'build','--project',project,'--output',path.join(fixture,target),'--assets',web,'--target',target]);builds.push({target,...built});
  record(target+' CLI builds the saved edited fixture successfully',JSON.parse(built.stdout).TopicCount===1);
  const page=await context.newPage();page.on('pageerror',e=>errors.push(e.message));await page.goto(origin+'/built/'+target+'/fixture.htm?content=1');await page.evaluate(()=>MathJax.startup.promise);await page.waitForFunction(()=>Array.from(document.images).every(i=>i.complete));
  const builtCases=await page.evaluate(inspect);const imageUrl=await page.locator('#current img.help-equation-original').evaluate(image=>image.src),imageBytes=Buffer.from(await(await fetch(imageUrl)).arrayBuffer()),cachedBytes=digest(imageBytes);
  await page.locator('#current img.help-equation-original').screenshot({path:path.join(evidence,'equation-artwork-fallback-'+target.toLowerCase()+'-build.png')});
  await editor.evaluate(target=>{HelpEditor.setWorkspaceMode(true);HelpEditor.setWorkspaceView(target.toLowerCase());},target);const frame=await(await editor.$('#preview-frame')).contentFrame();await frame.waitForFunction(()=>window.helpPreviewReady===true);await frame.waitForFunction(()=>Array.from(document.images).every(i=>i.complete));
  const previewCases=await frame.evaluate(inspect),previewImageUrl=await frame.locator('#current img.help-equation-original').evaluate(image=>image.src),previewImageBytes=Buffer.from(await(await fetch(previewImageUrl)).arrayBuffer());await frame.locator('#current img.help-equation-original').screenshot({path:path.join(evidence,'equation-artwork-fallback-'+target.toLowerCase()+'-preview.png')});
  for(const [kind,cases]of [['CLI',builtCases],['preview',previewCases]]){
   record(target+' '+kind+' unchanged equation uses the exact verified original artwork',cases.current.cached&&cases.current.images===1&&cases.current.visibleImage&&cases.current.sourceKey===sample.sourceKey&&!cases.current.visibleMathJax,{current:cases.current,expectedSourceKey:sample.sourceKey});
   record(target+' '+kind+' edited equation renders its new mathematics without stale artwork',!cases.edited.cached&&cases.edited.images===0&&cases.edited.visibleMathJax&&cases.edited.mathText.includes('12345')&&cases.edited.mathText.replace('12345','').replace(/\+$/,'')===cases.current.mathText,cases.edited);
   record(target+' '+kind+' new equation renders seven thirds plus alpha without cached artwork',!cases.new.cached&&cases.new.images===0&&cases.new.visibleMathJax&&cases.new.hasSevenThirds&&cases.new.hasAlpha,cases.new);
   record(target+' '+kind+' preserves accessible MathML with no renderer errors',Object.values(cases).every(e=>e.exists&&e.mathML&&e.errors===0));
  }
  record(target+' preview and CLI use byte-identical original SVG',cachedBytes===sample.artworkSha256&&digest(Buffer.from(previewImageBytes))===sample.artworkSha256,{expected:sample.artworkSha256,built:cachedBytes,preview:digest(Buffer.from(previewImageBytes))});
  record(target+' preview and CLI original artwork screenshots are pixel-identical',digest(fs.readFileSync(path.join(evidence,'equation-artwork-fallback-'+target.toLowerCase()+'-build.png')))===digest(fs.readFileSync(path.join(evidence,'equation-artwork-fallback-'+target.toLowerCase()+'-preview.png'))));
  record(target+' preview matches CLI mathematics and exact original image dimensions',JSON.stringify(Object.fromEntries(Object.entries(builtCases).map(([k,v])=>[k,v.mathML])))===JSON.stringify(Object.fromEntries(Object.entries(previewCases).map(([k,v])=>[k,v.mathML])))&&builtCases.current.width===previewCases.current.width&&builtCases.current.height===previewCases.current.height,{built:builtCases.current,preview:previewCases.current});
  record(target+' preview does not mutate the saved authored XML',await editor.evaluate(source=>HelpEditor.getSource()===source,editedSource.source));
  results.push({target,built:builtCases,preview:previewCases});await page.close();
 }
 record('CLI builds leave all isolated authored fixture files byte-identical',Array.from(fixtureBefore).every(([file,sha])=>fs.existsSync(file)&&digest(fs.readFileSync(file))===sha)&&treeDigests(project).size===fixtureBefore.size);
 const sourceAfter=treeDigests(realSource);record('All real help source files remain unchanged',sourceAfter.size===sourceBefore.size&&Array.from(sourceBefore).every(([file,sha])=>sourceAfter.get(file)===sha),{files:sourceBefore.size});
 record('No unhandled browser errors or external requests',errors.length===0&&external.length===0,{errors,external});
 fs.writeFileSync(path.join(evidence,'equation-artwork-fallback-fixture.xml'),editedSource.source);successful=checks.every(c=>c.passed);await context.close();
 }catch(error){record('End-to-end fallback checks completed',false,error.stack||String(error));}finally{
 if(browser)await browser.close();server.close();
 const report={passed:checks.length>0&&checks.every(c=>c.passed),checkedAtUtc:new Date().toISOString(),method:'Real MathLive edit and insert, source save, both CLI build targets and both editor previews in an isolated fixture; exact original image bytes, changed/new visible mathematics, MathML, source preservation and offline behavior.',checks,results,builds,errors,externalRequests:external,fixture:successful?'Temporary reproducible build fixture removed after passing':fixture};
 fs.writeFileSync(path.join(evidence,'equation-artwork-fallback-checks.json'),JSON.stringify(report,null,2));
 if(successful){const resolved=path.resolve(fixture),base=path.resolve(os.tmpdir())+path.sep;if(!resolved.startsWith(base)||!path.basename(resolved).startsWith('StatsDirectHelp-EquationFallback-'))throw new Error('Unexpected temporary fixture path.');fs.rmSync(resolved,{recursive:true,force:true});}
 console.log(JSON.stringify({passed:report.passed,checks:checks.length,failures:checks.filter(c=>!c.passed).map(c=>({name:c.name,detail:c.detail}))},null,2));if(!report.passed)process.exitCode=1;
 }})();
