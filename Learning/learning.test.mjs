import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync,mkdtempSync,writeFileSync,rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {questions,tracks} from './bank.mjs';
import {lessonChecks} from './lesson-checks.mjs';
import {createSession,recordAnswer,recordAssistance,learnerResult,sessionQuestion,reviewBundle} from './quiz.mjs';
import {newPortfolio,restorePortfolio,reviewRecord,reviewText,transcriptEntry,practiceContext} from './portfolio.mjs';
import {contentFile,runR} from './test-config.mjs';
const lessons=JSON.parse(readFileSync(contentFile('Learn/lessons.json')));
test('35 original draft questions map to five pathways, valid choices, real help and engine operations',()=>{
 const operations=JSON.parse(readFileSync(contentFile('analysis-menu.json'))).operations;
 assert.equal(questions.length,35);assert.equal(new Set(questions.map(q=>q.id)).size,35);
 for(const t of Object.keys(tracks))assert.ok(questions.filter(q=>q.track===t).length>=4);
 for(const q of questions){assert.equal(q.options.length,5);assert.equal(q.options.filter(o=>o.id===q.correct).length,1);assert.ok(existsSync(contentFile('Help/'+q.help)),q.help);if(q.operation)assert.ok(operations[q.operation]);}
 for(const l of lessons){assert.ok(existsSync(contentFile('Help/'+l.help)),l.help);assert.ok(operations[l.operation]);}
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
test('provisional scores, support, first answers and full conversation survive reopen and export',()=>{
 const p=newPortfolio();p.quiz=createSession('researcher','learn');recordAssistance(p.quiz,p.quiz.questionIds[0],'hints');
 for(const [i,id]of p.quiz.questionIds.entries()){const q=questions.find(q=>q.id===id);recordAnswer(p.quiz,id,i===3?'A':q.correct,'fairly','My reasoning '+i);}
 transcriptEntry(p,'user','Please explain this <script>not executable</script>.','Learner');
 transcriptEntry(p,'assistant','A detailed answer.','OpenAI tutor',{model:'mock-model',promptVersion:'test'});
 p.identity={name:'Synthetic Student',email:'student@example.invalid',goal:'Practice only'};p.reflection='I will check paired differences.';
 const restored=restorePortfolio(JSON.parse(JSON.stringify(p))),record=reviewRecord(restored),text=reviewText(record);
 assert.equal(record.attempts[0].result.score,p.quiz.questionIds.length-1);assert.equal(record.attempts[0].result.assisted,1);
 assert.equal(record.conversation.length,2);assert.ok(record.learningOptions.focus.includes('causal'));assert.ok(restored.quiz.questionIds.includes('CORE-MEDIATOR-01')); assert.match(text,/My reasoning 3/);assert.match(text,/Synthetic Student/);assert.match(text,/<script>not executable<\/script>/);assert.match(text,/No accreditation or CPD points awarded/);assert.match(text,/I will check paired differences/);
 assert.throws(()=>restorePortfolio({schemaVersion:999}));
});
test('bundled R examples run without packages, use exact fictional rows, and produce plots',()=>{
 const dir=mkdtempSync(tmpdir()+'/statsdirect-learn-');
 try{for(const l of lessons){const script=dir+'/'+l.id+'.R';writeFileSync(script,`pdf(${JSON.stringify(dir+'/'+l.id+'.pdf')})\n${l.r}\n${lessonChecks[l.id]}\ndev.off()\n`);const output=runR(['--vanilla',script]);assert.ok(existsSync(dir+'/'+l.id+'.pdf'));if(l.id==='paired'){assert.match(output,/Paired t-test/);assert.match(output,/5.25/);}}}finally{rmSync(dir,{recursive:true});}
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
