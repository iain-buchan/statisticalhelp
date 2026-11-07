const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {spawnSync}=require('node:child_process'),{pathToFileURL}=require('node:url');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence'),checks=[],errors=[],externalRequests=[];
fs.mkdirSync(evidence,{recursive:true});
// Exercise the builder, not copies of its HTML/JS constants. Every source and
// output used here is isolated from HelpSource and from the evaluation builds.
const fixture=fs.mkdtempSync(path.join(evidence,'ViewerSkinFixture-'));
const palettes={flare:'rgb(255, 255, 255)',readable:'rgb(255, 255, 255)','clean-blue':'rgb(244, 248, 252)','warm-paper':'rgb(255, 250, 240)'};
const sourceCss='body{font:10pt Arial,sans-serif;background:#fff;color:#000}p{font:10pt/1.5 Arial,sans-serif;margin:8px 0 12px;letter-spacing:0}h1{font:700 16pt Arial,sans-serif;margin:12px 0 18px}a{color:#006699}';
const topicBody='<h1>Design fixture</h1><p id="topic-text">The same statistics, spacing and equations in every design.</p><p><a id="next-topic" href="nested/beta.htm">Another topic</a> <a id="new-tab-topic" href="nested/beta.htm" target="_blank">Open another topic in a new tab</a></p><svg xmlns="http://www.w3.org/2000/svg" id="chart" width="150" height="60" viewBox="0 0 150 60" style="color:#c30020"><rect x="2" y="3" width="40" height="30" fill="#168b49"/><a href="#chart"><text x="4" y="52" fill="currentColor">Chart label</text></a></svg><math xmlns="http://www.w3.org/1998/Math/MathML" id="equation"><mfrac><mi>x</mi><mn>2</mn></mfrac></math><svg xmlns="http://www.w3.org/2000/svg" id="equation-glyph" width="30" height="25"><path fill="#111" d="M 2 2 L 20 20 M 20 2 L 2 20"/></svg>';
function topic(title,body,prefix=''){return '<html><head><title>'+title+'</title><link rel="stylesheet" href="'+prefix+'resources/stylesheets/statsdirect.css"/></head><body>'+body+'</body></html>';}
function write(base,name,value){const file=path.join(base,name);fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,value);}
function hash(file){return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');}
function files(base){return fs.readdirSync(base,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(path.join(base,e.name)):[path.join(base,e.name)]);}
function record(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed&&detail)console.log(typeof detail==='string'?detail:JSON.stringify(detail));}
function run(args){const result=spawnSync('dotnet',args,{cwd:root,encoding:'utf8',windowsHide:true});if(result.error||result.status!==0)throw Error('dotnet '+args.join(' ')+'\n'+(result.error||result.stderr||result.stdout));return result.stdout;}
const builds=[],sourceHashes=new Map();
function buildFixtures(){
  run(['build','src/StatsDirectHelpEditor.Cli/StatsDirectHelpEditor.Cli.csproj','-c','Release','--nologo']);
  const cli=path.join(root,'src/StatsDirectHelpEditor.Cli/bin/Release/net10.0/StatsDirectHelpBuilder.dll');
  const selections=[{name:'default',desktop:'flare',public:'flare'},{name:'readable',desktop:'readable',public:'readable'},{name:'blue-paper',desktop:'clean-blue',public:'warm-paper'},{name:'paper-blue',desktop:'warm-paper',public:'clean-blue'}];
  for(const choice of selections){
    const project=path.join(fixture,choice.name,'Source');
    write(project,'.statsdirect-help-editor.json','{"prototypeWorkingCopy":true}');
    write(project,'Project/TOCs/STATSDIRECT.fltoc','<CatapultToc><TocEntry Title="Fixture" Link="/Content/contents.htm"/><TocEntry Title="Nested topics" Link="/Content/nested/section.htm"><TocEntry Title="Beta" Link="/Content/nested/beta.htm"/></TocEntry><TocEntry Title="First anchored note" Link="/Content/nested/anchor.htm#note:1"><TocEntry Title="Unicode anchored note" Link="/Content/nested/anchor.htm#β-Δ"/></TocEntry></CatapultToc>');
    write(project,'Content/resources/stylesheets/statsdirect.css',sourceCss);
    for(const name of ['sd3helplogo.png','sd3.ico'])write(project,'Content/resources/Images/'+name,fs.readFileSync(path.join(root,'HelpSource/Content/resources/Images',name)));
    write(project,'Content/contents.htm',topic('Fixture',topicBody+'<p id="note:1">A local anchor with a colon.</p><p id="β-Δ">A local Unicode anchor.</p>'));
    write(project,'Content/nested/beta.htm',topic('Beta','<h1>Beta</h1><p id="topic-text">Quartzforecast second topic.</p><a href="../contents.htm">Back</a>','../'));
    write(project,'Content/nested/section.htm',topic('Nested topics','<h1>Nested topics</h1><p id="topic-text">A parent section.</p><a href="beta.htm">Beta</a>','../'));
    write(project,'Content/nested/anchor.htm',topic('Anchored notes','<h1>Anchored notes</h1><p id="topic-text">A topic represented only by anchored Contents entries.</p><p id="note:1">First note. <a id="anchor-next" href="#β-Δ">Unicode note</a></p><p id="β-Δ">Unicode note. <a id="anchor-back" href="#note:1">First note</a></p>','../'));
    if(choice.name!=='default')write(project,'Project/HelpEditorDesign.json',JSON.stringify({schemaVersion:1,appearance:{desktop:choice.desktop,public:choice.public}}));
    for(const file of files(project))sourceHashes.set(file,hash(file));
    for(const target of ['Desktop','Public']){
      const output=path.join(fixture,choice.name,target),skin=choice[target.toLowerCase()];
      run([cli,'build','--project',project,'--output',output,'--assets',path.join(root,'src/StatsDirectHelpEditor.App/Web'),'--target',target]);
      builds.push({name:choice.name+'/'+target,output,skin,project,url:'/'+choice.name+'/'+target+'/',desktop:target==='Desktop'});
    }
  }
}
const server=http.createServer((req,res)=>{
  const relative=decodeURIComponent(new URL(req.url,'http://localhost').pathname).replace(/^\/+/,''),file=path.resolve(fixture,relative||'index.html');
  if(!file.startsWith(fixture+path.sep)){res.statusCode=403;res.end();return;}
  try{const resolved=fs.statSync(file).isDirectory()?path.join(file,'index.html'):file;res.setHeader('Content-Type',({'.html':'text/html','.htm':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.ico':'image/x-icon','.woff':'font/woff','.woff2':'font/woff2'})[path.extname(resolved)]||'application/octet-stream');res.end(fs.readFileSync(resolved));}catch{res.statusCode=404;res.end();}
});
function textStyle(){return Array.from(document.querySelectorAll('h1,#topic-text')).map(el=>{const s=getComputedStyle(el);return {text:el.textContent,font:s.font,fontFamily:s.fontFamily,fontSize:s.fontSize,lineHeight:s.lineHeight,marginTop:s.marginTop,marginBottom:s.marginBottom,letterSpacing:s.letterSpacing,color:s.color};});}
function artwork(){const canonical=el=>({tag:el.tagName,attributes:Array.from(el.attributes).filter(a=>a.name!=='xmlns').map(a=>[a.name,a.value]).sort((a,b)=>a[0].localeCompare(b[0])),children:Array.from(el.childNodes).map(n=>n.nodeType===1?canonical(n):n.textContent)});return {chart:canonical(document.getElementById('chart')),glyph:canonical(document.getElementById('equation-glyph')),chartColour:getComputedStyle(document.querySelector('#chart a')).color};}
function equationArtwork(){return Array.from(document.querySelectorAll('mjx-container svg path')).map(p=>[p.getAttribute('d'),p.getAttribute('fill')]);}
function contrast(a,b){const rgb=c=>(c.match(/[\d.]+/g)||[]).slice(0,3).map(n=>{let v=Number(n)/255;return v<=.04045?v/12.92:((v+.055)/1.055)**2.4;});const lum=c=>{const v=rgb(c);return .2126*v[0]+.7152*v[1]+.0722*v[2];};const x=lum(a),y=lum(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);}
async function currentFrame(page,topic='contents.htm'){
  await page.waitForFunction(()=>!!window.StatsDirectHelp);
  if(await page.locator('article#topic').count()){
    await page.waitForFunction(topic=>document.documentElement.dataset.currentTopic===topic,topic);
    await page.waitForFunction(()=>!!window.StatsDirectHelp);
    await page.locator('#help-topic-body #topic-text').waitFor();return page;
  }
  const frame=page.frames().find(f=>f.parentFrame()===page.mainFrame());
  if(!frame)throw Error('Expected help topic iframe');
  await frame.waitForURL(url=>url.pathname.endsWith('/'+topic));await frame.locator('#topic-text').waitFor();return frame;
}
async function fixedSkin(page,skin,topic='contents.htm'){
  const frame=await currentFrame(page,topic);
  record('Viewer and '+topic+' retain the built '+skin+' design',await page.locator('html').getAttribute('data-help-skin')===skin&&await frame.locator('html').getAttribute('data-help-skin')===skin);
  return frame;
}
async function noReaderControl(page){
  return await page.locator('#appearance,.appearance,select').count()===0&&await page.locator('label').allTextContents().then(labels=>labels.every(x=>!/^\s*Appearance\s*$/i.test(x)))&&await page.evaluate(()=>!('setSkin' in window.StatsDirectHelp));
}
async function usableLayout(page){
  return page.evaluate(()=>{const header=document.querySelector('header').getBoundingClientRect(),main=document.querySelector('main').getBoundingClientRect(),identity=document.querySelector('.help-identity').getBoundingClientRect(),frame=document.getElementById('topic').getBoundingClientRect();return {width:innerWidth,scroll:document.documentElement.scrollWidth,headerHeight:header.height,headerBottom:header.bottom,mainTop:main.top,identityTop:identity.top,identityBottom:identity.bottom,frameWidth:frame.width,frameHeight:frame.height};});
}
async function logoLayout(page){
  return page.locator('header .help-logo').evaluate(el=>{const box=el.getBoundingClientRect(),header=el.closest('header').getBoundingClientRect(),style=getComputedStyle(el);return {naturalWidth:el.naturalWidth,naturalHeight:el.naturalHeight,width:box.width,height:box.height,left:box.left,right:box.right,top:box.top,bottom:box.bottom,headerLeft:header.left,headerRight:header.right,headerTop:header.top,headerBottom:header.bottom,visible:style.display!=='none'&&style.visibility!=='hidden'&&Number(style.opacity)>0,alt:el.alt};});
}
async function checkSiteControls(page,build,surface){
  const logo=page.locator('header .help-logo'),search=page.locator('#search-form');
  record(build.name+' '+surface+' shows website branding and search only on Public',
    await logo.isVisible()===!build.desktop&&await search.isVisible()===!build.desktop);
}
async function searchFixture(page,build,entry){
  if(build.desktop){
    const search=new URLSearchParams({searchQuery:'quartzforecast'});
    await page.goto(entry+'#'+search);
  }else{
    await page.locator('#search').fill('quartzforecast');
    await page.locator('#search-form button').click();
  }
  await page.locator('#results a[data-link="nested/beta.htm"]').waitFor();
}
async function favicon(page){return page.locator('head link[rel~="icon"]').evaluate(el=>({href:el.getAttribute('href'),resolved:el.href}));}
async function noDuplicateNavigation(page){return await page.locator('.help-topic-nav').count()===0||await page.locator('.help-topic-nav').evaluate(el=>getComputedStyle(el).display==='none');}
// A public article now keeps one reader document. Chromium cannot push another
// file:// path, so its logical topic URL may differ from the shell's location.
async function topicUrl(frame){return frame.evaluate(()=>document.documentElement.dataset.helpTarget!=='desktop'&&document.querySelector('article#topic')&&window.StatsDirectHelp?.currentTopic||location.href);}
async function waitTopicUrl(frame,predicate){
  const timeout=Date.now()+15000;let last;
  do{last=await topicUrl(frame);if(predicate(new URL(last)))return;await frame.waitForTimeout(25);}while(Date.now()<timeout);
  throw Error('Timed out waiting for the logical topic URL: '+last);
}
function trackNetwork(context,origin,label){context.on('request',request=>{const url=request.url();if(!url.startsWith(origin+'/')&&!/^(?:file:|data:|blob:)/.test(url))externalRequests.push({label,url});});}
async function topicUrlChecks(page,build,label){
  await page.evaluate(()=>StatsDirectHelp.openTopic('CoNtEnTs.HtM?sample=one%20two#note:1'));
  let frame=await currentFrame(page);await waitTopicUrl(frame,u=>u.hash==='#note:1'&&u.searchParams.get('sample')==='one two');
  record(build.name+' '+label+' canonicalises topic case and preserves queries/colon anchors',await page.locator('#contents a[data-link="contents.htm"]').getAttribute('aria-current')==='page'&&await frame.locator('[id="note:1"]').count()===1&&await page.title()==='Fixture');
  await page.reload();frame=await currentFrame(page);await waitTopicUrl(frame,u=>u.hash==='#note:1'&&u.searchParams.get('sample')==='one two');
  record(build.name+' '+label+' reload restores the topic title, query and colon anchor',new URL(await topicUrl(frame)).searchParams.get('sample')==='one two'&&new URL(await topicUrl(frame)).hash==='#note:1'&&await page.title()==='Fixture');
  await page.evaluate(()=>StatsDirectHelp.openTopic('CONTENTS.HTM#β-Δ'));frame=await currentFrame(page);await waitTopicUrl(frame,u=>decodeURIComponent(u.hash.slice(1))==='β-Δ');
  record(build.name+' '+label+' preserves Unicode anchors',await frame.locator('[id="β-Δ"]').count()===1);
  await page.reload();frame=await currentFrame(page);await waitTopicUrl(frame,u=>decodeURIComponent(u.hash.slice(1))==='β-Δ');
  record(build.name+' '+label+' Unicode anchors survive reload without double decoding',decodeURIComponent(new URL(await topicUrl(frame)).hash.slice(1))==='β-Δ');
  const before={url:page.url(),topic:await topicUrl(frame)};
  await page.evaluate(()=>{StatsDirectHelp.openTopic('index.html');StatsDirectHelp.openTopic('../contents.htm');StatsDirectHelp.openTopic('resources/images/sd3.ico');});
  record(build.name+' '+label+' ignores routes outside the generated topic catalogue',page.url()===before.url&&await topicUrl(frame)===before.topic);
  await page.evaluate(()=>StatsDirectHelp.openTopic('nested/beta.htm'));return currentFrame(page,'nested/beta.htm');
}
async function anchoredContentsChecks(page,build,label){
  async function selected(link){await page.waitForFunction(link=>{const links=[...document.querySelectorAll('#contents a')],active=links.filter(a=>a.classList.contains('selected')),current=links.filter(a=>a.getAttribute('aria-current')==='page');return active.length===1&&current.length===1&&active[0]===current[0]&&active[0].dataset.link===link;},link);}
  await page.evaluate(()=>StatsDirectHelp.openTopic('nested/anchor.htm#β-Δ'));let frame=await currentFrame(page,'nested/anchor.htm');await waitTopicUrl(frame,u=>decodeURIComponent(u.hash.slice(1))==='β-Δ');await selected('nested/anchor.htm#β-Δ');
  record(build.name+' '+label+' selects exactly the matching fragment-only Contents entry',await page.locator('#contents a.selected').count()===1);
  // File iframe documents can be isolated from their parent by the browser;
  // exercise their documented parent API while the normal article uses native links.
  if(label==='HTTP'||build.skin==='flare')await frame.locator('#anchor-back').click();else await page.evaluate(()=>StatsDirectHelp.openTopic('nested/anchor.htm#note:1'));
  frame=await currentFrame(page,'nested/anchor.htm');await waitTopicUrl(frame,u=>u.hash==='#note:1');await selected('nested/anchor.htm#note:1');
  record(build.name+' '+label+' changes Contents selection when navigating to another anchor',await page.locator('#contents a[aria-current="page"]').count()===1);
  await page.goBack();frame=await currentFrame(page,'nested/anchor.htm');await waitTopicUrl(frame,u=>decodeURIComponent(u.hash.slice(1))==='β-Δ');await selected('nested/anchor.htm#β-Δ');
  record(build.name+' '+label+' Back restores the anchored Contents selection',await page.locator('#contents a.selected').count()===1);
  await page.reload();frame=await currentFrame(page,'nested/anchor.htm');await waitTopicUrl(frame,u=>decodeURIComponent(u.hash.slice(1))==='β-Δ');await selected('nested/anchor.htm#β-Δ');
  record(build.name+' '+label+' direct reload retains fragment-specific Contents selection',await page.locator('#contents a.selected').count()===1);
  await page.evaluate(()=>StatsDirectHelp.openTopic('nested/anchor.htm'));frame=await currentFrame(page,'nested/anchor.htm');await waitTopicUrl(frame,u=>u.hash==='');await selected('nested/anchor.htm#note:1');
  record(build.name+' '+label+' a page without a plain Contents entry selects its first canonical entry',await page.locator('#contents a.selected').count()===1);
  await page.evaluate(()=>StatsDirectHelp.openTopic('nested/beta.htm'));return currentFrame(page,'nested/beta.htm');
}
(async()=>{let browser,browserServer;try{
  buildFixtures();
  record('The legacy project defaults to Flare without creating a design file',!fs.existsSync(path.join(fixture,'default/Source/Project/HelpEditorDesign.json')));
  for(const build of builds){
    const html=fs.readFileSync(path.join(build.output,'index.html'),'utf8'),topicHtml=fs.readFileSync(path.join(build.output,'contents.htm'),'utf8'),js=fs.readFileSync(path.join(build.output,'help-viewer.js'),'utf8');
    record(build.name+' bakes the selected design into viewer and topic HTML before scripts run',html.includes('data-help-skin="'+build.skin+'"')&&topicHtml.includes('data-help-skin="'+build.skin+'"'));
    record(build.name+' ships no editor design controls or visitor appearance persistence/protocol',!/<select|for="appearance"|id="appearance"|>Appearance</i.test(html)&&!/(?:localStorage|setSkin|statsdirect-help-appearance|statsdirect-help-topic-ready)/.test(js+topicHtml)&&!files(build.output).some(f=>/HelpEditorDesign\.json$|(?:^|[\\/])editor\.(?:js|css)$/i.test(f)));
    record(build.name+' preserves the original StatsDirect logo and icon bytes', ['sd3helplogo.png','sd3.ico'].every(name=>hash(path.join(build.output,'resources/images',name))===hash(path.join(build.project,'Content/resources/Images',name))));
  }
  await new Promise(r=>server.listen(0,'127.0.0.1',r));
  const origin='http://127.0.0.1:'+server.address().port;
  browserServer=await chromium.launchServer({channel:'msedge',headless:true});
  browser=await chromium.connect(browserServer.wsEndpoint());
  const baseline=await browser.newPage();
  await baseline.goto(origin+'/default/Source/Content/contents.htm');
  const sourceStyle=await baseline.evaluate(textStyle),sourceArtwork=await baseline.evaluate(artwork);
  await baseline.goto(origin+'/default/Desktop/contents.htm?content=1');await baseline.evaluate(()=>window.MathJax.startup.promise);
  const flareEquation=await baseline.evaluate(equationArtwork);await baseline.close();
  record('The generated Flare baseline typesets the source equation locally',flareEquation.length>0);
  for(const build of builds){
    const context=await browser.newContext({viewport:{width:1280,height:900}});
    trackNetwork(context,origin,build.name);
    const other=build.skin==='warm-paper'?'clean-blue':'warm-paper';
    await context.addInitScript(skin=>{try{localStorage.setItem('statsdirect-help-skin',skin);}catch{}},other);
    const page=await context.newPage();page.on('pageerror',e=>errors.push(build.name+': '+e.message));
    await page.goto(origin+build.url);let frame=await fixedSkin(page,build.skin);
    record(build.name+' home title uses the source topic title',await page.title()==='Fixture');
    await checkSiteControls(page,build,'home');
    await page.locator('header .help-logo').evaluate(el=>el.decode());
    const viewerIcon=await favicon(page),rootIcon=await favicon(frame);
    const iconResponse=await context.request.get(viewerIcon.resolved);
    record(build.name+' viewer and root topic use the local StatsDirect favicon',viewerIcon.href==='resources/images/sd3.ico'&&rootIcon.resolved===viewerIcon.resolved&&iconResponse.ok()&&crypto.createHash('sha256').update(await iconResponse.body()).digest('hex')===hash(path.join(build.output,'resources/images/sd3.ico')),{viewerIcon,rootIcon});
    await frame.evaluate(()=>window.MathJax.startup.promise);
    record(build.name+' exposes no reader Appearance selector, label or switching API',await noReaderControl(page));
    record(build.name+' preserves source fonts, paragraph/heading spacing, text and equation/chart artwork',JSON.stringify(await frame.evaluate(textStyle))===JSON.stringify(sourceStyle)&&JSON.stringify(await frame.evaluate(artwork))===JSON.stringify(sourceArtwork)&&JSON.stringify(await frame.evaluate(equationArtwork))===JSON.stringify(flareEquation),{sourceStyle,actualStyle:await frame.evaluate(textStyle),equationGlyphs:flareEquation.length});
    record(build.name+' uses its selected topic background and avoids duplicate topic navigation',await frame.locator('body').evaluate(el=>getComputedStyle(el).backgroundColor)===palettes[build.skin]&&await noDuplicateNavigation(frame));
    record(build.name+' uses the intended article or iframe renderer',build.skin==='flare'?await page.locator('article#topic>#help-topic-body').count()===1&&await page.locator('iframe').count()===0:await page.locator('iframe#topic').count()===1);
    const colours=await page.evaluate(()=>({header:getComputedStyle(document.querySelector('header')).backgroundColor,headerText:getComputedStyle(document.querySelector('header')).color,nav:getComputedStyle(document.querySelector('aside')).backgroundColor,navText:getComputedStyle(document.querySelector('#contents a')).color}));
    const topicColours=await frame.evaluate(()=>({background:getComputedStyle(document.body).backgroundColor,text:getComputedStyle(document.getElementById('topic-text')).color,link:getComputedStyle(document.getElementById('next-topic')).color}));
    record(build.name+' maintains at least 4.5:1 normal text/link contrast',(build.desktop||contrast(colours.header,colours.headerText)>=4.5)&&contrast(colours.nav,colours.navText)>=4.5&&contrast(topicColours.background,topicColours.text)>=4.5&&contrast(topicColours.background,topicColours.link)>=4.5,{colours,topicColours});
    const branch=page.locator('#contents details').first(),summary=branch.locator('summary'),beforeExpansion=frame.url();
    await summary.focus();await page.keyboard.press('Space');
    record(build.name+' keyboard Space expands a section without navigating',await branch.evaluate(e=>e.open)&&frame.url()===beforeExpansion);
    const parentLink=build.desktop?branch.locator(':scope > ul > li:first-child > a'):summary.locator('a');
    record(build.name+' retains the branch overview link in its target-specific position',
      await parentLink.getAttribute('data-link')==='nested/section.htm'&&
      await parentLink.textContent()===(build.desktop?'Overview':'Nested topics')&&
      (!build.desktop||await summary.locator('a').count()===0));
    await page.keyboard.press('Space');
    record(build.name+' keyboard Space closes the expanded section',!await branch.evaluate(e=>e.open)&&frame.url()===beforeExpansion);
    // A valid old preference and the former parent/topic protocol must have no
    // effect on the design chosen by the author at build time.
    await page.evaluate(skin=>localStorage.setItem('statsdirect-help-skin',skin),other);await page.reload();frame=await fixedSkin(page,build.skin);
    await page.evaluate(skin=>{window.postMessage({type:'statsdirect-help-appearance',skin},'*');document.getElementById('topic').contentWindow?.postMessage({type:'statsdirect-help-appearance',skin},'*');},other);
    await frame.evaluate(skin=>parent.postMessage({type:'statsdirect-help-appearance',skin},'*'),other);await page.waitForTimeout(40);
    record(build.name+' ignores saved visitor preferences and former appearance messages',await page.locator('html').getAttribute('data-help-skin')===build.skin&&await frame.locator('html').getAttribute('data-help-skin')===build.skin&&await page.evaluate(()=>localStorage.getItem('statsdirect-help-skin'))===other);
    const popupPromise=context.waitForEvent('page');await frame.locator('#new-tab-topic').click();const popup=await popupPromise;await popup.waitForLoadState();
    const popupFrame=await fixedSkin(popup,build.skin,'nested/beta.htm');
    await checkSiteControls(popup,build,'direct new-tab topic');
    record(build.name+' direct topics open the complete built design in a new tab',await popupFrame.locator('body').evaluate(el=>getComputedStyle(el).backgroundColor)===palettes[build.skin]&&await noDuplicateNavigation(popupFrame));
    record(build.name+' direct topic tab title follows its source topic',await popup.title()==='Beta');
    const popupIcon=await favicon(popup);
    record(build.name+' nested topic entries retain the shell and topic local favicon',popupIcon.href===(build.skin==='flare'?'../resources/images/sd3.ico':'resources/images/sd3.ico')&&popupIcon.resolved===viewerIcon.resolved&&(await favicon(popupFrame)).resolved===viewerIcon.resolved,popupIcon);await popup.close();
    await frame.locator('#next-topic').click();frame=await fixedSkin(page,build.skin,'nested/beta.htm');
    record(build.name+' keeps the selected design through iframe links',await frame.locator('#topic-text').textContent()==='Quartzforecast second topic.');
    const nestedIcon=await favicon(frame);
    record(build.name+' iframe topics at a nested path resolve the original favicon',(!build.desktop&&build.skin==='flare'||nestedIcon.href==='../resources/images/sd3.ico')&&nestedIcon.resolved===viewerIcon.resolved,nestedIcon);
    await page.locator('#contents a[data-link="contents.htm"]').click();await currentFrame(page);
    await searchFixture(page,build,origin+build.url+'index.html');
    record(build.name+' search tab title matches Flare',await page.title()==='Search');
    await page.locator('#results a[data-link="nested/beta.htm"]').click();frame=await fixedSkin(page,build.skin,'nested/beta.htm');
    record(build.name+' retains working contents and full-text search',await frame.locator('#topic-text').textContent()==='Quartzforecast second topic.');
    record(build.name+' search match restores the topic title',await page.title()==='Beta');
    await page.goBack();await page.locator('#results a[data-link="nested/beta.htm"]').waitFor();
    record(build.name+' browser Back restores search results and title after opening a match',await page.locator('#results').isVisible()&&new URL(page.url()).hash.includes('searchQuery=quartzforecast')&&await page.title()==='Search');
    await page.goForward();frame=await currentFrame(page,'nested/beta.htm');
    record(build.name+' browser Forward restores the selected topic and title',await frame.locator('#topic-text').textContent()==='Quartzforecast second topic.'&&(build.skin==='flare'?new URL(page.url()).pathname.endsWith('/nested/beta.htm'):new URL(page.url()).hash.includes('topic=nested%2Fbeta.htm'))&&await page.title()==='Beta');
    frame=await topicUrlChecks(page,build,'HTTP');
    frame=await anchoredContentsChecks(page,build,'HTTP');
    for(const width of [1280,390,320]){
      await page.setViewportSize({width,height:width<600?844:900});const layout=await usableLayout(page);
      const compactBrowse=build.desktop&&build.skin==='flare'&&width<1280;
      const expectedHeader=build.desktop?(compactBrowse?32:0):build.skin==='flare'?(width<1280?111.666667:90):(width<=700?52:60);
      record(build.name+' has the intended '+width+'px desktop/mobile geometry',layout.scroll<=layout.width&&Math.abs(layout.headerHeight-expectedHeader)<.1&&layout.identityTop>=0&&layout.identityBottom<=layout.headerBottom&&Math.abs(layout.mainTop-layout.headerBottom)<1&&layout.frameWidth>=150&&layout.frameHeight>300,layout);
      if(build.desktop){
        await checkSiteControls(page,build,width+'px');
        const browse=page.getByRole('button',{name:'Browse topics'});
        record(build.name+' uses the compact Browse topics button only at narrow Flare widths',await browse.isVisible()===compactBrowse);
        if(compactBrowse){
          const before=page.url();
          await browse.click();
          record(build.name+' compact Browse opens Contents in place',
            await browse.getAttribute('aria-expanded')==='true'&&await page.locator('#contents').isVisible()&&page.url()===before);
          await page.keyboard.press('Escape');
          record(build.name+' Escape closes the compact Contents panel',await browse.getAttribute('aria-expanded')==='false');
        }
      }else{
        const logo=await logoLayout(page);
        record(build.name+' keeps the original logo visible and proportional inside its '+width+'px title bar',logo.naturalWidth===228&&logo.naturalHeight===33&&logo.visible&&logo.alt==='StatsDirect StatisticalHelp'&&logo.width>0&&logo.height>0&&Math.abs(logo.width/logo.height-228/33)<0.01&&logo.left>=logo.headerLeft&&logo.right<=logo.headerRight&&logo.top>=logo.headerTop&&logo.bottom<=logo.headerBottom,logo);
      }
    }
    await page.emulateMedia({media:'print'});
    record(build.name+' prints without navigation or coloured page backgrounds',await page.locator('header').evaluate(el=>getComputedStyle(el).display==='none')&&await page.locator('aside').evaluate(el=>getComputedStyle(el).display==='none')&&await frame.locator('body').evaluate(el=>getComputedStyle(el).backgroundColor)==='rgb(255, 255, 255)');
    await context.close();
    const offlineContext=await browser.newContext(),offline=await offlineContext.newPage();offline.on('pageerror',e=>errors.push(build.name+' file://: '+e.message));
    trackNetwork(offlineContext,origin,build.name+' file://');
    await offline.goto(pathToFileURL(path.join(build.output,'contents.htm')).href);
    let directFrame=await fixedSkin(offline,build.skin);
    await checkSiteControls(offline,build,'direct file:// topic');
    record(build.name+' direct file:// topics retain the complete design and navigation',await directFrame.locator('body').evaluate(el=>getComputedStyle(el).backgroundColor)===palettes[build.skin]&&await noDuplicateNavigation(directFrame));
    record(build.name+' direct root topics use an existing local favicon file',(await favicon(offline)).resolved===pathToFileURL(path.join(build.output,'resources/images/sd3.ico')).href);
    const fileEntry=pathToFileURL(path.join(build.output,'index.html')).href;
    await offline.goto(fileEntry);await currentFrame(offline);
    await offline.locator('header .help-logo').evaluate(el=>el.decode());
    const offlineLogo=await logoLayout(offline),offlineIcon=await favicon(offline);
    record(build.name+' loads its original logo and favicon from local files',offlineLogo.naturalWidth===228&&offlineLogo.naturalHeight===33&&offlineIcon.resolved===pathToFileURL(path.join(build.output,'resources/images/sd3.ico')).href);
    await searchFixture(offline,build,fileEntry);await offline.locator('#results a[data-link="nested/beta.htm"]').click();frame=await currentFrame(offline,'nested/beta.htm');
    record(build.name+' works offline with file:// search and iframe navigation',await noReaderControl(offline)&&await frame.locator('html').getAttribute('data-help-skin')===build.skin&&await frame.locator('#topic-text').textContent()==='Quartzforecast second topic.');
    record(build.name+' nested file:// topics keep the existing local favicon file',(await favicon(frame)).resolved===pathToFileURL(path.join(build.output,'resources/images/sd3.ico')).href);
    await offline.goBack();await offline.locator('#results a[data-link="nested/beta.htm"]').waitFor();
    record(build.name+' file:// Back restores search results and title',await offline.locator('#results').isVisible()&&new URL(offline.url()).hash.includes('searchQuery=quartzforecast')&&await offline.title()==='Search');
    await offline.goForward();frame=await currentFrame(offline,'nested/beta.htm');
    record(build.name+' file:// Forward restores the selected topic and title',await frame.locator('#topic-text').textContent()==='Quartzforecast second topic.'&&await offline.title()==='Beta');
    frame=await topicUrlChecks(offline,build,'file://');
    frame=await anchoredContentsChecks(offline,build,'file://');
    await offlineContext.close();
  }
  record('Building and browsing every design leaves all source fixtures unchanged',Array.from(sourceHashes).every(([file,value])=>hash(file)===value));
  record('No unhandled browser errors in HTTP, new-tab or file:// cases',errors.length===0,errors);
  record('All branding, fonts and help navigation remain local in HTTP and file:// cases',externalRequests.length===0,externalRequests);
}catch(error){record('Viewer build-time design checks completed',false,error.stack||String(error));}finally{
  if(browser){let timer;await Promise.race([browser.close(),new Promise(resolve=>{timer=setTimeout(resolve,5000);})]);clearTimeout(timer);}
  if(browserServer)await browserServer.kill();server.closeAllConnections();server.close();
  const result={testedAt:new Date().toISOString(),fixture,passed:checks.every(c=>c.passed),checks,errors,externalRequests};
  fs.writeFileSync(path.join(evidence,'viewer-skin-results.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({passed:result.passed,total:checks.length,failures:checks.filter(c=>!c.passed),fixture},null,2));if(!result.passed)process.exitCode=1;
}})();
