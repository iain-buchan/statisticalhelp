(() => {
  'use strict';
  const contents = document.getElementById('contents'), results = document.getElementById('results'), frame = document.getElementById('topic');
  if(!contents||!results||!frame)return; // Explicit ?content=1 diagnostic view.
  const articleMode=frame.tagName==='ARTICLE';
  const index = window.StatsDirectSearch, tree = window.StatsDirectContents;
  const flare = document.documentElement.dataset.helpSkin === 'flare';
  const viewerRoot = new URL(document.documentElement.dataset.helpRoot||'.',location.href);
  function decodedPath(value) { try{return decodeURIComponent(value);}catch{return value;} }
  function localTopicPath(value) {
    if(typeof value!=='string'||!value||value.trim()!==value)return false;
    const path=decodedPath(value);
    return !/[:\\?#\x00-\x1f\x7f]/.test(path)&&!path.split('/').some(part=>!part||part==='.'||part==='..')&&/\.html?$/i.test(path);
  }
  const documents=Array.isArray(window.StatsDirectTopics)?window.StatsDirectTopics.filter(doc=>doc&&localTopicPath(doc.url)&&typeof doc.title==='string'):index.documents;
  const catalogue = new Map(documents.map(doc=>[decodedPath(doc.url).toLowerCase(),doc]));
  function resolveTopic(value) {
    if(typeof value!=='string'||!value)return null;
    try {
      const target=new URL(value,viewerRoot);
      if(target.origin!==viewerRoot.origin||target.host!==viewerRoot.host||!target.pathname.startsWith(viewerRoot.pathname))return null;
      const doc=catalogue.get(decodedPath(target.pathname.slice(viewerRoot.pathname.length)).toLowerCase());
      if(!doc)return null;
      const link=doc.url+target.search+target.hash;
      return {path:doc.url,title:doc.title,fragment:decodedPath(target.hash),link,url:new URL(link,viewerRoot).href};
    }catch{return null;}
  }
  function scrollSelection() { const selected=document.getElementById('keyword-index')?.hidden===false?document.querySelector('#index-entries a.index-selected'):contents.querySelector('a.selected');if(selected?.offsetHeight)(selected.closest('li')||selected).scrollIntoView({block:'nearest',inline:'nearest'}); }
  const menu = document.getElementById('navigation-toggle'), shade = document.querySelector('.navigation-shade');
  function navigation(open) { if(!open)saveNavigationState();document.body.classList.toggle('navigation-open',open); if(menu)menu.setAttribute('aria-expanded',String(open)); if(shade)shade.hidden=!open;if(open){if(navigationMode==='index')navigationPane.scrollTop=indexScroll;else scrollSelection();} }
  if(menu)menu.addEventListener('click',()=>navigation(!document.body.classList.contains('navigation-open')));
  if(shade)shade.addEventListener('click',()=>navigation(false));
  document.addEventListener('keydown',e=>{
    if(e.key!=='Escape')return;
    const fromNavigation=contents.contains(document.activeElement)||document.getElementById('help-navigation')?.contains(document.activeElement);
    if(!document.body.classList.contains('navigation-open')&&!fromNavigation)return;
    navigation(false);
    // Native hosts hide the reader header; the wide reader also hides its toggle.
    // Return focus to the topic instead of a button that cannot receive it.
    if(menu?.getClientRects().length)menu.focus({preventScroll:true});
    if(!menu?.getClientRects().length||document.activeElement!==menu){
      if(articleMode&&!frame.hasAttribute('tabindex'))frame.tabIndex=-1;
      frame.focus({preventScroll:true});
    }
    e.preventDefault();
  });
  matchMedia('(max-width:1279px)').addEventListener('change',()=>navigation(false));
  const tabs = [document.getElementById('contents-tab'), document.getElementById('index-tab')];
  const keywordPanel=document.getElementById('keyword-index'), keywordFilter=document.getElementById('index-filter'), keywordList=document.getElementById('index-entries'), keywordStatus=document.getElementById('index-status');
  const navigationPane=document.getElementById('help-navigation'), nativeDesktop=document.documentElement.dataset.helpTarget==='desktop', navigationKey='statsdirect-help-navigation:'+viewerRoot.href;
  let navigationMode='contents', restoredSelection='', indexScroll=0, restoringNavigation=true;
  if(!nativeDesktop)try {
    const saved=JSON.parse(sessionStorage.getItem(navigationKey)||'null');
    if(saved){navigationMode=saved.mode==='index'?'index':'contents';if(typeof saved.filter==='string')keywordFilter.value=saved.filter;if(typeof saved.selected==='string')restoredSelection=saved.selected;if(Number.isFinite(saved.scroll)&&saved.scroll>=0)indexScroll=saved.scroll;}
  }catch{/* Storage can be unavailable for local files or browser privacy settings. */}
  let keywordRows=[], selectedKeyword=0, keywordEntries=null, renderedKeywordFilter=null;
  function saveNavigationState() {
    if(nativeDesktop||restoringNavigation)return;
    if(!keywordPanel.hidden&&navigationPane.getClientRects().length)indexScroll=navigationPane.scrollTop;
    restoredSelection=keywordRows[selectedKeyword]?.dataset.indexKey||restoredSelection;
    try{sessionStorage.setItem(navigationKey,JSON.stringify({mode:navigationMode,filter:keywordFilter.value,selected:restoredSelection,scroll:indexScroll}));}catch{/* Reading help does not require browser storage. */}
  }
  window.addEventListener('pagehide',saveNavigationState);
  let activeTopic=(articleMode?resolveTopic(new URLSearchParams(location.hash.slice(1)).has('searchQuery')?location.href.split('#')[0]:location.href):null)||resolveTopic(document.documentElement.dataset.currentTopic||frame.getAttribute('src')||index.documents[0]?.url);
  function showPanel(mode) {
    if(mode!=='search')navigationMode=mode;
    const panel=mode==='search'&&flare?navigationMode:mode;
    contents.hidden=panel!=='contents';keywordPanel.hidden=panel!=='index';results.hidden=mode!=='search';
    if(flare)frame.hidden=mode==='search';
    tabs.forEach((tab,position)=>{const active=position===['contents','index'].indexOf(panel);tab.classList.toggle('active',active);tab.setAttribute('aria-pressed',String(active));});
    document.title=mode==='search'?'Search':activeTopic?.title||'StatsDirect Help';
  }
  function showResults(on) { if(on)saveNavigationState();showPanel(on?'search':navigationMode);if(navigationMode==='index'){renderKeywords();if(navigationPane.getClientRects().length)navigationPane.scrollTop=indexScroll;} }
  const keywordCollator=new Intl.Collator('en',{sensitivity:'base',numeric:true});
  const foldKeyword=value=>value.normalize('NFD').replace(/\p{M}/gu,'').toLocaleLowerCase('en');
  function loadKeywords() {
    const data=window.STATSDIRECT_HELP_KEYWORDS;
    if(!data){keywordStatus.textContent='The keyword index is not included in this help build.';return false;}
    try {
      if(data.schemaVersion!==1||!Array.isArray(data.entries)||!Array.isArray(window.StatsDirectTopics))throw Error('Unsupported or incomplete keyword data.');
      const seen=new Set(), entries=[];
      for(const entry of data.entries){
        if(!entry||typeof entry.term!=='string'||!entry.term.trim()||typeof entry.title!=='string'||!entry.title.trim()||!localTopicPath(entry.path))throw Error('Invalid keyword entry.');
        const target=catalogue.get(decodedPath(entry.path).toLowerCase());
        if(!target)throw Error('A keyword refers to a topic not included in this help build.');
        const key=entry.term.toLocaleLowerCase('en')+'\0'+target.url.toLocaleLowerCase('en');
        if(!seen.has(key)){seen.add(key);entries.push({term:entry.term,title:entry.title,path:target.url});}
      }
      entries.sort((a,b)=>keywordCollator.compare(a.term,b.term)||keywordCollator.compare(a.title,b.title)||keywordCollator.compare(a.path,b.path));
      const groups=new Map();
      for(const entry of entries){const key=entry.term.toLocaleLowerCase('en');if(!groups.has(key))groups.set(key,[]);groups.get(key).push(entry);}
      keywordEntries=entries.map(entry=>{const group=groups.get(entry.term.toLocaleLowerCase('en')),ambiguous=group.length>1,sameTitle=group.filter(other=>other.title.toLocaleLowerCase('en')===entry.title.toLocaleLowerCase('en')).length>1;return {...entry,label:entry.term+(ambiguous?' — '+entry.title:'')+(ambiguous&&sameTitle?' ('+entry.path+')':'')};});
      return true;
    }catch(error){keywordEntries=null;keywordStatus.textContent='Could not open the help index: '+error.message;return false;}
  }
  function selectKeyword(position,focus=false) {
    selectedKeyword=Math.max(0,Math.min(position,keywordRows.length-1));
    keywordRows.forEach((row,i)=>{row.classList.toggle('index-selected',i===selectedKeyword);row.tabIndex=i===selectedKeyword?0:-1;});
    if(focus&&keywordRows[selectedKeyword]){keywordRows[selectedKeyword].focus({preventScroll:true});keywordRows[selectedKeyword].scrollIntoView({block:'nearest'});saveNavigationState();}
  }
  function renderKeywords() {
    if(keywordEntries!==null&&renderedKeywordFilter===keywordFilter.value)return;
    const previous=keywordRows[selectedKeyword]?.dataset.indexKey||restoredSelection;
    keywordList.replaceChildren();keywordRows=[];
    if(keywordEntries===null&&!loadKeywords())return;
    const query=foldKeyword(keywordFilter.value.trim()),matches=keywordEntries.filter(entry=>foldKeyword(entry.term+' '+entry.title).includes(query));
    keywordStatus.textContent=matches.length?'':query?'No matching keywords. Try fewer words or a topic title.':'No keywords have been authored for this help build.';
    for(const entry of matches){const li=document.createElement('li'),link=makeLink(entry.label,entry.path);link.dataset.indexKey=JSON.stringify([entry.path,entry.label]);link.addEventListener('focus',()=>{selectKeyword(keywordRows.indexOf(link));saveNavigationState();});li.append(link);keywordList.append(li);keywordRows.push(link);}
    selectKeyword(Math.max(0,keywordRows.findIndex(row=>row.dataset.indexKey===previous)));
    renderedKeywordFilter=keywordFilter.value;
  }
  keywordFilter.addEventListener('input',()=>{renderKeywords();indexScroll=0;navigationPane.scrollTop=0;saveNavigationState();});
  keywordFilter.addEventListener('keydown',event=>{
    if(event.key==='ArrowDown'||event.key==='ArrowUp'){event.preventDefault();selectKeyword(event.key==='ArrowUp'?keywordRows.length-1:selectedKeyword,true);}
    else if(event.key==='Enter'){event.preventDefault();keywordRows[selectedKeyword]?.click();}
  });
  keywordList.addEventListener('keydown',event=>{
    const position=keywordRows.indexOf(event.target.closest('a'));
    if(position<0)return;
    if(['ArrowDown','ArrowUp','Home','End'].includes(event.key)){event.preventDefault();selectKeyword(event.key==='Home'?0:event.key==='End'?keywordRows.length-1:position+(event.key==='ArrowDown'?1:-1),true);}
    else if(event.key==='Enter'){event.preventDefault();keywordRows[position].click();}
  });
  function selectTopic(link) { const target=resolveTopic(link);if(!target)return;const links=[...document.querySelectorAll('#contents a')].map(a=>({a,topic:resolveTopic(a.dataset.link)}));const exact=target.fragment?links.find(item=>item.topic?.path===target.path&&item.topic.fragment===target.fragment):null,current=(exact||links.find(item=>item.topic?.path===target.path))?.a;links.forEach(({a})=>{const selected=a===current;a.classList.toggle('selected',selected);if(selected){a.setAttribute('aria-current','page');for(let p=a.parentElement;p&&p!==contents;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;}else a.removeAttribute('aria-current');});scrollSelection(); }
  let pendingArticle=null, articleRequest=0;
  function articlePosition(target){const id=target.fragment.replace(/^#/,'');const anchor=id?[...frame.querySelectorAll('[id],[name]')].find(node=>node.id===id||node.getAttribute('name')===id):null;if(anchor)anchor.scrollIntoView({block:'start'});else frame.scrollTop=0;}
  function articleHistory(target,replace=false){
    // Canonical topic paths may have different depths. Keep shell assets at
    // their original addresses while the existing document changes its URL.
    for(const element of document.querySelectorAll('head link[href],header img[src]')){const attribute=element.hasAttribute('href')?'href':'src';element.setAttribute(attribute,new URL(element.getAttribute(attribute),location.href).href);}
    document.documentElement.dataset.helpRoot=viewerRoot.href;
    try{history[replace?'replaceState':'pushState'](null,'',target.url);}catch{history[replace?'replaceState':'pushState'](null,'','#'+new URLSearchParams({topic:target.link}).toString());}
  }
  function articleError(){let status=document.getElementById('topic-status');if(!status){status=document.createElement('p');status.id='topic-status';status.setAttribute('role','status');document.body.append(status);}status.textContent='Could not open the help topic. Please try the link again.';}
  function loadArticle(target,updateHistory){
    if(pendingArticle?.link===target.link)return pendingArticle.promise;
    pendingArticle?.cancel();document.getElementById('topic-status')?.remove();
    if(activeTopic?.path===target.path&&activeTopic?.url.split('#')[0]===target.url.split('#')[0]){activeTopic=target;if(updateHistory&&location.href!==target.url)articleHistory(target);showResults(false);selectTopic(target.link);articlePosition(target);return Promise.resolve(true);}
    const request=String(++articleRequest),loader=document.createElement('iframe'),url=new URL(target.url);
    url.searchParams.set('content','1');url.searchParams.set('sdHelpLoad',request);
    loader.title='Loading help topic';loader.tabIndex=-1;loader.setAttribute('aria-hidden','true');loader.style.cssText=`position:fixed;left:-100000px;top:0;width:${Math.max(frame.clientWidth,300)}px;height:${Math.max(frame.clientHeight,200)}px;visibility:hidden;pointer-events:none;border:0`;
    frame.setAttribute('aria-busy','true');
    let finish;
    const promise=new Promise(resolve=>{
      const timer=setTimeout(()=>{articleError();finish(false);},15000);
      finish=success=>{clearTimeout(timer);window.removeEventListener('message',receive);loader.remove();if(pendingArticle?.request===request){pendingArticle=null;frame.removeAttribute('aria-busy');}resolve(success);};
      function receive(event){
        const data=event.data;if(event.source!==loader.contentWindow||data?.type!=='statsdirect-help-content'||data.request!==request)return;
        if(data.error||typeof data.html!=='string'||resolveTopic(data.url)?.path!==target.path){articleError();finish(false);return;}
        const body=document.getElementById('help-topic-body'),template=document.createElement('template');template.innerHTML=data.html;
        body.replaceChildren(template.content);activeTopic=target;document.documentElement.dataset.currentTopic=target.path;
        if(updateHistory)articleHistory(target);document.title=target.title;selectTopic(target.link);showPanel(navigationMode);articlePosition(target);finish(true);
      }
      window.addEventListener('message',receive);
    });
    pendingArticle={link:target.link,request,promise,cancel:()=>finish(false)};loader.src=url.href;document.body.append(loader);return promise;
  }
  function openTopic(link, anchor, updateHash=true) { const target=resolveTopic(link);if(!target)return;if(articleMode){navigation(false);if(!nativeDesktop)return loadArticle(target,updateHash);location[updateHash?'assign':'replace'](target.url);return;}activeTopic=target;const hash=new URLSearchParams({topic:target.link}).toString();if(updateHash&&location.hash.slice(1)!==hash)history.pushState(null,'','#'+hash);try{frame.contentWindow.location.replace(target.url);}catch{frame.src=target.url;} selectTopic(target.link); if(flare)showResults(false);else document.title=target.title;navigation(false); }
  function makeLink(title, link) { const a = document.createElement('a'); a.href = resolveTopic(link)?.url||link; a.dataset.link = link; a.textContent = title; a.addEventListener('click', e => { if(e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;if(articleMode&&nativeDesktop){navigation(false);return;}e.preventDefault();e.stopPropagation();openTopic(link,a); }); return a; }
  function renderTree(nodes) { const ul = document.createElement('ul'); for (const node of nodes) { const li = document.createElement('li'); if (node.children.length) { const d = document.createElement('details'), s = document.createElement('summary'); s.textContent = node.title;
const children = renderTree(node.children);
if (node.link) {
  const overview = document.createElement('li');
  const link = makeLink('Overview', node.link);
  link.setAttribute('aria-label', node.title + ' overview');
  overview.append(link);
  children.prepend(overview);
}
d.append(s, children); li.append(d); } else if (node.link) li.append(makeLink(node.title,node.link)); else { const span=document.createElement('span');span.textContent=node.title;li.append(span); } ul.append(li); } return ul; }
  contents.append(renderTree(tree));
  if(activeTopic)selectTopic(activeTopic.link);
  const home=document.querySelector('a.help-identity'),homeTopic=resolveTopic('contents.htm')||resolveTopic(index.documents[0]?.url);if(home&&homeTopic){home.href=homeTopic.url;home.addEventListener('click',e=>{if(articleMode&&nativeDesktop||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;e.preventDefault();showResults(false);openTopic(homeTopic.link);});}
  function clearSearchHash(){if(new URLSearchParams(location.hash.slice(1)).has('searchQuery')){if(articleMode&&!nativeDesktop&&activeTopic)articleHistory(activeTopic,true);else history.replaceState(null,'',location.pathname+location.search);}}
  tabs[0].addEventListener('click',()=>{saveNavigationState();showPanel('contents');clearSearchHash();scrollSelection();saveNavigationState();});
  tabs[1].addEventListener('click',()=>{showPanel('index');clearSearchHash();renderKeywords();navigationPane.scrollTop=indexScroll;keywordFilter.focus({preventScroll:true});keywordFilter.select();saveNavigationState();});
  function tokenize(value) { return value.normalize('NFD').replace(/\p{M}/gu,'').toLowerCase().match(/[\p{L}\p{N}]+/gu)?.filter(x=>x.length>1||/^\d$/.test(x)) || []; }
  function search(query) {
    pendingArticle?.cancel();
    const tokens=[...new Set(tokenize(query))], scores=new Map(), hits=new Map();
    for(const token of tokens) { const perWord=new Map(); const exact=index.terms[token]; if(exact)for(const [id,weight]of exact)perWord.set(id,weight*3);
      if(token.length>=3) for(const [term,postings]of Object.entries(index.terms)) if(term!==token&&term.startsWith(token)) for(const [id,weight]of postings)perWord.set(id,Math.max(perWord.get(id)||0,weight));
      for(const[id,weight]of perWord){scores.set(id,(scores.get(id)||0)+weight);hits.set(id,(hits.get(id)||0)+1);} }
    const found=[...scores].filter(([id])=>hits.get(id)===tokens.length).sort((a,b)=>b[1]-a[1]||index.documents[a[0]].title.localeCompare(index.documents[b[0]].title));
    results.replaceChildren();const count=document.createElement(flare?'h1':'p');count.className='search-count';count.textContent=flare?`Your search for "${query}" returned ${found.length} result(s).`:tokens.length?`${found.length} page${found.length===1?'':'s'} found`:'Enter words to search all help pages.';results.append(count);
    for(const[id]of found.slice(0,100)){const doc=index.documents[id],a=makeLink('',doc.url);a.className='result';const title=document.createElement('strong'),excerpt=document.createElement('span');title.textContent=doc.title;excerpt.textContent=doc.excerpt;a.append(title,excerpt);if(flare){const url=document.createElement('cite');url.className='result-url';url.textContent=doc.url;a.append(url);}results.append(a);}
    if(tokens.length&&!found.length){const p=document.createElement('p');p.textContent='No matching pages. Try fewer words or the name of the method.';results.append(p);}showResults(true);navigation(false);
    return found.map(([id,score])=>({url:index.documents[id].url,title:index.documents[id].title,score}));
  }
  document.getElementById('search-form').addEventListener('submit',e=>{e.preventDefault();const query=document.getElementById('search').value,hash=new URLSearchParams({searchQuery:query}).toString();if(location.hash.slice(1)!==hash)history.pushState(null,'','#'+hash);search(query);});
  window.StatsDirectHelp={search,openTopic,get currentTopic(){return activeTopic?.url;}};
  function readSearchHash(){const state=new URLSearchParams(location.hash.slice(1)),query=state.get('searchQuery'),topic=state.get('topic');if(query!==null){pendingArticle?.cancel();document.getElementById('search').value=query;search(query);}else{showResults(false);if(topic)openTopic(topic,null,false);else if(articleMode){const current=resolveTopic(location.href);if(current&&nativeDesktop){activeTopic=current;selectTopic(current.link);}else if(current&&current.link!==activeTopic?.link)openTopic(current.link,null,false);else if(current)selectTopic(current.link);}}}
  window.addEventListener('hashchange',readSearchHash);if(articleMode&&!nativeDesktop)window.addEventListener('popstate',readSearchHash);readSearchHash();
  restoringNavigation=false;
  if(articleMode&&!nativeDesktop)frame.addEventListener('click',event=>{const link=event.target.closest?.('a[href]');if(!link||event.defaultPrevented||event.button!==0||event.ctrlKey||event.metaKey||event.shiftKey||event.altKey||link.target==='_blank'||link.closest('svg,math'))return;const target=resolveTopic(link.href);if(!target)return;event.preventDefault();openTopic(target.link);});
  if(!articleMode)frame.addEventListener('load',()=>{try{const current=resolveTopic(frame.contentWindow.location.href);if(current){activeTopic=current;selectTopic(current.link);if(results.hidden&&!new URLSearchParams(location.hash.slice(1)).has('searchQuery')){document.title=current.title;history.replaceState(null,'','#'+new URLSearchParams({topic:current.link}).toString());}frame.contentDocument.addEventListener('click',e=>{const a=e.target.closest?.('a[href]');if(!a||e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey||a.target==='_blank'||a.closest('svg,math'))return;const target=resolveTopic(a.href);if(!target)return;e.preventDefault();openTopic(target.link);});}}catch{/* Local file browsers may isolate iframe documents; navigation still works. */}});
})();