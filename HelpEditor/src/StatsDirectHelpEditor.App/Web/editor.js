import { MathfieldElement, convertLatexToMarkup } from './mathlive/mathlive.min.mjs';
import { loadEquationArtwork, equationArtworkCss } from './equation-artwork.js';

const equationArtwork = await loadEquationArtwork();

const SDH = 'https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd';
const LEGACY_MC = 'http://www.madcapsoftware.com/Schemas/MadCap.xsd';
const XMLNS = 'http://www.w3.org/2000/xmlns/';
const isHelpNamespace = uri => uri === SDH || uri === LEGACY_MC;
const helpAttribute = (node,name) => node.hasAttributeNS(SDH,name) ? node.getAttributeNS(SDH,name) : node.getAttributeNS(LEGACY_MC,name);
const $ = id => document.getElementById(id);
const visual = $('visual-editor'), source = $('source-editor');
const state = { path: '', title: '', baseUrl: '', baseSource: '', savedSource: '', xml: null,
  tab: 'visual', buildTarget: 'Desktop', appearance:'flare', visualDirty: false, sourceDirty: false, dirty: false,workspaceMode:false,unifiedViews:false,pageView:'formatted',editTab:'visual',editPosition:null,sharedFooters:{},
  protected: new Map(), elements: new Map(), range: null, selectedEquation: null,topics:[],linkTarget:null,linkRange:null,linkInitial:'',contextLink:null,pendingFontSize:null,
  equationTarget: null, equationRange: null, equationInitialValue:'',equationInitialDisplay:true,history: [], historyIndex: -1, historyTimer: null, restoring: false, previewGeneration:0 };
const ordinaryTags = new Set(('a abbr address article aside b bdi bdo blockquote br button caption cite code col colgroup dd del details dfn div dl dt em figcaption figure font footer h1 h2 h3 h4 h5 h6 header hr i img input ins kbd label legend li main mark nav ol p pre q rp rt ruby s samp section small span strike strong sub summary sup table tbody td tfoot th thead time tr tt u ul var wbr').split(' '));
const voidTags = new Set(['area','base','br','col','embed','hr','img','input','link','meta','param','source','track','wbr']);
// Clipboard HTML can move between independent editor windows. A source-node
// ID must belong to this document instance; another window's "element1"
// must never inherit an unrelated local node's conditions or attributes.
const editorInstance = crypto.randomUUID();
let sequence = 0;
MathfieldElement.fontsDirectory = new URL('./mathlive/fonts', location.href).href;
MathfieldElement.soundsDirectory = null;
const equationDialog=$('equation-dialog'),equationKeyboard=$('equation-keyboard'),mathKeyboard=window.mathVirtualKeyboard;
// A modal dialog makes the rest of the document inert. The shared MathLive
// keyboard must be inside that dialog, rather than behind its backdrop.
mathKeyboard.container=equationKeyboard;
function resizeEquationKeyboard(){
  const height=equationDialog.open&&mathKeyboard.visible?Math.ceil(mathKeyboard.boundingRect.height):0;
  if(equationKeyboard.style.height!==height+'px')equationKeyboard.style.height=height+'px';
  equationKeyboard.classList.toggle('is-visible',height>0);
}
function hideEquationKeyboard(){mathKeyboard.hide({animate:false});resizeEquationKeyboard();}
function hideEquationMenu(){
  // MathLive's menu lives in the field's shadow root. Closing its HTML dialog
  // alone can leave the menu scrim intercepting input when that dialog reopens.
  const field=$('equation-field');
  if(field.shadowRoot?.querySelector('menu:popover-open'))field.executeCommand('toggleContextMenu');
}
function closeEquationDialog(){hideEquationMenu();hideEquationKeyboard();equationDialog.close();}
mathKeyboard.addEventListener('geometrychange',resizeEquationKeyboard);
mathKeyboard.addEventListener('before-virtual-keyboard-toggle',event=>{if(event.detail?.visible&&!equationDialog.open)event.preventDefault();});
function equationClosed(){if(!equationDialog.open){hideEquationMenu();hideEquationKeyboard();post({action:'equation-dialog',open:false});post({action:'editor-dialog',open:modalState().dialogOpen,kind:modalState().dialogKind});}}
equationDialog.addEventListener('close',equationClosed);
// Also catch a programmatic/native close while MathLive's menu scrim is
// handling events. The native open attribute remains authoritative.
new MutationObserver(equationClosed).observe(equationDialog,{attributes:true,attributeFilter:['open']});
equationDialog.addEventListener('cancel',()=>{hideEquationMenu();hideEquationKeyboard();});
equationDialog.addEventListener('keydown',event=>{if(event.key==='Escape'){event.preventDefault();event.stopPropagation();closeEquationDialog();}},true);
window.addEventListener('pagehide',()=>{hideEquationMenu();hideEquationKeyboard();});

