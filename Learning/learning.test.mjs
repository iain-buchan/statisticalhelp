import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync,mkdtempSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {questions,tracks} from './bank.mjs';
import {lessonChecks} from './lesson-checks.mjs';
import {createSession,recordAnswer,recordAssistance,learnerResult,sessionQuestion,reviewBundle} from './quiz.mjs';
import {newPortfolio,restorePortfolio,packPortfolio,reviewRecord,reviewText,transcriptEntry,practiceContext} from './portfolio.mjs';
import {recordHTML} from './record-html.mjs';
import {contentFile,runR} from './test-config.mjs';
const lessons=JSON.parse(readFileSync(contentFile('Learn/lessons.json')));
test('original questions map to established pathways, valid choices, real help and engine operations',()=>{
 const operations=JSON.parse(readFileSync(contentFile('analysis-menu.json'))).operations;
 assert.ok(questions.length>=35);assert.equal(new Set(questions.map(q=>q.id)).size,questions.length);
 for(const t of Object.keys(tracks))assert.ok(questions.filter(q=>q.track===t).length>=4);
 for(const q of questions){assert.equal(q.options.length,5);assert.equal(q.options.filter(o=>o.id===q.correct).length,1);if(q.help)assert.ok(existsSync(contentFile('Help/'+q.help)),q.help);if(q.operation)assert.ok(operations[q.operation]);}
 for(const l of lessons){if(l.help)assert.ok(existsSync(contentFile('Help/'+l.help)),l.help);if(l.operation)assert.ok(operations[l.operation],l.operation);}
});
test('independent practice hides answer keys and feedback from record previews until ended',()=>{
 const p=newPortfolio();p.quiz=createSession('foundation','test');const q=questions.find(q=>q.id===p.quiz.questionIds[0]);
 assert.throws(()=>recordAssistance(p.quiz,q.id,'hints'));assert.throws(()=>learnerResult(p.quiz));
 recordAnswer(p.quiz,q.id,q.correct,'confident','A denominator explanation');
 assert.throws(()=>recordAnswer(p.quiz,q.id,q.correct,'confident'));
 let r=reviewRecord(p);assert.equal(r.attempts[0].result,null);assert.equal(r.attempts[0].answers[0].correct,undefined);
 assert.ok(r.attempts[0].questionSnapshots.every(q=>!q.correct&&!q.explanation&&q.options.every(o=>!o.feedback)));
 assert.equal(practiceContext(p),'');assert.match(reviewText(r),/withheld until the attempt ends/);
 p.quiz.endedAt=new Date().toISOString();r=reviewRecord(p);assert.equal(r.attempts[0].status,'ended early');assert.equal(r.attempts[0].questionSnapshots[0].correct,q.correct);
});
test('all questions survive reopening in authored order without changing the learner pathway', () => {
 const state = newPortfolio();
 state.profile = 'medicine';
 state.quiz = createSession(state.profile, 'test', 'all-test', null, true);
 const [first] = questions;
 recordAnswer(state.quiz, first.id, first.correct, 'confident');

 const restored = restorePortfolio(JSON.parse(JSON.stringify(state)));
 assert.equal(restored.profile, 'medicine');
 assert.equal(restored.quiz.track, 'medicine');
 assert.equal(restored.quiz.questionSet, 'all');
 assert.deepEqual(restored.quiz.questionIds, questions.map(q => q.id));
 const inProgress = reviewRecord(restored);
 assert.equal(inProgress.attempts[0].answers[0].correct, undefined);
 assert.ok(inProgress.attempts[0].questionSnapshots.every(q => q.correct === undefined));
 assert.match(reviewText(inProgress), /Question set: All questions/);
 assert.match(recordHTML(inProgress), /<h3>All questions · in progress<\/h3>/);

 for (const q of questions.slice(1)) recordAnswer(restored.quiz, q.id, q.correct, 'confident');
 assert.equal(learnerResult(restored.quiz).score, questions.length);
});
test('provisional scores, support, first answers and full conversation survive reopen and export',()=>{
 const p=newPortfolio();p.quiz=createSession('researcher','learn');recordAssistance(p.quiz,p.quiz.questionIds[0],'hints');
 for(const [i,id]of p.quiz.questionIds.entries()){const q=questions.find(q=>q.id===id);recordAnswer(p.quiz,id,i===3?q.options.find(o=>o.id!==q.correct).id:q.correct,'fairly','My reasoning '+i);}
 transcriptEntry(p,'user','Please explain this <script>not executable</script>.','Learner');
 transcriptEntry(p,'assistant','A detailed answer.','OpenAI tutor',{model:'mock-model',promptVersion:'test'});
 p.identity={name:'Synthetic Student',email:'student@example.invalid',goal:'Practice only'};p.reflection='I will check paired differences.';
 const restored=restorePortfolio(JSON.parse(JSON.stringify(p))),record=reviewRecord(restored),text=reviewText(record);
 assert.equal(record.attempts[0].result.score,p.quiz.questionIds.length-1);assert.equal(record.attempts[0].result.assisted,1);
 assert.equal(record.conversation.length,2);assert.ok(record.learningOptions.focus.includes('causal'));assert.ok(restored.quiz.questionIds.includes('CORE-MEDIATOR-01')); assert.match(text,/My reasoning 3/);assert.match(text,/Synthetic Student/);assert.match(text,/<script>not executable<\/script>/);assert.match(text,/No accreditation or CPD points awarded/);assert.match(text,/I will check paired differences/);
 assert.throws(()=>restorePortfolio({schemaVersion:999}));
});
test('bundled R examples run with numerical assertions',()=>{
 const dir=mkdtempSync(tmpdir()+'/statsdirect-learn-');
 try{for(const l of lessons){const script=dir+'/'+l.id+'.R';const checks=lessonChecks[l.id];if(!checks)assert.match(l.r,/\bstopifnot\s*\(/,l.id);writeFileSync(script,`pdf(${JSON.stringify(dir+'/'+l.id+'.pdf')})\n${l.r}\n${checks??''}\ndev.off()\n`);const output=runR(['--vanilla',script]);if(checks)assert.ok(existsSync(dir+'/'+l.id+'.pdf'));if(l.id==='paired'){assert.match(output,/Paired t-test/);assert.match(output,/5.25/);}}}finally{rmSync(dir,{recursive:true});}
});
test('optional I-squared extension truncates below zero and responds to greater heterogeneity',()=>{
 const dir=mkdtempSync(tmpdir()+'/statsdirect-meta-');
 try{
  const lesson=lessons.find(l=>l.id==='meta');assert.match(lesson.r,/if \(FALSE\)/);
  for(const effect of [null,'c(-0.04,-0.18,-0.03)']){
   let code=lesson.r.replace('if (FALSE)','if (TRUE)');
   if(effect)code=code.replace(/effect <- c\([^)]*\)/,`effect <- ${effect}`);
   const check=effect?'stopifnot(q>study_df,i_squared>0,i_squared<100,abs(i_squared-100*(q-2)/q)<1e-12)':'stopifnot(abs(q-1.25)<1e-12,study_df==2,i_squared==0)';
   const script=dir+'/meta.R';writeFileSync(script,`pdf(${JSON.stringify(dir+'/meta.pdf')})\n${code}\n${check}\ndev.off()\n`);
   runR(['--vanilla',script]);
  }
 }finally{rmSync(dir,{recursive:true});}
});
test('saved goals round trip; malformed nested records are rejected before replacement',()=>{
 const p=newPortfolio();p.learning.qualifications='MRCGP AKT';p.learning.needs='Causal inference and diagnostic test interpretation';p.quiz=createSession('foundation','learn');
 const restored=restorePortfolio(JSON.parse(JSON.stringify(p)));
 assert.equal(restored.learning.qualifications,'MRCGP AKT');assert.equal(restored.learning.needs,p.learning.needs);
 for(const corrupt of [x=>x.identity=null,x=>x.learning.focus=null,x=>x.conversation.push({text:3}),x=>x.quiz.answers=[null],x=>x.quiz.questionIds[0]='missing',x=>x.quiz.questionSnapshots[0].options=null]){
  const damaged=structuredClone(p);corrupt(damaged);assert.throws(()=>restorePortfolio(damaged),/not been overwritten/);
 }
});
test('a saved attempt keeps its wording and answer key when the question bank changes',()=>{
 const p=newPortfolio();p.quiz=createSession('foundation','learn');const id=p.quiz.questionIds[0],q=questions.find(q=>q.id===id),oldStem=q.stem,oldKey=q.correct;
 try{q.stem='Replacement question';q.correct=oldKey==='A'?'B':'A';const restored=restorePortfolio(JSON.parse(JSON.stringify(p)));assert.equal(sessionQuestion(restored.quiz,id).stem,oldStem);recordAnswer(restored.quiz,id,oldKey,'confident');assert.equal(restored.quiz.answers[0].correct,true);assert.equal(reviewRecord(restored).attempts[0].questionSnapshots[0].correct,oldKey);for(const next of restored.quiz.questionIds.slice(1))recordAnswer(restored.quiz,next,sessionQuestion(restored.quiz,next).correct,'confident');assert.equal(reviewBundle(restored.quiz).questionSnapshots[0].stem,oldStem);}finally{q.stem=oldStem;q.correct=oldKey;}
});

