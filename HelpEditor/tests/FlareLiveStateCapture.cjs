// Read-only capture of live expanded navigation and search state for shell comparison.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('./browser-runtime.cjs');
const evidence=path.resolve(__dirname,'../evidence/FlareLiveBaseline');
const inspect=()=>{
 const describe=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return{tag:e.tagName,id:e.id,class:e.className,text:e.textContent.trim().slice(0,150),x:r.x,y:r.y,width:r.width,height:r.height,font:s.font,color:s.color,background:s.backgroundColor,padding:s.padding,margin:s.margin,border:s.border};};
 return{url:location.href,viewport:{width:innerWidth,height:innerHeight},elements:Array.from(document.querySelectorAll('.title-bar,.search-field,.sidenav-wrapper,.sidenav a,.off-canvas-wrapper,.off-canvas-list a,#search-results,#search-results-flex,#searchPane,#resultList,#resultList>li,#resultList h3,#resultList h3>a,#resultList .description,#resultList .url,#pagination,#pagination>a,h1,h2,.body-container')).filter(e=>{const r=e.getBoundingClientRect();return r.width>0&&r.height>0;}).slice(0,175).map(describe),text:document.body.innerText};
};
(async()=>{const browser=await chromium.launch({channel:'msedge',headless:true});const page=await browser.newPage({viewport:{width:1280,height:900}});const states=[],errors=[];page.on('pageerror',e=>errors.push(e.message));try{
 await page.goto('https://statsdirect.com/help/',{waitUntil:'load'});await page.waitForTimeout(1000);
 await page.locator('.sidenav li[data-mc-id="1"] .submenu-toggle-container').click();await page.waitForTimeout(600);
 await page.screenshot({path:path.join(evidence,'live-1280-expanded.png')});fs.writeFileSync(path.join(evidence,'live-1280-expanded.html'),await page.content());states.push({name:'expanded',...await page.evaluate(inspect)});
 await page.locator('.search-field:visible').fill('paired t');await page.locator('.search-field:visible').press('Enter');await page.waitForTimeout(1800);
 await page.screenshot({path:path.join(evidence,'live-1280-search.png')});fs.writeFileSync(path.join(evidence,'live-1280-search.html'),await page.content());states.push({name:'search',...await page.evaluate(inspect)});
 await page.setViewportSize({width:390,height:844});await page.waitForTimeout(500);await page.screenshot({path:path.join(evidence,'live-390-search.png')});states.push({name:'mobileSearch',...await page.evaluate(inspect)});
 await page.setViewportSize({width:390,height:844});await page.goto('https://statsdirect.com/help/',{waitUntil:'load'});await page.waitForTimeout(1000);await page.locator('.menu-icon:visible').click();await page.waitForTimeout(500);
 await page.screenshot({path:path.join(evidence,'live-390-expanded.png')});fs.writeFileSync(path.join(evidence,'live-390-expanded.html'),await page.content());states.push({name:'mobileExpanded',...await page.evaluate(inspect)});
 }finally{await browser.close();fs.writeFileSync(path.join(evidence,'states.json'),JSON.stringify({checkedAtUtc:new Date().toISOString(),states,errors},null,2));console.log(JSON.stringify({states:states.map(s=>({name:s.name,url:s.url,elements:s.elements.length})),errors},null,2));}
})().catch(e=>{console.error(e);process.exitCode=1;});
