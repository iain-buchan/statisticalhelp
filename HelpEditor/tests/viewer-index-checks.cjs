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
    write(source,'Project/TOCs/STATSDIRECT.fltoc','<CatapultToc><TocEntry Title="Home" Link="/Content/contents.htm"/><TocEntry Title="Methods" Link="/Content/nested/alpha.htm"><TocEntry Title="Nested beta" Link="/Content/nested/beta.htm"/><TocEntry Title="Other beta" Link="/Content/other/beta.htm"/></TocEntry></CatapultToc>');
    write(source,'Content/contents.htm',pageSource('Home','<sdh:keyword term="Zulu; Shared; Cramér\'s V; &lt;img src=x onerror=alert(1)&gt;"/><p>Reader index fixture home.</p>'));
    write(source,'Content/nested/alpha.htm',pageSource('Alpha method','<sdh:keyword term="Shared; Alpha"/><p>Quartzforecast reader search fixture.</p>'));
    for(const name of ['nested/beta.htm','other/beta.htm'])write(source,'Content/'+name,pageSource('Same title','<sdh:keyword term="Duplicate"/><p>Separate authored keyword destination.</p>'));
    write(source,'Content/private.htm',pageSource('Private indexed topic','<sdh:keyword term="Unsearchable keyword"/><p>Privateprose search exclusion.</p>', 'sdh:searchable="false"'));
    write(source,'Content/unindexed.htm',pageSource('Unindexed title','<p>No authored keyword.</p>'));
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
  if(skin==='flare')await page.waitForURL(url=>url.pathname.endsWith('/'+name));
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
      record(label+' exposes Contents, Index and Search in navigation order',JSON.stringify(await page.locator('.tabs button').allTextContents())===JSON.stringify(['Contents','Index','Search']));
      const start=await snapshot(page);await index(page);
      record(label+' switches to Index without changing topic, URL or history',await sameDocument(page,start)&&await page.locator('#keyword-index').isVisible()&&!await page.locator('#contents').isVisible()&&!await page.locator('#results').isVisible());
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
      await page.locator('#index-filter').press('Enter');
      record(label+' Enter with no matches preserves the topic',await sameDocument(page,start));
      await page.locator('#index-filter').fill('Shared');await page.locator('#index-filter').press('ArrowDown');
      record(label+' changing keyboard selection does not navigate',await sameDocument(page,start));
      record(label+' ArrowDown moves from the filter to the first matching topic',await page.evaluate(()=>document.activeElement.matches('#index-entries a[data-link="nested/alpha.htm"]')));
      await page.keyboard.press('ArrowDown');
      record(label+' row arrows change selection without opening its topic',await sameDocument(page,start)&&await page.evaluate(()=>document.activeElement.matches('#index-entries a[data-link="contents.htm"]')));
      await page.keyboard.press('ArrowUp');await page.keyboard.press('Enter');await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' Enter on a keyboard-selected row opens the selected destination',await page.locator('#topic').isVisible());
      await index(page);
      await page.locator('#index-filter').fill('Unsearchable keyword');await page.locator('#index-filter').press('Enter');await topicIs(page,'private.htm',build.skin);
      record(label+' Enter opens a keyword on a search-excluded topic in the same viewer',await page.locator('#topic').isVisible());
      await index(page);await page.locator('#index-filter').fill('Shared');
      await page.locator('#index-entries a[data-link="nested/alpha.htm"]').click();await topicIs(page,'nested/alpha.htm',build.skin);
      record(label+' clicking a keyword opens its destination and marks Contents',await page.locator('#contents a[aria-current="page"]').getAttribute('data-link')==='nested/alpha.htm');
      await index(page);await page.locator('#index-filter').fill('Shared');const retained=await snapshot(page);
      await page.locator('#contents-tab').click();
      record(label+' Contents replaces Index and retains the selected topic',await page.locator('#contents').isVisible()&&!await page.locator('#keyword-index').isVisible()&&await sameDocument(page,retained));
      await page.locator('#index-tab').click();
      record(label+' reopening Index retains its filter',await page.locator('#index-filter').inputValue()==='Shared');
      await page.locator('#results-tab').click();
      record(label+' Search replaces Index without navigation',!await page.locator('#keyword-index').isVisible()&&await page.locator('#results').isVisible()&&await sameDocument(page,retained));
      if(width<1280&&build.skin==='flare')record(label+' Search closes the drawer and exposes its search field',await page.evaluate(()=>{const box=document.getElementById('search').getBoundingClientRect();return !document.body.classList.contains('navigation-open')&&box.width>0&&box.left>=0&&box.right<=innerWidth&&box.top>=0&&box.bottom<=innerHeight;}));
      const query=page.locator('#search');await query.fill('quartzforecast');await query.press('Enter');await page.locator('#results a[data-link="nested/alpha.htm"]').waitFor();
      await openNavigation(page);await page.locator('#index-tab').click();
      record(label+' Index remains usable after full-text search',await page.locator('#keyword-index').isVisible()&&!await page.locator('#results').isVisible());
      await page.locator('#contents-tab').click();
      record(label+' Contents restores the reading topic after search and Index',await page.locator('#topic').isVisible()&&await page.locator('#contents a[aria-current="page"]').getAttribute('data-link')==='nested/alpha.htm');
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
    await offline.close();
  }
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
    record(name+' produces a visible Index status and no partial list',/index|keyword/i.test(message)&&/unavailable|not included|could not|invalid|unsupported|missing|failed/i.test(message)&&await page.locator('#index-entries a[data-link]').count()===0,message);
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
