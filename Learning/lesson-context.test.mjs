import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {newPortfolio,transcriptEntry,restorePortfolio,reviewRecord,reviewText} from './portfolio.mjs';
import {labelStudyGuides,ensureLessonPrompt,guidePresentation,conversationForTutor,lessonConversation,visibleLessonConversation,optionLetters,awaitingAnswer} from './lesson-context.mjs';
import {contentFile} from './test-config.mjs';
const lessons=JSON.parse(readFileSync(contentFile('Learn/lessons.json')));
const epi=lessons.find(l=>l.id==='epidemiology'),paired=lessons.find(l=>l.id==='paired');
test('reopening paired lesson labels and collapses a legacy epidemiology prompt without deleting history',()=>{
  const s=newPortfolio();s.lesson='paired';s.draft='My unfinished answer';
  const old=transcriptEntry(s,'assistant',epi.challenge,'Study guide');
  const learner=transcriptEntry(s,'user',epi.challenge,'Learner');
  assert.equal(ensureLessonPrompt(s,lessons),true);
  assert.equal(old.lessonId,'epidemiology');assert.equal(learner.lessonId,undefined);
  assert.deepEqual(guidePresentation(old,paired),{label:'Earlier study guide · Epidemiological foundations',earlier:true});
  assert.equal(s.conversation.at(-1).text,paired.challenge);assert.equal(s.draft,'My unfinished answer');
  assert.equal(ensureLessonPrompt(s,lessons),false);assert.equal(s.conversation.length,3);
  const restored=restorePortfolio(JSON.parse(JSON.stringify(s)));assert.equal(ensureLessonPrompt(restored,lessons),false);
  assert.match(reviewText(reviewRecord(restored)),/Study guide · Epidemiological foundations/);
  assert.equal(restored.conversation[0].text,epi.challenge);
});
test('switching lessons isolates tutor context while retaining all portfolio entries',()=>{
  const s=newPortfolio();ensureLessonPrompt(s,lessons);s.lesson='paired';ensureLessonPrompt(s,lessons);
  assert.equal(guidePresentation(s.conversation[0],paired).earlier,true);assert.equal(guidePresentation(s.conversation[1],paired).earlier,false);
  const history=conversationForTutor(s);assert.equal(history.length,1);assert.match(history[0].text,/Lesson context: Comparing paired measurements/);assert.equal(s.conversation.length,2);
  s.lesson='epidemiology';assert.equal(ensureLessonPrompt(s,lessons),false);assert.equal(s.conversation.length,2);assert.equal(visibleLessonConversation(s).at(-1).text,epi.challenge);
});
test('the paired lesson establishes actual pairing and upgrades its older prompt without rewriting it',()=>{
  assert.match(paired.challenge,/same eight people/);assert.match(paired.challenge,/different, unmatched people/);assert.match(paired.summary,/not automatically paired/);
  const s=newPortfolio();s.lesson='paired';const old=transcriptEntry(s,'assistant',paired.previousChallenges[0],'Study guide');
  labelStudyGuides(s,lessons);assert.equal(old.lessonId,'paired');assert.equal(guidePresentation(old,paired).earlier,true);
  assert.equal(ensureLessonPrompt(s,lessons),true);assert.equal(old.text,paired.previousChallenges[0]);
});
test('returning to a lesson keeps its single prompt, repeats already in a record are shown once, and tutor questions are recognised',()=>{
  const s=newPortfolio();ensureLessonPrompt(s,lessons);
  transcriptEntry(s,'user','Ask me one original exam-style question on this topic, then wait for my answer.','Learner',{lessonId:'epidemiology',lessonTitle:epi.title});
  const mcq=transcriptEntry(s,'assistant','Which statement is **most appropriate**?\n\n**A.** One\n**B.** Two\n**C.** Three\n**D.** Four\n**E.** Five\n\nWhich option would you choose, and why?','StatsDirect AI tutor',{lessonId:'epidemiology',lessonTitle:epi.title});
  for(const id of ['causal','paired','diagnostic','epidemiology']){s.lesson=id;ensureLessonPrompt(s,lessons);}
  assert.equal(s.conversation.filter(c=>c.lessonId==='epidemiology'&&c.lessonPrompt===true).length,1);
  assert.equal(visibleLessonConversation(s).at(-1),mcq);
  const repeat=transcriptEntry(s,'assistant',epi.challenge,'Study guide',{lessonId:'epidemiology',lessonTitle:epi.title,lessonPrompt:true});
  assert.equal(lessonConversation(s).at(-1),repeat);assert.equal(visibleLessonConversation(s).at(-1),mcq);
  assert.equal(visibleLessonConversation(s).filter(c=>c.lessonPrompt===true).length,1);
  assert.deepEqual(optionLetters(mcq.text),['A','B','C','D','E']);assert.equal(awaitingAnswer(mcq),true);
  assert.equal(awaitingAnswer(s.conversation[0]),true);assert.deepEqual(optionLetters(epi.challenge),[]);
  assert.deepEqual(optionLetters('A) Yes\nB) No'),['A','B']);assert.equal(awaitingAnswer({role:'assistant',text:'A) Yes\nB) No'}),false);
  assert.equal(awaitingAnswer(transcriptEntry(s,'user','C, because the counts alone say nothing about the population at risk.','Learner',{lessonId:'epidemiology'})),false);
  assert.equal(awaitingAnswer({role:'assistant',text:'Good: the population at risk is what matters.'}),false);
});
