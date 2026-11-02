// Compare source condition choices against both independently generated targets.
// HTML is parsed inertly; no help scripts or external resources are executed.
const fs=require('node:fs'),path=require('node:path');
const{chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),source=path.join(root,'HelpSource','Content'),evidence=path.join(root,'evidence');
const buildRoot=path.resolve(process.argv[2]||path.join(root,'Build'));
function files(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?files(path.join(dir,e.name)):[path.join(dir,e.name)]);}
const inputs=files(source).filter(p=>/\.html?$/i.test(p)).map(p=>{const relative=path.relative(source,p).replaceAll('\\','/');return{relative,source:fs.readFileSync(p,'utf8'),targets:['Desktop','Public'].map(target=>({target,html:fs.readFileSync(path.join(buildRoot,target,relative.toLowerCase()),'utf8')}))};});
(async()=>{let browser;try{browser=await chromium.launch({channel:process.env.STATSDIRECT_BROWSER_CHANNEL||'msedge',headless:true});const page=await browser.newPage();await page.goto('about:blank');
 const result=await page.evaluate(inputs=>{
  const authoringNamespaces=['https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd','http://www.madcapsoftware.com/Schemas/MadCap.xsd'],checks=[],failures=[],conditionTypes={};let conditionedElements=0;
  const conditionValue=e=>authoringNamespaces.map(uri=>e.getAttributeNS(uri,'conditions')).filter(Boolean).join(',');
  const normalizeText=s=>s.replace(/\s+/g,' ').trim();
  function normalizeImage(value,relative){try{const url=new URL(value,'https://help-test.invalid/'+relative);return url.origin==='https://help-test.invalid'?decodeURIComponent(url.pathname).toLowerCase()+url.search+url.hash:url.href;}catch{return value;}}
  for(const input of inputs){const source=new DOMParser().parseFromString(input.source,'application/xml');if(source.querySelector('parsererror')){failures.push({page:input.relative,error:'Source XHTML did not parse'});continue;}
   const conditions=[...source.getElementsByTagName('*')].filter(e=>authoringNamespaces.some(uri=>e.hasAttributeNS(uri,'conditions')));conditionedElements+=conditions.length;
   for(const e of conditions){const key=e.localName+': '+conditionValue(e);conditionTypes[key]=(conditionTypes[key]||0)+1;}
   for(const built of input.targets){const excluded=built.target==='Desktop'?'Primary.WEBHELP':'Primary.SWHELP',expected=[];
    function excludedElement(e){return conditionValue(e).split(/[;,\s]+/).includes(excluded);}
    function visit(e){if(excludedElement(e))return;if(e.localName.toLowerCase()==='img')expected.push(normalizeImage(e.getAttribute('src')||'',input.relative));for(const c of e.children)visit(c);}
    visit(source.documentElement);
    const html=new DOMParser().parseFromString(built.html,'text/html'),topic=html.querySelector('#help-topic-body')||html.body;
    const actual=[...topic.querySelectorAll('img')].filter(e=>!e.closest('.help-equation,.help-shared-footer')).map(e=>normalizeImage(e.getAttribute('src')||'',input.relative));
    const imageMatch=JSON.stringify(expected)===JSON.stringify(actual),text=normalizeText(topic.textContent);const paragraphs=[];
    for(const e of conditions.filter(e=>e.localName.toLowerCase()==='p')){const snippet=normalizeText(e.textContent),included=!excludedElement(e),found=snippet.length>0&&text.includes(snippet);paragraphs.push({text:snippet,included,found});if(snippet&&included!==found)failures.push({page:input.relative,target:built.target,paragraph:snippet,included,found});}
    checks.push({page:input.relative,target:built.target,imageMatch,expectedImages:expected.length,actualImages:actual.length,paragraphs});
    if(!imageMatch)failures.push({page:input.relative,target:built.target,expectedImages:expected,actualImages:actual});
   }
  }
  return{pages:inputs.length,targetChecks:checks.length,conditionedElements,conditionTypes,passed:failures.length===0,failures,checks};
 },inputs);
 fs.writeFileSync(path.join(evidence,'build-conditions.json'),JSON.stringify({checkedAt:new Date().toISOString(),buildRoot,...result},null,2));console.log(JSON.stringify({...result,checks:undefined},null,2));if(!result.passed||result.targetChecks!==inputs.length*2)process.exitCode=1;
 }finally{if(browser)await browser.close();}})().catch(error=>{console.error(error);process.exitCode=1;});
