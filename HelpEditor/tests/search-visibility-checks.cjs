// Exercise the generated, file-based reader rather than the builder's index objects.
const fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),buildRoot=process.env.STATSDIRECT_HELP_BUILD_ROOT||path.join(root,'Build');
const checks=[],external=[];
function check(target,name,passed,detail){checks.push({target,name,passed:!!passed,detail});if(!passed)throw new Error(target+': '+name);}
(async()=>{let browser;try{
 browser=await chromium.launch({channel:'msedge',headless:true});
 const context=await browser.newContext();
 await context.route('**/*',route=>{const u=route.request().url();if(/^(file:|data:|blob:|about:blank)/.test(u))return route.continue();external.push(u);return route.abort();});
 for(const target of ['Desktop','Public']){
  const folder=path.join(buildRoot,target),page=await context.newPage(),errors=[];
  page.on('pageerror',error=>errors.push(error.message));
  const entry=pathToFileURL(path.join(folder,'index.html')).href;
  await page.goto(entry,{waitUntil:'load'});
  await page.waitForFunction(()=>window.StatsDirectHelp&&window.StatsDirectSearch);
  const index=await page.evaluate(()=>({count:StatsDirectSearch.documents.length,urls:StatsDirectSearch.documents.map(d=>d.url),about:StatsDirectHelp.search('About StatsDirect').some(d=>d.url==='menus/about.htm')}));
  check(target,'The generated reader indexes 403 unique topics and excludes the opt-out page',index.count===403&&new Set(index.urls).size===403&&!index.urls.includes('menus/about.htm')&&!index.about,{count:index.count});
  const desktop=target==='Desktop';
  check(target,'Only Public shows the website search form',await page.locator('#search-form').isVisible()===!desktop);
  if(desktop)await page.goto(entry+'#'+new URLSearchParams({searchQuery:'paired t test'}));
  else {await page.fill('#search','paired t test');await page.locator('#search').press('Enter');}
  await page.locator('#results a[data-link]').first().waitFor();
  const result=await page.locator('#results a[data-link]').first().evaluate(a=>({url:a.dataset.link,title:a.textContent}));
  check(target,'A normal search still finds the paired t-test topic',/paired.*t.test/i.test(result.title)&&index.urls.includes(result.url),result);
  await page.locator('#results a[data-link]').first().click();
  // Public file readers retain their document and route through #topic when
  // file:// history cannot change the pathname. Wait for the selected body.
  await page.waitForFunction(topic=>document.documentElement.dataset.currentTopic===topic&&
    !!document.querySelector('#help-topic-body h1')&&!document.querySelector('#topic[aria-busy="true"]'),result.url);
  const heading=page.locator('#help-topic-body h1');
  await heading.waitFor();
  const currentTopic=await page.locator('html').getAttribute('data-current-topic');
  const selectedUrl=new URL(page.url()),routedTopic=new URLSearchParams(selectedUrl.hash.slice(1)).get('topic');
  const correctRoute=routedTopic?routedTopic===result.url:decodeURIComponent(selectedUrl.pathname).endsWith('/'+result.url);
  const headingText=await heading.innerText();
  check(target,'Search opens the correct topic after index IDs change',currentTopic===result.url&&correctRoute&&await heading.count()===1&&/paired.*t.test/i.test(headingText),
    {expectedTopic:result.url,currentTopic,headingText,url:page.url(),routedTopic});
  await page.goto(pathToFileURL(path.join(folder,'menus/about.htm')).href,{waitUntil:'load'});
  await page.locator('#help-topic-body').waitFor();
  check(target,'The excluded topic is still a readable help page',await page.locator('#help-topic-body').innerText().then(text=>text.length>100));
  check(target,'The reader raises no JavaScript errors',errors.length===0,errors);
  await page.close();
 }
 check('Both','The complete reader smoke test stays offline',external.length===0,external);
}catch(error){checks.push({name:'Generated search visibility run completed',passed:false,error:error.stack});process.exitCode=1;}
finally{if(browser)await browser.close();const report={checkedAtUtc:new Date().toISOString(),buildRoot,passed:checks.every(c=>c.passed),checks};fs.writeFileSync(path.join(root,'evidence','search-visibility-checks.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));}})();
