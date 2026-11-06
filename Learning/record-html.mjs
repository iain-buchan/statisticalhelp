import {richText} from './rich-text.mjs';
const e=s=>String(s??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function exportRichText(text){
 const content=document.createElement('div');content.innerHTML=richText(text);
 content.querySelectorAll('.katex-html,.code-actions').forEach(e=>e.remove());
 return content.innerHTML;
}
export function recordHTML(r){
 const section=(title,body)=>`<section><h2>${e(title)}</h2>${body}</section>`;
 const para=s=>`<p>${e(s)}</p>`;
 const attempts=r.attempts.map(a=>`<h3>${e(a.course?.title??a.track)} · ${e(a.status)}</h3>${para(a.result?`Provisional score: ${a.result.score}/${a.result.total}`:'Attempt not completed')}`+a.questionSnapshots.map(q=>{const ans=a.answers.find(x=>x.questionId===q.id);return `<h4>${e(q.topic)}</h4>${para(q.stem)}<ol type="A">${q.options.map(o=>`<li>${e(o.text)}</li>`).join('')}</ol>${para(`First answer: ${ans?.choice??'—'} · Key: ${q.correct??'withheld'} · Confidence: ${ans?.confidence??'—'}`)}${para('Reasoning: '+(ans?.reasoning??'—'))}${para('Reflection: '+(ans?.reflection??'—'))}${para(q.explanation??'Feedback withheld until the attempt ends')}`;}).join('')).join('');
 const body=`<h1>StatsDirect learning record</h1>${para(r.learner.name||'Learner')}${para(r.learner.email||'')}${para(`Prepared ${r.exportedAt}`)}${para(r.pathway+' · '+r.rExperience)}${para(r.learner.goal||r.learningOptions.needs||'')}${para(r.marking)}${para(r.reviewStatus)}`+
 section('Practice attempts',attempts||para('No practice attempts'))+
 section('Document sharing confirmations',(r.sharingConsents??[]).map(c=>para(`${c.at} · question ${c.requestID} · ${c.policyVersion}`)+para(c.statement)+para(c.documents.map(d=>`${d.title} (${d.kind}) · revision ${d.version}`).join('; '))).join('')||para('No document sharing confirmed'))+
 section('Learning conversation',r.conversation.map(c=>`<article><h3>${c.role==='user'?'Learner':'Tutor'} · ${e(c.lessonTitle??c.source)}</h3><small>${e(c.at)}</small>${exportRichText(c.text)}${(c.courseSources??[]).length?para('Lesson context supplied: '+c.courseSources.map(s=>s.title+(s.url?' — '+s.url:'')).join('; ')):''}${(c.workspaceSources??[]).length?para('StatsDirect context: '+c.workspaceSources.join('; ')):''}</article>`).join(''))+
 section('Course and assessment',Object.entries(r.training).map(([k,v])=>para(k+': '+v)).join(''))+
 section('Practical submissions',r.courseWork.map(w=>`<h3>${e(w.lessonTitle)}</h3>${para(w.text)}`).join(''))+
 section('Analysis evidence',r.evidence.map(x=>`<h3>${e(x.title)}</h3><pre>${e(x.text)}</pre>`).join(''))+
 section('Provider responses',r.providerReviews.map(x=>para(`${x.provider}: ${x.decision}\n${x.feedback}\n${x.creditStatement??''}\n${x.verification}`)).join(''))+
 section('Reflection',para(r.reflection||'Not supplied'))+section('Learning activities',r.activities.map(x=>para(`${x.at} — ${x.text}`)).join(''))+section('Versions and diagnostics',`<pre>${e(JSON.stringify(r.diagnostics??{},null,2))}</pre>`);
 return '<!doctype html><html><head><meta charset="utf-8"><title>StatsDirect learning record</title><style>body{font:14px/1.5 -apple-system,Arial,sans-serif;color:#213b48;max-width:900px;margin:auto;padding:32px}h1,h2,h3{break-after:avoid}h2{border-bottom:1px solid #ccc;margin-top:28px}article{margin:20px 0}pre{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse}th,td{border:1px solid #bbb;padding:6px}.code-actions{display:none}.katex-html{display:none}</style></head><body>'+body+'</body></html>';
}
