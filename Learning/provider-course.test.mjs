import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {courseLessons,courseQuestions,adoptCourse,submitCourseWork,addProviderReview,validEmail,safeURL} from './provider-course.mjs';
import {newPortfolio,restorePortfolio,reviewRecord,reviewText,transcriptEntry} from './portfolio.mjs';
import {createSession,recordAnswer,recordAssistance} from './quiz.mjs';
import {contentFile} from './test-config.mjs';
const fixture=()=>JSON.parse(readFileSync(contentFile('Learn/provider-course-example.json')));
test('provider lessons, submissions, keys and metadata survive changes to the active pack',()=>{
 const p=newPortfolio(),pack=fixture();adoptCourse(p,pack);const l=courseLessons(pack)[1];
 submitCourseWork(p,l,'Paired differences use the same staff. The design cannot isolate a causal effect.');
 p.quiz=createSession('foundation','learn','attempt',{version:pack.version,questions:courseQuestions(pack),course:{title:pack.title,provider:pack.provider.name,version:pack.version}});
 const first=p.quiz.questionSnapshots[0];recordAssistance(p.quiz,first.id,'hints');recordAnswer(p.quiz,first.id,first.correct,'fairly','Same staff, repeated observations.');
 for(const q of p.quiz.questionSnapshots.slice(1))recordAnswer(p.quiz,q.id,q.correct,'confident','Interpret the design.');
 pack.version='2';pack.questions[0].correct='C';adoptCourse(p,pack);
 const restored=restorePortfolio(JSON.parse(JSON.stringify(p))),record=reviewRecord(restored),text=reviewText(record);
 assert.equal(record.attempts[0].result.score,3);assert.equal(record.attempts[0].result.assisted,1);assert.equal(record.attempts[0].course.version,'1.1');assert.equal(record.attempts[0].questionSnapshots[0].correct,'A');
 assert.equal(record.courseWork[0].training.courseVersion,'1.1');assert.equal(record.training.courseVersion,'2');assert.match(text,/Same staff, repeated observations/);assert.match(text,/cannot isolate a causal effect/);
});
test('provider independent practice withholds keys and hints from exports until finished',()=>{
 const p=newPortfolio(),pack=fixture();p.quiz=createSession('foundation','test','test',{version:pack.version,questions:courseQuestions(pack)});
 const q=p.quiz.questionSnapshots[0];assert.throws(()=>recordAssistance(p.quiz,q.id,'hints'));
 recordAnswer(p.quiz,q.id,'B','unsure');const record=reviewRecord(p);assert.equal(record.attempts[0].questionSnapshots[0].correct,undefined);assert.equal(record.attempts[0].answers[0].correct,undefined);
 assert.equal(restorePortfolio(p).quiz.questionSnapshots[0].correct,'A');
});
test('provider feedback binds to the learner record without asserting authenticity or an award',()=>{
 const p=newPortfolio(),r={schemaVersion:1,portfolioID:p.id,provider:'Example University',reviewer:'Tutor',reviewedAt:'2026-09-30',decision:'Further work requested',feedback:'Discuss the missing comparison group.',creditStatement:'No credit yet.'};
 assert.throws(()=>addProviderReview(p,{...r,portfolioID:'someone-else'}));addProviderReview(p,r);
 const text=reviewText(reviewRecord(restorePortfolio(p)));assert.match(text,/Further work requested/);assert.match(text,/authenticity not independently verified/);assert.match(text,/No credit yet/);
});
test('resource provenance and attached R evidence persist in the complete review record',()=>{
 const p=newPortfolio();transcriptEntry(p,'assistant','Consider the study design.','Tutor',{courseSources:[{id:'resource-1',title:'Study design',url:'https://openintro-ims.netlify.app/data-design',retrievedAt:'2026-09-30'}]});
 p.evidence.push({title:'R report',at:'2026-09-30',text:'t.test(before, after, paired=TRUE)'});
 const text=reviewText(reviewRecord(restorePortfolio(p)));assert.match(text,/https:\/\/openintro-ims/);assert.match(text,/retrieved 2026-09-30/);assert.match(text,/t.test\(before/);
 const bad=structuredClone(p);bad.courseWork=[null];assert.throws(()=>restorePortfolio(bad));
});
test('old records migrate; unsafe recipient and link formats cannot be used',()=>{
 const p=newPortfolio();for(const k of ['training','courseKey','courseWork','workDrafts','providerReviews','evidence'])delete p[k];const r=restorePortfolio(p);assert.equal(r.training.reviewEmail,'support@statisticalhelp.org');assert.deepEqual(r.courseWork,[]);
 assert.ok(validEmail('assessment@university.example'));for(const s of ['a@b.example\n','a@b.example,b@c.example','mailto:a@b.example','a@b.example\r\nBcc:c@d.example'])assert.equal(validEmail(s),false);
 for(const s of ['javascript:alert(1)','file:///etc/passwd','https://user:password@site.example'])assert.equal(safeURL(s),'');
});

test('simplified options migrate old settings without losing history or provider details',()=>{
 const p=newPortfolio(),pack=fixture();adoptCourse(p,pack);submitCourseWork(p,courseLessons(pack)[1],'Original submission');
 delete p.learning.statisticalSkills;delete p.training.assessmentType;delete p.courseWork[0].training.assessmentType;
 p.training.reviewEmail='tutor@university.example';p.training.requirements='Original provider conditions';p.learning.targetDate='2027-01-01';p.learning.focus.push('r');
 const before=structuredClone(p.courseWork),r=restorePortfolio(p);
 assert.equal(r.learning.statisticalSkills,'beginner');assert.equal(r.training.assessmentType,'none');assert.equal(r.training.reviewEmail,'tutor@university.example');assert.equal(r.training.requirements,'Original provider conditions');assert.equal(r.learning.targetDate,'2027-01-01');assert.ok(r.learning.focus.includes('r'));assert.deepEqual(r.courseWork,before);
 r.learning.statisticalSkills='advanced';r.training.assessmentType='cpd';const saved=restorePortfolio(JSON.parse(JSON.stringify(r)));const text=reviewText(reviewRecord(saved));
 assert.match(text,/Statistical skills: Advanced/);assert.match(text,/Continued professional development credits/);
 const bad=structuredClone(r);bad.learning.statisticalSkills='expert';assert.throws(()=>restorePortfolio(bad));bad.learning.statisticalSkills='beginner';bad.training.assessmentType='guaranteed credit';assert.throws(()=>restorePortfolio(bad));
});
test('only an untouched old default recipient changes; historical submissions never migrate',()=>{
 const p=newPortfolio();p.training.reviewEmail='support@statsdirect.com';delete p.training.assessmentType;
 assert.equal(restorePortfolio(p).training.reviewEmail,'support@statisticalhelp.org');
 p.training.courseTitle='An existing course';assert.equal(restorePortfolio(p).training.reviewEmail,'support@statsdirect.com');
});