function post(message) { window.chrome?.webview?.postMessage(message); }
function modalState(){const kind=equationDialog.open?'equation':$('link-dialog').open?'link':null;return{dialogOpen:!!kind,dialogKind:kind,dialogDraftDirty:kind==='equation'?$('equation-field').value!==state.equationInitialValue||$('equation-display').checked!==state.equationInitialDisplay:kind==='link'?linkDraft()!==state.linkInitial:false};}
function showError(message = '') { $('error').textContent = message; $('error').hidden = !message; }
function parseXml(text) {
  const doc = new DOMParser().parseFromString(text, 'application/xml');
  const error = doc.querySelector('parsererror');
  if (error) throw new Error('The XHTML is not well formed. '+error.textContent.replace(/\s+/g,' ').slice(0,400));
  if (!doc.documentElement || doc.documentElement.localName.toLowerCase() !== 'html' || !findBody(doc)) throw new Error('The page needs an html root and a body element.');
  return doc;
}
function findBody(doc) { return Array.from(doc.documentElement.children).find(x=>x.localName.toLowerCase()==='body'); }
function findTitle(doc) { return Array.from(doc.getElementsByTagName('*')).find(x=>x.localName.toLowerCase()==='title'); }
function sourcePositionMapper(before,after){
  let prefix=0,suffix=0;while(prefix<before.length&&prefix<after.length&&before[prefix]===after[prefix])prefix++;
  while(suffix<before.length-prefix&&suffix<after.length-prefix&&before[before.length-1-suffix]===after[after.length-1-suffix])suffix++;
  const unique=(text,offset)=>{if(!text)return null;const found=after.indexOf(text);return found>=0&&after.indexOf(text,found+1)<0?found+offset:null;};
  return offset=>{
    if(offset<=prefix)return offset;
    if(offset>=before.length-suffix)return offset+after.length-before.length;
    // Multiple accepted changes can surround the caret. Prefer an unchanged,
    // unique text context before falling back to the nearest retained offset.
    const left=before.slice(Math.max(0,offset-48),offset),right=before.slice(offset,offset+48);
    return unique(left+right,left.length)??unique(right,0)??unique(left,left.length)??Math.min(offset,after.length-suffix);
  };
}
function canonicalizeHelpNamespaces(doc) {
  let used=false;
  for(let node of Array.from(doc.getElementsByTagName('*'))){
    if(isHelpNamespace(node.namespaceURI)){
      const replacement=doc.createElementNS(SDH,'sdh:'+node.localName);
      for(const attr of Array.from(node.attributes))replacement.setAttributeNS(attr.namespaceURI,attr.name,attr.value);
      replacement.append(...Array.from(node.childNodes));node.replaceWith(replacement);node=replacement;used=true;
    }
    const helpAttributes=Array.from(node.attributes).filter(attr=>isHelpNamespace(attr.namespaceURI));
    const attributeNames=new Set();
    for(const attr of helpAttributes){
      if(attributeNames.has(attr.localName))throw new Error('The source contains conflicting legacy and StatsDirect authoring attributes. Resolve the duplicate attributes in HTML source before saving.');
      attributeNames.add(attr.localName);
    }
    for(const attr of Array.from(node.attributes))if(attr.namespaceURI===XMLNS&&isHelpNamespace(attr.value))node.removeAttributeNode(attr);
    for(const attr of helpAttributes){node.removeAttributeNS(attr.namespaceURI,attr.localName);node.setAttributeNS(SDH,'sdh:'+attr.localName,attr.value);used=true;}
  }
  if(used)doc.documentElement.setAttributeNS(XMLNS,'xmlns:sdh',SDH);
  return doc;
}
function absolute(value) { try { return new URL(value, state.baseUrl).href; } catch { return value; } }
function withoutEditorImageKey(value) {
  // Keep authored query spelling intact. Only our document-specific cache key
  // is transient, including when an image is pasted from another editor window.
  const hash=value.indexOf('#'),end=hash<0?value.length:hash,query=value.indexOf('?');
  if(query<0||query>end)return value;
  const parts=value.slice(query+1,end).split('&'),kept=parts.filter(part=>!/^__statsdirect_editor_asset=editor-[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(part));
  return kept.length===parts.length?value:value.slice(0,query)+(kept.length?'?'+kept.join('&'):'')+value.slice(end);
}
function imageSource(value,baseUrl=state.baseUrl) {
  try {
    const url=new URL(value,baseUrl),base=new URL(baseUrl);
    if(url.origin!==base.origin||!['http:','https:'].includes(url.protocol))return url.href;
    // A remapped virtual host can retain bytes from another working copy in
    // WebView2's HTTP cache. Scope source artwork to this editor document.
    const clean=withoutEditorImageKey(url.href),hash=clean.indexOf('#'),end=hash<0?clean.length:hash;
    return clean.slice(0,end)+(clean.slice(0,end).includes('?')?'&':'?')+'__statsdirect_editor_asset=editor-'+editorInstance+clean.slice(end);
  } catch { return value; }
}
function projectRelativeUrl(value) {
  if(!/^(https?:)?\/\//i.test(value))return value;
  value=withoutEditorImageKey(value);
  try{
    const base=new URL(state.baseUrl),url=new URL(value,base),parts=base.pathname.split('/'),topicCount=state.path.split('/').filter(Boolean).length;
    const projectPrefix=parts.slice(0,parts.length-topicCount).join('/')+'/';
    if(url.origin!==base.origin||!url.pathname.startsWith(projectPrefix))return value;
    const from=parts.slice(0,-1),to=url.pathname.split('/');let same=0;while(same<from.length&&same<to.length&&from[same]===to[same])same++;
    return '../'.repeat(from.length-same)+to.slice(same).join('/')+url.search+url.hash;
  }catch{return value;}
}
function stampProtected(element,id,node){element.dataset.protectedId=id;element.dataset.editorProtectedXml=new XMLSerializer().serializeToString(node);element.dataset.editorSourceBase=state.baseUrl;}
function recoverProtected(element){
  const payload=element.dataset.editorProtectedXml;
  if(!payload||payload.length>20_000_000||/<!DOCTYPE/i.test(payload))return null;
  const doc=new DOMParser().parseFromString('<root xmlns:sdh="'+SDH+'" xmlns:MadCap="'+LEGACY_MC+'">'+payload+'</root>','application/xml');
  if(doc.querySelector('parsererror')||doc.documentElement.childNodes.length!==1)return null;
  const node=doc.documentElement.firstChild;if(![Node.ELEMENT_NODE,Node.COMMENT_NODE].includes(node.nodeType))return null;
  const oldBase=element.dataset.editorSourceBase;
  if(oldBase&&node.nodeType===Node.ELEMENT_NODE){
    for(const part of [node,...node.getElementsByTagName('*')])for(const attr of Array.from(part.attributes)){
      if(!['src','href'].includes(attr.localName.toLowerCase())||!validUrl(attr.value,attr.localName.toLowerCase()==='src'))continue;
      try{part.setAttributeNS(attr.namespaceURI,attr.name,projectRelativeUrl(new URL(attr.value,oldBase).href));}catch{}
    }
  }
  return node;
}
function validUrl(value, image = false) {
  try { const u = new URL(value, state.baseUrl); return ['http:','https:','mailto:'].includes(u.protocol) || (image && /^data:image\/(png|jpeg|gif|webp|svg\+xml);/i.test(value)); } catch { return false; }
}
function plainLatex(node) {
  const text = node.textContent.trim();
  if (node.children.length) return null;
  if (text.startsWith('$$') && text.endsWith('$$')) return text.slice(2,-2);
  return text.startsWith('$') && text.endsWith('$') ? text.slice(1,-1) : text;
}
function prepareLatexForPreview(latex) {
  let value=String(latex);
  const lines=[];let start=0,braces=0;const environments=[];
  for(let i=0;i<value.length;i++){
    const c=value[i];
    if(c==='\\'){
      const environment=value.slice(i).match(/^\\(begin|end)\s*\{([^}]+)\}/);
      if(environment){if(environment[1]==='begin')environments.push(environment[2]);else environments.pop();i+=environment[0].length-1;continue;}
      if(value[i+1]==='\\'){
        if(braces===0&&environments.length===0){lines.push(value.slice(start,i).trim());start=i+2;}
        i++;continue;
      }
      if(value[i+1]==='{'||value[i+1]==='}')i++;
    }else if(c==='{')braces++;else if(c==='}')braces--;
  }
  if(lines.length){lines.push(value.slice(start).trim());while(lines.length&&lines.at(-1)==='')lines.pop();value='\\begin{array}{l}\n'+lines.map(line=>'\\displaystyle '+line).join(' \\\\\n')+'\n\\end{array}';}
  return value;
}
function prepareLatexForMathLive(latex) {
  // These three legacy MathJax forms occur in this help corpus. Adapt only the
  // editing/rendering view; the original XML is retained until Apply is used.
  let value=String(latex);
  value=value.replace(/^\s*\\sf\b\s*([\s\S]*)$/,(_,body)=>'\\mathsf{'+body+'}');
  value=value.replace(/\\(?:big|Big|bigg|Bigg)[lmr]?\s*\//g,'/');
  return prepareLatexForPreview(value);
}
function makeId(prefix) { return prefix+editorInstance+'-'+(++sequence); }
function includedInPreview(node) {
  const conditions=(helpAttribute(node,'conditions')||'').split(/[;,]/).map(x=>x.trim());
  return !conditions.includes(state.buildTarget==='Public'?'Primary.SWHELP':'Primary.WEBHELP');
}
function annotateVariant(element,node) {
  const conditions=helpAttribute(node,'conditions');if(!conditions)return;
  element.dataset.editorCondition=conditions;
  const label=conditions==='Primary.SWHELP'?'Desktop help only':conditions==='Primary.WEBHELP'?'Public help only':'Content variant: '+conditions;
  element.title=(node.getAttribute('title')?node.getAttribute('title')+' — ':'')+label;
}
function setBuildTarget(target) {
  state.buildTarget=String(target).toLowerCase()==='public'?'Public':'Desktop';
  $('editor-hint').textContent='Double-click an equation to edit it. Preview: '+(state.buildTarget==='Public'?'public help.':'desktop help.');
  if(state.path&&state.tab==='preview'&&!state.unifiedViews)try{renderPreview();}catch(error){showError(error.message);}
  notifyView();
}
function workspaceView(){return state.unifiedViews?(state.pageView==='browser'?'public':state.pageView==='app'?'desktop':'edit'):state.tab==='preview'?(state.buildTarget==='Public'?'public':'desktop'):'edit';}
function notifyView(){document.body.dataset.workspaceView=workspaceView();document.body.dataset.pageView=state.pageView;post({action:'view',workspaceView:workspaceView(),pageView:state.pageView,editTab:state.editTab,tab:state.tab,target:state.buildTarget});}
function setWorkspaceMode(enabled=true){state.workspaceMode=!!enabled;document.body.classList.toggle('workspace-mode',state.workspaceMode);$('visual-tab').textContent=state.workspaceMode||state.unifiedViews?'Formatted':'Edit page';notifyView();}
function syncOutputTabs(){
  if(!state.unifiedViews)return;
  for(const button of document.querySelectorAll('[data-tab]'))button.setAttribute('aria-selected',String(button.dataset.tab===(state.pageView==='formatted'?'visual':state.pageView==='source'?'source':null)));
  for(const button of document.querySelectorAll('[data-output-view]'))button.setAttribute('aria-selected',String(button.dataset.outputView===state.pageView));
  $('preview-panel').setAttribute('aria-labelledby','browser-tab');
}
function setUnifiedViews(enabled=true){
  state.unifiedViews=!!enabled;document.body.classList.toggle('unified-views',state.unifiedViews);
  state.pageView=state.tab==='source'?'source':state.tab==='preview'?(state.buildTarget==='Public'?'browser':'app'):'formatted';
  $('visual-tab').textContent=state.unifiedViews||state.workspaceMode?'Formatted':'Edit page';
  syncOutputTabs();notifyView();return true;
}
function requestOutputPreview(view,reason='user'){
  if(!state.unifiedViews||!['browser','app'].includes(view)||!state.path||modalState().dialogOpen)return false;
  try{getSource();}catch{return false;}
  post({action:'preview-request',view,reason});return true;
}
// The native host validates/builds first, then accepts the selected output view.
// Keep state.tab tied to the real editing buffer so source drafts stay authoritative.
function setOutputView(view){
  if(!['formatted','source','browser','app'].includes(view)||!state.path||modalState().dialogOpen)return false;
  if(!state.unifiedViews)return view==='formatted'?setTab('visual'):view==='source'?setTab('source'):setWorkspaceView(view==='browser'?'public':'desktop');
  try{getSource();}catch{return false;}
  if(view==='browser'){
    if(state.tab!=='preview')rememberEditPosition();
    state.pageView='browser';state.buildTarget='Public';
    if(!setTab('preview'))return false;
    $('editor-hint').textContent='Browser view: generated website output.';
  }else if(view==='app'){
    const previous=state.pageView;
    if(state.tab!=='preview')rememberEditPosition();
    state.pageView='app';state.buildTarget='Desktop';
    if(state.tab==='preview'&&!setTab(state.editTab)){state.pageView=previous;return false;}
    restoreEditPosition();
    $('editor-hint').textContent='App view: edit this page alongside the dockable StatsDirect help pane.';
  }else{
    const wanted=view==='source'?'source':'visual',wasPreview=state.tab==='preview';
    state.pageView=view;
    if(state.tab!==wanted&&!setTab(wanted))return false;
    if(wasPreview)restoreEditPosition();
    $('editor-hint').textContent='Double-click an equation to edit it.';
  }
  syncOutputTabs();notifyView();return true;
}
function setGeneratedPreview(url,preserveExisting=false){
  if(!state.unifiedViews)return false;
  let parsed;try{parsed=new URL(url,location.href);}catch{return false;}
  if(!['http:','https:','file:'].includes(parsed.protocol))return false;
  const current=$('preview-frame');
  if(preserveExisting&&current.hasAttribute('src')&&!current.hasAttribute('srcdoc')){
    const previous=new URL(current.src),requested=new URL(parsed.href);
    previous.searchParams.delete('__help_preview');requested.searchParams.delete('__help_preview');
    // Native authoring state has verified the generated site's inputs. Keep
    // the reader's own scroll, search and expanded sections on an unchanged
    // view revisit; a different page/anchor still gets a fresh navigation.
    if(previous.href===requested.href){$('preview-loading').hidden=true;return true;}
  }
  const frame=document.createElement('iframe');frame.id='preview-frame';frame.title='Generated website help';frame.setAttribute('sandbox','allow-scripts');
  frame.addEventListener('load',()=>{if($('preview-frame')===frame)$('preview-loading').hidden=true;});
  parsed.searchParams.set('__help_preview',String(++state.previewGeneration));
  $('preview-frame').replaceWith(frame);frame.removeAttribute('srcdoc');frame.src=parsed.href;return true;
}
function setSharedFooters(footers){state.sharedFooters=footers&&typeof footers==='object'?footers:{};if(state.path&&state.tab==='preview'&&!state.unifiedViews)renderPreview();}
// These fixed build palettes apply only to the preview document. Authored
// typography, chart/SVG/MathML colours and the editing DOM keep their source.
const previewAppearanceCss='html[data-help-skin="flare"] body{background:#fff}html[data-help-skin] *,html[data-help-skin] *::before,html[data-help-skin] *::after{box-sizing:border-box}@media screen{html[data-help-skin] body{transform:translateZ(0);padding-top:calc(1em + .1px)}}html[data-help-skin="flare"] body img{max-width:none}html[data-help-skin="clean-blue"] body{background:#f4f8fc!important}html[data-help-skin="clean-blue"] body a:not(svg a):not(math a){color:#124f7c!important}html[data-help-skin="warm-paper"] body{background:#fffaf0!important}html[data-help-skin="warm-paper"] body a:not(svg a):not(math a){color:#704310!important}';
function setAppearance(key){
  if(!['flare','readable','clean-blue','warm-paper'].includes(key)||modalState().dialogOpen)return false;
  state.appearance=key;
  if(state.unifiedViews)return true; // Native host rebuilds after persisting the design.
  const frame=$('preview-frame');
  // Reuse the already rendered preview source rather than asking the editor
  // for HTML. This also works while an incomplete HTML-source draft is open.
  if(frame.srcdoc)frame.srcdoc=frame.srcdoc.replace(/(<html\b[^>]*\bdata-help-skin=")[^"]*(")/,'$1'+key+'$2');
  return true;
}
function rememberEditPosition(){
  if(state.tab==='visual'){rememberSelection();state.editPosition={tab:'visual',range:state.range?.cloneRange(),scrollTop:$('visual-panel').scrollTop,scrollLeft:$('visual-panel').scrollLeft};}
  else if(state.tab==='source')state.editPosition={tab:'source',start:source.selectionStart,end:source.selectionEnd,direction:source.selectionDirection,scrollTop:source.scrollTop,scrollLeft:source.scrollLeft};
}
function restoreEditPosition(){
  const position=state.editPosition;if(!position||position.tab!==state.tab)return;
  if(state.tab==='visual'){restoreSelection(position.range);$('visual-panel').scrollTop=position.scrollTop;$('visual-panel').scrollLeft=position.scrollLeft;}
  else{source.focus();source.setSelectionRange(position.start,position.end,position.direction);source.scrollTop=position.scrollTop;source.scrollLeft=position.scrollLeft;}
}
function setWorkspaceView(view){
  if(!['edit','desktop','public'].includes(view)||!state.path||modalState().dialogOpen)return false;
  if(state.unifiedViews)return setOutputView(view==='public'?'browser':view==='desktop'?'app':state.editTab==='source'?'source':'formatted');
  if(view==='edit'){if(state.tab!=='preview'){notifyView();return true;}if(!setTab(state.editTab))return false;restoreEditPosition();return true;}
  // Validate before changing either the target or visible view. An incomplete
  // source edit must remain available for correction in the editing workspace.
  try{getSource();}catch{notifyView();return false;}
  if(state.tab!=='preview')rememberEditPosition();
  state.buildTarget=view==='public'?'Public':'Desktop';
  $('editor-hint').textContent='Current page content preview: '+(state.buildTarget==='Public'?'website.':'StatsDirect app.');
  return setTab('preview');
}
function setDirty(value = true) {
  if (state.dirty !== value) { state.dirty = value; post({action:'dirty', dirty:value, path:state.path}); }
  $('change-state').textContent = !state.path ? 'No page open' : value ? 'Unsaved changes' : 'Saved';
}

function equationWidget(node, id) {
  const span = document.createElement('span');
  span.className = 'equation-widget'; span.contentEditable = 'false'; stampProtected(span,id,node); span.dataset.widgetKind = 'equation';
  span.setAttribute('role','button'); span.tabIndex = 0; span.title = 'Double-click to edit equation';
  if (node.getAttribute('style')) span.setAttribute('style',node.getAttribute('style'));
  const latex = plainLatex(node);
  if (latex === null) { span.textContent = 'MathML equation — edit in HTML source'; span.classList.add('equation-error'); }
  else {
    span.setAttribute('aria-label','Equation: '+latex.slice(0,180));
    try { span.innerHTML = convertLatexToMarkup(prepareLatexForMathLive(latex)); }
    catch { span.textContent = latex; span.classList.add('equation-error'); }
  }
  return span;
}
function protectedWidget(node) {
  const id = makeId('protected'); state.protected.set(id,node.cloneNode(true));
  if (isHelpNamespace(node.namespaceURI) && node.localName.toLowerCase() === 'equation') return equationWidget(node,id);
  const element = document.createElement('span'); stampProtected(element,id,node); element.contentEditable = 'false';
  if (isHelpNamespace(node.namespaceURI) && node.localName.toLowerCase() === 'keyword') { element.className='metadata-widget'; return element; }
  element.className = 'protected-block';
  const header = document.createElement('header'), label = document.createElement('strong'), button = document.createElement('button');
  const isDropDown = isHelpNamespace(node.namespaceURI) && node.localName.toLowerCase() === 'dropdown';
  const hotspot = Array.from(node.getElementsByTagName('*')).find(x=>x.localName.toLowerCase()==='dropdownhotspot');
  label.textContent = isDropDown ? hotspot?.textContent.trim() || 'Expandable section' : node.localName.toLowerCase()==='script' ? 'Page script' : node.localName;
  button.type='button'; button.textContent='Edit HTML'; button.dataset.sourceWidget=id;
  header.append(label,button); element.append(header);
  const pre = Array.from(node.getElementsByTagName('*')).find(x=>x.localName.toLowerCase()==='pre');
  if (pre) { const details=document.createElement('details'),summary=document.createElement('summary'),code=document.createElement('pre');summary.textContent='Show code';code.textContent=pre.textContent;details.append(summary,code);element.append(details); }
  else if (node.localName.toLowerCase()!=='script') { const text=document.createElement('div');text.className='protected-summary';text.textContent=node.textContent.trim().slice(0,400);element.append(text); }
  return element;
}
function editableNode(node) {
  if (node.nodeType === Node.TEXT_NODE || node.nodeType === Node.CDATA_SECTION_NODE) return document.createTextNode(node.nodeValue);
  if (node.nodeType === Node.COMMENT_NODE) { const id=makeId('comment');state.protected.set(id,node.cloneNode(true));const s=document.createElement('span');s.className='metadata-widget';s.contentEditable='false';stampProtected(s,id,node);return s; }
  if (node.nodeType !== Node.ELEMENT_NODE) return document.createTextNode('');
  const tag=node.localName.toLowerCase();
  if (isHelpNamespace(node.namespaceURI) || !ordinaryTags.has(tag) || node.namespaceURI && node.namespaceURI!=='http://www.w3.org/1999/xhtml') return protectedWidget(node);
  const element=document.createElement(tag),id=makeId('element');state.elements.set(id,node.cloneNode(false));element.dataset.sourceNode=id;
  for (const attr of node.attributes) {
    if (/^on/i.test(attr.name) || attr.name.startsWith('xmlns') || attr.name.toLowerCase()==='contenteditable') continue;
    try {
      if (['src','href'].includes(attr.name.toLowerCase())) {
        if (validUrl(attr.value, tag==='img')) { element.setAttribute(attr.name,tag==='img'&&attr.name.toLowerCase()==='src'?imageSource(attr.value):absolute(attr.value));element.dataset['original'+(attr.name==='src'?'Src':'Href')]=attr.value; }
      } else element.setAttribute(attr.name,attr.value);
    } catch { /* Attributes unsupported by HTML remain on the original XML node. */ }
  }
  const namespaced=Object.fromEntries(Array.from(node.attributes).filter(a=>a.namespaceURI&&a.namespaceURI!==XMLNS).map(a=>[a.name.toLowerCase(),{name:a.name,namespace:a.namespaceURI}]));
  if(Object.keys(namespaced).length)element.dataset.editorNamespaces=JSON.stringify(namespaced);
  annotateVariant(element,node);
  for(const child of node.childNodes)element.append(editableNode(child));
  return element;
}
function renderVisual() {
  state.protected.clear();state.elements.clear();state.range=null;state.selectedEquation=null;state.pendingFontSize=null; $('edit-equation').disabled=true;
  visual.replaceChildren();
  for(const child of findBody(state.xml).childNodes)visual.append(editableNode(child));
}
function normalizeProtectedCopies() {
  const seen=new Set();
  for(const element of visual.querySelectorAll('[data-protected-id]')){
    let id=element.dataset.protectedId;
    const current=state.protected.get(id),payload=element.dataset.editorProtectedXml;
    if(!current||payload&&payload!==new XMLSerializer().serializeToString(current)||element.dataset.editorSourceBase&&element.dataset.editorSourceBase!==state.baseUrl){
      const recovered=recoverProtected(element);
      if(recovered){const fresh=makeId('protected');state.protected.set(fresh,recovered);for(const button of element.querySelectorAll('[data-source-widget]'))if(button.dataset.sourceWidget===id)button.dataset.sourceWidget=fresh;id=fresh;stampProtected(element,id,recovered);}
    }
    if(seen.has(id)&&state.protected.has(id)){
      // An HTML clipboard paste duplicates the widget markup. Give each copy
      // its own source node, so editing one equation cannot change its sibling.
      const fresh=makeId('protected');state.protected.set(fresh,state.protected.get(id).cloneNode(true));
      stampProtected(element,fresh,state.protected.get(fresh));
      for(const button of element.querySelectorAll('[data-source-widget]'))if(button.dataset.sourceWidget===id)button.dataset.sourceWidget=fresh;
      id=fresh;
    }
    seen.add(id);
  }
}
function importVisual(node, doc) {
  if(node.nodeType===Node.TEXT_NODE)return doc.createTextNode(node.nodeValue);
  if(node.nodeType!==Node.ELEMENT_NODE)return null;
  if(node.dataset.protectedId) {
    const original=state.protected.get(node.dataset.protectedId);
    if(!original)throw new Error('An equation or specialised section could not be restored. Use Undo or reopen the page before saving.');
    return doc.importNode(original,true);
  }
  const tag=node.localName.toLowerCase();
  if(!ordinaryTags.has(tag))return doc.createTextNode(node.textContent);
  const original=state.elements.get(node.dataset.sourceNode);
  const result=original && original.localName.toLowerCase()===tag ? doc.importNode(original,false) : doc.createElement(tag);
  for(const attr of node.attributes) {
    if(attr.name.startsWith('data-source-')||attr.name.startsWith('data-original-')||attr.name.startsWith('data-editor-')||attr.name==='contenteditable'||attr.name==='title'&&node.dataset.editorCondition||/^on/i.test(attr.name))continue;
    let value=attr.value;
    if(['src','href'].includes(attr.name)) {
      const sourceValue=node.dataset[attr.name==='src'?'originalSrc':'originalHref'];
      if(sourceValue!==undefined && (value===(tag==='img'&&attr.name==='src'?imageSource(sourceValue):absolute(sourceValue))||withoutEditorImageKey(value)===absolute(sourceValue)))value=sourceValue;
      else value=projectRelativeUrl(value);
      if(!validUrl(value,tag==='img'))continue;
    }
    const old=original && Array.from(original.attributes).find(a=>a.name.toLowerCase()===attr.name.toLowerCase());
    let pastedNamespace;try{pastedNamespace=JSON.parse(node.dataset.editorNamespaces||'{}')[attr.name.toLowerCase()];}catch{}
    try { if(old?.namespaceURI)result.setAttributeNS(old.namespaceURI,old.name,value);else if(pastedNamespace?.namespace&&pastedNamespace?.name)result.setAttributeNS(pastedNamespace.namespace,pastedNamespace.name,value);else result.setAttribute(old?.name||attr.name,value); }
    catch { /* Preserve the original attribute if the browser cannot represent it. */ }
  }
  if(!voidTags.has(tag))for(const child of node.childNodes){const imported=importVisual(child,doc);if(imported)result.append(imported);}
  return result;
}
function serializeVisual() {
  normalizeProtectedCopies();
  const doc=state.xml.cloneNode(true),body=findBody(doc);body.replaceChildren();
  for(const node of visual.childNodes){const imported=importVisual(node,doc);if(imported)body.append(imported);}
  canonicalizeHelpNamespaces(doc);
  const declaration=state.baseSource.match(/^\uFEFF?(<\?xml[^>]*\?>)[\r\n]*/)?.[1];
  let result=new XMLSerializer().serializeToString(doc);
  if(declaration && !result.startsWith('<?xml'))result=declaration+'\n'+result;
  if(state.baseSource.includes('\r\n'))result=result.replace(/\r?\n/g,'\r\n');
  parseXml(result);
  return result;
}
function getSource() {
  if(!state.path)return '';
  try {
    if(state.tab==='source') { const value=state.sourceDirty?source.value:state.baseSource;parseXml(value);return value; }
    return state.visualDirty ? serializeVisual() : state.baseSource;
  } catch(error) { showError(error.message);throw error; }
}
function currentSnapshot() { return state.tab==='source' ? state.sourceDirty?source.value:state.baseSource : state.visualDirty ? serializeVisual() : state.baseSource; }
function captureHistory() {
  clearTimeout(state.historyTimer);state.historyTimer=null;
  if(!state.path||state.restoring)return false;
  let text;try{text=currentSnapshot();}catch{$('undo').disabled=state.historyIndex<0;$('redo').disabled=true;return false;}
  if(state.history[state.historyIndex]===text)return true;
  state.history.splice(state.historyIndex+1);state.history.push(text);state.historyIndex=state.history.length-1;
  while(state.history.length>80 || state.history.length>2 && state.history.reduce((n,s)=>n+s.length,0)>5_000_000){state.history.shift();state.historyIndex--;}
  updateUndoButtons();
  return true;
}
function scheduleHistory() { clearTimeout(state.historyTimer);state.historyTimer=setTimeout(captureHistory,220); }
function updateUndoButtons(){ $('undo').disabled=state.historyIndex<=0;$('redo').disabled=state.historyIndex>=state.history.length-1; }
function restoreSnapshot(text) {
  state.restoring=true;
  try {
    state.baseSource=text;source.value=text;state.visualDirty=false;state.sourceDirty=false;
    try{state.xml=parseXml(text);renderVisual();showError();if(state.tab==='preview'){if(state.unifiedViews)requestOutputPreview('browser');else renderPreview();}}
    catch(error){if(state.unifiedViews)state.pageView='source';setVisibleTab('source');showError(error.message);}
    setDirty(text!==state.savedSource);
  } finally{state.restoring=false;updateUndoButtons();updateToolbar();}
}
function undo(){
  if(!captureHistory()){
    // A damaged specialised paste has no serializable snapshot. Undo removes
    // that paste by restoring the latest good draft, rather than skipping it.
    if(state.historyIndex>=0)restoreSnapshot(state.history[state.historyIndex]);return;
  }
  if(state.historyIndex>0){state.historyIndex--;restoreSnapshot(state.history[state.historyIndex]);}
}
function redo(){captureHistory();if(state.historyIndex<state.history.length-1){state.historyIndex++;restoreSnapshot(state.history[state.historyIndex]);}}
// Accepted AI suggestions are one ordinary draft edit. Keep the disk baseline
// and the existing history so Undo restores every edit made before the review.
function applyReviewedSource(path,expectedSource,newSource){
  if(typeof path!=='string'||typeof expectedSource!=='string'||typeof newSource!=='string')throw new Error('The reviewed page is invalid.');
  if(!state.path||state.path!==path)throw new Error('The current page changed. Review the current page again.');
  if(modalState().dialogOpen)throw new Error('Apply or Cancel the open editing dialog before accepting suggestions.');
  if(getSource()!==expectedSource)throw new Error('The page changed after this review. Review the current text again.');
  parseXml(newSource); // Refuse invalid output before touching the draft/history.
  if(newSource===expectedSource)return true;
  const selection={text:source.value,start:source.selectionStart,end:source.selectionEnd,direction:source.selectionDirection,top:source.scrollTop,left:source.scrollLeft};
  const visualPanel=$('visual-panel'),visualPosition={top:visualPanel.scrollTop,left:visualPanel.scrollLeft},visualSelection=reviewSelectionBookmark();
  captureHistory();
  restoreSnapshot(newSource);
  captureHistory();
  if(state.tab==='source'){
    const position=sourcePositionMapper(selection.text,source.value);
    source.setSelectionRange(position(selection.start),position(selection.end),selection.direction);
    source.scrollTop=selection.top;source.scrollLeft=selection.left;
  }
  if(state.tab==='visual')restoreReviewSelection(visualSelection);
  visualPanel.scrollTop=visualPosition.top;visualPanel.scrollLeft=visualPosition.left;
  const title=findTitle(state.xml)?.textContent.trim();if(title){state.title=title;$('page-title').textContent=title;}
  return true;
}
function setVisibleTab(name) {
  state.tab=name;
  if(state.unifiedViews){
    if(name==='preview')state.pageView='browser';
    else if(state.pageView!=='app')state.pageView=name==='source'?'source':'formatted';
  }
  document.body.dataset.workspaceView=workspaceView();
  if(name!=='preview')state.editTab=name;
  for(const button of document.querySelectorAll('[data-tab]'))button.setAttribute('aria-selected',String(button.dataset.tab===name));
  for(const namePart of ['visual','source','preview'])$(namePart+'-panel').hidden=namePart!==name;
  $('editor-toolbar').hidden=name!=='visual';
  syncOutputTabs();
  notifyView();
}
function setTab(name) {
  if(!['visual','source','preview'].includes(name)||!state.path||modalState().dialogOpen)return false;
  const enteringPreview=name==='preview'&&state.tab!=='preview';
  try{
    captureHistory();
    if(state.tab==='source'){
      const value=state.sourceDirty?source.value:state.baseSource;state.xml=parseXml(value);state.baseSource=value;state.visualDirty=false;state.sourceDirty=false;renderVisual();
    }else if(state.visualDirty){state.baseSource=serializeVisual();state.xml=parseXml(state.baseSource);state.visualDirty=false;}
    source.value=state.baseSource;setVisibleTab(name);showError();
    if(name==='preview'){if(state.unifiedViews){if(enteringPreview)$('preview-loading').hidden=false;}else renderPreview();}
    else if(name==='source')source.focus();
    return true;
  }catch(error){showError(error.message);if(state.unifiedViews)state.pageView='source';setVisibleTab('source');return false;}
}
function previewNode(node) {
  if(node.nodeType===Node.TEXT_NODE||node.nodeType===Node.CDATA_SECTION_NODE)return document.createTextNode(node.nodeValue);
  if(node.nodeType!==Node.ELEMENT_NODE)return document.createTextNode('');
  if(!includedInPreview(node))return document.createTextNode('');
  const tag=node.localName.toLowerCase();
  if(isHelpNamespace(node.namespaceURI)){
    if(tag==='keyword')return document.createTextNode('');
    if(tag==='equation'){
      const original=equationArtwork.create(node,document,prepareLatexForPreview);
      if(original)return original;
      const span=document.createElement('span');span.className='help-equation help-equation-live';
      if(node.getAttribute('style'))span.setAttribute('style',node.getAttribute('style'));
      if(node.children.length){span.innerHTML=new XMLSerializer().serializeToString(node.firstElementChild);}
      else {const delimiters=node.textContent.trim().startsWith('$$')?'$$':'$';span.textContent=delimiters+prepareLatexForPreview(plainLatex(node))+delimiters;}
      return span;
    }
    if(tag==='dropdown'){
      const details=document.createElement('details'),summary=document.createElement('summary');
      if(node.getAttribute('class'))details.className=node.getAttribute('class');
      const hotspot=Array.from(node.getElementsByTagName('*')).find(x=>x.localName.toLowerCase()==='dropdownhotspot');
      if(hotspot?.getAttribute('class'))summary.className=hotspot.getAttribute('class');
      if(hotspot)for(const child of hotspot.childNodes)summary.append(previewNode(child));else summary.textContent='More information';details.append(summary);
      const body=Array.from(node.getElementsByTagName('*')).find(x=>x.localName.toLowerCase()==='dropdownbody');
      if(body&&includedInPreview(body)){const content=document.createElement('div');for(const name of ['class','style','id','lang','dir'])if(body.hasAttribute(name))content.setAttribute(name,body.getAttribute(name));for(const c of body.childNodes)content.append(previewNode(c));details.append(content);}return details;
    }
    const span=document.createElement('span');for(const c of node.childNodes)span.append(previewNode(c));return span;
  }
  if(tag==='script'||tag==='iframe'||tag==='object'||tag==='embed')return document.createTextNode('');
  const element=document.createElement(ordinaryTags.has(tag)?tag:'span');
  for(const attr of node.attributes){
    if(/^on/i.test(attr.name)||attr.name.startsWith('xmlns')||attr.name.toLowerCase()==='contenteditable')continue;
    if(['src','href'].includes(attr.name.toLowerCase())){if(validUrl(attr.value,tag==='img'))element.setAttribute(attr.name,tag==='img'&&attr.name.toLowerCase()==='src'?imageSource(attr.value):absolute(attr.value));}
    else try{element.setAttribute(attr.name,attr.value);}catch{}
  }
  for(const c of node.childNodes)element.append(previewNode(c));return element;
}
function renderPreview(){
  const doc=parseXml(getSource()),fragment=document.createElement('div');for(const child of findBody(doc).childNodes)fragment.append(previewNode(child));
  const shared=state.sharedFooters[state.buildTarget];
  if(shared?.html){
    // Shared output furniture belongs only to the preview, never the topic's
    // editing DOM or XML. Parse it inertly and resolve its own resource base.
    const footerDocument=new DOMParser().parseFromString(String(shared.html),'text/html'),footer=document.createElement('footer');footer.className='help-shared-footer';
    for(const element of footerDocument.querySelectorAll('script,iframe,object,embed,base,meta,link'))element.remove();
    for(const element of footerDocument.querySelectorAll('*'))for(const attr of Array.from(element.attributes)){
      if(/^on/i.test(attr.name)||['srcdoc','contenteditable'].includes(attr.name.toLowerCase()))element.removeAttribute(attr.name);
      else if(['href','src'].includes(attr.name.toLowerCase())){try{const value=new URL(attr.value,shared.baseUrl||state.baseUrl).href;if(validUrl(value,attr.name.toLowerCase()==='src'))element.setAttribute(attr.name,value);else element.removeAttribute(attr.name);}catch{element.removeAttribute(attr.name);}}
    }
    footer.append(...Array.from(footerDocument.body.childNodes));fragment.append(footer);
  }
  const styleLinks=Array.from(doc.getElementsByTagName('*')).filter(x=>x.localName.toLowerCase()==='link'&&/stylesheet/i.test(x.getAttribute('rel')||'')).map(x=>absolute(x.getAttribute('href')||'')).filter(url=>validUrl(url));
  const escape=s=>String(s).replaceAll('&','&amp;').replaceAll('"','&quot;').replaceAll('<','&lt;');
  const mathjax=new URL('./mathjax/tex-mml-svg.js',location.href).href;
  const generation=++state.previewGeneration;
  const mathConfig=JSON.stringify({startup:{typeset:false},tex:{inlineMath:[['$','$'],['\\(','\\)']]},options:{enableMenu:false,ignoreHtmlClass:'help-math-ignore',processHtmlClass:'help-equation-live'}});
  const linkBridge='document.addEventListener("click",function(event){var anchor=event.target instanceof Element?event.target.closest("a[href]"):null;if(!anchor)return;event.preventDefault();parent.postMessage({type:"help-editor-preview-link",generation:'+generation+',href:anchor.href},"*");});';
  // Give every preview its own document lifecycle. A hidden, reused srcdoc
  // frame can retain stale layout state after an equation dialog is applied.
  const frame=document.createElement('iframe');frame.id='preview-frame';frame.title='Help page preview';frame.setAttribute('sandbox','allow-scripts');$('preview-frame').replaceWith(frame);
  // Match generated pages: preserve SVG glyph overhang and bound only the
  // visually hidden MathML copy, without removing its accessibility content.
  frame.srcdoc='<!doctype html><html data-help-skin="'+state.appearance+'"><head><meta charset="utf-8">'+styleLinks.map(h=>'<link rel="stylesheet" href="'+escape(h)+'">').join('')+'<style>body{margin:0;padding:1em;font:10pt Arial,sans-serif;line-height:normal;-webkit-font-smoothing:antialiased;-moz-osx-font-smoothing:grayscale}img{max-width:100%;height:auto}details.rcode{margin:1em 0}details.rcode>summary{cursor:pointer;color:#2c5f8a;font-weight:bold}details.rcode>div{margin-left:18px}details.rcode>summary{list-style:none}details.rcode>summary::-webkit-details-marker{display:none}details.rcode>summary:before{content:"";display:inline-block;width:.45em;height:.45em;border-right:.15em solid #2c5f8a;border-bottom:.15em solid #2c5f8a;margin-left:.1em;margin-right:.65em;vertical-align:.12em;transform:rotate(-45deg);transition:transform .15s ease}details.rcode[open]>summary:before{transform:rotate(45deg);vertical-align:.22em}.help-equation{display:inline-block;max-width:100%;overflow:visible;vertical-align:middle}.help-equation mjx-container{max-width:100%}.help-equation mjx-assistive-mml{width:1px!important;height:1px!important}</style><style id="help-preview-appearance">'+equationArtworkCss+previewAppearanceCss+'</style><script>window.helpPreviewGeneration='+generation+';window.MathJax='+mathConfig+';</script><script src="'+escape(mathjax)+'"></script></head><body>'+fragment.innerHTML+'<script>'+linkBridge+'window.helpPreviewTypesetPromise=MathJax.startup.promise.then(function(){return MathJax.typesetPromise(Array.from(document.querySelectorAll(".help-equation-live")));}).then(function(){window.helpPreviewReady=true;});</script></body></html>';
}
window.addEventListener('message',event=>{
  const data=event.data,frame=$('preview-frame');
  if(state.tab!=='preview'||event.source!==frame.contentWindow||data?.type!=='help-editor-preview-link'||data.generation!==state.previewGeneration||typeof data.href!=='string'||!validUrl(data.href))return;
  post({action:'open-link',href:absolute(data.href),newWindow:false});
});
function load(payload){
  const previousWorkspace=state.workspaceMode?workspaceView():'edit';
  const previousPageView=state.pageView,previousEditTab=state.editTab;
  clearTimeout(state.historyTimer);state.historyTimer=null;state.pendingFontSize=null;state.editPosition=null;showError();
  hideEquationMenu();hideEquationKeyboard();if($('equation-dialog').open)closeEquationDialog();if($('link-dialog').open)closeLinkDialog();hideLinkContext();
  state.path=payload.path||'';state.title=payload.title||payload.path||'Untitled';state.baseUrl=payload.baseUrl||'';state.baseSource=payload.source||'';state.savedSource=state.baseSource;state.visualDirty=false;state.sourceDirty=false;
  const retainedView=state.path?previousPageView:'formatted';
  if(state.unifiedViews)state.pageView=retainedView;
  state.buildTarget=String(payload.target||'Desktop').toLowerCase()==='public'?'Public':'Desktop';
  if(state.unifiedViews&&['browser','app'].includes(retainedView))state.buildTarget=retainedView==='browser'?'Public':'Desktop';
  if(payload.appearance)state.appearance=['flare','readable','clean-blue','warm-paper'].includes(payload.appearance)?payload.appearance:'flare';
  $('editor-hint').textContent='Double-click an equation to edit it. Preview: '+(state.buildTarget==='Public'?'public help.':'desktop help.');
  $('page-title').textContent=state.title;$('page-path').textContent=state.path;source.value=state.baseSource;
  state.history=[state.baseSource];state.historyIndex=0;
  // Parse the new page before committing its visible view. A cached Browser
  // handoff must not expose the formatted editor or its full loading overlay.
  try{
    state.xml=parseXml(state.baseSource);renderVisual();
    const tab=state.unifiedViews?(retainedView==='browser'?'preview':retainedView==='source'||retainedView==='app'&&previousEditTab==='source'?'source':'visual'):'visual';
    setVisibleTab(tab);
    if(state.unifiedViews&&retainedView==='browser')$('editor-hint').textContent='Browser view: generated website output.';
    else if(state.unifiedViews&&retainedView==='app')$('editor-hint').textContent='App view: edit this page alongside the dockable StatsDirect help pane.';
  }
  catch(error){state.xml=null;visual.replaceChildren();if(state.unifiedViews)state.pageView='source';setVisibleTab('source');showError(error.message);}
  if(payload.topics)setTopics(payload.topics);setDirty(false);updateUndoButtons();updateToolbar();if(payload.fragment)revealFragment(payload.fragment);
  if(state.unifiedViews){
    if(state.xml&&['browser','app'].includes(retainedView))requestOutputPreview(retainedView,'load');
  }else if(previousWorkspace!=='edit'&&state.xml)setWorkspaceView(previousWorkspace);
}
function markSaved(authoritativeSource){
  const current=getSource(),text=authoritativeSource===undefined?current:authoritativeSource;
  if(typeof text!=='string')throw new Error('The saved page source is invalid.');
  const xml=parseXml(text);
  if(text!==current){
    // The host can normalize an imported namespace before writing the page.
    // Adopt those exact disk bytes without adding a normalization-only Undo.
    captureHistory();state.history[state.historyIndex]=text;
  }
  if(source.value!==text){
    const old=source.value,selection={start:source.selectionStart,end:source.selectionEnd,direction:source.selectionDirection,top:source.scrollTop,left:source.scrollLeft};
    const position=sourcePositionMapper(old,text);
    source.value=text;source.setSelectionRange(position(selection.start),position(selection.end),selection.direction);source.scrollTop=selection.top;source.scrollLeft=selection.left;
  }
  state.savedSource=text;state.baseSource=text;state.xml=xml;state.visualDirty=false;state.sourceDirty=false;setDirty(false);captureHistory();
  const title=findTitle(state.xml)?.textContent.trim();if(title){state.title=title;$('page-title').textContent=title;}
}
function rememberSelection(){const s=getSelection();if(s.rangeCount&&visual.contains(s.anchorNode)&&visual.contains(s.focusNode)){state.range=s.getRangeAt(0).cloneRange();updateToolbar();}}
function reviewSelectionBookmark(){
  const selection=getSelection();
  if(!selection.rangeCount||!visual.contains(selection.anchorNode)||!visual.contains(selection.focusNode))return null;
  const point=(node,offset)=>{
    const parent=node.nodeType===Node.ELEMENT_NODE?node:node.parentElement,block=parent.closest('p,h1,h2,h3,h4,h5,h6,li,td,th,pre,div')||visual;
    const path=[];for(let part=block;part!==visual;part=part.parentNode)path.unshift(Array.prototype.indexOf.call(part.parentNode.childNodes,part));
    const range=document.createRange();range.selectNodeContents(block);range.setEnd(node,offset);
    return{id:block===visual?null:block.id,path,tag:block.localName,text:block.textContent,offset:range.toString().length};
  };
  return{anchor:point(selection.anchorNode,selection.anchorOffset),focus:point(selection.focusNode,selection.focusOffset)};
}
function restoreReviewSelection(bookmark){
  if(!bookmark)return;
  const point=saved=>{
    let block=saved.id?Array.from(visual.querySelectorAll('[id]')).find(element=>element.id===saved.id):null;
    if(!block){block=visual;for(const index of saved.path){block=block?.childNodes[index];if(!block)break;}}
    if(!block||block.localName!==saved.tag||block.textContent!==saved.text&&!saved.id){
      const matches=Array.from(visual.getElementsByTagName(saved.tag)).filter(element=>element.textContent===saved.text);if(matches.length===1)block=matches[0];
    }
    if(!block||!visual.contains(block))block=visual;
    const walker=document.createTreeWalker(block,NodeFilter.SHOW_TEXT);let remaining=sourcePositionMapper(saved.text,block.textContent)(saved.offset),last=null;
    for(let node;(node=walker.nextNode());){last=node;if(remaining<=node.nodeValue.length)return{node,offset:remaining};remaining-=node.nodeValue.length;}
    return last?{node:last,offset:last.nodeValue.length}:{node:block,offset:0};
  };
  const anchor=point(bookmark.anchor),focus=point(bookmark.focus);visual.focus();getSelection().setBaseAndExtent(anchor.node,anchor.offset,focus.node,focus.offset);rememberSelection();
}
function restoreSelection(range=state.range){visual.focus();const selection=getSelection();selection.removeAllRanges();if(range?.startContainer.isConnected&&visual.contains(range.startContainer))selection.addRange(range);else{const r=document.createRange();r.selectNodeContents(visual);r.collapse(false);selection.addRange(r);}}
function visualChanged(){applyPendingFontSize();normalizeProtectedCopies();state.visualDirty=true;setDirty(true);showError();scheduleHistory();}
function selectEquation(element){normalizeProtectedCopies();for(const other of visual.querySelectorAll('.equation-widget.selected'))other.classList.remove('selected');state.selectedEquation=element?.dataset.protectedId||null;if(element)element.classList.add('selected');$('edit-equation').disabled=!state.selectedEquation;}
function openEquation(id=null){
  if(!state.path)return;
  captureHistory();
  const existing=id&&state.protected.get(id);let latex=existing?plainLatex(existing):'\\frac{a}{b}';
  if(latex===null){showError('This MathML equation is preserved. Edit it in HTML source, or replace it with a LaTeX equation.');setTab('source');return;}
  const originalLatex=latex,displayStyle=existing?/^\s*(?:\\sf\s*)?\\displaystyle\b/.test(latex):true;
  latex=prepareLatexForMathLive(latex);
  $('equation-dialog').querySelector('.dialog-note').textContent=latex!==originalLatex?'This equation uses older LaTeX formatting. Applying edits saves the compatible notation shown here.':'Type mathematical notation or use the symbol keyboard.';
  state.equationTarget=id;state.equationRange=state.range?.cloneRange();$('equation-heading').textContent=existing?'Edit equation':'Insert equation';
  $('equation-display').checked=displayStyle;latex=latex.replace(/^\s*\\displaystyle\s*/,'');
  $('equation-field').value=latex;$('equation-latex').value=latex;state.equationInitialValue=$('equation-field').value;state.equationInitialDisplay=$('equation-display').checked;$('equation-error').hidden=true;$('equation-dialog').showModal();post({action:'equation-dialog',open:true});post({action:'editor-dialog',open:true,kind:'equation'});setTimeout(()=>$('equation-field').focus(),0);
}
function applyEquation(){
  const field=$('equation-field'),latex=field.getValue('latex-without-placeholders').trim(),errors=field.errors||[];
  if(!latex||errors.length){$('equation-error').textContent=!latex?'Enter an equation before applying it.':'Please correct the LaTeX before applying it: '+errors.map(e=>e.code||e.message||String(e)).join(', ');$('equation-error').hidden=false;return;}
  const node=state.equationTarget?state.protected.get(state.equationTarget).cloneNode(true):state.xml.createElementNS(SDH,'sdh:equation');
  node.replaceChildren(state.xml.createTextNode('$'+($('equation-display').checked?'\\displaystyle ':'')+latex+'$'));
  if(state.equationTarget){const id=state.equationTarget;state.protected.set(id,node);const old=visual.querySelector('[data-protected-id="'+id+'"]');if(!old){$('equation-error').textContent='The selected equation is no longer in the page. Cancel and select it again.';$('equation-error').hidden=false;return;}old.replaceWith(equationWidget(node,id));selectEquation(visual.querySelector('[data-protected-id="'+id+'"]'));}
  else{
    const id=makeId('protected');state.protected.set(id,node);
    // A modal dialog makes the page inert. Close it before returning the caret
    // to the editor; otherwise an insertion can target the equation field.
    closeEquationDialog();restoreSelection(state.equationRange);
    const selection=getSelection(),range=selection.getRangeAt(0),widget=equationWidget(node,id),space=document.createTextNode(' ');
    range.deleteContents();range.insertNode(widget);widget.after(space);range.setStart(space,1);range.collapse(true);selection.removeAllRanges();selection.addRange(range);state.range=range.cloneRange();selectEquation(widget);
  }
  closeEquationDialog();state.visualDirty=true;setDirty(true);captureHistory();
}
function widgetSourceOrdinal(id){
  // Follow the same visual-to-XML rules as importVisual. A protected widget
  // represents its complete original subtree, including hidden R markup.
  let before=0,found=false;
  function visit(node){
    if(found||node.nodeType!==Node.ELEMENT_NODE)return;
    if(node.dataset.protectedId){
      const original=state.protected.get(node.dataset.protectedId);
      if(node.dataset.protectedId===id){found=true;return;}
      if(original?.nodeType===Node.ELEMENT_NODE)before+=1+original.getElementsByTagName('*').length;
    }else if(ordinaryTags.has(node.localName.toLowerCase())){
      before++;
      if(!voidTags.has(node.localName.toLowerCase()))for(const child of node.childNodes)visit(child);
    }
  }
  for(const child of visual.childNodes)visit(child);
  const body=findBody(state.xml),all=Array.from(state.xml.getElementsByTagName('*'));
  return found?all.indexOf(body)+1+before:null;
}
function sourceElementAt(text,ordinal){
  let count=0;
  for(let at=0;(at=text.indexOf('<',at))>=0;){
    if(text.startsWith('<!--',at)){at=text.indexOf('-->',at)+3;continue;}
    if(text.startsWith('<![CDATA[',at)){at=text.indexOf(']]>',at)+3;continue;}
    if(text.startsWith('<?',at)){at=text.indexOf('?>',at)+2;continue;}
    let end=at+1,quote='',brackets=0;
    for(;end<text.length;end++){
      const c=text[end];
      if(quote){if(c===quote)quote='';}
      else if(c==='"'||c==="'")quote=c;
      else if(text.startsWith('<!',at)&&c==='[')brackets++;
      else if(text.startsWith('<!',at)&&c===']')brackets--;
      else if(c==='>'&&brackets===0)break;
    }
    const name=text.slice(at,end+1).match(/^<([^\s/!?<>]+)/)?.[1];
    if(name&&count++===ordinal)return{start:at,end:at+name.length+1};
    at=end+1;
  }
  return null;
}
function focusWidgetSource(id){
  normalizeProtectedCopies();
  const ordinal=widgetSourceOrdinal(id);
  if(!setTab('source')||ordinal===null)return;
  const selected=sourceElementAt(source.value,ordinal);
  if(selected){source.focus();source.setSelectionRange(selected.start,selected.end);const line=source.value.slice(0,selected.start).split('\n').length;source.scrollTop=Math.max(0,(line-3)*parseFloat(getComputedStyle(source).lineHeight));}
}
function format(command,value){if(!state.path||modalState().dialogOpen)return;captureHistory();restoreSelection();if(command==='removeFormat')state.pendingFontSize=null;document.execCommand('styleWithCSS',false,true);document.execCommand(command,false,value);rememberSelection();visualChanged();captureHistory();updateToolbar();}
function insertImage(src){
  if(!state.path||modalState().dialogOpen)return;captureHistory();restoreSelection();
  const selection=getSelection(),range=selection.getRangeAt(0),image=document.createElement('img');image.src=imageSource(src);image.dataset.originalSrc=src;
  range.deleteContents();range.insertNode(image);range.setStartAfter(image);range.collapse(true);selection.removeAllRanges();selection.addRange(range);
  rememberSelection();visualChanged();captureHistory();updateToolbar();
}
function updateToolbar(){
  if(!state.path||state.tab!=='visual')return;
  for(const button of document.querySelectorAll('[data-command][aria-pressed]'))try{button.setAttribute('aria-pressed',String(document.queryCommandState(button.dataset.command)));}catch{}
  const selection=getSelection(),node=selection.rangeCount?(selection.anchorNode.nodeType===Node.ELEMENT_NODE?selection.anchorNode:selection.anchorNode.parentElement):null;
  if(!node||!visual.contains(node))return;
  const style=getComputedStyle(node),block=node.closest('p,h1,h2,h3,pre,div,li');if(block&&Array.from($('block-style').options).some(o=>o.value===block.localName))$('block-style').value=block.localName;
  const points=Math.round(parseFloat(style.fontSize)*.75);$('font-size').value=String(points);const family=style.fontFamily.split(',')[0].replace(/["']/g,'');$('font-family').value=Array.from($('font-family').options).some(o=>o.value===family)?family:'Arial';
}
function setFontSize(points){
  if(!state.path||modalState().dialogOpen)return;captureHistory();restoreSelection();document.execCommand('styleWithCSS',false,false);document.execCommand('fontSize',false,'7');const selection=getSelection();
  state.pendingFontSize=selection.isCollapsed?points:null;
  for(const font of visual.querySelectorAll('font[size="7"]'))if(selection.containsNode(font,true)){font.removeAttribute('size');font.style.fontSize=points+'pt';}
  document.execCommand('styleWithCSS',false,true);rememberSelection();visualChanged();captureHistory();updateToolbar();
}
function applyPendingFontSize(){
  if(!state.pendingFontSize)return;const selection=getSelection();let node=selection.anchorNode?.nodeType===Node.ELEMENT_NODE?selection.anchorNode:selection.anchorNode?.parentElement;
  while(node&&node!==visual){if(node.localName==='font'&&node.getAttribute('size')==='7'||node.style?.fontSize==='xxx-large'){node.removeAttribute('size');node.style.fontSize=state.pendingFontSize+'pt';state.pendingFontSize=null;return;}node=node.parentElement;}
}
function projectRoot(){const base=new URL(state.baseUrl),count=state.path.split('/').filter(Boolean).length;base.pathname=base.pathname.split('/').slice(0,-count).join('/')+'/';base.search='';base.hash='';return base;}
function setTopics(topics){state.topics=Array.isArray(topics)?topics.filter(x=>x&&typeof x.path==='string').map(x=>({path:x.path.replaceAll('\\','/'),title:String(x.title||x.path)})):[];if($('link-dialog').open)filterTopics($('link-page').value);}
function filterTopics(selected=''){
  const query=$('link-search').value.trim().toLocaleLowerCase(),items=state.topics.filter(x=>(x.title+' '+x.path).toLocaleLowerCase().includes(query));$('link-page').replaceChildren();
  for(const topic of items){const option=new Option(topic.title+' — '+topic.path,topic.path);option.title=topic.path;$('link-page').append(option);}
  if(items.some(x=>x.path===selected))$('link-page').value=selected;else if(items.length)$('link-page').selectedIndex=0;
  $('link-result-count').textContent=items.length?items.length+' help page'+(items.length===1?'':'s'):'No matching help pages.';
}
function selectedAnchor(){const selection=getSelection(),node=selection.anchorNode?.nodeType===Node.ELEMENT_NODE?selection.anchorNode:selection.anchorNode?.parentElement;const link=node?.closest('a[href]');return link&&visual.contains(link)?link:null;}
function setLinkMode(mode){for(const radio of document.querySelectorAll('[name="link-mode"]'))radio.checked=radio.value===mode;$('internal-link-fields').hidden=mode!=='internal';$('external-link-fields').hidden=mode!=='external';$('link-open').textContent=mode==='internal'?'Open in new window':'Open in browser';$('link-error').hidden=true;}
function linkMode(){return document.querySelector('[name="link-mode"]:checked').value;}
function linkDraft(){return JSON.stringify({mode:linkMode(),page:$('link-page').value,fragment:$('link-fragment').value,url:$('link-url').value,text:$('link-text').value});}
function openLink(anchor=null){
  if(!state.path||modalState().dialogOpen)return;rememberSelection();captureHistory();hideLinkContext();state.linkTarget=anchor||selectedAnchor();state.linkRange=state.range?.cloneRange();
  const existing=state.linkTarget,selection=state.linkRange?.toString()||'';$('link-heading').textContent=existing?'Edit link':'Insert link';$('link-apply').textContent=existing?'Apply link':'Insert link';$('link-remove').hidden=!existing;$('link-open').hidden=false;$('link-text').value=existing?existing.textContent:selection;$('link-search').value='';$('link-fragment').value='';$('link-url').value=existing?existing.getAttribute('href'):'https://';filterTopics();
  let internal=null;try{if(existing){const url=new URL(existing.getAttribute('href'),state.baseUrl),root=projectRoot();if(url.origin===root.origin&&url.pathname.startsWith(root.pathname)){const relative=decodeURI(url.pathname.slice(root.pathname.length));internal=state.topics.find(t=>t.path===relative);if(internal){$('link-page').value=internal.path;try{$('link-fragment').value=decodeURIComponent(url.hash.slice(1));}catch{$('link-fragment').value=url.hash.slice(1);}}}}}catch{}
  setLinkMode(existing&&!internal?'external':'internal');$('link-error').hidden=true;state.linkInitial=linkDraft();$('link-dialog').showModal();post({action:'editor-dialog',open:true,kind:'link'});setTimeout(()=>(linkMode()==='internal'?$('link-search'):$('link-url')).focus(),0);
}
function closeLinkDialog(){if($('link-dialog').open)$('link-dialog').close();}
function linkClosed(){if(!$('link-dialog').open)post({action:'editor-dialog',open:modalState().dialogOpen,kind:modalState().dialogKind});}
function linkDestination(){
  if(linkMode()==='internal'){const topic=state.topics.find(t=>t.path===$('link-page').value);if(!topic)throw new Error('Choose a help page from the list.');const url=new URL(topic.path,projectRoot()),fragment=$('link-fragment').value.trim().replace(/^#/,'');if(state.linkTarget){const old=new URL(state.linkTarget.getAttribute('href'),state.baseUrl);if(old.origin===url.origin&&old.pathname===url.pathname)url.search=old.search;}if(fragment)url.hash=fragment;return url.href;}
  const value=$('link-url').value.trim();let url;try{url=new URL(value);}catch{throw new Error('Enter a complete website address starting with https://, or an email link starting with mailto:.');}if(!['http:','https:','mailto:'].includes(url.protocol))throw new Error('Use an https:// website or mailto: email address.');return url.href;
}
function applyLink(){
  let href;try{href=linkDestination();}catch(error){$('link-error').textContent=error.message;$('link-error').hidden=false;return;}
  const target=state.linkTarget,text=$('link-text').value,initial=JSON.parse(state.linkInitial).text,range=state.linkRange?.cloneRange();closeLinkDialog();restoreSelection(range);
  if(target?.isConnected&&visual.contains(target)){target.setAttribute('href',href);target.dataset.originalHref=projectRelativeUrl(href);if(text!==initial)target.textContent=text||href;const r=document.createRange();r.selectNodeContents(target);getSelection().removeAllRanges();getSelection().addRange(r);}
  else if(range&&!range.collapsed&&text===initial)document.execCommand('createLink',false,href);
  else{const selection=getSelection(),r=selection.getRangeAt(0),a=document.createElement('a');a.href=href;a.textContent=text||state.topics.find(t=>t.path===$('link-page').value)?.title||href;r.deleteContents();r.insertNode(a);r.selectNodeContents(a);selection.removeAllRanges();selection.addRange(r);}
  rememberSelection();visualChanged();captureHistory();updateToolbar();
}
function removeLink(anchor=state.linkTarget){if(!anchor?.isConnected||!visual.contains(anchor))return;captureHistory();closeLinkDialog();const range=document.createRange();range.selectNodeContents(anchor);const children=Array.from(anchor.childNodes);anchor.replaceWith(...children);visual.focus();const selection=getSelection();selection.removeAllRanges();if(children.length){range.setStartBefore(children[0]);range.setEndAfter(children.at(-1));selection.addRange(range);}rememberSelection();visualChanged();captureHistory();}
function openLinkedPage(anchor=state.linkTarget){if(!anchor)return;const href=anchor.getAttribute('href');if(validUrl(href))post({action:'open-link',href:absolute(href),newWindow:true});}
function openDraftLink(){try{post({action:'open-link',href:linkDestination(),newWindow:true});$('link-error').hidden=true;}catch(error){$('link-error').textContent=error.message;$('link-error').hidden=false;}}
function hideLinkContext(){$('link-context-menu').hidden=true;}
function showLinkContext(event,anchor){event.preventDefault();state.contextLink=anchor;state.range=null;const range=document.createRange();range.selectNodeContents(anchor);state.range=range;const menu=$('link-context-menu');menu.hidden=false;const internal=new URL(anchor.href,state.baseUrl).origin===new URL(state.baseUrl).origin;$('context-open-link').textContent=internal?'Open linked page in new window':'Open in browser';menu.style.left=Math.min(event.clientX,innerWidth-menu.offsetWidth-8)+'px';menu.style.top=Math.min(event.clientY,innerHeight-menu.offsetHeight-8)+'px';$('context-edit-link').focus();}
function revealFragment(fragment){try{fragment=decodeURIComponent(String(fragment).replace(/^#/,''));}catch{}const target=Array.from(visual.querySelectorAll('[id],[name]')).find(e=>e.id===fragment||e.getAttribute('name')===fragment);if(target)target.scrollIntoView({block:'start'});return!!target;}
function requestSave(){
  if(equationDialog.open){$('equation-error').textContent='Apply or cancel the equation before saving the page.';$('equation-error').hidden=false;return false;}
  if($('link-dialog').open){$('link-error').textContent='Apply or cancel the link before saving the page.';$('link-error').hidden=false;return false;}
  try{post({action:'save',path:state.path,source:getSource()});return true;}catch{return false;}
}

for(const button of document.querySelectorAll('[data-tab]'))button.addEventListener('click',()=>state.unifiedViews?setOutputView(button.dataset.tab==='source'?'source':button.dataset.tab==='visual'?'formatted':'browser'):setTab(button.dataset.tab));
for(const button of document.querySelectorAll('[data-output-view]'))button.addEventListener('click',()=>requestOutputPreview(button.dataset.outputView));
for(const button of document.querySelectorAll('[data-command]')){
  button.addEventListener('mousedown',e=>e.preventDefault());
  button.addEventListener('click',()=>button.dataset.command==='undo'?undo():button.dataset.command==='redo'?redo():format(button.dataset.command));
}
$('block-style').addEventListener('change',e=>format('formatBlock',e.target.value));
$('font-family').addEventListener('change',e=>format('fontName',e.target.value));
$('font-size').addEventListener('change',e=>setFontSize(e.target.value));
for(const id of ['block-style','font-family','font-size','text-colour','highlight-colour'])$(id).addEventListener('mousedown',rememberSelection);
$('text-colour').addEventListener('input',e=>format('foreColor',e.target.value));
$('highlight-colour').addEventListener('input',e=>format('hiliteColor',e.target.value));
$('insert-link').addEventListener('mousedown',e=>e.preventDefault());
$('insert-link').addEventListener('click',()=>openLink());
$('link-form').addEventListener('submit',e=>{e.preventDefault();applyLink();});
$('link-cancel').addEventListener('click',closeLinkDialog);$('link-remove').addEventListener('click',()=>removeLink());$('link-open').addEventListener('click',openDraftLink);
$('link-dialog').addEventListener('close',linkClosed);new MutationObserver(linkClosed).observe($('link-dialog'),{attributes:true,attributeFilter:['open']});
$('link-search').addEventListener('input',()=>filterTopics($('link-page').value));
$('link-search').addEventListener('keydown',e=>{if(e.key==='ArrowDown'){e.preventDefault();$('link-page').focus();}});
$('link-page').addEventListener('dblclick',applyLink);
for(const radio of document.querySelectorAll('[name="link-mode"]'))radio.addEventListener('change',()=>setLinkMode(radio.value));
for(const element of $('link-form').querySelectorAll('input,select'))element.addEventListener('input',()=>{$('link-error').hidden=true;});
$('context-edit-link').addEventListener('click',()=>openLink(state.contextLink));
$('context-open-link').addEventListener('click',()=>{hideLinkContext();openLinkedPage(state.contextLink);});
$('context-remove-link').addEventListener('click',()=>{hideLinkContext();removeLink(state.contextLink);});
document.addEventListener('pointerdown',e=>{if(!e.target.closest('#link-context-menu'))hideLinkContext();});
window.addEventListener('resize',hideLinkContext);$('visual-panel').addEventListener('scroll',hideLinkContext);
$('link-context-menu').addEventListener('keydown',e=>{const items=Array.from($('link-context-menu').querySelectorAll('button'));if(e.key==='Escape'){e.preventDefault();hideLinkContext();visual.focus();}else if(['ArrowDown','ArrowUp'].includes(e.key)){e.preventDefault();const i=items.indexOf(document.activeElement);items[(i+(e.key==='ArrowDown'?1:-1)+items.length)%items.length].focus();}});
$('insert-image').addEventListener('mousedown',e=>e.preventDefault());
$('insert-image').addEventListener('click',()=>{const src=prompt('Image path relative to this page:','../resources/Images/');if(src===null)return;if(!src.trim()||!validUrl(src,true)){showError('Enter a relative image path or an https image URL.');return;}insertImage(src);});
$('insert-equation').addEventListener('mousedown',e=>e.preventDefault());$('insert-equation').addEventListener('click',()=>openEquation());
$('edit-equation').addEventListener('click',()=>openEquation(state.selectedEquation));
$('equation-cancel').addEventListener('click',closeEquationDialog);$('equation-apply').addEventListener('click',applyEquation);
$('equation-form').addEventListener('submit',e=>e.preventDefault());
$('equation-field').addEventListener('input',()=>{$('equation-latex').value=$('equation-field').value;$('equation-error').hidden=true;});
$('equation-latex').addEventListener('input',()=>{$('equation-field').setValue($('equation-latex').value,{silenceNotifications:true});$('equation-error').hidden=true;});
$('save-page').addEventListener('click',requestSave);
visual.addEventListener('input',visualChanged);
visual.addEventListener('pointerdown',()=>{state.pendingFontSize=null;});
visual.addEventListener('click',e=>{const sourceButton=e.target.closest('[data-source-widget]');if(sourceButton){e.preventDefault();focusWidgetSource(sourceButton.dataset.sourceWidget);return;}const eq=e.target.closest('.equation-widget');selectEquation(eq);if(e.target.closest('a'))e.preventDefault();});
visual.addEventListener('dblclick',e=>{const eq=e.target.closest('.equation-widget');if(eq){e.preventDefault();openEquation(eq.dataset.protectedId);return;}const link=e.target.closest('a[href]');if(link){e.preventDefault();openLink(link);}});
visual.addEventListener('contextmenu',e=>{const link=e.target.closest('a[href]');if(link)showLinkContext(e,link);});
visual.addEventListener('keydown',e=>{if(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End','PageUp','PageDown'].includes(e.key))state.pendingFontSize=null;if((e.key==='Enter'||e.key===' ')&&e.target.closest('.equation-widget')){e.preventDefault();openEquation(e.target.closest('.equation-widget').dataset.protectedId);}});
source.addEventListener('input',()=>{state.sourceDirty=true;setDirty(true);showError();scheduleHistory();});
source.addEventListener('keydown',e=>{if(e.key==='Tab'){e.preventDefault();const start=source.selectionStart,end=source.selectionEnd;source.setRangeText('    ',start,end,'end');source.dispatchEvent(new Event('input',{bubbles:true}));}});
document.addEventListener('selectionchange',rememberSelection);
document.addEventListener('keydown',e=>{
  if(modalState().dialogOpen){if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='s'){e.preventDefault();requestSave();}return;}
  if(!(e.ctrlKey||e.metaKey)||e.altKey)return;
  if(e.shiftKey&&e.key.toLowerCase()==='h'){e.preventDefault();post({action:'search-replace'});}
  if(e.key.toLowerCase()==='s'){e.preventDefault();requestSave();}
  if(e.key.toLowerCase()==='z'){e.preventDefault();e.shiftKey?redo():undo();}
  if(e.key.toLowerCase()==='y'){e.preventDefault();redo();}
  if(e.key.toLowerCase()==='k'&&state.tab==='visual'){e.preventDefault();openLink();}
});
window.HelpEditor={load,getSource,markSaved,applyReviewedSource,setTab,setBuildTarget,setAppearance,setWorkspaceMode,setWorkspaceView,setUnifiedViews,setOutputView,setGeneratedPreview,setSharedFooters,setTopics,revealFragment,undo,redo,openEquation,openLink,prepareLatexForMathLive,prepareLatexForPreview,requestSave,getState:()=>({path:state.path,dirty:state.dirty,tab:state.tab,buildTarget:state.buildTarget,appearance:state.appearance,workspaceMode:state.workspaceMode,unifiedViews:state.unifiedViews,pageView:state.pageView,workspaceView:workspaceView(),editTab:state.editTab,protectedCount:state.protected.size,historyIndex:state.historyIndex,previewGeneration:state.previewGeneration,equationDialogOpen:equationDialog.open,equationDraftDirty:equationDialog.open&&($('equation-field').value!==state.equationInitialValue||$('equation-display').checked!==state.equationInitialDisplay),...modalState()})};
post({action:'ready'});
