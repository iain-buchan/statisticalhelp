const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),oldRoot='C:/Develop/SD3Help/StatsDirect/Output/ieb21';
const definitions=[['DesktopHelp','Desktop'],['Website','Public'],['GitHub','Public']];
const referenceLinkCorrections=JSON.parse(fs.readFileSync(path.join(root,'evidence/reference-link-corrections.json'),'utf8'));
if(!Array.isArray(referenceLinkCorrections))throw new Error('Reference link corrections must be an array');
const correctionKeys=new Set(),newCorrectionKeys=new Set(),replacementKeys=new Set();
for(const [index,correction]of referenceLinkCorrections.entries()){
 if(!correction||typeof correction!=='object'||Array.isArray(correction)||['referenceId','oldUrl','newUrl','reason'].some(key=>typeof correction[key]!=='string'||!correction[key].trim())||!Array.isArray(correction.evidence)||!correction.evidence.length||correction.evidence.some(value=>typeof value!=='string'||!value.trim()))throw new Error(`Invalid reference link correction at index ${index}`);
 for(const field of ['oldLinkText','newLinkText'])if(Object.hasOwn(correction,field)&&(typeof correction[field]!=='string'||!correction[field].trim()))throw new Error(`Invalid ${field} for reference link correction: ${correction.referenceId}`);
 if(Object.hasOwn(correction,'textReplacements')&&!Array.isArray(correction.textReplacements))throw new Error(`Invalid textReplacements for reference link correction: ${correction.referenceId}`);
 for(const replacement of correction.textReplacements||[]){
  if(!replacement||typeof replacement!=='object'||Array.isArray(replacement)||typeof replacement.oldText!=='string'||!replacement.oldText.trim()||typeof replacement.newText!=='string'||!replacement.newText.trim()||replacement.oldText===replacement.newText)throw new Error(`Invalid paragraph text replacement for reference link correction: ${correction.referenceId}`);
  const replacementKey=JSON.stringify([correction.referenceId,replacement.oldText]);if(replacementKeys.has(replacementKey))throw new Error(`Duplicate paragraph text replacement: ${correction.referenceId} ${replacement.oldText}`);replacementKeys.add(replacementKey);
 }
 const oldLinkText=correction.oldLinkText??correction.oldUrl,newLinkText=correction.newLinkText??correction.newUrl;
 if(correction.oldUrl===correction.newUrl&&oldLinkText===newLinkText&&!correction.textReplacements?.length)throw new Error(`Reference link correction has no URL, display text or paragraph text change: ${correction.referenceId}`);
 const key=JSON.stringify([correction.referenceId,correction.oldUrl]);if(correctionKeys.has(key))throw new Error(`Duplicate reference link correction: ${correction.referenceId} ${correction.oldUrl}`);correctionKeys.add(key);
 const newKey=JSON.stringify([correction.referenceId,correction.newUrl]);if(newCorrectionKeys.has(newKey))throw new Error(`Duplicate reference link correction destination: ${correction.referenceId} ${correction.newUrl}`);newCorrectionKeys.add(newKey);
}
const sourceContent=path.join(root,'HelpSource/Content');
const files=[];function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const p=path.join(dir,e.name);if(e.isDirectory())walk(p);else if(e.name.endsWith('.htm'))files.push(path.relative(sourceContent,p).replaceAll('\\','/'));}}walk(sourceContent);
const norm=s=>s.replaceAll('\r\n','\n');
const mime={'.html':'text/html','.htm':'text/html','.js':'text/javascript','.css':'text/css','.svg':'image/svg+xml','.png':'image/png','.gif':'image/gif','.jpg':'image/jpeg','.woff':'font/woff','.woff2':'font/woff2','.ttf':'font/ttf'};
const server=http.createServer((req,res)=>{const u=new URL(req.url,'http://localhost');const bits=decodeURIComponent(u.pathname).split('/').filter(Boolean);const kind=bits.shift(),target=bits.shift();const base=kind==='old'?path.resolve(oldRoot,target||''):path.resolve(root,'Build',target||'');const p=path.resolve(base,bits.join('/'));if(!p.startsWith(base+path.sep)){res.statusCode=403;res.end();return;}try{res.setHeader('Content-Type',(mime[path.extname(p)]||'application/octet-stream')+';charset=utf-8');res.end(fs.readFileSync(p));}catch{res.statusCode=404;res.end();}});
(async()=>{await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;try{
 browser=await chromium.launch({channel:'msedge',headless:true});const page=await browser.newPage();const comparisons=[];
 const compareBatch=inputs=>inputs.map(item=>{
  const parse=s=>new DOMParser().parseFromString(s,'text/html');const old=parse(item.oldHtml),fresh=parse(item.newHtml);const oldMain=old.querySelector('#mc-main-content')||old.body,newMain=fresh.querySelector('#help-topic-body')||fresh.body;
  const cleanText=main=>{const copy=main.cloneNode(true);copy.querySelectorAll('script,style,.help-topic-nav,.help-equation,.help-shared-footer').forEach(e=>e.remove());copy.querySelectorAll('p,h1,h2,h3,pre,li,div,details,summary,br,table,td,tr').forEach(e=>e.append(document.createTextNode(' ')));return copy.textContent.replace(/\s+/g,' ').trim();};
  const toUrl=v=>{try{const u=new URL(v,'https://help.invalid/'+item.url);return u.host==='help.invalid'?u.pathname.replace(/^\//,'').toLowerCase()+u.search+u.hash:u.href;}catch{return v;}};
  const images=main=>[...main.querySelectorAll('img')].filter(e=>!e.closest('.help-equation')).map(e=>e.getAttribute('src')).filter(s=>s&&!/generatedimages\/equations|Skins\//i.test(s)).map(toUrl);
  const links=main=>[...main.querySelectorAll('a[href]')].filter(e=>!e.closest('.help-topic-nav,.help-shared-footer')).map(e=>e.getAttribute('href')).filter(s=>s&&s!=='#'&&!s.startsWith('javascript:')).map(toUrl);
  const anchors=main=>[...main.querySelectorAll('[id],a[name]')].flatMap(e=>[e.id,e.getAttribute('name')]).filter(Boolean).filter(s=>!/^kanchor|^MCDropDown|^mc-/i.test(s));
  const pre=main=>[...main.querySelectorAll('pre')].map(e=>e.textContent.replaceAll('\r\n','\n'));
  const rawTextSame=cleanText(oldMain)===cleanText(newMain),rawLinksSame=JSON.stringify(links(oldMain))===JSON.stringify(links(newMain));
  const bibliographyAnchorsSame=item.url!=='references/reference_list.htm'||JSON.stringify(anchors(oldMain))===JSON.stringify(anchors(newMain));
  const recognizedReferenceCorrections=[],referenceCorrectionErrors=[],referenceCorrectionPlans=[],paragraphTextPlans=[];
  const paragraphTextMatches=(paragraph,text)=>{
   const matches=[];if(!paragraph)return matches;
   const walker=document.createTreeWalker(paragraph,NodeFilter.SHOW_TEXT);let node;
   while((node=walker.nextNode())){
    if(node.parentElement?.closest('a'))continue;
    let at=-1;while((at=node.data.indexOf(text,at+1))!==-1)matches.push({node,at});
   }
   return matches;
  };
  for(const correction of item.referenceCorrections){
   const referenceAnchors=main=>[...main.querySelectorAll('a[id],a[name]')].filter(e=>e.id===correction.referenceId||e.getAttribute('name')===correction.referenceId);
   const oldAnchors=referenceAnchors(oldMain),newAnchors=referenceAnchors(newMain);
   if(oldAnchors.length!==1||newAnchors.length!==1){referenceCorrectionErrors.push({referenceId:correction.referenceId,reason:'Expected one reference anchor in each version',oldCount:oldAnchors.length,newCount:newAnchors.length});continue;}
   const oldParagraph=oldAnchors[0].closest('p'),newParagraph=newAnchors[0].closest('p');
   const matchingLinks=(paragraph,url)=>paragraph?[...paragraph.querySelectorAll('a[href]')].filter(e=>e.getAttribute('href')===url):[];
   const oldLinks=matchingLinks(oldParagraph,correction.oldUrl),newLinks=matchingLinks(newParagraph,correction.newUrl);
   const oldLinkText=correction.oldLinkText??correction.oldUrl,newLinkText=correction.newLinkText??correction.newUrl,textReplacements=correction.textReplacements||[];
   if(oldLinks.length!==1||newLinks.length!==1||oldLinks[0].textContent!==oldLinkText||newLinks[0].textContent!==newLinkText){referenceCorrectionErrors.push({referenceId:correction.referenceId,reason:'Expected exact old/new URL href and display text within the reference paragraph',oldUrl:correction.oldUrl,newUrl:correction.newUrl,oldLinkText,newLinkText,oldCount:oldLinks.length,newCount:newLinks.length});continue;}
   if(referenceCorrectionPlans.some(plan=>plan.oldLink===oldLinks[0]||plan.newLink===newLinks[0])){referenceCorrectionErrors.push({referenceId:correction.referenceId,reason:'A hyperlink is matched by more than one reference correction'});continue;}
   const textPlans=[];let invalidReplacement=false;
   for(const replacement of textReplacements){
    const oldMatches=paragraphTextMatches(oldParagraph,replacement.oldText),newMatches=paragraphTextMatches(newParagraph,replacement.newText);
    if(oldMatches.length!==1||newMatches.length!==1){referenceCorrectionErrors.push({referenceId:correction.referenceId,reason:'Expected one exact old/new paragraph text occurrence in a single text node outside anchors',...replacement,oldCount:oldMatches.length,newCount:newMatches.length});invalidReplacement=true;continue;}
    const match=oldMatches[0],end=match.at+replacement.oldText.length;
    if([...paragraphTextPlans,...textPlans].some(plan=>plan.node===match.node&&match.at<plan.end&&plan.at<end)){referenceCorrectionErrors.push({referenceId:correction.referenceId,reason:'Overlapping paragraph text replacements',...replacement});invalidReplacement=true;continue;}
    textPlans.push({...match,end,newText:replacement.newText});
   }
   if(invalidReplacement)continue;
   paragraphTextPlans.push(...textPlans);referenceCorrectionPlans.push({correction,oldLink:oldLinks[0],newLink:newLinks[0],oldLinkText,newLinkText,textReplacements});
  }
  // Normalize only verified old DOM nodes; the fresh document and every other difference remain untouched.
  const plansByNode=new Map();for(const plan of paragraphTextPlans){if(!plansByNode.has(plan.node))plansByNode.set(plan.node,[]);plansByNode.get(plan.node).push(plan);}
  for(const [node,plans]of plansByNode){let text=node.data;for(const plan of plans.sort((a,b)=>b.at-a.at))text=text.slice(0,plan.at)+plan.newText+text.slice(plan.end);node.data=text;}
  for(const {correction,oldLink,oldLinkText,newLinkText,textReplacements}of referenceCorrectionPlans){
   oldLink.setAttribute('href',correction.newUrl);oldLink.textContent=newLinkText;recognizedReferenceCorrections.push({...correction,oldLinkText,newLinkText,textReplacements});
  }
  const a=cleanText(oldMain),b=cleanText(newMain);let at=0;while(at<Math.min(a.length,b.length)&&a[at]===b[at])at++;
  const oldFooter=[...old.querySelectorAll('p')].find(p=>p.textContent.trim().startsWith('Copyright ©')),newFooter=fresh.querySelector('.help-shared-footer');
  const footerText=e=>e?.textContent.replace(/\s+/g,' ').trim()||'';
  const footerLinks=e=>e?[...e.querySelectorAll('a[href]')].map(a=>toUrl(a.getAttribute('href'))):[];
  return {url:item.url,rawTextSame,rawLinksSame,recognizedReferenceCorrections,referenceCorrectionErrors,bibliographyAnchorsSame,titleSame:old.title.replace(/\s+/g,' ').trim()===fresh.title.replace(/\s+/g,' ').trim(),textSame:a===b,textDifference:a===b?null:{at,old:a.slice(Math.max(0,at-50),at+180),new:b.slice(Math.max(0,at-50),at+180)},rCodeSame:JSON.stringify(pre(oldMain))===JSON.stringify(pre(newMain)),imagesSame:JSON.stringify(images(oldMain))===JSON.stringify(images(newMain)),imageDifference:JSON.stringify(images(oldMain))===JSON.stringify(images(newMain))?null:{old:images(oldMain),new:images(newMain)},linksSame:JSON.stringify(links(oldMain))===JSON.stringify(links(newMain)),linkDifference:JSON.stringify(links(oldMain))===JSON.stringify(links(newMain))?null:{old:links(oldMain),new:links(newMain)},anchorsPreserved:anchors(oldMain).every(a=>anchors(newMain).includes(a)),oldEquations:oldMain.querySelectorAll('img[src*="generatedimages/equations/"]').length,newEquations:newMain.querySelectorAll('.help-equation').length,footerTextSame:!!oldFooter&&!!newFooter&&footerText(oldFooter)===footerText(newFooter),footerLinksSame:JSON.stringify(footerLinks(oldFooter))===JSON.stringify(footerLinks(newFooter))};
 });
 for(const [target,profile]of definitions){const rows=[],missing=[];for(let at=0;at<files.length;at+=25){const batch=[];for(const url of files.slice(at,at+25)){const old=path.join(oldRoot,target,url.toLowerCase()),fresh=path.join(root,'Build',profile,url.toLowerCase());if(!fs.existsSync(old)||!fs.existsSync(fresh)){missing.push(url);continue;}batch.push({url,oldHtml:fs.readFileSync(old,'utf8'),newHtml:fs.readFileSync(fresh,'utf8'),referenceCorrections:url==='references/reference_list.htm'?referenceLinkCorrections:[]});}rows.push(...await page.evaluate(compareBatch,batch));}
  const oldToc=fs.readFileSync(path.join(oldRoot,target,'Data/FullToc.xml'),'utf8'),newToc=fs.readFileSync(path.join(root,'Build',profile,'contents-data.js'),'utf8');
  const toc=await page.evaluate(({oldToc,newToc})=>{const doc=new DOMParser().parseFromString(oldToc,'application/xml');function read(n){return [...n.children].filter(e=>e.localName==='TocEntry').map(e=>({title:e.getAttribute('Title'),link:e.getAttribute('Link')?.replace(/^\//,'')||null,children:read(e)}));}const before=read(doc.documentElement),after=JSON.parse(newToc.slice(newToc.indexOf('=')+1).replace(/;\s*$/,''));const flat=(nodes,parents=[])=>nodes.flatMap(n=>[{title:n.title,link:n.link,parents:parents.join(' > ')},...flat(n.children,parents.concat(n.title))]);const a=flat(before),b=flat(after);return {oldCount:a.length,newCount:b.length,missing:a.filter(n=>!b.some(x=>x.title===n.title&&x.link===n.link&&x.parents===n.parents)),added:b.filter(n=>!a.some(x=>x.title===n.title&&x.link===n.link&&x.parents===n.parents))};},{oldToc,newToc});
  const aliases=await page.evaluate(({before,after})=>{const parse=s=>[...new DOMParser().parseFromString(s,'application/xml').querySelectorAll('Map')].map(e=>({name:e.getAttribute('Name'),id:e.getAttribute('ResolvedId'),link:e.getAttribute('Link')}));const a=parse(before),b=parse(after);return {oldCount:a.length,newCount:b.length,differences:a.filter(x=>!b.some(y=>JSON.stringify(x)===JSON.stringify(y)))};},{before:fs.readFileSync(path.join(oldRoot,target,'Data/Alias.xml'),'utf8'),after:fs.readFileSync(path.join(root,'Build',profile,'Data/Alias.xml'),'utf8')});
  const summary={target,profile,comparedPages:rows.length,missingPages:missing,titlesEqual:rows.filter(r=>r.titleSame).length,rawTextEqual:rows.filter(r=>r.rawTextSame).length,textEqual:rows.filter(r=>r.textSame).length,rCodeEqual:rows.filter(r=>r.rCodeSame).length,imagesEqual:rows.filter(r=>r.imagesSame).length,rawLinksEqual:rows.filter(r=>r.rawLinksSame).length,linksEqual:rows.filter(r=>r.linksSame).length,recognizedReferenceCorrections:rows.flatMap(r=>r.recognizedReferenceCorrections),bibliographyAnchorsSame:rows.find(r=>r.url==='references/reference_list.htm')?.bibliographyAnchorsSame??false,anchorsPreserved:rows.filter(r=>r.anchorsPreserved).length,oldEquationCount:rows.reduce((n,r)=>n+r.oldEquations,0),newEquationCount:rows.reduce((n,r)=>n+r.newEquations,0),toc,aliases,differences:rows.filter(r=>!r.titleSame||!r.textSame||!r.rCodeSame||!r.imagesSame||!r.linksSame||!r.anchorsPreserved||!r.bibliographyAnchorsSame||r.referenceCorrectionErrors.length||r.oldEquations!==r.newEquations)};comparisons.push(summary);
  comparisons.at(-1).footersTextEqual=rows.filter(r=>r.footerTextSame).length;
  comparisons.at(-1).footersLinksEqual=rows.filter(r=>r.footerLinksSame).length;
  comparisons.at(-1).footerFailures=rows.filter(r=>!r.footerTextSame||!r.footerLinksSame);
 }
 const origin='http://127.0.0.1:'+server.address().port;const shots=[];const ctx=await browser.newContext({viewport:{width:1200,height:950}});await ctx.route('**/*',r=>r.request().url().startsWith(origin)||r.request().url().startsWith('data:')?r.continue():r.abort());
 for(const url of ['rates/incidence_and_prevalence.htm','basic_descriptive_statistics/time_series.htm','parametric_methods/paired_t.htm','graphics/box_whisker.htm']){
  for(const [type,target]of [['old','DesktopHelp'],['new','Desktop']]){const p=await ctx.newPage();await p.goto(`${origin}/${type}/${target}/${url}${type==='new'?'?content=1':''}`,{waitUntil:'load'});if(type==='new')await p.evaluate(()=>MathJax.startup.promise);const locator=type==='old'?p.locator('#mc-main-content'):p.locator('body');const name=type+'-'+url.replaceAll('/','-').replace('.htm','.png');await locator.screenshot({path:path.join(root,'evidence',name)});shots.push(name);await p.close();}
 }
 const unexpected=comparisons.flatMap(c=>[
  ...c.missingPages.map(url=>({target:c.target,url,reason:'Missing page'})),
  ...c.differences.filter(r=>r.url!=='rates/rates.htm'||!r.titleSame||!r.rCodeSame||!r.imagesSame||!r.anchorsPreserved||r.oldEquations!==r.newEquations).map(r=>({target:c.target,...r})),
  ...c.footerFailures.map(r=>({target:c.target,url:r.url,reason:'Shared footer differs',footerTextSame:r.footerTextSame,footerLinksSame:r.footerLinksSame})),
  ...c.toc.missing.map(r=>({target:c.target,reason:'Original Contents entry missing',...r})),
  ...c.toc.added.filter(r=>r.link!=='rates/incidence_and_prevalence.htm').map(r=>({target:c.target,reason:'Unexpected Contents addition',...r})),
  ...c.aliases.differences.map(r=>({target:c.target,reason:'Context mapping differs',...r}))
 ]);
 const result={checkedAt:new Date().toISOString(),passed:unexpected.length===0,unexpected,comparisons,screenshots:shots};fs.writeFileSync(path.join(root,'evidence/flare-target-comparison.json'),JSON.stringify(result,null,2));console.log(JSON.stringify({passed:result.passed,unexpected,comparisons:comparisons.map(({differences,...s})=>({...s,differenceCount:differences.length})),screenshots:shots},null,2));if(unexpected.length)process.exitCode=1;
 }finally{if(browser)await browser.close();server.close();}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