test('worksheet provenance survives portfolio restore and text export',()=>{
  const state=newPortfolio();transcriptEntry(state,'assistant','The engine calculated the paired differences.','StatsDirect AI tutor',{workspaceSources:['test.xlsx · PEFR · A, B · rows 2–10 · 9 rows']});
  const restored=restorePortfolio(JSON.parse(JSON.stringify(state)));
  assert.match(reviewText(reviewRecord(restored)),/StatsDirect context: test.xlsx/);
  const bad=JSON.parse(JSON.stringify(state));bad.conversation[0].workspaceSources='invalid';assert.throws(()=>restorePortfolio(bad));
});

test('sharing confirmations persist in the learning record and exports; old portfolios default to none',()=>{
 const p=newPortfolio();delete p.sharingConsents;
 assert.deepEqual(restorePortfolio(p).sharingConsents,[]);
 const c={id:'consent',at:'2026-10-08T12:00:00Z',requestID:'question',policyVersion:'document-sharing-v1',noPersonIdentifiers:true,statement:'I checked that these documents contain no person identifiers.',documents:[{id:'grid',title:'Synthetic data',kind:'grid',version:3}]};
 p.sharingConsents=[c];const restored=restorePortfolio(JSON.parse(JSON.stringify(p))),record=reviewRecord(restored);
 assert.deepEqual(record.sharingConsents,[c]);assert.match(reviewText(record),/DOCUMENT SHARING CONFIRMATIONS/);assert.match(reviewText(record),/Synthetic data \(grid\) · revision 3/);
 for(const bad of [{...c,noPersonIdentifiers:false},{...c,documents:[{title:'incomplete'}]}])assert.throws(()=>restorePortfolio({...p,sharingConsents:[bad]}),/sharing confirmations/);
});

