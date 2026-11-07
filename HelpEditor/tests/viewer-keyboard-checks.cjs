const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {spawnSync}=require('node:child_process');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),evidence=path.join(root,'evidence'),checks=[],errors=[];
fs.mkdirSync(evidence,{recursive:true});
const fixture=fs.mkdtempSync(path.join(evidence,'ReaderKeyboardFixture-'));
function record(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);}
function write(base,name,text){const file=path.join(base,name);fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,text);}
function run(args){const result=spawnSync('dotnet',args,{cwd:root,encoding:'utf8',windowsHide:true});if(result.error||result.status!==0)throw Error('dotnet '+args.join(' ')+'\n'+(result.error||result.stderr||result.stdout));}
function sourceHashes(base){return fs.readdirSync(base,{withFileTypes:true}).flatMap(entry=>{const file=path.join(base,entry.name);return entry.isDirectory()?sourceHashes(file):[[file,crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')]];});}
const nativeToolbarCss=`
:root[data-help-skin]:has(> body > header > .help-identity){--help-height:0px !important}
html[data-help-skin] > body > header:has(> .help-identity),
html[data-help-skin] > body > main > aside > #search-form{display:none !important}`;
const builds=[],sources=[];
function buildFixtures(){
  run(['build','src/StatsDirectHelpEditor.Cli/StatsDirectHelpEditor.Cli.csproj','-c','Release','--nologo']);
  const cli=path.join(root,'src/StatsDirectHelpEditor.Cli/bin/Release/net10.0/StatsDirectHelpBuilder.dll');
  for(const skin of ['flare','readable']){
    const source=path.join(fixture,skin,'Source');
    write(source,'.statsdirect-help-editor.json','{}');
    write(source,'Project/TOCs/STATSDIRECT.fltoc','<CatapultToc><TocEntry Title="Home" Link="/Content/contents.htm"/><TocEntry Title="Section" Link="/Content/nested/section.htm"><TocEntry Title="Topic" Link="/Content/nested/topic.htm"/></TocEntry></CatapultToc>');
    for(const [name,title]of [['contents.htm','Home'],['nested/section.htm','Section'],['nested/topic.htm','Topic']])
      write(source,'Content/'+name,'<html><head><title>'+title+'</title></head><body><h1>'+title+'</h1><p>Reader keyboard fixture.</p></body></html>');
    if(skin!=='flare')write(source,'Project/HelpEditorDesign.json',JSON.stringify({schemaVersion:1,appearance:{desktop:skin,public:skin}}));
    sources.push(...sourceHashes(source));
    for(const target of skin==='flare'?['Desktop','Public']:['Desktop']){
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
async function state(page){return page.evaluate(()=>({url:location.href,history:history.length,active:document.activeElement?.id,open:document.body.classList.contains('navigation-open'),expanded:document.getElementById('navigation-toggle')?.getAttribute('aria-expanded'),topicVisible:document.getElementById('topic').getClientRects().length>0,topicTabIndex:document.getElementById('topic').getAttribute('tabindex')}));}
let browser;
(async()=>{try{
  buildFixtures();await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const origin='http://127.0.0.1:'+server.address().port;
  browser=await chromium.launch({channel:'msedge',headless:true});
  for(const build of builds)for(const width of [390,1280])for(const native of [false,true]){
    const context=await browser.newContext({viewport:{width,height:900}}),page=await context.newPage();
    page.on('pageerror',error=>errors.push(build.name+': '+error.message));
    const label=build.name+' '+width+'px '+(native?'native hidden header':'ordinary reader');
    await page.goto(origin+build.url+(build.skin==='flare'?'nested/topic.htm':'index.html#topic=nested%2Ftopic.htm'));
    await page.waitForFunction(()=>!!window.StatsDirectHelp&&!!document.querySelector('#contents a[aria-current="page"]'));
    if(native)await page.addStyleTag({content:nativeToolbarCss});
    const menu=page.locator('#navigation-toggle'),hasMenu=await menu.count()>0;
    const menuVisible=hasMenu&&await menu.isVisible();
    const initial=await state(page);
    await page.evaluate(()=>{
      const navigation=document.getElementById('help-navigation');
      if(navigation&&getComputedStyle(navigation).display==='none')document.getElementById('navigation-toggle').click();
      document.querySelector('#contents a[aria-current="page"]').focus();
    });
    const wasOpen=(await state(page)).open;
    record(label+' exercises a focused Contents link',await page.evaluate(()=>document.activeElement===document.querySelector('#contents a[aria-current="page"]')));
    if(width===1280)record(label+' starts with the wide tree visible without navigation-open',!wasOpen);
    await page.keyboard.press('Escape');const after=await state(page);
    const expected=menuVisible?'navigation-toggle':'topic';
    record(label+' Escape returns focus to '+expected,after.active===expected&&after.topicVisible,after);
    record(label+' Escape closes the transient tree without navigation or history changes',!after.open&&after.url===initial.url&&after.history===initial.history&&(!hasMenu||after.expanded==='false'),after);
    if(expected==='topic'&&build.skin==='flare')record(label+' topic receives programmatic focus without becoming a Tab stop',after.topicTabIndex==='-1');
    // Escape outside the tree should not steal focus after the panel has closed.
    await page.evaluate(()=>{const button=document.createElement('button');button.id='outside-control';button.textContent='Outside';document.getElementById('topic').after(button);button.focus();});
    await page.keyboard.press('Escape');
    record(label+' Escape outside navigation preserves the current control',await page.evaluate(()=>document.activeElement.id==='outside-control'));
    await context.close();
  }
  record('Keyboard checks preserve every source fixture byte',sources.every(([file,hash])=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')===hash));
  record('Keyboard checks have no unhandled browser errors',errors.length===0,errors);
}catch(error){record('Generated-reader keyboard checks completed',false,error.stack||String(error));}
finally{
  if(browser)await browser.close();server.close();
  const result={testedAt:new Date().toISOString(),fixture,passed:checks.every(check=>check.passed),checks,errors};
  fs.writeFileSync(path.join(evidence,'viewer-keyboard-results.json'),JSON.stringify(result,null,2));
  console.log(JSON.stringify({passed:result.passed,total:checks.length,failures:checks.filter(check=>!check.passed),fixture},null,2));
  if(!result.passed)process.exitCode=1;
}})();
