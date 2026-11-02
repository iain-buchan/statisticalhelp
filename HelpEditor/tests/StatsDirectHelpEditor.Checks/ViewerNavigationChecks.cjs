const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const{chromium}=require('../browser-runtime.cjs');
const root=path.resolve(__dirname,'../..'),build=path.join(root,'Build','Desktop'),evidence=path.join(root,'evidence');
const checks=[];function check(name,passed,detail){checks.push({name,passed:!!passed,...(detail?{detail}:{})});if(!passed)throw new Error(name);}
const server=http.createServer((req,res)=>{const u=new URL(req.url,'http://localhost'),p=path.resolve(build,'.'+decodeURIComponent(u.pathname));if(!p.startsWith(build+path.sep)){res.statusCode=403;res.end();return;}try{res.setHeader('Content-Type',({'.html':'text/html','.htm':'text/html','.js':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml'})[path.extname(p)]||'application/octet-stream');res.end(fs.readFileSync(p));}catch{res.statusCode=404;res.end();}});
(async()=>{await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;try{
 browser=await chromium.launch({channel:process.env.STATSDIRECT_BROWSER_CHANNEL||'msedge',headless:true});const page=await browser.newPage({viewport:{width:1200,height:850}}),errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.goto(`http://127.0.0.1:${server.address().port}/index.html#searchQuery=incidence`);await page.waitForFunction(()=>window.StatsDirectHelp&&document.getElementById('search').value==='incidence'&&!document.getElementById('results').hidden);
 check('Initial help-search fragment opens matching results',await page.locator('#results').innerText().then(t=>t.includes('Incidence and Prevalence')));
 await page.evaluate(()=>location.hash='searchQuery=box%20whisker');await page.waitForFunction(()=>document.getElementById('search').value==='box whisker');
 check('A second same-document host search updates results',await page.locator('#results').innerText().then(t=>t.includes('Box and Whisker')));
 await page.fill('#search','paired t test');await page.locator('#search').press('Enter');await page.waitForFunction(()=>new URLSearchParams(location.hash.slice(1)).get('searchQuery')==='paired t test'&&document.getElementById('results').textContent.includes('Paired Student t Test'));
 check('Submitting search updates the shareable help fragment',await page.locator('#results').innerText().then(t=>t.includes('Paired Student t Test')));
 await page.goBack();await page.waitForFunction(()=>document.getElementById('search').value==='box whisker');
 check('Browser Back restores the previous query and results',await page.locator('#results').innerText().then(t=>t.includes('Box and Whisker')));
 await page.goForward();await page.waitForFunction(()=>document.getElementById('search').value==='paired t test');
 check('Browser Forward restores the newer query',await page.locator('#results').innerText().then(t=>t.includes('Paired Student t Test')));
 await page.click('a.help-identity');await page.waitForFunction(()=>!document.getElementById('contents').hidden&&!new URLSearchParams(location.hash.slice(1)).has('searchQuery'));
 await page.evaluate(()=>location.hash='searchQuery=paired%20t%20test');await page.waitForFunction(()=>!document.getElementById('results').hidden);
 check('Repeating the same host query after viewing Contents reopens search results',await page.locator('#results').innerText().then(t=>t.includes('Paired Student t Test')));
 await page.fill('#search','café α & β');await page.locator('#search').press('Enter');await page.waitForFunction(()=>new URLSearchParams(location.hash.slice(1)).get('searchQuery')==='café α & β');
 check('Search fragments preserve Unicode and ampersands',await page.inputValue('#search')==='café α & β');
 check('Navigation raises no browser script errors',errors.length===0,errors);
 await page.screenshot({path:path.join(evidence,'viewer-search-navigation.png')});
 }catch(error){checks.push({name:'Viewer navigation run completed',passed:false,error:error.stack});process.exitCode=1;}
 finally{if(browser)await browser.close();server.close();const result={passed:checks.every(c=>c.passed),checks};fs.writeFileSync(path.join(evidence,'viewer-navigation-checks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));}})();
