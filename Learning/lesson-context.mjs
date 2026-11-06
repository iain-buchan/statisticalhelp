import {transcriptEntry} from './portfolio.mjs';

// Older records contain unlabelled guide prompts. Identify only exact bundled text;
// never infer the topic of a learner's own words or rewrite their conversation.
export function labelStudyGuides(state,lessons){
  for(const entry of state.conversation){
    if(entry.source!=='Study guide'||entry.role!=='assistant')continue;
    const origin=lessons.find(l=>l.challenge===entry.text||(l.previousChallenges??[]).includes(entry.text));
    if(origin){entry.lessonId=origin.id;entry.lessonTitle=origin.title;entry.lessonPrompt=true;}
  }
}
export function ensureLessonPrompt(state,lessons){
  labelStudyGuides(state,lessons);
  const current=lessons.find(l=>l.id===state.lesson);
  if(!current||typeof current.challenge!=='string'||!current.challenge.trim())return false;
  const latest=lessonConversation(state).findLast(c=>c.lessonPrompt===true);
  if(latest?.lessonId===current.id&&latest.text===current.challenge)return false;
  transcriptEntry(state,'assistant',current.challenge,'Study guide',{lessonId:current.id,lessonTitle:current.title,lessonPrompt:true});
  return true;
}
export function guidePresentation(entry,currentLesson){
  if(entry.lessonPrompt!==true)return {label:entry.lessonTitle?`${entry.source} · ${entry.lessonTitle}`:entry.source,earlier:false};
  const earlier=entry.lessonId!==currentLesson.id||entry.text!==currentLesson.challenge;
  return {label:`${earlier?'Earlier study guide':'Study guide'} · ${entry.lessonTitle}`,earlier};
}
export function conversationForTutor(state){
  return lessonConversation(state).slice(-40).map(c=>({role:c.role,text:c.lessonTitle?`[Lesson context: ${c.lessonTitle}]\n${c.text}`:c.text}));
}

// Keep a separate conversation per lesson while preserving the complete portfolio.
export function lessonConversation(state){
 return state.conversation.filter(c=>c.lessonId===state.lesson || (!c.lessonId && state.lesson==='epidemiology'));
}
// Show each lesson prompt once per lesson thread; records written before 6 November 2026 may hold repeats from lesson switches.
export function visibleLessonConversation(state){
  const shown=new Set();
  return lessonConversation(state).filter(c=>{
    if(c.lessonPrompt!==true)return true;
    const key=c.lessonId+'\n'+c.text;
    if(shown.has(key))return false;
    shown.add(key);return true;
  });
}
// Lettered options at the start of lines ("A.", "B)", "**C.**", "- D."); three or more make a multiple-choice question.
export function optionLetters(text){
  const found=new Set();
  for(const m of String(text??'').matchAll(/^[ \t]*(?:[-*][ \t]+)?(?:\*\*)?([A-H])(?:\*\*)?[.)](?:\*\*)?[ \t]+\S/gm))found.add(m[1]);
  return [...found].sort();
}
// A tutor or study-guide message that offers options or ends with a question is answered in the composer.
export function awaitingAnswer(entry){
  if(!entry||entry.role!=='assistant')return false;
  return optionLetters(entry.text).length>=3||/\?[\s*_]*$/.test(String(entry.text??'').trim());
}
