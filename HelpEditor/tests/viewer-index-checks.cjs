const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {spawnSync}=require('node:child_process'),{pathToFileURL}=require('node:url');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence'),checks=[],errors=[];
fs.mkdirSync(evidence,{recursive:true});
// Build real reader fixtures. No working or published authoring content is used
// as an output destination, and the source bytes are checked after browsing.
const fixture=fs.mkdtempSync(path.join(evidence,'ReaderIndexFixture-')),builds=[],sourceHashes=[];
function record(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed&&detail)console.log(typeof detail==='string'?detail:JSON.stringify(detail));}
function write(base,name,value){const file=path.join(base,name);fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,value);}
function hash(file){return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');}
function files(base){return fs.readdirSync(base,{withFileTypes:true}).flatMap(entry=>entry.isDirectory()?files(path.join(base,entry.name)):[path.join(base,entry.name)]);}
function run(args){const result=spawnSync('dotnet',args,{cwd:root,encoding:'utf8',windowsHide:true});if(result.error||result.status!==0)throw Error('dotnet '+args.join(' ')+'\n'+(result.error||result.stderr||result.stdout));}
function pageSource(title,body,attributes=''){return `<html xmlns:sdh="https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd" ${attributes}><head><title>${title}</title></head><body><h1>${title}</h1>${body}</body></html>`;}
function buildFixtures(){
  run(['build','src/StatsDirectHelpEditor.Cli/StatsDirectHelpEditor.Cli.csproj','-c','Release','--nologo']);
  const cli=path.join(root,'src/StatsDirectHelpEditor.Cli/bin/Release/net10.0/StatsDirectHelpBuilder.dll');
  for(const skin of ['flare','readable']){
    const source=path.join(fixture,skin,'Source');
    write(source,'.statsdirect-help-editor.json','{}');
    write(source,'Project/TOCs/STATSDIRECT.fltoc','<CatapultToc><TocEntry Title="Home" Link="/Content/contents.htm"/><TocEntry Title="Methods" Link="/Content/nested/alpha.htm"><TocEntry Title="Nested beta" Link="/Content/nested/beta.htm"/><TocEntry Title="Other beta" Link="/Content/other/beta.htm"/><TocEntry Title="Artwork" Link="/Content/deep/nested/gamma.htm"/></TocEntry></CatapultToc>');
    const scrollTerms=Array.from({length:60},(_,i)=>'Scroll fixture '+String(i+1).padStart(2,'0')).join('; ');
    write(source,'Content/contents.htm',pageSource('Home',`<sdh:keyword term="Zulu; Shared; Cramér's V; &lt;img src=x onerror=alert(1)&gt;; ${scrollTerms}"/><p>Reader index fixture home.</p>`));
    write(source,'Content/nested/alpha.htm',pageSource('Alpha method','<sdh:keyword term="Shared; Alpha"/><p>Quartzforecast reader search fixture.</p><a id="body-next" href="../deep/nested/gamma.htm">Artwork topic</a>'));
    for(const name of ['nested/beta.htm','other/beta.htm'])write(source,'Content/'+name,pageSource('Same title','<sdh:keyword term="Duplicate"/><p>Separate authored keyword destination.</p>'));
    write(source,'Content/private.htm',pageSource('Private indexed topic','<sdh:keyword term="Unsearchable keyword"/><p>Privateprose search exclusion.</p>', 'sdh:searchable="false"'));
    write(source,'Content/unindexed.htm',pageSource('Unindexed title','<p>No authored keyword.</p>'));
    write(source,'Content/deep/nested/gamma.htm',pageSource('Artwork topic','<sdh:keyword term="Artwork"/><p id="artwork-note">New topic body with local artwork and mathematics.</p><img id="topic-image" src="../../resources/images/fixture.svg" alt="Local chart"/><sdh:equation><math xmlns="http://www.w3.org/1998/Math/MathML"><mfrac><mi>x</mi><mn>2</mn></mfrac></math></sdh:equation><sdh:dropDown class="rcode"><sdh:dropDownHead><sdh:dropDownHotspot>R code</sdh:dropDownHotspot></sdh:dropDownHead><sdh:dropDownBody><pre id="r-script">x &lt;- c(1, 2)\nmean(x)</pre></sdh:dropDownBody></sdh:dropDown><p><a id="body-anchor" href="#note">Go to the note</a> <a id="body-home" href="../../contents.htm">Home</a></p><div style="height:900px"></div><p id="note">Anchored note.</p>'));
    write(source,'Content/resources/images/fixture.svg','<svg xmlns="http://www.w3.org/2000/svg" width="77" height="24" viewBox="0 0 77 24"><rect width="77" height="24" fill="#168b49"/></svg>');
    if(skin!=='flare')write(source,'Project/HelpEditorDesign.json',JSON.stringify({schemaVersion:1,appearance:{desktop:skin,public:skin}}));
    sourceHashes.push(...files(source).map(file=>[file,hash(file)]));
    for(const target of skin==='flare'?['Desktop','Public']:['Public']){
      const output=path.join(fixture,skin,target);
      run([cli,'build','--project',source,'--output',output,'--assets',path.join(root,'src/StatsDirectHelpEditor.App/Web'),'--target',target]);
      builds.push({skin,target,output,url:'/'+skin+'/'+target+'/',name:skin+'/'+target});
    }
  }
}
const server=http.createServer((request,response)=>{
  const name=decodeURIComponent(new URL(request.url,'http://localhost').pathname).replace(/^\/+/,''),file=path.resolve(fixture,name);
  if(!file.startsWith(fixture+path.sep)){response.writeHead(403).end();return;}
  try{response.setHeader('Content-Type',({'.htm':'text/html','.html':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml'})[path.extname(file)]||'application/octet-stream');response.end(fs.readFileSync(file));}
  catch{response.writeHead(404).end();}
});
async function ready(page){await page.waitForFunction(()=>!!window.StatsDirectHelp&&!!document.querySelector('#contents a'));}
async function topicIs(page,name,skin){
  if(skin==='flare')await page.waitForFunction(name=>document.documentElement.dataset.currentTopic?.split(/[?#]/)[0]===name&&document.querySelector('#help-topic-body h1')?.textContent===window.StatsDirectTopics?.find(topic=>topic.url===name)?.title,name);
  else {const frame=page.frames().find(f=>f.parentFrame()===page.mainFrame());if(!frame)throw Error('Expected the reader topic iframe');await frame.waitForURL(url=>url.pathname.endsWith('/'+name));}
  await ready(page);
}
async function openNavigation(page){
  const toggle=page.locator('#navigation-toggle');
  if(await toggle.count()&&await toggle.isVisible()&&!await page.locator('aside').isVisible())await toggle.click();
}
async function index(page){await openNavigation(page);await page.locator('#index-tab').click();await page.locator('#index-filter').waitFor();}
async function snapshot(page){return page.evaluate(()=>({url:location.href,history:history.length,title:document.title,frame:document.getElementById('topic').getAttribute('src')}));}
async function sameDocument(page,before){const after=await snapshot(page);return before.url===after.url&&before.history===after.history&&before.frame===after.frame;}
async function entries(page){return page.locator('#index-entries a[data-link]').evaluateAll(links=>links.map(a=>({label:a.textContent,path:a.dataset.link})));}
async function retainedIndex(page,filter){
  await openNavigation(page);
  return await page.locator('#index-tab').getAttribute('aria-pressed')==='true'&&await page.locator('#keyword-index').isVisible()&&!await page.locator('#contents').isVisible()&&await page.locator('#index-filter').inputValue()===filter;
}
async function selectedEntry(page){return page.locator('#index-entries a.index-selected').evaluateAll(links=>links.map(a=>({label:a.textContent,path:a.dataset.link})));}
async function navigationChecks(browser,build,origin){
  for(const protocol of ['http','file'])for(const width of [390,1280]){
    const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage(),topRequests=[];
    const label='Persistent shell '+protocol+' '+width+'px';
    page.on('pageerror',error=>errors.push(label+': '+error.message));
    page.on('request',request=>{if(request.isNavigationRequest()&&request.frame()===page.mainFrame())topRequests.push(request.url());});
    const initial=protocol==='http'?origin+build.url+'nested/alpha.htm':pathToFileURL(path.join(build.output,'nested/alpha.htm')).href;
    await page.goto(initial);await topicIs(page,'nested/alpha.htm','flare');
    await page.evaluate(()=>{window.shellProof={token:'persistent-help-reader',document,header:document.querySelector('header'),aside:document.querySelector('aside'),filter:document.getElementById('index-filter'),list:document.getElementById('index-entries'),article:document.getElementById('topic'),navigation:performance.getEntriesByType('navigation').length};});
    const requestBaseline=topRequests.length;
    async function unchanged(){return page.evaluate(()=>{const p=window.shellProof;return !!p&&p.token==='persistent-help-reader'&&p.document===document&&p.header===document.querySelector('header')&&p.aside===document.querySelector('aside')&&p.filter===document.getElementById('index-filter')&&p.list===document.getElementById('index-entries')&&p.article===document.getElementById('topic')&&p.navigation===performance.getEntriesByType('navigation').length;});}
    await openNavigation(page);await page.locator('#contents-tab').click();await page.locator('#contents a[data-link="deep/nested/gamma.htm"]').click();await topicIs(page,'deep/nested/gamma.htm','flare');
    record(label+' Contents changes only the article body, retaining the reader and navigation DOM',await unchanged()&&await page.locator('#help-topic-body h1').textContent()==='Artwork topic');
    record(label+' Contents makes no top-frame navigation request',topRequests.length===requestBaseline,topRequests.slice(requestBaseline));
    const image=await page.locator('#topic-image').evaluate(async img=>{await img.decode();return {width:img.naturalWidth,height:img.naturalHeight,url:img.currentSrc};});
    record(label+' rebases a nested topic image to the packaged artwork',image.width===77&&image.height===24&&image.url.endsWith('/resources/images/fixture.svg'),image);
    await page.waitForFunction(()=>!!document.querySelector('#help-topic-body mjx-container svg'));
    record(label+' typesets mathematics in the newly selected topic',await page.locator('#help-topic-body mjx-container svg path').count()>0);
    await page.locator('#help-topic-body details.rcode > summary').click();
    record(label+' preserves usable R-code dropdowns after updating the body',await page.locator('#help-topic-body details.rcode').evaluate(el=>el.open)&&await page.locator('#r-script').isVisible()&&(await page.locator('#r-script').textContent()).includes('mean(x)'));
    const historyBeforeAnchor=await page.evaluate(()=>history.length);
    await page.locator('#body-anchor').click();
    await page.waitForFunction(()=>location.hash.includes('note'));
    record(label+' follows a local body anchor without replacing the shell',await unchanged()&&await page.evaluate(()=>history.length)===historyBeforeAnchor+1&&await page.locator('#note').textContent()==='Anchored note.');
    await page.locator('#body-home').click();await topicIs(page,'contents.htm','flare');
    record(label+' resolves a nested relative body link within the same reader',await unchanged()&&await page.locator('#help-topic-body h1').textContent()==='Home');
    await index(page);await page.locator('#index-filter').fill('Duplicate');
    await page.locator('#index-entries a[data-link="other/beta.htm"]').click();await topicIs(page,'other/beta.htm','flare');
    record(label+' Index opens a topic without rebuilding the header, sidebar, filter or list',await unchanged()&&await retainedIndex(page,'Duplicate')&&(await selectedEntry(page)).some(entry=>entry.path==='other/beta.htm'));
    const afterIndex=topRequests.length;
    await page.goBack();await topicIs(page,'contents.htm','flare');
    record(label+' Back updates the article while retaining its reader and Index context',await unchanged()&&await retainedIndex(page,'Duplicate'));
    await page.goForward();await topicIs(page,'other/beta.htm','flare');
    record(label+' Forward updates the article while retaining its reader and Index context',await unchanged()&&await retainedIndex(page,'Duplicate')&&topRequests.length===afterIndex);
    await page.locator('#index-filter').fill('Scroll fixture');await page.locator('#index-filter').press('ArrowDown');await page.keyboard.press('End');
    const position=await page.locator('aside').evaluate(el=>el.scrollTop),selection=await selectedEntry(page);
    await page.keyboard.press('Enter');await topicIs(page,'contents.htm','flare');await openNavigation(page);
    record(label+' Index navigation retains its filter, exact row and list position without a reload',await unchanged()&&await retainedIndex(page,'Scroll fixture')&&JSON.stringify(await selectedEntry(page))===JSON.stringify(selection)&&Math.abs(await page.locator('aside').evaluate(el=>el.scrollTop)-position)<3);
    if(protocol==='http'&&width===1280){
      // Force the first topic request to complete after the following choice.
      // Implementations with an already-loaded local topic package still prove
      // the same last-choice-wins behaviour without issuing this request.
      await context.route('**/deep/nested/gamma.htm*',async route=>{await new Promise(resolve=>setTimeout(resolve,200));await route.continue();});
      await page.locator('#contents-tab').click();
      await page.evaluate(()=>{document.querySelector('#contents a[data-link="deep/nested/gamma.htm"]').click();document.querySelector('#contents a[data-link="nested/alpha.htm"]').click();});
      await topicIs(page,'nested/alpha.htm','flare');await page.waitForTimeout(300);
      record(label+' a slower earlier topic cannot overwrite the last choice',await unchanged()&&await page.locator('#help-topic-body h1').textContent()==='Alpha method'&&await page.evaluate(()=>document.documentElement.dataset.currentTopic)==='nested/alpha.htm');
      await page.locator('#body-next').click();await topicIs(page,'deep/nested/gamma.htm','flare');
      record(label+' follow-on topic-body links use the same persistent reader',await unchanged()&&await page.locator('#help-topic-body h1').textContent()==='Artwork topic');
      await page.evaluate(()=>StatsDirectHelp.openTopic('nested/alpha.htm'));await topicIs(page,'nested/alpha.htm','flare');
      await context.unroute('**/deep/nested/gamma.htm*');
      let releaseTopic,topicRequested;
      const heldTopic=new Promise(resolve=>{releaseTopic=resolve;}),requestedTopic=new Promise(resolve=>{topicRequested=resolve;});
      await context.route('**/deep/nested/gamma.htm*',async route=>{topicRequested();await heldTopic;try{await route.continue();}catch{/* Cancelling the pending loader may already have aborted its request. */}});
      await page.locator('#contents a[data-link="deep/nested/gamma.htm"]').click();await requestedTopic;
      await page.locator('#search').fill('quartzforecast');await page.locator('#search').press('Enter');await page.locator('#results a[data-link="nested/alpha.htm"]').waitFor();
      releaseTopic();await page.waitForTimeout(300);
      record(label+' a pending topic cannot replace newer search results',await unchanged()&&await page.locator('#results').isVisible()&&await page.title()==='Search'&&await page.evaluate(()=>document.documentElement.dataset.currentTopic)==='nested/alpha.htm');
    }
    record(label+' completes all topic changes without a top-frame document request',topRequests.length===requestBaseline,topRequests.slice(requestBaseline));
    if(protocol==='file'){
      await index(page);await page.locator('#index-filter').fill('Artwork');await page.locator('#index-filter').press('Enter');await topicIs(page,'deep/nested/gamma.htm','flare');
      if(width<1280){await openNavigation(page);await page.keyboard.press('Escape');}
      await page.locator('#search').fill('quartzforecast');await page.locator('#search').press('Enter');await page.locator('#results a[data-link="nested/alpha.htm"]').waitFor();
      await openNavigation(page);const tab=width<1280?'index':'contents';await page.locator('#'+tab+'-tab').click();
      record(label+' leaving Search restores the current topic before reloading',await unchanged()&&await page.locator('#topic').isVisible()&&await page.evaluate(()=>document.documentElement.dataset.currentTopic)==='deep/nested/gamma.htm');
      await page.reload();await topicIs(page,'deep/nested/gamma.htm','flare');await openNavigation(page);
      record(label+' Search to '+tab+' retains the file-routed topic across reload',await page.locator('#'+tab+'-tab').getAttribute('aria-pressed')==='true'&&await page.locator('#help-topic-body h1').textContent()==='Artwork topic'&&(tab!=='index'||await page.locator('#index-filter').inputValue()==='Artwork'));
    }
    await context.close();
  }
}
let browser,browserServer;
(async()=>{try{
  buildFixtures();await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin='http://127.0.0.1:'+server.address().port;
  browserServer=await chromium.launchServer({channel:'msedge',headless:true});
  browser=await chromium.connect(browserServer.wsEndpoint());
  for(const build of builds){
    const metadata=JSON.parse(fs.readFileSync(path.join(build.output,'keyword-index.json'),'utf8'));
    record(build.name+' emits authored keyword metadata including search-excluded pages',metadata.entries.some(e=>e.path==='private.htm')&&!metadata.entries.some(e=>e.term==='Unindexed title'));
    if(build.target==='Desktop'){
      const context=await browser.newContext({viewport:{width:1280,height:900}}),page=await context.newPage();
      await page.goto(origin+build.url+'contents.htm');await ready(page);
      record(build.name+' keeps duplicate web navigation controls out of the native reader',!await page.locator('#index-tab').isVisible()&&!await page.locator('#keyword-index').isVisible());
      await context.close();continue;
    }
    for(const width of [390,1280]){
      const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();
      page.on('pageerror',error=>errors.push(build.name+': '+error.message));
      const label=build.name+' '+width+'px';
      await page.goto(origin+build.url+'index.html');await ready(page);await openNavigation(page);
      record(label+' exposes only Contents and Index selectors',JSON.stringify(await page.locator('.tabs button').allTextContents())===JSON.stringify(['Contents','Index'])&&await page.locator('#results-tab').count()===0);
      const start=await snapshot(page);await index(page);
      record(label+' switches to Index without changing topic, URL or history',await sameDocument(page,start)&&await page.locator('#keyword-index').isVisible()&&!await page.locator('#contents').isVisible()&&!await page.locator('#results').isVisible());
      record(label+' omits the routine entry count',!await page.locator('#index-status').isVisible()||!(await page.locator('#index-status').textContent()).trim());
      const all=await entries(page),ordered=[...all].sort((a,b)=>a.label.localeCompare(b.label,'en',{sensitivity:'base'})||a.path.localeCompare(b.path,'en',{sensitivity:'base'}));
      record(label+' lists only authored keywords in alphabetical order',all.length===metadata.entries.length&&JSON.stringify(all)===JSON.stringify(ordered)&&!all.some(e=>e.label==='Unindexed title'),all);
      record(label+' keeps the Index controls within the navigation pane',await page.evaluate(()=>{const pane=document.querySelector('aside').getBoundingClientRect(),filter=document.getElementById('index-filter').getBoundingClientRect();return document.documentElement.scrollWidth<=innerWidth&&filter.width>70&&filter.left>=pane.left&&filter.right<=pane.right+1;}));
      record(label+' renders HTML-looking keywords as inert text',all.some(e=>e.label.includes('<img src=x onerror=alert(1)>'))&&await page.locator('#index-entries img, #index-entries script').count()===0);
      if(build.skin==='flare')await page.screenshot({path:path.join(evidence,'viewer-index-flare-'+width+'.png')});
      await page.locator('#index-filter').fill('CRAMER');
      record(label+' filters without requiring case or accents',await page.locator('#index-entries a[data-link]').count()===1&&(await entries(page))[0].label.includes('Cramér'));
      await page.locator('#index-filter').fill('Alpha method');
      record(label+' also filters by destination title',await page.locator('#index-entries a[data-link]').count()===2&&(await entries(page)).every(e=>e.path==='nested/alpha.htm'));
      await page.locator('#index-filter').fill('Duplicate');const duplicate=await entries(page);
      record(label+' disambiguates matching keyword and title by relative path',duplicate.length===2&&duplicate[0].label!==duplicate[1].label&&duplicate.every(e=>e.label.includes(e.path)),duplicate);
      await page.locator('#index-filter').fill('absentxyz');
      record(label+' shows a no-matches notice for an empty filter result',await page.locator('#index-entries a[data-link]').count()===0&&/no.+match/i.test(await page.locator('#index-status').textContent()));
      record(label+' keeps the no-matches notice visible',await page.locator('#index-status').isVisible());
      await page.locator('#index-filter').press('Enter');
      record(label+' Enter with no matches preserves the topic',await sameDocument(page,start));
      await page.locator('#index-filter').fill('Shared');await page.locator('#index-filter').press('ArrowDown');
      record(label+' changing keyboard selection does not navigate',await sameDocument(page,start));
      record(label+' ArrowDown moves from the filter to the first matching topic',await page.evaluate(()=>document.activeElement.matches('#index-entries a[data-link="nested/alpha.htm"]')));
      await page.keyboard.press('ArrowDown');
      record(label+' row arrows change selection without opening its topic',await sameDocument(page,start)&&await page.evaluate(()=>document.activeElement.matches('#index-entries a[data-link="contents.htm"]')));
      await page.keyboard.press('ArrowUp');await page.keyboard.press('Enter');await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' Enter on a keyboard-selected row opens the selected destination',await page.locator('#topic').isVisible());
      record(label+' keyboard activation retains Index and its filter',await retainedIndex(page,'Shared')&&(await selectedEntry(page)).some(e=>e.path==='nested/alpha.htm'));
      await page.reload();await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' reload restores Index mode, filter and selected destination',await retainedIndex(page,'Shared')&&(await selectedEntry(page)).some(e=>e.path==='nested/alpha.htm'));
      await page.locator('#index-filter').fill('Unsearchable keyword');await page.locator('#index-filter').press('Enter');await topicIs(page,'private.htm',build.skin);
      record(label+' Enter opens a keyword on a search-excluded topic in the same viewer',await page.locator('#topic').isVisible());
      record(label+' search-excluded keyword activation also retains Index and filter',await retainedIndex(page,'Unsearchable keyword'));
      await page.goBack();await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' Back retains the current Index filter while restoring the earlier topic',await retainedIndex(page,'Unsearchable keyword'));
      await page.goForward();await topicIs(page,'private.htm',build.skin);
      record(label+' Forward retains Index while restoring its destination topic',await retainedIndex(page,'Unsearchable keyword'));
      await page.locator('#index-filter').fill('Shared');
      await page.locator('#index-entries a[data-link="nested/alpha.htm"]').click();await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' clicking a keyword opens its destination and marks Contents',await page.locator('#contents a[aria-current="page"]').getAttribute('data-link')==='nested/alpha.htm');
      record(label+' mouse activation retains Index and its filter',await retainedIndex(page,'Shared'));
      await page.locator('#index-entries a[data-link="contents.htm"]').click();await topicIs(page,'contents.htm',build.skin);
      record(label+' selecting a later matching row retains that exact selection',await retainedIndex(page,'Shared')&&(await selectedEntry(page)).some(e=>e.path==='contents.htm'));
      await page.reload();await topicIs(page,'contents.htm',build.skin);
      record(label+' reload retains a later matching row instead of selecting the first',await retainedIndex(page,'Shared')&&(await selectedEntry(page)).some(e=>e.path==='contents.htm'));
      const retained=await snapshot(page);
      await page.locator('#contents-tab').click();
      record(label+' Contents replaces Index and retains the selected topic',await page.locator('#contents').isVisible()&&!await page.locator('#keyword-index').isVisible()&&await sameDocument(page,retained));
      await page.reload();await topicIs(page,'contents.htm',build.skin);await openNavigation(page);
      record(label+' an explicit Contents choice remains selected after reload',await page.locator('#contents-tab').getAttribute('aria-pressed')==='true'&&await page.locator('#contents').isVisible()&&!await page.locator('#keyword-index').isVisible());
      await page.locator('#index-tab').click();
      record(label+' reopening Index retains its filter',await page.locator('#index-filter').inputValue()==='Shared');
      if(width<1280&&build.skin==='flare')await page.keyboard.press('Escape');
      const query=page.locator('#search');await query.fill('quartzforecast');await query.press('Enter');await page.locator('#results a[data-link="nested/alpha.htm"]').waitFor();
      record(label+' full-text search still works through its form without a Search tab',await page.locator('#results').isVisible());
      await openNavigation(page);await page.locator('#index-tab').click();
      record(label+' Index remains usable after full-text search',await page.locator('#keyword-index').isVisible()&&!await page.locator('#results').isVisible());
      await page.locator('#contents-tab').click();
      record(label+' Contents restores the reading topic after search and Index',await page.locator('#topic').isVisible()&&await page.locator('#contents a[aria-current="page"]').getAttribute('data-link')==='contents.htm');
      await index(page);await page.locator('#index-filter').fill('Scroll fixture');await page.locator('#index-filter').press('ArrowDown');await page.keyboard.press('End');
      const scrollBefore=await page.locator('aside').evaluate(el=>el.scrollTop),selectedBefore=await selectedEntry(page);
      record(label+' scrolling to a later keyword does not navigate',selectedBefore.length===1&&selectedBefore[0].label==='Scroll fixture 60'&&scrollBefore>0);
      await page.reload();await topicIs(page,'contents.htm',build.skin);await openNavigation(page);
      const scrollAfter=await page.locator('aside').evaluate(el=>el.scrollTop);
      record(label+' reload preserves Index filter, exact row and scrolling position',await retainedIndex(page,'Scroll fixture')&&JSON.stringify(await selectedEntry(page))===JSON.stringify(selectedBefore)&&Math.abs(scrollAfter-scrollBefore)<3,{scrollBefore,scrollAfter,selectedBefore,selectedAfter:await selectedEntry(page)});
      if(width<1280&&build.skin==='flare'){
        await index(page);await page.locator('#index-filter').focus();const beforeEscape=await snapshot(page);await page.keyboard.press('Escape');
        record(label+' Escape closes the compact Index navigation without changing the topic',!await page.locator('body').evaluate(e=>e.classList.contains('navigation-open'))&&await sameDocument(page,beforeEscape)&&await page.evaluate(()=>document.activeElement.id==='navigation-toggle'));
      }
      await context.close();
    }
    // Direct file URLs must work without fetch/CORS or a server. Nested Flare
    // articles must resolve the generated metadata from the help root.
    const offline=await browser.newContext(),page=await offline.newPage();page.on('pageerror',error=>errors.push(build.name+' file://: '+error.message));
    const entry=build.skin==='flare'?'nested/alpha.htm':'index.html';
    await page.goto(pathToFileURL(path.join(build.output,entry)).href);await ready(page);await index(page);await page.locator('#index-filter').fill('Duplicate');
    record(build.name+' direct file:// '+entry+' loads the keyword index from its help root',await page.locator('#index-entries a[data-link]').count()===2);
    await page.locator('#index-entries a[data-link="other/beta.htm"]').click();await topicIs(page,'other/beta.htm',build.skin);
    record(build.name+' file:// keyword navigation stays in its local help tree',new URL(page.url()).protocol==='file:'&&await page.locator('#topic').isVisible());
    record(build.name+' file:// topic activation retains Index, filter and destination',await retainedIndex(page,'Duplicate')&&(await selectedEntry(page)).some(e=>e.path==='other/beta.htm'));
    await page.reload();await topicIs(page,'other/beta.htm',build.skin);
    record(build.name+' nested file:// reload restores the same Index context',await retainedIndex(page,'Duplicate')&&(await selectedEntry(page)).some(e=>e.path==='other/beta.htm'));
    await offline.close();
  }
  const publicBuilds=builds.filter(build=>build.target==='Public'),isolationContext=await browser.newContext({viewport:{width:1280,height:900}}),isolated=await isolationContext.newPage();
  await isolated.goto(origin+publicBuilds[0].url+'index.html');await ready(isolated);await index(isolated);await isolated.locator('#index-filter').fill('Duplicate');
  // Use the same browsing session: separate tabs normally get independent
  // sessionStorage and would not detect a key that accidentally ignores root.
  await isolated.goto(origin+publicBuilds[1].url+'index.html');await ready(isolated);
  record('A second help root does not inherit another root\'s Index mode or filter',await isolated.locator('#contents-tab').getAttribute('aria-pressed')==='true'&&await isolated.locator('#index-filter').inputValue()==='');
  await index(isolated);await isolated.locator('#index-filter').fill('Shared');
  await isolated.goto(origin+publicBuilds[0].url+'index.html');await ready(isolated);
  record('Returning to the first help root restores its own filter',await retainedIndex(isolated,'Duplicate'));
  await isolated.goto(origin+publicBuilds[1].url+'index.html');await ready(isolated);
  record('Returning to the second help root restores its separate filter',await retainedIndex(isolated,'Shared'));
  await isolationContext.close();
  for(const build of publicBuilds){
    const context=await browser.newContext({viewport:{width:1280,height:900}});
    await context.addInitScript(()=>Object.defineProperty(window,'sessionStorage',{get(){throw new DOMException('Storage disabled by test','SecurityError');}}));
    const page=await context.newPage();page.on('pageerror',error=>errors.push(build.name+' blocked storage: '+error.message));
    await page.goto(origin+build.url+'index.html');await ready(page);await index(page);await page.locator('#index-filter').fill('Cramer');
    record(build.name+' Index filtering works when session storage is blocked',await page.locator('#index-entries a[data-link]').count()===1);
    await page.locator('#index-filter').fill('Unsearchable keyword');await page.locator('#index-filter').press('Enter');await topicIs(page,'private.htm',build.skin);
    record(build.name+' Index activation works without persistent browser storage',await page.locator('#topic').isVisible());
    await openNavigation(page);await page.locator('#contents-tab').click();const query=page.locator('#search');await query.fill('quartzforecast');await query.press('Enter');await page.locator('#results a[data-link="nested/alpha.htm"]').waitFor();
    record(build.name+' full-text search also works with storage blocked',await page.locator('#results').isVisible());
    await context.close();
  }
  await navigationChecks(browser,publicBuilds.find(build=>build.skin==='flare'),origin);
  const build=builds.find(b=>b.skin==='flare'&&b.target==='Public');
  const corrupt=[
    ['missing metadata',null],['unsupported schema',{schemaVersion:2,entries:[]}],
    ['unsafe absolute target',{schemaVersion:1,entries:[{term:'Unsafe',title:'Unsafe',path:'https://example.com/'}]}],
    ['missing topic target',{schemaVersion:1,entries:[{term:'Missing',title:'Missing',path:'not-present.htm'}]}],
    ['malformed entry',{schemaVersion:1,entries:[{term:'Valid',title:'Home',path:'contents.htm'},{term:null,title:'Bad',path:'contents.htm'}]}]
  ];
  for(const [name,value]of corrupt){
    const context=await browser.newContext({viewport:{width:1280,height:900}}),page=await context.newPage();page.on('pageerror',error=>errors.push(name+': '+error.message));
    const original=fs.readFileSync(path.join(build.output,'keyword-index.js'),'utf8');
    const catalogue=original.slice(original.indexOf('window.StatsDirectTopics'));
    if(!catalogue.startsWith('window.StatsDirectTopics'))throw Error('Expected full topic catalogue in companion script');
    await context.route('**/keyword-index.js',route=>route.fulfill({contentType:'text/javascript',body:(value===null?'/* unavailable metadata */':'window.STATSDIRECT_HELP_KEYWORDS='+JSON.stringify(value)+';')+'\n'+catalogue}));
    await page.goto(origin+build.url+'nested/alpha.htm');await ready(page);const before=await snapshot(page);await index(page);
    const message=await page.locator('#index-status').textContent();
    record(name+' produces a visible Index status and no partial list',await page.locator('#index-status').isVisible()&&/index|keyword/i.test(message)&&/unavailable|not included|could not|invalid|unsupported|missing|failed/i.test(message)&&await page.locator('#index-entries a[data-link]').count()===0,message);
    record(name+' preserves the reading topic and history',await sameDocument(page,before));
    await page.locator('#contents-tab').click();record(name+' leaves Contents usable',await page.locator('#contents').isVisible());await context.close();
  }
  record('Index checks preserve every source fixture byte',sourceHashes.every(([file,expected])=>hash(file)===expected));
  record('Index checks have no unhandled browser errors',errors.length===0,errors);
}catch(error){record('Generated-reader Index checks completed',false,error.stack||String(error));}
finally{
  // The remote browser connection can outlive a close response on Windows.
  // Own its server explicitly so cleanup can terminate only this test browser.
  if(browser){let timer;await Promise.race([browser.close(),new Promise(resolve=>{timer=setTimeout(resolve,5000);})]);clearTimeout(timer);}
  if(browserServer)await browserServer.kill();server.closeAllConnections();server.close();
  const result={testedAt:new Date().toISOString(),fixture,passed:checks.every(c=>c.passed),checks,errors};
  fs.writeFileSync(path.join(evidence,'viewer-index-results.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({passed:result.passed,total:checks.length,failures:checks.filter(c=>!c.passed),fixture},null,2));if(!result.passed)process.exitCode=1;
}})();
