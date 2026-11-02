// Read-only baseline verification. This uses the upstream MathJax library
// installed with Flare only as an independent verifier; it is never copied or
// required by the editor or its generated help.
const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),crypto=require('node:crypto');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..');
const source='C:/Develop/SD3Help/StatsDirect/Content',baseline='C:/Develop/SD3Help/StatsDirect/Output/ieb21/Website';
const mathjax='C:/Program Files/MadCap Software/MadCap Flare 22/Flare.app/Resources/EquationEditor/mathjax';
const dest=path.join(root,'src/StatsDirectHelpEditor.App/Web/equation-artwork');
const hash=value=>crypto.createHash('sha256').update(value).digest('hex');
const files=[];function walk(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const file=path.join(dir,e.name);if(e.isDirectory())walk(file);else if(/\.htm$/i.test(file)&&/<MadCap:equation\b/.test(fs.readFileSync(file,'utf8')))files.push(file);}}walk(source);
const mime={'.js':'text/javascript','.html':'text/html','.svg':'image/svg+xml','.json':'application/json'};
const server=http.createServer((req,res)=>{const name=decodeURIComponent(new URL(req.url,'http://localhost').pathname);if(name==='/verify.html'){res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><head><script>window.MathJax={skipStartupTypeset:true,messageStyle:"none",showProcessingMessages:false,tex2jax:{inlineMath:[["$","$"],["\\\\(","\\\\)"]],preview:"none",processEnvironments:true,balanceBraces:true,processEscapes:true},TeX:{extensions:["AMSsymbols.js","AMSmath.js","enclose.js","autobold.js","color.js"]}};</script><script src="/mathjax/MathJax.js?config=TeX-AMS-MML_SVG-full"></script></head><body style="font-size:10pt;font-family:Microsoft Sans Serif"><div id="math"></div></body></html>');return;}const parts=name.split('/').filter(Boolean),mount=parts[0]==='mathjax'?(parts.shift(),mathjax):root;const file=path.resolve(mount,...parts);if(!file.startsWith(path.resolve(mount)+path.sep)){res.writeHead(403).end();return;}try{res.setHeader('Content-Type',mime[path.extname(file).toLowerCase()]||'application/octet-stream');res.end(fs.readFileSync(file));}catch{res.writeHead(404).end();}});
(async()=>{let browser;try{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:'msedge',headless:true});const page=await browser.newPage({viewport:{width:1500,height:1000}});page.on('pageerror',e=>console.error('Browser:',e.message));
 await page.goto(origin+'/verify.html',{waitUntil:'load'});await page.waitForFunction(()=>window.MathJax?.Hub?.Startup?.onload);
 const input=files.map(file=>({topic:path.relative(source,file).replaceAll('\\','/').toLowerCase(),source:fs.readFileSync(file,'utf8'),output:fs.readFileSync(path.join(baseline,path.relative(source,file)),'utf8'),sourceSha256:hash(fs.readFileSync(file)),outputSha256:hash(fs.readFileSync(path.join(baseline,path.relative(source,file))))}));
 const equations=await page.evaluate(async input=>{
   const {canonicalEquationSource}=await import('/src/StatsDirectHelpEditor.App/Web/equation-artwork.js');const equations=[];
   for(const file of input){const doc=new DOMParser().parseFromString(file.source,'text/xml'),eqs=Array.from(doc.getElementsByTagNameNS('http://www.madcapsoftware.com/Schemas/MadCap.xsd','equation'));const old=new DOMParser().parseFromString(file.output,'text/html'),images=Array.from(old.querySelectorAll('#mc-main-content img[src*="generatedimages/equations/"]'));if(eqs.length!==images.length)throw new Error('Equation mapping count differs: '+file.topic);
     eqs.forEach((eq,i)=>equations.push({topic:file.topic,index:i+1,canonicalSource:canonicalEquationSource(eq),sourceText:eq.textContent,sourceSha256:file.sourceSha256,outputSha256:file.outputSha256,artwork:images[i].getAttribute('src').replace(/^\.\.\//,'')}));}
   const container=document.getElementById('math');for(let i=0;i<equations.length;i++){const span=document.createElement('span');span.id='e'+i;span.textContent=equations[i].sourceText;container.append(span,document.createElement('br'));}
   await new Promise(resolve=>MathJax.Hub.Queue(['Typeset',MathJax.Hub,container],resolve));return equations;
 },input);
 // The original SVG bytes are supplied directly to the DOM comparison so no
 // baseline path can accidentally select a different mount.
 const originalSvgs=equations.map(e=>fs.readFileSync(path.join(baseline,e.artwork),'utf8'));
 const comparisons=await page.evaluate(({equations,originalSvgs})=>{
   const signature=svg=>{const defs=new Map([...document.querySelectorAll('#MathJax_SVG_glyphs [id]'),...svg.querySelectorAll('defs [id]')].map(e=>[e.id,e]));
    const glyphNames=[];
    const visit=e=>{if(['defs','title','desc'].includes(e.localName))return '';if(e.localName==='use'){const ref=e.getAttributeNS('http://www.w3.org/1999/xlink','href')||e.getAttribute('href');const target=defs.get((ref||'').slice(1));if(!target)throw new Error('Missing glyph '+ref);glyphNames.push(ref.replace(/^#(?:MJX-\d+-)?/,''));return '<use>'+visit(target)+'</use>';}
     const outline=e.localName==='path'?(e.getAttribute('d').match(/[a-zA-Z]|[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?/g)||[]).join(','):'';
     return '<'+e.localName+'>'+outline+Array.from(e.children).map(visit).join('')+'</'+e.localName+'>';};
    const glyphStructure=Array.from(svg.children).map(visit).join('');
    // MathJax's IE and Chromium bounds occasionally select adjacent sizes of
    // the same delimiter, or assembled braces/radicals rather than one glyph.
    // Resolve only those documented SVG font variants to their same symbol.
    const symbols=glyphNames.flatMap(name=>{
      if(!/^MJSZ\d-/.test(name))return[name];const code=name.split('-').at(-1);
      if(['23A8','23A9','23AC','23AD','E000','23B7'].includes(code))return[];
      return[{'23A7':'7B','23AB':'7D','E001':'221A'}[code]||code];
    });
    return {viewBox:svg.getAttribute('viewBox'),width:svg.getAttribute('width'),height:svg.getAttribute('height'),glyphStructure,glyphNames,symbols};};
   return equations.map((e,i)=>{const oldSvg=new DOMParser().parseFromString(originalSvgs[i],'image/svg+xml').documentElement,newSvg=document.querySelector('#e'+i+' svg');if(!newSvg)return{topic:e.topic,index:e.index,passed:false,error:'No MathJax SVG'};const before=signature(oldSvg),after=signature(newSvg),widthRelativeError=Math.abs(parseFloat(before.width)-parseFloat(after.width))/parseFloat(before.width),exactGlyphStructure=before.glyphStructure===after.glyphStructure,sameSymbolSequence=JSON.stringify(before.symbols)===JSON.stringify(after.symbols);return{topic:e.topic,index:e.index,passed:(exactGlyphStructure||sameSymbolSequence)&&widthRelativeError<0.01,exactGlyphStructure,sameSymbolSequence,widthRelativeError,before,after};});
 },{equations,originalSvgs});
 const failures=comparisons.filter(c=>!c.passed);const report={checkedAtUtc:new Date().toISOString(),rendererVersion:await page.evaluate(()=>MathJax.version),method:'Source-to-artwork mapping verified by each source equation independently rendered with upstream MathJax 2.6.1: resolved glyph outlines and nesting/order, or identical symbol sequence allowing only SVG font delimiter-size variants, with width within 1%. Chromium and the Flare IE renderer differ in bounding metrics; this verification does not assert pixel equality of independently rendered equations. Original SVG bytes are retained unchanged.',equations:equations.length,exactGlyphStructure:comparisons.filter(c=>c.exactGlyphStructure).length,delimiterSizeVariants:comparisons.filter(c=>c.passed&&!c.exactGlyphStructure).map(c=>({topic:c.topic,index:c.index,beforeGlyphs:c.before.glyphNames,afterGlyphs:c.after.glyphNames})),passed:failures.length===0,maxWidthRelativeError:Math.max(...comparisons.map(c=>c.widthRelativeError||0)),failures};
 fs.writeFileSync(path.join(root,'evidence/equation-artwork-verification.json'),JSON.stringify(report,null,2));
 if(failures.length){console.log(JSON.stringify({passed:false,equations:equations.length,failures:failures.slice(0,5).map(f=>({topic:f.topic,index:f.index,error:f.error,viewBoxBefore:f.before?.viewBox,viewBoxAfter:f.after?.viewBox,widthBefore:f.before?.width,widthAfter:f.after?.width,geometrySame:f.before?.geometry===f.after?.geometry}))},null,2));process.exitCode=1;return;}
 if(process.argv.includes('--capture')){fs.mkdirSync(dest,{recursive:true});const entries=equations.map((e,i)=>{const bytes=Buffer.from(originalSvgs[i]),artworkSha256=hash(bytes),file=artworkSha256+'.svg';fs.writeFileSync(path.join(dest,file),bytes);return{sourceKey:hash(e.canonicalSource),canonicalSource:e.canonicalSource,file,artworkSha256,topic:e.topic,index:e.index,originalSourceSha256:e.sourceSha256,originalOutputSha256:e.outputSha256};});fs.writeFileSync(path.join(dest,'catalog.json'),JSON.stringify({schemaVersion:1,verifiedAtUtc:report.checkedAtUtc,verificationMethod:report.method,rendererVersion:report.rendererVersion,entries},null,2));}
 console.log(JSON.stringify({...report,failures:undefined,delimiterSizeVariants:report.delimiterSizeVariants.length,uniqueSources:new Set(equations.map(e=>e.canonicalSource)).size,captured:process.argv.includes('--capture')},null,2));
 }finally{if(browser)await browser.close();server.close();}})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
