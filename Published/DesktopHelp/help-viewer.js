(() => {
  'use strict';
  const contents = document.getElementById('contents'), results = document.getElementById('results'), frame = document.getElementById('topic');
  if(!contents||!results||!frame)return; // Explicit ?content=1 diagnostic view.
  const articleMode=frame.tagName==='ARTICLE';
  const index = window.StatsDirectSearch, tree = window.StatsDirectContents;
  const flare = document.documentElement.dataset.helpSkin === 'flare';
  const viewerRoot = new URL(document.documentElement.dataset.helpRoot||'.',location.href);
  function decodedPath(value) { try{return decodeURIComponent(value);}catch{return value;} }
  const catalogue = new Map(index.documents.map(doc=>[decodedPath(doc.url).toLowerCase(),doc]));
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
  function scrollSelection() { const selected=contents.querySelector('a.selected');if(selected?.offsetHeight)(selected.closest('li')||selected).scrollIntoView({block:'nearest',inline:'nearest'}); }
  const menu = document.getElementById('navigation-toggle'), shade = document.querySelector('.navigation-shade');
  function navigation(open) { document.body.classList.toggle('navigation-open',open); if(menu)menu.setAttribute('aria-expanded',String(open)); if(shade)shade.hidden=!open;if(open)scrollSelection(); }
  if(menu)menu.addEventListener('click',()=>navigation(!document.body.classList.contains('navigation-open')));
  if(shade)shade.addEventListener('click',()=>navigation(false));
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&document.body.classList.contains('navigation-open')){navigation(false);menu?.focus();}});
  matchMedia('(max-width:1279px)').addEventListener('change',()=>navigation(false));
  const tabs = [document.getElementById('contents-tab'), document.getElementById('results-tab')];
  let activeTopic=(articleMode?resolveTopic(location.href):null)||resolveTopic(document.documentElement.dataset.currentTopic||frame.getAttribute('src')||index.documents[0]?.url);
  function showResults(on) { contents.hidden = flare ? false : on; results.hidden = !on; if(flare)frame.hidden=on; tabs[0].classList.toggle('active', !on); tabs[1].classList.toggle('active', on);document.title=on?'Search':activeTopic?.title||'StatsDirect Help'; }
  function selectTopic(link) { const target=resolveTopic(link);if(!target)return;const links=[...document.querySelectorAll('#contents a')].map(a=>({a,topic:resolveTopic(a.dataset.link)}));const exact=target.fragment?links.find(item=>item.topic?.path===target.path&&item.topic.fragment===target.fragment):null,current=(exact||links.find(item=>item.topic?.path===target.path))?.a;links.forEach(({a})=>{const selected=a===current;a.classList.toggle('selected',selected);if(selected){a.setAttribute('aria-current','page');for(let p=a.parentElement;p&&p!==contents;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;}else a.removeAttribute('aria-current');});scrollSelection(); }
  function openTopic(link, anchor, updateHash=true) { const target=resolveTopic(link);if(!target)return;if(articleMode){navigation(false);location[updateHash?'assign':'replace'](target.url);return;}activeTopic=target;const hash=new URLSearchParams({topic:target.link}).toString();if(updateHash&&location.hash.slice(1)!==hash)history.pushState(null,'','#'+hash);try{frame.contentWindow.location.replace(target.url);}catch{frame.src=target.url;} selectTopic(target.link); if(flare)showResults(false);else document.title=target.title;navigation(false); }
  function makeLink(title, link) { const a = document.createElement('a'); a.href = resolveTopic(link)?.url||link; a.dataset.link = link; a.textContent = title; a.addEventListener('click', e => { if(e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;if(articleMode){navigation(false);return;}e.preventDefault();e.stopPropagation();openTopic(link,a); }); return a; }
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
  const home=document.querySelector('a.help-identity'),homeTopic=resolveTopic('contents.htm')||resolveTopic(index.documents[0]?.url);if(home&&homeTopic){home.href=homeTopic.url;home.addEventListener('click',e=>{if(articleMode||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey)return;e.preventDefault();showResults(false);openTopic(homeTopic.link);});}
  tabs[0].addEventListener('click',()=>{showResults(false);if(new URLSearchParams(location.hash.slice(1)).has('searchQuery'))location.hash='';}); tabs[1].addEventListener('click',()=>showResults(true));
  function tokenize(value) { return value.normalize('NFD').replace(/\p{M}/gu,'').toLowerCase().match(/[\p{L}\p{N}]+/gu)?.filter(x=>x.length>1||/^\d$/.test(x)) || []; }
  function search(query) {
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
  window.StatsDirectHelp={search,openTopic};
  function readSearchHash(){const state=new URLSearchParams(location.hash.slice(1)),query=state.get('searchQuery'),topic=state.get('topic');if(query!==null){document.getElementById('search').value=query;search(query);}else{if(articleMode&&!topic){const current=resolveTopic(location.href);if(current){activeTopic=current;selectTopic(current.link);}}showResults(false);if(topic)openTopic(topic,null,false);}}
  window.addEventListener('hashchange',readSearchHash);readSearchHash();
  if(!articleMode)frame.addEventListener('load',()=>{try{const current=resolveTopic(frame.contentWindow.location.href);if(current){activeTopic=current;selectTopic(current.link);if(results.hidden&&!new URLSearchParams(location.hash.slice(1)).has('searchQuery')){document.title=current.title;history.replaceState(null,'','#'+new URLSearchParams({topic:current.link}).toString());}frame.contentDocument.addEventListener('click',e=>{const a=e.target.closest?.('a[href]');if(!a||e.defaultPrevented||e.button!==0||e.ctrlKey||e.metaKey||e.shiftKey||e.altKey||a.target==='_blank'||a.closest('svg,math'))return;const target=resolveTopic(a.href);if(!target)return;e.preventDefault();openTopic(target.link);});}}catch{/* Local file browsers may isolate iframe documents; navigation still works. */}});
})();