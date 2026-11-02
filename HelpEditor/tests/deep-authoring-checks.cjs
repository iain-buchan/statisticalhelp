const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('./browser-runtime.cjs');
const root=path.resolve(__dirname,'..'),web=path.join(root,'src/StatsDirectHelpEditor.App/Web'),evidence=path.join(root,'evidence');
const checks=[],errors=[],network=[],baseline=process.argv.includes('--baseline');
function check(name,passed,detail){checks.push({name,passed:!!passed,detail});}
const mime={'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.woff2':'font/woff2','.woff':'font/woff','.svg':'image/svg+xml'};
const server=http.createServer((req,res)=>{
 const u=new URL(req.url,'http://localhost');
 if(u.pathname.startsWith('/generated/')){res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><body><h1>Generated fixture</h1><details id="r"><summary>R example</summary><pre>x &lt;- 1</pre></details>'+Array.from({length:80},(_,i)=>'<p>Reader paragraph '+i+'</p>').join('')+'</body></html>');return;}
 const f=path.resolve(web,u.pathname==='/'?'index.html':decodeURIComponent(u.pathname.slice(1)));
 if(!f.startsWith(web+path.sep)){res.writeHead(403).end();return;}
 try{res.setHeader('Content-Type',mime[path.extname(f)]||'application/octet-stream');res.end(fs.readFileSync(f));}catch{res.writeHead(404).end();}
});
const mc='http://www.madcapsoftware.com/Schemas/MadCap.xsd';
const xml=body=>'<html xmlns:MadCap="'+mc+'" xmlns:qa="urn:deep-test"><head><title>Deep authoring</title><meta name="retain" content="unchanged"/></head><body>'+body+'</body></html>';
(async()=>{await new Promise(r=>server.listen(0,'127.0.0.1',r));let browser;try{
 const origin='http://127.0.0.1:'+server.address().port;
 browser=await chromium.launch({channel:'msedge',headless:true});
 const context=await browser.newContext({viewport:{width:1400,height:1000}});
 await context.route('**/*',route=>{const url=route.request().url();if(url.startsWith(origin+'/')||url.startsWith('data:')||url.startsWith('blob:'))return route.continue();network.push(url);return route.abort();});
 async function open(){const p=await context.newPage();p.on('pageerror',e=>errors.push(e.message));await p.addInitScript(()=>{window.messages=[];window.chrome ||= {};window.chrome.webview={postMessage:m=>messages.push(m)};});await p.goto(origin);await p.waitForFunction(()=>!!window.HelpEditor);return p;}
 const sourcePage=await open(),destPage=await open();
 const from={path:'from.htm',title:'From',baseUrl:origin+'/project/from.htm',source:xml('<p id="from" title="Original source title">Pasted ordinary prose</p><p><span qa:flag="source">Namespaced source</span></p>')};
 const to={path:'to.htm',title:'To',baseUrl:origin+'/project/to.htm',source:xml('<p id="to" class="desktop-only" MadCap:conditions="Primary.SWHELP">Destination prose</p><p><span class="wrong" qa:flag="destination">Existing namespace</span></p>')};
 await sourcePage.evaluate(p=>HelpEditor.load(p),from);await destPage.evaluate(p=>HelpEditor.load(p),to);
 const clip=await sourcePage.locator('#visual-editor').innerHTML();
 const pasted=await destPage.evaluate(clip=>{const v=document.getElementById('visual-editor');v.insertAdjacentHTML('beforeend',clip);v.dispatchEvent(new Event('input',{bubbles:true}));const text=HelpEditor.getSource(),d=new DOMParser().parseFromString(text,'application/xml'),p=d.querySelector('#from');return{text,condition:p.getAttributeNS('https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd','conditions'),klass:p.getAttribute('class'),title:p.getAttribute('title'),ownCondition:d.querySelector('#to').getAttributeNS('https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd','conditions'),copiedFlag:d.querySelectorAll('span')[1].getAttributeNS('urn:deep-test','flag'),copiedClass:d.querySelectorAll('span')[1].getAttribute('class'),valid:!d.querySelector('parsererror')};},clip);
 check('Cross-window prose paste cannot inherit unrelated target conditions or styling',pasted.condition===null&&pasted.klass===null&&pasted.copiedClass===null,pasted);
 check('Cross-window prose paste retains its own metadata and namespaced attributes',pasted.valid&&pasted.title==='Original source title'&&pasted.ownCondition==='Primary.SWHELP'&&pasted.copiedFlag==='source');
 await destPage.evaluate(()=>HelpEditor.undo());check('A cross-window paste is undone without changing destination source',await destPage.evaluate(s=>HelpEditor.getSource()===s,to.source));await destPage.evaluate(()=>HelpEditor.redo());
 check('Redo retains the copied prose without conditional leakage',await destPage.evaluate(s=>HelpEditor.getSource()===s,pasted.text));
 const ids=await Promise.all([sourcePage,destPage].map(p=>p.locator('#visual-editor p').first().getAttribute('data-source-node')));
 check('Independent editor windows allocate different internal source identities',ids[0]!==ids[1],ids);

 const code='<MadCap:dropDown class="rcode"><MadCap:dropDownHead><MadCap:dropDownHotspot>R code</MadCap:dropDownHotspot></MadCap:dropDownHead><MadCap:dropDownBody><pre xml:space="preserve">';
 const close='</pre></MadCap:dropDownBody></MadCap:dropDown>';
 const blocks={path:'blocks.htm',title:'Blocks',baseUrl:origin+'/project/blocks.htm',source:xml('<p>Prose before blocks.</p>'+code+'x &lt;- 1'+close+'<p>Middle.</p>'+code+'y &lt;- 2'+close+'<script type="application/json"><![CDATA[{"name":"first"}]]></script><script type="application/json"><![CDATA[{"name":"second"}]]></script>')};
 await destPage.evaluate(p=>HelpEditor.load(p),blocks);
 await destPage.locator('#visual-editor .protected-block').nth(1).getByRole('button',{name:'Edit HTML'}).click();
 let focus=await destPage.evaluate(()=>{const t=document.getElementById('source-editor');return{start:t.selectionStart,end:t.selectionEnd,selected:t.value.slice(t.selectionStart,t.selectionEnd),text:t.value};});
 check('Edit HTML selects the chosen second R block rather than the first one',focus.start===[...blocks.source.matchAll(/<MadCap:dropDown(?=[\s>])/g)][1].index&&focus.selected==='<MadCap:dropDown',focus);
 check('Opening the selected code block leaves source bytes and clean state intact',focus.text===blocks.source&&!await destPage.evaluate(()=>HelpEditor.getState().dirty));
 await destPage.evaluate(()=>HelpEditor.setTab('visual'));
 await destPage.locator('#visual-editor .protected-block').nth(3).getByRole('button',{name:'Edit HTML'}).click();
 focus=await destPage.evaluate(()=>{const t=document.getElementById('source-editor');return{start:t.selectionStart,selected:t.value.slice(t.selectionStart,t.selectionEnd)};});
 check('Edit HTML selects the chosen second script and skips CDATA content',focus.start===blocks.source.lastIndexOf('<script')&&focus.selected==='<script',focus);
 const multiline={...blocks,source:blocks.source.replace('Prose before blocks.','Prose before blocks. &lt;tag&gt;').replaceAll('><','>\r\n<').replace('<p>Middle.</p>','<!-- <MadCap:dropDown fake="not a block"/> -->\r\n<p title="A &gt; B">Middle.</p>')};
 await destPage.evaluate(p=>HelpEditor.load(p),multiline);
 await destPage.evaluate(()=>{const v=document.getElementById('visual-editor'),p=document.createElement('p');p.textContent='A new paragraph before the code blocks.';v.prepend(p);v.dispatchEvent(new Event('input',{bubbles:true}));});
 const beforeFocus=await destPage.evaluate(()=>HelpEditor.getSource());
 await destPage.locator('#visual-editor .protected-block').nth(1).getByRole('button',{name:'Edit HTML'}).click();
 focus=await destPage.evaluate(()=>{const t=document.getElementById('source-editor'),matches=[...t.value.matchAll(/<(?:MadCap|sdh):dropDown(?=[\s>])/g)];return{start:t.selectionStart,expected:matches.at(-1).index,text:HelpEditor.getSource(),dirty:HelpEditor.getState().dirty};});
 check('Edit HTML maps the intended block after an unsaved insertion, CRLFs and a fake tag in a comment',focus.start===focus.expected&&focus.dirty&&focus.text===beforeFocus,{start:focus.start,expected:focus.expected});
 await destPage.evaluate(()=>HelpEditor.undo());check('Edit HTML navigation adds no phantom Undo and retains original CRLF source',await destPage.evaluate(s=>HelpEditor.getSource()===s,multiline.source));

 const fromEquation={...from,source:xml('<p>Copied formula: <MadCap:equation>$x+1$</MadCap:equation></p>')};
 const toEquation={...to,source:xml('<p>Original formula: <MadCap:equation>$y+2$</MadCap:equation></p>')};
 await sourcePage.evaluate(p=>HelpEditor.load(p),fromEquation);await destPage.evaluate(p=>HelpEditor.load(p),toEquation);
 const equationClip=await sourcePage.locator('#visual-editor .equation-widget').evaluate(e=>e.outerHTML);
 const equationCopy=await destPage.evaluate(clip=>{const v=document.getElementById('visual-editor');v.insertAdjacentHTML('beforeend',clip);v.dispatchEvent(new Event('input',{bubbles:true}));const copied=HelpEditor.getSource();const widgets=v.querySelectorAll('.equation-widget');HelpEditor.openEquation(widgets[1].dataset.protectedId);document.getElementById('equation-field').value='z+3';document.getElementById('equation-apply').click();const d=new DOMParser().parseFromString(HelpEditor.getSource(),'application/xml'),equations=[...d.getElementsByTagNameNS('*','equation')].map(e=>e.textContent);HelpEditor.undo();return{copied,equations,undo:HelpEditor.getSource()===copied};},equationClip);
 check('A formula copied from another editor remains independent of the original formula',equationCopy.equations[0]==='$y+2$'&&equationCopy.equations[1].includes('z+3'),equationCopy.equations);
 check('Undoing the copied formula edit restores that copy without removing it',equationCopy.undo);

 // Review acceptance should not make the author lose their reading position.
 const long={...to,source:xml(Array.from({length:150},(_,i)=>'<p id="line-'+i+'">Paragraph '+i+' with original wording.</p>').join(''))};
 await destPage.evaluate(p=>HelpEditor.load(p),long);
 const reviewedPosition=await destPage.evaluate(()=>{HelpEditor.setOutputView('formatted');const p=document.querySelector('#line-120');p.scrollIntoView();p.closest('[contenteditable]').focus();const range=document.createRange();range.selectNodeContents(p);getSelection().removeAllRanges();getSelection().addRange(range);document.dispatchEvent(new Event('selectionchange'));const panel=document.getElementById('visual-panel'),before=panel.scrollTop,expected=HelpEditor.getSource(),changed=expected.replace('Paragraph 1 with original wording.','Paragraph 1 with clearer wording.');HelpEditor.applyReviewedSource('to.htm',expected,changed);return{before,after:panel.scrollTop,selection:getSelection().toString(),expectedSelection:p.textContent};});
 check('Accepting reviewed text retains the Formatted reading position',reviewedPosition.after===reviewedPosition.before,reviewedPosition);

 // An unchanged generated view must retain the actual reader document, not
 // just the C# BuildResult. Keep R expansion, scroll and search DOM state.
 await destPage.evaluate(p=>{HelpEditor.load(p);HelpEditor.setUnifiedViews(true);HelpEditor.setOutputView('browser');HelpEditor.setGeneratedPreview(location.origin+'/generated/reader.htm#Start');},blocks);
 let frame=await(await destPage.$('#preview-frame')).contentFrame();await frame.waitForSelector('#r');
 await frame.evaluate(()=>{document.getElementById('r').open=true;window.scrollTo(0,850);document.body.dataset.readerState='keep';});
 const initialGeneration=await destPage.evaluate(()=>HelpEditor.getState().previewGeneration);
 await destPage.evaluate(()=>{HelpEditor.setOutputView('formatted');HelpEditor.setOutputView('browser');HelpEditor.setGeneratedPreview(location.origin+'/generated/reader.htm#Start',true);});
 frame=await(await destPage.$('#preview-frame')).contentFrame();await frame.waitForSelector('#r');
 const reader=await frame.evaluate(()=>({open:document.getElementById('r').open,scroll:scrollY,state:document.body.dataset.readerState}));
 check('Unchanged Browser revisit preserves the R expansion, scroll and reader document',reader.open&&reader.scroll===850&&reader.state==='keep',reader);
 check('Unchanged Browser revisit creates no new preview generation',await destPage.evaluate(n=>HelpEditor.getState().previewGeneration===n,initialGeneration));
 await destPage.evaluate(()=>HelpEditor.setGeneratedPreview(location.origin+'/generated/other.htm#New',true));
 frame=await(await destPage.$('#preview-frame')).contentFrame();await frame.waitForSelector('#r');
 check('A different page or anchor still receives a new generated preview',await destPage.evaluate(n=>HelpEditor.getState().previewGeneration===n+1,initialGeneration)&&!await frame.locator('#r').evaluate(e=>e.open));

 // Source errors remain editable through repeated rejected switches, Undo,
 // normal typing and Save; neither protected code nor earlier edits vanish.
 await destPage.evaluate(p=>{HelpEditor.load(p);HelpEditor.setOutputView('source');const t=document.getElementById('source-editor');t.value=t.value.replace('Middle.','Changed middle.');t.dispatchEvent(new Event('input',{bubbles:true}));},blocks);
 await destPage.waitForTimeout(260);
 const edited=await destPage.evaluate(()=>HelpEditor.getSource());
 await destPage.evaluate(()=>{const t=document.getElementById('source-editor');t.value=t.value.replace('</body>','<p>broken & input</body>');t.dispatchEvent(new Event('input',{bubbles:true}));});
 const refused=await destPage.evaluate(()=>['formatted','browser','app'].map(v=>HelpEditor.setOutputView(v)));
 check('Malformed source refuses every other view without discarding its buffer',refused.every(x=>!x)&&await destPage.locator('#source-editor').inputValue().then(v=>v.includes('broken & input')));
 await destPage.evaluate(()=>HelpEditor.undo());check('Undo after failed view switches restores the preceding valid edit',await destPage.evaluate(s=>HelpEditor.getSource()===s,edited));
 await destPage.evaluate(()=>HelpEditor.undo());check('Further Undo restores the exact pre-edit source, including protected R blocks',await destPage.evaluate(s=>HelpEditor.getSource()===s,blocks.source));
 await destPage.evaluate(()=>HelpEditor.redo());check('Redo restores the valid edit after failed previews',await destPage.evaluate(s=>HelpEditor.getSource()===s,edited));
 check('Deep authoring scenarios produce no script errors or outside network requests',errors.length===0&&network.length===0,{errors,network});
 }catch(error){checks.push({name:'Deep authoring run completed',passed:false,error:error.stack});}finally{await browser?.close();server.close();const out={checkedAtUtc:new Date().toISOString(),passed:checks.every(c=>c.passed),total:checks.length,checks,errors};fs.writeFileSync(path.join(evidence,baseline?'deep-authoring-baseline.json':'deep-authoring-checks.json'),JSON.stringify(out,null,2));console.log(JSON.stringify(out,null,2));if(!out.passed)process.exitCode=1;}})();
