import {marked} from 'marked';
import DOMPurify from 'dompurify';
import katex from 'katex';
import hljs from 'highlight.js/lib/core';
import r from 'highlight.js/lib/languages/r';
hljs.registerLanguage('r',r);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const math=(text,displayMode)=>katex.renderToString(text,{displayMode,throwOnError:false,trust:false,strict:'ignore',maxExpand:500,maxSize:20});
const renderer=new marked.Renderer();
renderer.html=esc;
renderer.code=(code,language='')=>{const isR=/^(r|rscript)$/i.test(language.trim());return `<div class="code-block"><div class="code-actions"><span>${isR?'R':'Code'}</span><button type="button" data-code="copy">Copy</button>${isR?'<button type="button" data-code="r">Open in R</button>':''}</div><pre><code>${isR?hljs.highlight(code,{language:'r'}).value:esc(code)}</code></pre></div>`;};
marked.use({renderer,mangle:false,headerIds:false,extensions:[
 {name:'mathBlock',level:'block',start:src=>src.search(/\\\[|\$\$/),tokenizer(src){const m=/^(?:\\\[([\s\S]+?)\\\]|\$\$([\s\S]+?)\$\$)(?:\n|$)/.exec(src);if(m)return {type:'mathBlock',raw:m[0],text:m[1]??m[2]};},renderer:t=>math(t.text,true)},
 {name:'mathInline',level:'inline',start:src=>src.search(/\\\(|\$/),tokenizer(src){const m=/^(?:\\\(([\s\S]+?)\\\)|\$([^$\n]+?)\$(?!\d))/.exec(src);if(m)return {type:'mathInline',raw:m[0],text:m[1]??m[2]};},renderer:t=>math(t.text,false)}
]});
export function richText(text){
 return DOMPurify.sanitize(marked.parse(String(text)),{USE_PROFILES:{html:true,mathMl:true,svg:true},FORBID_TAGS:['img','video','audio','iframe','style','form','input','textarea','select'],FORBID_ATTR:['id','name'],ALLOW_DATA_ATTR:true});
}
export function bindRichText(element,{copy,openR}){
 element.querySelectorAll('a').forEach(a=>{if(!/^https?:\/\//i.test(a.getAttribute('href')??''))a.removeAttribute('href');else {a.target='_blank';a.rel='noopener noreferrer';}});
 element.querySelectorAll('[data-code]').forEach(button=>button.onclick=()=>{
  const code=button.closest('.code-block').querySelector('code').textContent;
  if(button.dataset.code==='r'){const message=button.closest('[data-message-id]');openR(code,{messageID:message?.dataset.messageId,blockIndex:message?Array.from(message.querySelectorAll('[data-code=r]')).indexOf(button):-1});}else {copy(code);button.textContent='Copied';setTimeout(()=>button.textContent='Copy',1500);}
 });
}
