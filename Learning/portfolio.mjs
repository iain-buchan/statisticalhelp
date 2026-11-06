import {questions,tracks,bankVersion} from './bank.mjs';
import {learnerResult,sessionQuestion} from './quiz.mjs';
import {trainingDefaults,currentTraining,statisticalSkills,assessmentTypes} from './provider-course.mjs';
export const stages = {menus:'Start with menus',bridge:'Connect menus to R',coding:'Practise R coding'};
export function newPortfolio() {
  return {schemaVersion:2,id:crypto.randomUUID(),startedAt:new Date().toISOString(),profile:'foundation',stage:'menus',lesson:'epidemiology',view:'study',identity:{name:'',email:'',goal:''},learning:{statisticalSkills:'beginner',needs:'',qualifications:'',priorKnowledge:'',targetDate:'',focus:['epidemiology','causal'],style:'Worked examples and questions'},training:trainingDefaults(),courseKey:'',courseWork:[],workDrafts:{},providerReviews:[],evidence:[],conversation:[],sharingConsents:[],activities:[],attempts:[],quiz:null,reflection:'',draft:''};
}
export function restorePortfolio(value) {
  const object=v=>v!==null&&typeof v==='object'&&!Array.isArray(v);
  const strings=(v,keys)=>object(v)&&keys.every(k=>typeof v[k]==='string');
  const validQuestion=q=>strings(q,['id','topic','stem','correct','explanation','hint','lesson','help'])&&Array.isArray(q.options)&&q.options.length>=2&&q.options.length<=8&&q.options.every(o=>strings(o,['id','text','feedback']))&&q.options.filter(o=>o.id===q.correct).length===1;
  const validSession=s=>{
    if(!strings(s,['id','track','mode','startedAt'])||!tracks[s.track]||!['learn','test'].includes(s.mode)||!Array.isArray(s.questionIds)||!s.questionIds.length||new Set(s.questionIds).size!==s.questionIds.length||!Array.isArray(s.answers)||s.answers.length>s.questionIds.length||!['hints','explanations'].every(k=>Array.isArray(s[k])&&s[k].every(id=>s.questionIds.includes(id))))return false;
    if(s.questionSnapshots!==undefined&&(!Array.isArray(s.questionSnapshots)||s.questionSnapshots.length!==s.questionIds.length||!s.questionSnapshots.every(validQuestion)||new Set(s.questionSnapshots.map(q=>q.id)).size!==s.questionIds.length))return false;
    if(!s.questionIds.every(id=>typeof id==='string'&&validQuestion(sessionQuestion(s,id))))return false;
    if(!['completedAt','endedAt'].every(k=>s[k]===undefined||s[k]===null||typeof s[k]==='string')||Boolean(s.completedAt)!==(s.answers.length===s.questionIds.length))return false;
    return s.answers.every((a,i)=>strings(a,['questionId','choice','confidence','reasoning','answeredAt'])&&a.questionId===s.questionIds[i]&&['unsure','fairly','confident'].includes(a.confidence)&&typeof a.assisted==='boolean'&&typeof a.correct==='boolean'&&sessionQuestion(s,a.questionId).options.some(o=>o.id===a.choice)&&a.correct===(a.choice===sessionQuestion(s,a.questionId).correct));
  };
  const defaults=newPortfolio();
  const learning=value?.learning===undefined?defaults.learning:object(value.learning)?{statisticalSkills:'beginner',...value.learning}:value.learning;
  if(value?.sharingConsents!==undefined&&(!Array.isArray(value.sharingConsents)||!value.sharingConsents.every(c=>strings(c,['id','at','requestID','policyVersion','statement'])&&c.noPersonIdentifiers===true&&Array.isArray(c.documents)&&c.documents.every(d=>strings(d,['id','title','kind'])&&Number.isInteger(d.version)))))throw Error('The saved sharing confirmations are incompatible. The record has not been overwritten.');
  const validTraining=v=>strings(v,Object.keys(trainingDefaults()).filter(k=>k!=='assessmentType'))&&(v.assessmentType===undefined||Object.hasOwn(assessmentTypes,v.assessmentType));
  if(value?.training!==undefined&&!validTraining(value.training)||value?.courseKey!==undefined&&typeof value.courseKey!=='string'||value?.courseWork!==undefined&&(!Array.isArray(value.courseWork)||!value.courseWork.every(w=>strings(w,['id','at','courseKey','lessonID','lessonTitle','objective','task','text','status'])&&validTraining(w.training)))||value?.workDrafts!==undefined&&(!object(value.workDrafts)||!Object.values(value.workDrafts).every(x=>typeof x==='string'))||value?.providerReviews!==undefined&&(!Array.isArray(value.providerReviews)||!value.providerReviews.every(r=>strings(r,['portfolioID','provider','reviewer','reviewedAt','decision','feedback','verification'])) )||value?.evidence!==undefined&&(!Array.isArray(value.evidence)||!value.evidence.every(r=>strings(r,['title','at','text']))))throw Error('The saved course record is incompatible. It has not been overwritten.');
  if (!strings(value,['id','startedAt','profile','stage','lesson','view','reflection','draft']) || value.schemaVersion!==2 || !tracks[value.profile] || !stages[value.stage] || !['study','options','sources','practice','record'].includes(value.view) || !strings(value.identity,['name','email','goal']) || !Object.hasOwn(statisticalSkills,learning?.statisticalSkills)||!strings(learning,['needs','qualifications','priorKnowledge','targetDate','style']) || !Array.isArray(learning.focus) || !learning.focus.every(x=>typeof x==='string') || !Array.isArray(value.conversation) || !value.conversation.every(c=>strings(c,['id','at','role','text','source'])&&['user','assistant'].includes(c.role)&&['lessonId','lessonTitle'].every(k=>c[k]===undefined||typeof c[k]==='string')&&(c.lessonPrompt===undefined||typeof c.lessonPrompt==='boolean')&&(!c.workspaceSources||Array.isArray(c.workspaceSources)&&c.workspaceSources.every(s=>typeof s==='string'))&&(!c.courseSources||Array.isArray(c.courseSources)&&c.courseSources.every(s=>strings(s,['id','title'])))) || !Array.isArray(value.attempts) || !value.attempts.every(validSession) || value.quiz!==null&&!validSession(value.quiz) || !Array.isArray(value.activities) || !value.activities.every(a=>strings(a,['at','text']))) throw new Error('The saved learning record is incompatible. It has not been overwritten.');
  return {...defaults,...value,training:currentTraining(value.training),learning:{...learning,focus:[...new Set(['epidemiology','causal',...learning.focus])]}};
}
export function transcriptEntry(state,role,text,source='study guide',details={}) {
  const entry={id:crypto.randomUUID(),at:new Date().toISOString(),role,text,source,...details};
  state.conversation.push(entry); return entry;
}
export function reviewRecord(state) {
  const attempts=[...state.attempts,...(state.quiz?[state.quiz]:[])].map(session=>({
    ...structuredClone(session),
    answers:session.answers.map(a=>{const copy=structuredClone(a);if(session.mode==='test'&&!session.completedAt&&!session.endedAt)delete copy.correct;return copy;}),result:session.completedAt?learnerResult(session):null,
    questionSnapshots:session.questionIds.map(id=>{const q=structuredClone(sessionQuestion(session,id));if(session.mode==='test'&&!session.completedAt&&!session.endedAt){delete q.correct;delete q.explanation;delete q.hint;delete q.lesson;q.options=q.options.map(({id,text})=>({id,text}));}return q;}),
    status:session.completedAt?'completed':session.endedAt?'ended early':'in progress'
  }));
  return {schemaVersion:2,exportedAt:new Date().toISOString(),portfolioID:state.id,startedAt:state.startedAt,
    learner:structuredClone(state.identity),learningOptions:structuredClone(state.learning),pathway:tracks[state.profile].title,rExperience:stages[state.stage],
    diagnostics:structuredClone(state.diagnostics??{}),bankVersion,questionSource:'StatsDirect items are original teaching material. Provider items retain the course and version recorded with each attempt. These are not official examination questions.',
    marking:'Provisional fixed-key MCQ scoring: 1 correct, 0 otherwise. First answers retained. Confidence and free-text reasoning are not graded. Independent practice is unsupervised and cannot certify exam readiness.',
    reviewStatus:'No accreditation or CPD points awarded by StatsDirect. Imported provider responses are recorded separately and are not authenticated. Email preparation does not confirm delivery or acceptance.',
    training:structuredClone(state.training),courseKey:state.courseKey,courseWork:structuredClone(state.courseWork),providerReviews:structuredClone(state.providerReviews),evidence:structuredClone(state.evidence),
    sharingConsents:structuredClone(state.sharingConsents??[]),attempts,conversation:structuredClone(state.conversation),activities:structuredClone(state.activities),reflection:state.reflection};
}
export function reviewText(record) {
  const lines=['STATSDIRECT — LEARNING RECORD','External review request',`Prepared: ${record.exportedAt}`,`Record: ${record.portfolioID}`,`Started: ${record.startedAt}`,'',`Learner: ${record.learner.name || '(not supplied)'}`,`Reply email: ${record.learner.email || '(not supplied)'}`,`Learning goal: ${record.learner.goal || '(not supplied)'}`,`Pathway: ${record.pathway}`,`Statistical skills: ${statisticalSkills[record.learningOptions?.statisticalSkills]||'Not recorded'}`,`R experience: ${record.rExperience}`,`Exams / qualifications: ${record.learningOptions?.qualifications || '(not supplied)'}`,`Learning needs: ${record.learningOptions?.needs || '(not supplied)'}`,`Prior knowledge: ${record.learningOptions?.priorKnowledge || '(not supplied)'}`,`Target date: ${record.learningOptions?.targetDate || '(not supplied)'}`,`Focus: ${(record.learningOptions?.focus || []).join(', ')}`,`Preferred teaching style: ${record.learningOptions?.style || ''}`,'',record.questionSource,record.marking,record.reviewStatus,'','PRACTICE ATTEMPTS'];
  for(const a of record.attempts) {
    if(a.course)lines.push(`Provider assessment: ${a.course.title} · ${a.course.provider} · version ${a.course.version}`);
    lines.push('',`${tracks[a.track].title} — ${a.mode==='test'?'Independent practice (unsupervised)':'Supported practice'} — ${a.status}`,`Started: ${a.startedAt} | Completed: ${a.completedAt || 'not completed'}`,a.result?`Provisional score: ${a.result.score}/${a.result.total}; ${a.result.assisted} answers flagged as assisted`:`Answered: ${a.answers.length}/${a.questionIds.length}`);
    for(const q of a.questionSnapshots) {
      const answer=a.answers.find(x=>x.questionId===q.id);
      lines.push('',`${q.id} v${q.version} — ${q.topic}`,q.stem,...q.options.map(o=>`${o.id}. ${o.text}`),`First answer: ${answer?.choice || 'not answered'} | Key: ${q.correct || 'withheld until the attempt ends'}`,`Confidence: ${answer?.confidence || 'not recorded'} | Assisted: ${answer?.assisted??false}`,`Reasoning before answer: ${answer?.reasoning || '(none)'}`,`Reflection after feedback: ${answer?.reflection || '(none)'}`,`Feedback: ${q.explanation || 'withheld until the attempt ends'}`,q.help?`Help: https://www.statsdirect.com/help/${q.help}`:'');
    }
  }
  lines.push('','DOCUMENT SHARING CONFIRMATIONS');
  for(const c of record.sharingConsents??[])lines.push('',`${c.at} · question ${c.requestID} · ${c.policyVersion}`,c.statement,...c.documents.map(d=>`${d.title} (${d.kind}) · revision ${d.version}`));
  lines.push('','COMPLETE LEARNING CONVERSATION');
  for(const c of record.conversation) lines.push('',`[${c.at}] ${c.role==='user'?'Learner':'Tutor'} — ${c.source}${c.lessonTitle?' · '+c.lessonTitle:''}${c.model?' / '+c.model:''}`,c.text,...(c.courseSources??[]).map(s=>'Lesson context supplied: '+s.id+' — '+s.title+(s.url?' · '+s.url:'')+(s.retrievedAt?' · retrieved '+s.retrievedAt:'')),...(c.workspaceSources??[]).map(s=>'StatsDirect context: '+s));
  lines.push('','PROVIDER AND COURSE',...Object.entries(record.training??{}).map(([k,v])=>`${k==='assessmentType'?'Assessment type':k}: ${k==='assessmentType'?assessmentTypes[v]??v:v}`),'','PRACTICAL SUBMISSIONS');
  for(const w of record.courseWork??[])lines.push('',`${w.at} · ${w.training.courseTitle} v${w.training.courseVersion} · ${w.lessonTitle}`,`Objective: ${w.objective}`,`Task: ${w.task}`,w.text,w.status);
  lines.push('','ATTACHED ANALYSIS EVIDENCE');for(const e of record.evidence??[])lines.push('',`${e.at} · ${e.title}`,e.text);
  lines.push('','RETURNED PROVIDER ASSESSMENTS');for(const r of record.providerReviews??[])lines.push('',`${r.provider} · ${r.reviewer} · ${r.reviewedAt}`,`Decision: ${r.decision}`,r.feedback,`Credit statement supplied: ${r.creditStatement||'(none)'}`,`Reference: ${r.reference||'(none)'}`,r.verification);
  lines.push('','LEARNING ACTIVITIES');
  for(const a of record.activities) lines.push(`[${a.at}] ${a.text}`);
  lines.push('','DIAGNOSTICS',JSON.stringify(record.diagnostics??{},null,2));
  lines.push('','LEARNER REFLECTION',record.reflection || '(not supplied)');
  return lines.join('\n');
}
export function practiceContext(state) {
  const q=state.quiz;
  if(!q || q.mode==='test'&&!q.completedAt)return '';
  const item=sessionQuestion(q,q.questionIds[Math.min(q.answers.length,q.questionIds.length-1)]);
  return item?`${item.stem}\n${item.options.map(x=>x.id+'. '+x.text).join('\n')}\nTeaching rationale: ${item.explanation}`:'';
}
