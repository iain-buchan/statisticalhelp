// Namespace migration checks use in-memory topics and never write HelpSource.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),web=path.join(root,'src/StatsDirectHelpEditor.App/Web'),evidence=path.join(root,'evidence');
const SDH='https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd',MC='http://www.madcapsoftware.com/Schemas/MadCap.xsd';
const checks=[],errors=[];
function check(name,passed,detail){checks.push({name,passed:!!passed,detail});if(!passed)throw new Error(name+': '+JSON.stringify(detail));}
const server=http.createServer((req,res)=>{
 const relative=decodeURIComponent(new URL(req.url,'http://localhost').pathname).slice(1)||'index.html',target=path.resolve(web,relative);
 if(!target.startsWith(web+path.sep)){res.writeHead(403).end();return;}
 try{res.setHeader('Content-Type',({'.html':'text/html;charset=utf-8','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.json':'application/json','.svg':'image/svg+xml','.woff2':'font/woff2','.woff':'font/woff'})[path.extname(target)]||'application/octet-stream');res.end(fs.readFileSync(target));}catch{res.writeHead(404).end();}
});
function fixture(namespace,prefix='sdh'){
 return '<?xml version="1.0" encoding="utf-8"?>\n<html xmlns:'+prefix+'="'+namespace+'" xmlns:qa="urn:qa"><head><title>Namespace fixture</title></head><body>'+
 '<p id="editable" qa:flag="α">Start.</p><p id="desktop" '+prefix+':conditions="Primary.SWHELP">Desktop only.</p><p id="public" '+prefix+':conditions="Primary.WEBHELP">Public only.</p>'+
 '<p><'+prefix+':equation style="font-size:10pt">$x+1$</'+prefix+':equation></p><'+prefix+':keyword term="namespace"/>'+
 '<'+prefix+':dropDown class="rcode"><'+prefix+':dropDownHead><'+prefix+':dropDownHotspot>R code</'+prefix+':dropDownHotspot></'+prefix+':dropDownHead><'+prefix+':dropDownBody><pre><![CDATA[x <- 3\nif (x < 4 && x > 1) print("α")]]></pre></'+prefix+':dropDownBody></'+prefix+':dropDown></body></html>';
}
(async()=>{let browser;try{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:process.env.STATSDIRECT_BROWSER_CHANNEL||'msedge',headless:true});const page=await browser.newPage();page.on('pageerror',error=>errors.push(error.message));
 await page.goto(origin+'/');await page.waitForFunction(()=>!!window.HelpEditor);
 const payload=(namespace,prefix)=>({path:'isolated/namespace.htm',title:'Namespace fixture',baseUrl:origin+'/isolated/namespace.htm',source:fixture(namespace,prefix)});
 for(const [name,namespace,prefix]of [['canonical',SDH,'sdh'],['canonical alias',SDH,'own'],['legacy import',MC,'MadCap']]){
  const p=payload(namespace,prefix);
  const loaded=await page.evaluate(p=>{HelpEditor.load(p);return{unchanged:HelpEditor.getSource()===p.source,equations:document.querySelectorAll('.equation-widget').length,dropdown:document.querySelector('.protected-block strong')?.textContent,metadata:document.querySelectorAll('.metadata-widget').length,condition:document.querySelector('#desktop').dataset.editorCondition};},p);
  check(name+' loads equations, dropdowns and conditions without changing source',loaded.unchanged&&loaded.equations===1&&loaded.dropdown==='R code'&&loaded.metadata===1&&loaded.condition==='Primary.SWHELP',loaded);
  const edited=await page.evaluate(({p,SDH,MC})=>{
   document.querySelector('#editable').append(document.createTextNode(' Edited.'));document.querySelector('#visual-editor').dispatchEvent(new Event('input',{bubbles:true}));const source=HelpEditor.getSource(),doc=new DOMParser().parseFromString(source,'application/xml');
   const canonical=Array.from(doc.getElementsByTagName('*')).flatMap(node=>[node,...node.attributes]).filter(node=>node.namespaceURI===SDH);
   const foreign=doc.querySelector('#editable').getAttributeNS('urn:qa','flag');
   HelpEditor.setTab('source');const sourceTab=HelpEditor.getSource()===source;HelpEditor.markSaved();HelpEditor.setTab('visual');const saved=HelpEditor.getSource()===source;HelpEditor.undo();const undo=HelpEditor.getSource()===p.source;HelpEditor.redo();
   return{valid:!doc.querySelector('parsererror'),canonical:canonical.length>=8&&canonical.every(node=>node.prefix==='sdh'),declaration:doc.documentElement.lookupNamespaceURI('sdh')===SDH,noLegacy:!source.includes(MC)&&!source.includes('xmlns:'+p.prefix+'="'+MC+'"'),foreign:foreign==='α',code:doc.querySelector('pre').textContent==='x <- 3\nif (x < 4 && x > 1) print("α")',equation:doc.getElementsByTagNameNS(SDH,'equation')[0].textContent==='$x+1$',sourceTab,saved,undo,redo:HelpEditor.getSource()===source};
  },{p:{...p,prefix},SDH,MC});
  check(name+' visual edits save canonical sdh XML and preserve Unicode, code, equations and history',Object.values(edited).every(Boolean),edited);
  const conditions=[];
  for(const target of ['Desktop','Public']){
   await page.evaluate(({p,target})=>{HelpEditor.load(p);HelpEditor.setBuildTarget(target);HelpEditor.setTab('preview');},{p,target});
   const handle=await page.$('#preview-frame'),frame=await handle.contentFrame();await frame.waitForFunction(()=>window.helpPreviewReady===true);
   conditions.push(await frame.evaluate(target=>({target,desktop:!!document.querySelector('#desktop'),public:!!document.querySelector('#public'),dropdown:document.querySelector('details summary')?.textContent==='R code',code:document.querySelector('details pre')?.textContent==='x <- 3\nif (x < 4 && x > 1) print("α")',equation:document.querySelectorAll('.help-equation mjx-container svg').length===1}),target));
  }
  check(name+' preview applies both target conditions and renders R dropdowns and equations',conditions.every(p=>p.desktop===(p.target==='Desktop')&&p.public===(p.target==='Public')&&p.dropdown&&p.code&&p.equation),conditions);
  const equationEdit=await page.evaluate(({p,SDH,MC})=>{HelpEditor.load(p);HelpEditor.openEquation(document.querySelector('.equation-widget').dataset.protectedId);document.querySelector('#equation-field').value='\\gamma^2';document.querySelector('#equation-apply').click();const source=HelpEditor.getSource(),doc=new DOMParser().parseFromString(source,'application/xml');return{canonical:doc.getElementsByTagNameNS(SDH,'equation')[0]?.textContent.includes('\\gamma^2'),legacy:!source.includes(MC),dialogClosed:!document.querySelector('#equation-dialog').open,valid:!doc.querySelector('parsererror')};},{p,SDH,MC});
  check(name+' equation edits serialize in the own namespace',Object.values(equationEdit).every(Boolean),equationEdit);
  const pasted=await page.evaluate(({p,SDH,MC})=>{
   HelpEditor.load(p);const copy=['#desktop','.equation-widget','.protected-block'].map(selector=>document.querySelector(selector).outerHTML).join('');HelpEditor.load({...p,source:'<html><head><title>Destination</title></head><body><p>Paste here.</p></body></html>',path:'isolated/deep/destination.htm',baseUrl:p.baseUrl.replace('namespace.htm','deep/destination.htm')});
   document.querySelector('#visual-editor').insertAdjacentHTML('beforeend',copy);document.querySelector('#visual-editor').dispatchEvent(new Event('input',{bubbles:true}));const source=HelpEditor.getSource(),doc=new DOMParser().parseFromString(source,'application/xml');
   return{valid:!doc.querySelector('parsererror'),condition:doc.querySelector('#desktop').getAttributeNS(SDH,'conditions')==='Primary.SWHELP',equation:doc.getElementsByTagNameNS(SDH,'equation').length===1,dropdown:doc.getElementsByTagNameNS(SDH,'dropDown').length===1,code:doc.querySelector('pre').textContent==='x <- 3\nif (x < 4 && x > 1) print("α")',noLegacy:!source.includes(MC)};
  },{p,SDH,MC});
  check(name+' cross-page rich copies preserve namespaced conditions, equations and R code',Object.values(pasted).every(Boolean),pasted);
 }
 const conflict=await page.evaluate(({SDH,MC})=>{
  const source='<html xmlns:sdh="'+SDH+'" xmlns:MadCap="'+MC+'"><head><title>Conflicting import</title></head><body><p sdh:conditions="Primary.SWHELP" MadCap:conditions="Primary.WEBHELP">Both values must survive.</p></body></html>';HelpEditor.load({path:'isolated/conflict.htm',baseUrl:location.href,source});const unchanged=HelpEditor.getSource()===source;document.querySelector('#visual-editor p').append(document.createTextNode(' Edited.'));document.querySelector('#visual-editor').dispatchEvent(new Event('input',{bubbles:true}));let message='';try{HelpEditor.getSource();}catch(error){message=error.message;}return{unchanged,rejected:message.includes('conflicting legacy and StatsDirect authoring attributes')};
 },{SDH,MC});
 check('A mixed import cannot silently discard duplicate authoring attributes during visual save',Object.values(conflict).every(Boolean),conflict);
 const authoritativeSave=await page.evaluate(({p,SDH,MC})=>{
  HelpEditor.load(p);const authoritative=p.source.replace(MC,SDH),node=document.querySelector('#editable'),range=document.createRange();range.selectNodeContents(node);getSelection().removeAllRanges();getSelection().addRange(range);const selected=getSelection().toString();
  HelpEditor.markSaved(authoritative);const exactBaseline=HelpEditor.getSource()===authoritative&&!HelpEditor.getState().dirty,stableDom=document.querySelector('#editable')===node&&getSelection().toString()===selected,noPhantomUndo=HelpEditor.getState().historyIndex===0;HelpEditor.undo();const undoUnchanged=HelpEditor.getSource()===authoritative;
  node.append(document.createTextNode(' Second edit.'));document.querySelector('#visual-editor').dispatchEvent(new Event('input',{bubbles:true}));const edited=HelpEditor.getSource(),secondEdit=HelpEditor.getState().dirty&&edited.includes('Second edit.')&&!edited.includes(MC);HelpEditor.markSaved(edited);const secondBaseline=HelpEditor.getSource()===edited&&!HelpEditor.getState().dirty&&HelpEditor.getState().historyIndex===1;
  HelpEditor.undo();const undo=HelpEditor.getSource()===authoritative&&HelpEditor.getState().dirty;HelpEditor.redo();return{exactBaseline,stableDom,noPhantomUndo,undoUnchanged,secondEdit,secondBaseline,undo,redo:HelpEditor.getSource()===edited&&!HelpEditor.getState().dirty};
 },{p:payload(MC,'MadCap'),SDH,MC});
 check('Host-normalized saves adopt exact disk source while preserving visual selection, later edits and Undo',Object.values(authoritativeSave).every(Boolean),authoritativeSave);
 const authoritativeSourceSave=await page.evaluate(({p,SDH,MC})=>{
  HelpEditor.load(p);HelpEditor.setTab('source');const field=document.querySelector('#source-editor'),start=field.value.indexOf('Start.');field.focus();field.setSelectionRange(start,start+6);const authoritative=p.source.replace(MC,SDH);HelpEditor.markSaved(authoritative);const selection=field.value.slice(field.selectionStart,field.selectionEnd)==='Start.',exactBaseline=HelpEditor.getSource()===authoritative&&field.value===authoritative&&!HelpEditor.getState().dirty;
  field.value=field.value.replace('Start.','Source second edit.');field.dispatchEvent(new Event('input',{bubbles:true}));const edited=HelpEditor.getSource();HelpEditor.markSaved(edited);HelpEditor.undo();const undo=HelpEditor.getSource()===authoritative;HelpEditor.redo();return{selection,exactBaseline,undo,redo:HelpEditor.getSource()===edited&&!HelpEditor.getState().dirty};
 },{p:payload(MC,'MadCap'),SDH,MC});
 check('Host-normalized source saves preserve the source selection and exact baseline through a second edit',Object.values(authoritativeSourceSave).every(Boolean),authoritativeSourceSave);
 const insertion=await page.evaluate(({SDH,MC})=>{HelpEditor.load({path:'isolated/new.htm',baseUrl:location.href,source:'<html><head><title>New</title></head><body><p>Equation:</p></body></html>'});HelpEditor.openEquation();document.querySelector('#equation-field').value='a+b';document.querySelector('#equation-apply').click();const source=HelpEditor.getSource(),doc=new DOMParser().parseFromString(source,'application/xml');return{valid:!doc.querySelector('parsererror'),canonical:doc.getElementsByTagNameNS(SDH,'equation')[0]?.prefix==='sdh',declaration:doc.documentElement.lookupNamespaceURI('sdh')===SDH,noLegacy:!source.includes(MC)};},{SDH,MC});
 check('A new equation introduces only the canonical sdh namespace',Object.values(insertion).every(Boolean),insertion);
 const artwork=await page.evaluate(async({SDH,MC})=>{
  const {canonicalEquationSource,loadEquationArtwork}=await import('./equation-artwork.js'),catalog=await(await fetch('./equation-artwork/catalog.json')).json(),loader=await loadEquationArtwork();
  // Decode the catalog's length-prefixed source into isolated XML fixtures so
  // every immutable original key is checked without reading editable topics.
  function decode(value,namespace){let at=0;const doc=document.implementation.createDocument(null,null);const part=()=>{const colon=value.indexOf(':',at),length=Number(value.slice(at,colon));at=colon+1;const result=value.slice(at,at+length);at+=length;return result;};
   const element=()=>{if(value[at++]!=='E')throw Error('Expected element');const uri=part(),name=part(),node=doc.createElementNS(uri===MC?namespace:uri||null,uri?'ns:'+name:name),colon=value.indexOf(':',at),count=Number(value.slice(at,colon));at=colon+1;for(let i=0;i<count;i++){const uri=part(),name=part(),text=part();node.setAttributeNS(uri===MC?namespace:uri||null,uri?'a'+i+':'+name:name,text);}while(value[at]!=='Z'){if(value[at]==='T'){at++;node.append(doc.createTextNode(part()));}else node.append(element());}at++;return node;};return element();
  }
  const failures=[];for(const entry of catalog.entries){const legacy=decode(entry.canonicalSource,MC),canonical=decode(entry.canonicalSource,SDH),a=canonicalEquationSource(legacy),b=canonicalEquationSource(canonical);if(a!==entry.canonicalSource||b!==entry.canonicalSource||loader.create(canonical)?.dataset.equationSourceKey!==entry.sourceKey)failures.push(entry.sourceKey);}
  const parse=text=>new DOMParser().parseFromString(text,'application/xml').documentElement;
  const sample=uri=>parse('<n:equation xmlns:n="'+uri+'" xmlns:q="urn:foreign" n:z="z" q:a="a" n:a="a"><math xmlns="http://www.w3.org/1998/Math/MathML"><mi>x</mi></math></n:equation>');
  return{count:catalog.entries.length,failures,attributesAndMathML:canonicalEquationSource(sample(SDH))===canonicalEquationSource(sample(MC)),foreignNamespaceDistinct:canonicalEquationSource(sample('urn:foreign-help'))!==canonicalEquationSource(sample(MC))};
 },{SDH,MC});
 check('Every original artwork key matches canonical sdh equations without rewriting the catalog',artwork.count>0&&artwork.failures.length===0&&artwork.attributesAndMathML&&artwork.foreignNamespaceDistinct,artwork);
 check('No unhandled browser errors',errors.length===0,errors);
 }catch(error){checks.push({name:'Namespace browser checks completed',passed:false,detail:error.stack||String(error)});}finally{
  if(browser)await browser.close();server.close();fs.mkdirSync(evidence,{recursive:true});const result={testedAt:new Date().toISOString(),passed:checks.length>0&&checks.every(check=>check.passed),checks,errors};fs.writeFileSync(path.join(evidence,'web-namespace-checks.json'),JSON.stringify(result,null,2));console.log(JSON.stringify(result,null,2));if(!result.passed)process.exitCode=1;
 }
})();