test('local R replay snapshots survive reopening but stay out of learner exports and provider conversation',async()=>{
 const {conversationForTutor}=await import('./lesson-context.mjs');
 const p=newPortfolio();transcriptEntry(p,'user','Explain pairing','Learner',{lessonId:p.lesson});
 p.rContexts={request:{userMessageID:p.conversation[0].id,rSource:'PRIVATE_SOURCE_SNAPSHOT',rData:'PRIVATE_DATA_SNAPSHOT'}};
 const restored=restorePortfolio(JSON.parse(JSON.stringify(p)));
 assert.equal(restored.rContexts.request.rSource,'PRIVATE_SOURCE_SNAPSHOT');
 assert.doesNotMatch(JSON.stringify(reviewRecord(restored)),/PRIVATE_(SOURCE|DATA)_SNAPSHOT/);
 assert.doesNotMatch(JSON.stringify(conversationForTutor(restored)),/PRIVATE_(SOURCE|DATA)_SNAPSHOT/);
});
test('the saved form stores each question and publication once and restores every attempt exactly',()=>{
 const p=newPortfolio(),first=tracks.foundation?'foundation':Object.keys(tracks)[0];
 const whole=()=>{const s=createSession(first,'learn',undefined,null,true);for(const q of s.questionSnapshots)recordAnswer(s,q.id,q.correct,'fairly','Because.');return s;};
 for(let i=0;i<5;i++)p.attempts.push(whole());
 const early=createSession(first,'learn',undefined,null,true);recordAnswer(early,early.questionIds[0],early.questionSnapshots[0].options[0].id,'unsure');early.endedAt=new Date().toISOString();p.attempts.push(early);
 p.quiz=createSession(first,'test');
 const packed=packPortfolio(p),saved=JSON.parse(JSON.stringify(packed));
 assert.equal(packed.schemaVersion,2);assert.equal(packed.quiz.mode,'test');assert.equal(packed.quiz.completedAt,null);
 assert.equal(packed.sharedContent.questions.length,questions.length);
 assert.ok(packed.sharedContent.publications.length<=1);
 for(const s of [...packed.attempts,packed.quiz]){assert.equal(s.questionSnapshots,undefined);assert.equal(s.questionRefs.length,s.questionIds.length);assert.ok(!('contentPublication' in s)||s.contentPublication===null);}
 assert.equal(p.attempts[0].questionSnapshots.length,questions.length,'packing leaves the record in memory as it was');
 const restored=restorePortfolio(saved);
 assert.deepEqual(JSON.parse(JSON.stringify(restored)),JSON.parse(JSON.stringify({...newPortfolio(),...p,training:restored.training,learning:restored.learning})));
 assert.equal(restored.sharedContent,undefined);assert.notEqual(restored.attempts[0].questionSnapshots[0],restored.attempts[1].questionSnapshots[0]);
 const inline=JSON.stringify(p).length,compact=JSON.stringify(packed).length,one=JSON.stringify(p.attempts[0]).length;
 assert.ok(compact<inline-4*one*0.7,`the saved form (${compact}) must be much smaller than the inline form (${inline})`);
 const more=structuredClone(p);for(let i=0;i<40;i++)more.attempts.push(whole());
 assert.ok(JSON.stringify(packPortfolio(more)).length<10_000_000/3,'forty more whole-bank attempts stay far below the save limit');
});
test('the saved form keeps a question as it was asked, ignores key order and still reads the inline form',()=>{
 const p=newPortfolio(),s=createSession('foundation','learn'),q=questions.find(x=>x.id===s.questionIds[0]),oldStem=q.stem,oldKey=q.correct;
 recordAnswer(s,q.id,q.correct,'confident');s.endedAt=new Date().toISOString();p.attempts.push(s);
 const sorted=v=>Array.isArray(v)?v.map(sorted):v!==null&&typeof v==='object'?Object.fromEntries(Object.keys(v).sort().map(k=>[k,sorted(v[k])])):v;
 const reloaded=restorePortfolio(sorted(JSON.parse(JSON.stringify(packPortfolio(p)))));
 try{
  q.stem='Replacement question';q.correct=oldKey==='A'?'B':'A';
  const again=createSession('foundation','learn');reloaded.attempts.push(again);
  const packed=packPortfolio(reloaded),stems=packed.sharedContent.questions.filter(x=>x.id===q.id).map(x=>x.stem).sort();
  assert.deepEqual(stems,[oldStem,'Replacement question'].sort(),'both versions of a changed question are kept');
  assert.equal(packed.sharedContent.questions.length,new Set(s.questionIds).size+1,'unchanged questions are stored once whatever their key order');
  const back=restorePortfolio(JSON.parse(JSON.stringify(packed)));
  assert.equal(sessionQuestion(back.attempts[0],q.id).stem,oldStem);assert.equal(sessionQuestion(back.attempts[0],q.id).correct,oldKey);assert.equal(sessionQuestion(back.attempts[1],q.id).stem,'Replacement question');
 }finally{q.stem=oldStem;q.correct=oldKey;}
 const legacy=JSON.parse(JSON.stringify(p));assert.equal(legacy.sharedContent,undefined);
 assert.deepEqual(restorePortfolio(legacy).attempts[0].questionSnapshots,p.attempts[0].questionSnapshots);
});
test('a saved form with a broken reference is refused, not repaired',()=>{
 const p=newPortfolio();p.quiz=createSession('foundation','learn');
 const good=()=>JSON.parse(JSON.stringify(packPortfolio(p)));
 for(const corrupt of [x=>x.quiz.questionRefs[0]=9999,x=>x.quiz.questionRefs[0]=-1,x=>x.quiz.questionRefs[0]='0',x=>x.quiz.questionRefs='all',x=>x.sharedContent.questions=null,x=>x.sharedContent=[],x=>x.quiz.questionSnapshots=[],x=>x.quiz.questionRefs.pop(),x=>{if(x.quiz.publicationRef===undefined)x.quiz.publicationRef=0;else x.quiz.publicationRef=5;}]){
  const x=good();corrupt(x);assert.throws(()=>restorePortfolio(x),/incompatible/);
 }
 assert.doesNotThrow(()=>restorePortfolio(good()));
});
