// Static references are checked with exact filename case, as required by GitHub Pages.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..');
function walk(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(dir,e.name)):[path.join(dir,e.name)]);}
(async()=>{
 const browser=await chromium.launch({channel:'msedge',headless:true});
 const checks=[];
 try{
  const page=await browser.newPage();
  const targets=process.argv.length>2?process.argv.slice(2):['Desktop','Public'];
  if(targets.some(t=>!['Desktop','Public'].includes(t)))throw new Error('Targets must be Desktop and/or Public');
  for(const target of targets){
   const build=path.join(root,'Build',target),files=walk(build).map(f=>path.relative(build,f).replaceAll('\\','/'));
   const exact=new Set(files),folded=new Map(files.map(f=>[f.toLowerCase(),f]));
   const pages=files.filter(f=>/\.html?$/i.test(f)),docs=[];
   for(let offset=0;offset<pages.length;offset+=30){
    const input=pages.slice(offset,offset+30).map(url=>({url,source:fs.readFileSync(path.join(build,url),'utf8')}));
    docs.push(...await page.evaluate(input=>input.map(({url,source})=>{
     const doc=new DOMParser().parseFromString(source,'text/html');
     const ids=[...doc.querySelectorAll('[id],a[name]')].flatMap(e=>[e.id,e.getAttribute('name')]).filter(Boolean);
     const links=[...doc.querySelectorAll('[href],[src],object[data]')].flatMap(e=>['href','src','data'].filter(a=>e.hasAttribute(a)).map(a=>({tag:e.tagName,attribute:a,value:e.getAttribute(a)})));
     return{url,ids,links};
    }),input));
   }
   const byPath=new Map(docs.map(d=>[d.url,d])),missing=[],caseMismatches=[],missingAnchors=[],privateOrigins=[];
   let references=0,anchors=0;
   function inspect(from,reference){
    const raw=reference.value;
    if(!raw||/^data:|^blob:|^mailto:|^tel:|^javascript:/i.test(raw))return;
    let url;try{url=new URL(raw,'https://help.test/'+from);}catch{missing.push({from,...reference,reason:'Invalid URL'});return;}
    if(/help-(?:editor|project|preview)\.invalid/.test(url.hostname)||url.protocol==='file:'){privateOrigins.push({from,...reference});return;}
    if(url.origin!=='https://help.test')return;
    let dest;try{dest=decodeURIComponent(url.pathname.slice(1));}catch{missing.push({from,...reference,reason:'Invalid encoding'});return;}
    references++;
    if(!exact.has(dest)){
     const differentlyCased=folded.get(dest.toLowerCase());
     if(differentlyCased)caseMismatches.push({from,...reference,destination:dest,actual:differentlyCased});
     else missing.push({from,...reference,destination:dest});
     return;
    }
    if(url.hash&&byPath.has(dest)){
     anchors++;let anchor;try{anchor=decodeURIComponent(url.hash.slice(1));}catch{anchor=url.hash.slice(1);}
     if(anchor&&!byPath.get(dest).ids.includes(anchor)&&anchor.toLowerCase()!=='top')missingAnchors.push({from,...reference,destination:dest,anchor});
    }
   }
   const styles=new Set();
   for(const doc of docs)for(const ref of doc.links){inspect(doc.url,ref);if(ref.tag==='LINK'&&/\.css(?:[?#]|$)/i.test(ref.value||'')){const u=new URL(ref.value,'https://help.test/'+doc.url);if(u.origin==='https://help.test')styles.add(decodeURIComponent(u.pathname.slice(1)));}}
   const visitedStyles=new Set();
   for(const stylesheet of styles){
    if(visitedStyles.has(stylesheet)||!exact.has(stylesheet))continue;visitedStyles.add(stylesheet);
    const text=fs.readFileSync(path.join(build,stylesheet),'utf8');
    for(const match of text.matchAll(/url\(\s*(['"]?)(.*?)\1\s*\)/g)){
     inspect(stylesheet,{tag:'CSS',attribute:'url',value:match[2]});
     if(/\.css(?:[?#]|$)/i.test(match[2])){const u=new URL(match[2],'https://help.test/'+stylesheet);if(u.origin==='https://help.test')styles.add(decodeURIComponent(u.pathname.slice(1)));}
    }
   }
   const contents=JSON.parse(fs.readFileSync(path.join(build,'contents-data.js'),'utf8').replace(/^window\.StatsDirectContents\s*=\s*/,'').replace(/;\s*$/,''));
   function visit(nodes){for(const node of nodes){if(node.link)inspect('index.html',{tag:'Contents',attribute:'link',value:node.link});visit(node.children||[]);}}visit(contents);
   const aliasSource=fs.readFileSync(path.join(build,'Data/Alias.xml'),'utf8');
   const aliases=await page.evaluate(xml=>[...new DOMParser().parseFromString(xml,'application/xml').querySelectorAll('Map')].map(e=>e.getAttribute('Link')),aliasSource);
   for(const link of aliases)inspect('index.html',{tag:'Context',attribute:'Link',value:link.replace(/^\//,'')});
   checks.push({target,pages:pages.length,files:files.length,references,anchors,contextMappings:aliases.length,missing,caseMismatches,missingAnchors,privateOrigins,passed:!missing.length&&!caseMismatches.length&&!missingAnchors.length&&!privateOrigins.length});
  }
 }finally{await browser.close();}
 const report={checkedAtUtc:new Date().toISOString(),passed:checks.every(c=>c.passed),checks};
 fs.writeFileSync(path.join(root,'evidence','output-link-checks.json'),JSON.stringify(report,null,2));
 console.log(JSON.stringify(report,null,2));if(!report.passed)process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1;});
