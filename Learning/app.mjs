import {recordHTML} from './record-html.mjs';
import {richText,bindRichText} from './rich-text.mjs';
import {conversationReading,restoreConversationReading} from './conversation-view.mjs';
import {questions,tracks} from './bank.mjs';
import {createSession,recordAnswer,recordAssistance,learnerResult,sessionQuestion} from './quiz.mjs';
import {newPortfolio,restorePortfolio,transcriptEntry,reviewRecord,reviewText,practiceContext,stages} from './portfolio.mjs';
import {examSources,communicationPrompt} from './exam-sources.mjs';
import {labelStudyGuides,ensureLessonPrompt,guidePresentation,conversationForTutor,lessonConversation,visibleLessonConversation,optionLetters,awaitingAnswer} from './lesson-context.mjs';
import lessons from '../Content/Learn/lessons.json';
import contract from './host-contract.json' with {type:'json'};
import {lessonGuideHTML,learningTargetHTML} from './lesson-guide.mjs';
import {courseKey,courseLessons,courseQuestions,adoptCourse,submitCourseWork,addProviderReview,safeURL,validEmail,statisticalSkills} from './provider-course.mjs';
import {trainingHTML,bindTraining,courseWorkHTML,providerRecordHTML} from './course-ui.mjs';
import {bridge,platformOf,wordsFor,hostClassName} from './host.mjs';
const $=id=>document.getElementById(id);
const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let state=newPortfolio(),loaded=false,settings={configured:false,service:'',label:'Not connected'},pending=null,revision=0,feedback=null,reviewApproved=false,coursePack=null;
let bridgeGeneration='';
let streamedText='',streamTimer=null,renderedStudy=null;
let workspace={choice:"__none__",choices:[],label:"Lessons only · open documents are not shared"};
// The host registers window.statsDirectHost before the page runs (HOST-CONTRACT.md); host.mjs holds the fallback, the platform rule and the wording.
const host=bridge(window);
const native=Boolean(host);
const post=body=>host?.postMessage(body);
const platform=()=>platformOf(window,host,settings);
const words=()=>wordsFor(platform());
const hostClass=()=>{for(const c of [...document.documentElement.classList])if(c.startsWith('host-'))document.documentElement.classList.remove(c);document.documentElement.classList.add(hostClassName(platform()));};
const allLessons=()=>[...lessons,...courseLessons(coursePack)];
const lesson=()=>allLessons().find(x=>x.id===state.lesson)??lessons[0];
const privacyText=()=>settings.disabled?`The online tutor is disabled by ${words().computer}’s learning policy. Local lessons, practice, help and R examples remain available.`:settings.configured?'Send shares learning context and up to 40 recent messages with OpenAI. Document sharing requires confirmation for each question. Earlier shared content can remain in the conversation. Do not include person identifiers.':'Connect your ChatGPT account to talk with the tutor here. No API key is needed. Your account’s Codex access and usage allowance apply.';
const sendLabel=()=>settings.disabled?'Online tutor disabled':settings.configured?'Send':settings.signingIn?'Finish sign-in':'Use my ChatGPT';
const independent=()=>state.quiz?.mode==='test'&&!state.quiz.completedAt;
const notice=text=>{ $('notice').textContent=text;$('notice').hidden=!text; };
function save(){if(!loaded)return;state.updatedAt=new Date().toISOString();revision++;reviewApproved=false;$('saveStatus').textContent='Saving…';if(native)post({action:'save',state,revision});else{try{localStorage.setItem('statsdirect-learning-preview',JSON.stringify(state));$('saveStatus').textContent='Saved in this browser';}catch{$('saveStatus').textContent='Not saved — export before closing';}}}
function activity(text){state.activities.push({at:new Date().toISOString(),text});save();}
function teachingSupport(){if(state.quiz?.mode==='learn'&&!state.quiz.completedAt)recordAssistance(state.quiz,state.quiz.questionIds[state.quiz.answers.length],'explanations');}
function welcome(){if(!state.conversation.length)transcriptEntry(state,'assistant','Welcome. We can start with a clinical, service or research question, work through an example in StatsDirect, then connect each step to R when you are ready.\n\nFirst, tell me the population and question you want to understand. Are you describing what happens, predicting an outcome, or asking whether something causes it?','Study guide');}
function render(){
  document.body.classList.toggle('study-mode',state.view==='study');
  document.body.classList.toggle('conversation-focus',state.view==='study'&&state.conversationFocus===true);
  $('workspaceContext').hidden=state.view!=='study';
  $('profile').innerHTML=Object.entries(tracks).map(([id,t])=>`<option value="${id}" ${state.profile===id?'selected':''}>${esc(t.title)}</option>`).join('');
  $('stage').innerHTML=Object.entries(stages).map(([id,t])=>`<option value="${id}" ${state.stage===id?'selected':''}>${esc(t)}</option>`).join('');
  $('profile').disabled=Boolean(state.quiz&&!state.quiz.completedAt)||Boolean(pending);$('stage').disabled=independent()||Boolean(pending);
  $('lessons').innerHTML=allLessons().map((l,i)=>`<button data-lesson="${esc(l.id)}" class="${state.view==='study'&&state.lesson===l.id?'current':''}" ${independent()||pending?'disabled':''}>${i+1}. ${esc(l.title)}</button>`).join('');
  $('practiceNav').classList.toggle('current',state.view==='practice');$('practiceNav').disabled=Boolean(pending);$('recordNav').classList.toggle('current',state.view==='record');
  $('sourcesNav').classList.toggle('current',state.view==='sources');$('sourcesNav').disabled=independent();
  $('practiceNav').querySelector('span').textContent=state.quiz?.questionIds.length??(courseQuestions(coursePack).length||questions.filter(q=>q.track===state.profile||q.track==='core').length);
  $('options').disabled=independent()||Boolean(pending);$('settings').disabled=Boolean(pending)||Boolean(settings.disabled);
  if(state.view==='options')renderOptions();else if(state.view==='sources')renderSources();else if(state.view==='practice')renderPractice();else if(state.view==='record')renderRecord();else renderStudy();
}
const bodyHTML=richText;
// Learners see reading recommendations (sources with a link) under a reply; the provenance of lesson prompts stays in the record for teachers.
const furtherReadingHTML=entry=>{const links=(entry.courseSources??[]).filter(s=>safeURL(s.url));return links.length?`<div class="small context-used">Further reading: ${links.map(s=>`<a href="${esc(safeURL(s.url))}" target="_blank" rel="noopener">${esc(s.title)}</a>`).join('; ')}</div>`:'';};
// The composer doubles as the answer box: say so when the latest tutor or study-guide message asks a question.
const composerLabel=()=>awaitingAnswer(visibleLessonConversation(state).at(-1))?'Answer the question above, or ask your own':'Ask a question, explain your thinking, or paste a small R example';
const composerPlaceholder=()=>{const last=visibleLessonConversation(state).at(-1);if(!awaitingAnswer(last))return 'For example: why do we analyse the differences?';return optionLetters(last.text).length>=3?'For example: C, because …':'Type your answer, then press Send';};
const quickAnswersHTML=entry=>{const letters=optionLetters(entry.text);return letters.length>=3?`<div class="quick-answers" role="group" aria-label="Choose an answer">${letters.map(x=>`<button type="button" data-answer="${x}">Answer ${x}</button>`).join('')}</div>`:'';};
function renderStudy(){const l=lesson();if(ensureLessonPrompt(state,allLessons()))save();
  const key=state.id+':'+l.id,reading=renderedStudy===key?conversationReading($('messages')):null;renderedStudy=key;
  $('main').innerHTML=`<section class="intro"><div class="eyebrow">${esc(l.topic)}</div><h1>${esc(l.title)}</h1></section><div class="actions">${l.operation?'<button class="primary" data-action="example">Try in StatsDirect</button>':''}${l.r?'<button data-action="r">Explore in R</button>':''}${l.help?'<button class="link-button" data-action="help">Read help ↗</button>':''}</div><details class="lesson-guide"><summary>Lesson guide and worked example</summary>${lessonGuideHTML(l,allLessons(),esc,safeURL)}</details>${courseWorkHTML(state,l)}<section class="chat"><div class="chat-heading"><strong>Your biostatistics tutor</strong><button id="focusConversation" class="link-button">${state.conversationFocus?'Show lesson controls':'Expand conversation'}</button><span id="tutorBadge" class="badge">${esc(settings.label)}</span></div><div id="messages" class="messages" role="log" aria-label="Learning conversation"></div><div class="composer"><label for="question">${esc(composerLabel())}</label><textarea id="question" rows="2" maxlength="4000" placeholder="${esc(composerPlaceholder())}" ${pending?'disabled':''}>${esc(state.draft)}</textarea><div class="composer-bottom"><small id="connectionPrivacy">${esc(privacyText())}</small><button id="send" class="primary" ${pending||settings.disabled?'disabled':''}>${esc(sendLabel())}</button>${pending?'<button id="stop">Stop</button>':''}</div><p class="small" id="chatStatus" role="status">${pending?'The tutor is thinking…':''}</p></div></section><div class="suggestions"><button data-prompt="Explain this without assuming I know any statistics.">Explain simply</button><button data-prompt="Ask me one original exam-style question on this topic, then wait for my answer.">Test my understanding</button><button data-prompt="Walk me through the bundled R example line by line, and suggest one small change I can try.">Help me learn R</button></div>`;
  const thread=visibleLessonConversation(state),last=thread.at(-1);
  $('messages').innerHTML=thread.map(c=>{
    const guide=guidePresentation(c,l);
    const content=`<div class="message-body">${bodyHTML(c.text)}</div>${furtherReadingHTML(c)}${c.workspaceSources?.length?`<div class="small context-used">Used: ${c.workspaceSources.map(esc).join('; ')}</div>`:''}`;
    if(guide.earlier)return `<details class="earlier-guide" data-message-id="${esc(c.id)}"><summary>${esc(guide.label)}</summary>${content}</details>`;
    return `<article data-message-id="${esc(c.id)}" class="message ${c.role==='user'?'user':''}"><div class="message-label">${c.role==='user'?'YOU':esc(guide.label.toUpperCase())}${c.model?' · '+esc(c.model):''}</div>${content}${c===last&&c.role==='assistant'?quickAnswersHTML(c):''}</article>`;
  }).join('');
  if($('courseAnswer')){$('courseAnswer').oninput=()=>{state.workDrafts[l.id]=$('courseAnswer').value;save();};$('saveCourseWork').onclick=()=>{try{submitCourseWork(state,l,$('courseAnswer').value);activity('Saved practical submission: '+l.title);notice('Submission saved to My learning record.');renderStudy();}catch(e){notice(e.message);}};}
  $('focusConversation').onclick=()=>{state.conversationFocus=!state.conversationFocus;save();render();};
  bindConversation();
  if(pending)renderStream(false);
  renderWorkspace();
  restoreConversationReading($('messages'),reading);
  $('question').oninput=()=>{state.draft=$('question').value;save();};
  $('question').onkeydown=e=>{if(e.key==='Enter'&&(e.metaKey||e.ctrlKey)){e.preventDefault();send();}};
  $('send').onclick=()=>settings.configured?send():configure();
  if($('stop'))$('stop').onclick=()=>{const id=pending;post({action:'cancel',id});window.statsDirectLearn.tutorError({id,message:'Reply stopped. You can send another question.'});};
  document.querySelectorAll('[data-action]').forEach(b=>b.onclick=()=>openActivity(b.dataset.action));
  document.querySelectorAll('[data-prompt]').forEach(b=>{b.disabled=Boolean(pending);b.onclick=()=>{state.draft=b.dataset.prompt;save();renderStudy();$('question').focus();};});
  document.querySelectorAll('[data-answer]').forEach(b=>{b.disabled=Boolean(pending);b.onclick=()=>{state.draft='My answer: '+b.dataset.answer+', because ';save();renderStudy();const q=$('question');q.focus();q.setSelectionRange(q.value.length,q.value.length);};});
}
function bindConversation(){const generation=bridgeGeneration,lessonID=lesson().id;bindRichText($('messages'),{
  copy:code=>{if(native)post({action:'copyCode',code});else navigator.clipboard.writeText(code).catch(()=>notice('Select the code and copy it.'));},
  openR:(code,position)=>{if(native)post({action:'openRCode',generation,lessonID,...position});else notice(`Open ${words().app} to review and run this code in an R session.`);}
});}
function renderStream(preserve=true){
  if(!$('messages')||!pending)return;
  let article=$('streamedReply');
  const reading=preserve?conversationReading($('messages')):null;
  if(!article){article=document.createElement('article');article.id='streamedReply';article.dataset.messageId=pending;article.className='message';$('messages').append(article);}
  article.innerHTML='<div class="message-label">TUTOR · REPLYING</div><div class="message-body">'+richText(streamedText||'Thinking…')+'</div>';
  bindConversation();if(preserve)restoreConversationReading($('messages'),reading);
}
function refreshEvidenceChoices(){
  const select=$('evidenceDocument');if(!select)return;
  const previous=select.value;const choices=workspace.choices.filter(d=>['report','r'].includes(d.kind));
  select.innerHTML=choices.length?choices.map(d=>`<option value="${esc(d.id)}">${esc(d.title)}</option>`).join(''):'<option value="">Open a report or R session first</option>';
  if(choices.some(d=>d.id===previous))select.value=previous;
  $('captureEvidence').disabled=!choices.length;
}
function renderWorkspace(){
  const element=$('workspaceContext');if(!element)return;
  element.innerHTML=`<label for="workspaceChoice">Tutor context</label><div class="workspace-controls"><select id="workspaceChoice" ${pending||independent()||settings.disabled?'disabled':''}><option value="__none__">Lessons only · no documents</option><option value="">All open documents · focus active</option>${workspace.choices.map(d=>`<option value="${esc(d.id)}">${esc(d.title)}</option>`).join('')}</select><button id="refreshWorkspace" class="link-button" ${pending?'disabled':''}>Refresh</button></div><small id="workspaceLabel" role="status" title="${esc(workspace.label)}">${esc(workspace.label)}</small>`;
  $('workspaceChoice').value=workspace.choice;
  $('workspaceChoice').onchange=()=>post({action:'workspace',choice:$('workspaceChoice').value});
  $('refreshWorkspace').onclick=()=>post({action:'workspace'});
}
function configure(){if(settings.disabled){notice(privacyText());return;}if(native)post({action:'settings'});else notice(`Choose Use my ChatGPT in ${words().application} to sign in through your browser. No API key is needed.`);}
function openActivity(action){if(independent())return;teachingSupport();const l=lesson();activity(`${action==='r'?'Opened bundled R example':action==='help'?'Opened help':'Opened StatsDirect analysis'}: ${l.title}`);if(native)post({action,lesson:l.id});else if(action==='help')window.open('../Help/'+l.help,'_blank');else notice(`Open this learning window in ${words().application} to start the `+(action==='r'?'R session':'StatsDirect analysis')+'.');}
function send(){
  if(pending||independent()||settings.disabled)return;
  const text=$('question')?.value.trim()||state.draft.trim();if(!text)return;
  if(!settings.configured){configure();return;}
  teachingSupport();
  const entry=transcriptEntry(state,'user',text,'Learner',{lessonId:lesson().id,lessonTitle:lesson().title});state.draft='';pending=crypto.randomUUID();streamedText='';save();render();
  post({action:'ask',id:pending,userMessageID:entry.id,lesson:lesson().id,profile:tracks[state.profile].title,stage:stages[state.stage],practiceContext:practiceContext(state),learningGoals:state.learning,messages:conversationForTutor(state)});
}
function startQuiz(mode,provider=false){if(pending||state.quiz&&!state.quiz.completedAt)return;if(state.quiz)state.attempts.push(state.quiz);state.quiz=createSession(state.profile,mode,crypto.randomUUID(),provider?{version:coursePack.version,questions:courseQuestions(coursePack),course:{key:courseKey(coursePack),title:coursePack.title,version:coursePack.version,provider:coursePack.provider.name}}:null);feedback=null;state.view='practice';activity(`Started ${mode==='test'?'independent':'supported'} practice for ${tracks[state.profile].title}`);render();}
function renderPractice(){const qz=state.quiz;
  $('main').innerHTML=`<div class="eyebrow">PRACTICE WITH PURPOSE</div><h1>Put your understanding to work</h1><p class="subtle"><span id="questionSetSummary">${esc(qz?.course?.title||(!qz&&courseQuestions(coursePack).length?coursePack.title:tracks[state.profile].title))} · ${qz?.questionIds.length??(courseQuestions(coursePack).length||questions.filter(q=>q.track===state.profile||q.track==='core').length)} questions.</span> These short practice sets do not establish exam readiness.</p>`;
  if(!qz){$('main').insertAdjacentHTML('beforeend',`<section class="card"><h2>Choose how to practise</h2><p>Supported practice gives feedback after each answer. Independent practice pauses the tutor and withholds feedback until the end.</p>${courseQuestions(coursePack).length?`<label for="questionSource">Question set</label><select id="questionSource"><option value="course">${esc(coursePack.title)} — provider questions</option><option value="builtin">StatsDirect foundation / pathway questions</option></select>`:''}<div class="actions"><button class="primary" id="supported">Supported practice</button><button id="independent">Independent practice</button></div><p class="small">First answers are retained. Confidence and reasoning help you reflect; they do not alter the score.</p></section>`);$('supported').onclick=()=>startQuiz('learn',$('questionSource')?.value==='course');$('independent').onclick=()=>startQuiz('test',$('questionSource')?.value==='course');if($('questionSource'))$('questionSource').onchange=()=>{const course=$('questionSource').value==='course',count=course?courseQuestions(coursePack).length:questions.filter(q=>q.track===state.profile||q.track==='core').length;$('practiceNav').querySelector('span').textContent=count;$('questionSetSummary').textContent=(course?coursePack.title:tracks[state.profile].title)+' · '+count+' questions.';};return;}
  if(qz.completedAt){const r=learnerResult(qz);$('main').insertAdjacentHTML('beforeend',`<section class="card"><div class="eyebrow">${qz.mode==='test'?'INDEPENDENT PRACTICE · UNSUPERVISED':'SUPPORTED PRACTICE'}</div><p class="score">${r.score}<small> / ${r.total} · provisional score</small></p><p>${r.assisted?`${r.assisted} answer(s) used recorded teaching support.`:'No in-app assistance was recorded for these answers.'}</p><p class="small">This score has not been independently verified. Prior exposure or help outside this app cannot be ruled out.</p>${qz.answers.map(a=>{const q=sessionQuestion(qz,a.questionId);return `<div class="review-line"><strong>${a.correct?'✓':'↻'} ${esc(q.topic)}</strong><p>Your answer: ${a.choice} · Key: ${q.correct} · Confidence: ${esc(a.confidence)}${a.assisted?' · Assisted':''}</p><p>${esc(q.explanation)}</p></div>`;}).join('')}<div class="actions"><button id="reviewResults" class="primary">Review my learning record</button><button id="morePractice">New practice attempt</button><button id="discussResults">Discuss with the tutor</button></div></section>`);$('reviewResults').onclick=()=>navigate('record');$('morePractice').onclick=()=>{state.attempts.push(state.quiz);state.quiz=null;save();render();};$('discussResults').onclick=()=>{state.draft='Help me understand these provisional practice results and plan my next study step:\n'+qz.answers.map(a=>{const q=sessionQuestion(qz,a.questionId);return `${q.topic}: chose ${a.choice}; key ${q.correct}. ${q.explanation}`;}).join('\n');navigate('study');};return;}
  const q=sessionQuestion(qz,qz.questionIds[qz.answers.length]);
  $('main').insertAdjacentHTML('beforeend',`<section class="card"><div class="eyebrow">${qz.mode==='test'?'INDEPENDENT':'SUPPORTED'} · QUESTION ${qz.answers.length+1} OF ${qz.questionIds.length}</div>${qz.course?`<p class="small">${esc(qz.course.title)} · version ${esc(qz.course.version)}</p>`:''}<p class="question">${esc(q.stem)}</p>${learningTargetHTML(q,esc)}<form id="answerForm"><div class="options">${q.options.map(o=>`<label class="option"><input type="radio" name="answer" value="${o.id}" required><span><b>${o.id}.</b> ${esc(o.text)}</span></label>`).join('')}</div><div class="form-row"><div><label for="confidence">How confident are you?</label><select id="confidence" required><option value="">Choose confidence</option><option value="unsure">Unsure</option><option value="fairly">Fairly confident</option><option value="confident">Confident</option></select></div><div><label for="reasoning">Your reasoning (optional)</label><textarea id="reasoning" rows="2" maxlength="1500" placeholder="What led you to this answer?"></textarea></div></div><div class="actions"><button class="primary" type="submit">Submit first answer</button>${qz.mode==='learn'?'<button id="hint" type="button">Give me a hint</button><button id="teach" type="button">Explain the principle</button>':''}</div></form><div id="hintText" class="small"></div><button id="endPractice" class="link-button">End this attempt early</button></section><div id="feedback"></div>`);
  $('answerForm').onsubmit=e=>{e.preventDefault();const choice=new FormData(e.target).get('answer');const confidence=$('confidence').value;const reasoning=$('reasoning').value;try{recordAnswer(qz,q.id,choice,confidence,reasoning);activity(`Answered ${q.id}: ${choice}; confidence ${confidence}${qz.mode==='learn'?'; '+(choice===q.correct?'correct':'incorrect'):''}`);if(qz.mode==='learn'){feedback={q,choice};renderFeedback();}else render();}catch(error){notice(error.message);}};
  if($('hint'))$('hint').onclick=()=>{recordAssistance(qz,q.id,'hints');$('hintText').textContent=q.hint;activity(`Hint shown for ${q.id}`);};
  if($('teach'))$('teach').onclick=()=>{recordAssistance(qz,q.id,'explanations');$('hintText').textContent=q.lesson;activity(`Teaching principle shown for ${q.id}`);};
  $('endPractice').onclick=()=>{qz.endedAt=new Date().toISOString();state.attempts.push(qz);state.quiz=null;activity('Practice attempt ended before completion');render();};
}
function renderFeedback(){const {q,choice}=feedback;const option=q.options.find(o=>o.id===choice);$('answerForm').querySelectorAll('input,select,textarea,button').forEach(x=>x.disabled=true);$('endPractice').disabled=true;$('feedback').innerHTML=`<div class="feedback ${choice===q.correct?'':'wrong'}"><h2>${choice===q.correct?'Correct':'A useful point to revisit'}</h2><p>${esc(option.feedback)}</p>${option.feedback.trim()===q.explanation.trim()?'':`<p>${esc(q.explanation)}</p>`}<label for="answerReflection">What will you take from this? (optional reflection)</label><textarea id="answerReflection" rows="2" maxlength="1500"></textarea><button id="nextQuestion" class="primary">${state.quiz.completedAt?'See my results':'Next question'}</button></div>`;$('answerReflection').oninput=()=>{const answer=state.quiz.answers.find(a=>a.questionId===q.id);if(answer){answer.reflection=$('answerReflection').value;save();}};$('nextQuestion').onclick=()=>{feedback=null;render();};$('feedback').scrollIntoView({block:'nearest'});}
function renderRecord(){const record=reviewRecord(state),text=reviewText(record);reviewApproved=false;
  $('main').innerHTML=`<div class="eyebrow">YOUR LEARNING PORTFOLIO</div><h1>A record you can reflect on and share</h1><p class="subtle">Your answers, explanations, confidence, learning conversation and practical activities stay together. Prepare a review request when you are ready.</p><section class="card"><h2>About you and your learning</h2><div class="form-row"><div><label for="learnerName">Name for the reviewer</label><input id="learnerName" maxlength="120" value="${esc(state.identity.name)}" autocomplete="name"></div><div><label for="learnerEmail">Reply email</label><input id="learnerEmail" type="email" maxlength="180" value="${esc(state.identity.email)}" autocomplete="email"></div></div><label for="goal">Course, professional role or learning goal</label><input id="goal" maxlength="500" value="${esc(state.identity.goal)}"><label for="reflection">Reflection and next learning step</label><textarea id="reflection" rows="3" maxlength="3000" placeholder="What did you learn, and what will you practise next?">${esc(state.reflection)}</textarea><p class="small">Identity fields are kept locally and included in your review record. They are not automatically sent to the AI tutor.</p></section>${providerRecordHTML(state,workspace)}<section class="card"><h2>Review before sharing</h2><p>${record.attempts.filter(a=>a.completedAt).length} completed practice attempt(s) · ${state.conversation.length} conversation entries · ${state.activities.length} practical activities</p><p class="small">These are unsupervised learning records, not certificates. Any assessment decision or CPD recognition comes from your provider under their stated conditions.</p><details><summary>Preview the complete record to be attached</summary><pre id="recordPreview" class="record-preview">${esc(text)}</pre></details><div class="actions"><select id="recordFormat" aria-label="Export format"><option value="pdf">PDF document</option><option value="docx">Word document</option><option value="html">HTML document</option><option value="json">JSON record and diagnostics</option><option value="txt">Plain text</option></select><button id="exportRecord">Export learning record…</button></div><label for="recipient">Ask for external review</label><input id="recipient" type="email" maxlength="254" value="${esc(state.training.reviewEmail)}" list="reviewRecipients"><datalist id="reviewRecipients"><option value="support@statisticalhelp.org"><option value="chil@liverpool.ac.uk"></datalist><label id="mailConsent"><input id="consent" type="checkbox"><span>I have reviewed the record and want to include the full conversation and practice answers in an email to this recipient.</span></label><div class="actions"><button id="emailRecord" class="primary" disabled>Prepare email draft…</button></div><p class="small">Opens Mail with the record attached. You review and send the message. Your chosen tutor or training provider reviews the record. StatsDirect does not award credits or arrange a reviewer.</p></section>`;
  for(const [id,key] of [['learnerName','name'],['learnerEmail','email'],['goal','goal']])$(id).oninput=()=>{state.identity[key]=$(id).value;save();updatePreview();};
  $('reflection').oninput=()=>{state.reflection=$('reflection').value;save();updatePreview();};
  $('captureEvidence').onclick=()=>{if(native)post({action:'captureLearningEvidence',documentID:$('evidenceDocument').value});else notice(`Open ${words().app} to attach analysis evidence.`);};
  $('importProviderReview').onclick=()=>{if(native)post({action:'importProviderReview'});else notice(`Provider response import is available in ${words().app}.`);};
  $('exportRecord').onclick=exportRecord;
  $('consent').onchange=()=>{reviewApproved=$('consent').checked;$('emailRecord').disabled=!reviewApproved;};
  $('recipient').oninput=()=>{state.training.reviewEmail=$('recipient').value;save();updatePreview();reviewApproved=false;$('consent').checked=false;$('emailRecord').disabled=true;};
  $('emailRecord').onclick=()=>{if(!reviewApproved)return;if(!$('learnerEmail').checkValidity()){$('learnerEmail').reportValidity();return;}const recipient=$('recipient').value.trim();if(!validEmail(recipient)){notice('Enter a single valid assessment email address.');return;}if(native)post({action:'email',recipient,...exportPayload()});else notice(`Email drafts with attachments are available in ${words().application}. Export the record here and attach it yourself.`);};
}
function renderOptions(){
  const o=state.learning;
  $('main').innerHTML=`<div class="eyebrow">LEARNING OPTIONS</div><h1>Make learning work for you</h1>
  <section class="card"><h2>Your experience level and goals</h2>
  <div class="form-row"><div><label for="statisticalSkills">Statistical skills</label><select id="statisticalSkills">${Object.entries(statisticalSkills).map(([id,title])=>`<option value="${id}" ${o.statisticalSkills===id?'selected':''}>${title}</option>`).join('')}</select></div><div><label for="optionStage">Confidence with R</label><select id="optionStage">${Object.entries(stages).map(([id,t])=>`<option value="${id}" ${state.stage===id?'selected':''}>${esc(t)}</option>`).join('')}</select></div></div>
  <label for="qualifications">Exams or courses you are studying for</label><textarea id="qualifications" rows="2" maxlength="1200" placeholder="For example: MRCP Part 1, MRCGP AKT, DFPH/MFPH, MPH, an undergraduate module, or a professional qualification">${esc(o.qualifications)}</textarea>
  <label for="needs">Your learning goals</label><textarea id="needs" rows="2" maxlength="2500" placeholder="What would you like to understand or be able to do?">${esc(o.needs)}</textarea>
  <details class="option-details"><summary>More learning preferences</summary>
  <label for="optionProfile">Professional or study context</label><p class="small">Chooses examples and practice topics. Statistical skills sets the depth; confidence with R sets how much code the tutor explains.</p><select id="optionProfile" ${state.quiz&&!state.quiz.completedAt?'disabled':''}>${Object.entries(tracks).map(([id,t])=>`<option value="${id}" ${state.profile===id?'selected':''}>${esc(t.title)}</option>`).join('')}</select>
  <label for="priorKnowledge">Previous experience with statistics and epidemiology</label><textarea id="priorKnowledge" rows="2" maxlength="1500">${esc(o.priorKnowledge)}</textarea>
  <label for="targetDate">Target exam or completion date</label><input id="targetDate" type="date" value="${esc(o.targetDate)}">
  <p class="small">The target date helps the tutor suggest a study plan when you ask; it does not schedule reminders.</p><p class="small">Epidemiology and causal inference are included in every pathway.</p><div class="focus-grid">${[['epidemiology','Epidemiology and study design'],['causal','Causal inference and bias'],['interpretation','Interpreting results and uncertainty'],['methods','Choosing and checking statistical methods'],['r','R coding and reproducible analysis'],['communication','Explaining evidence to others']].map(([id,title])=>`<label class="option"><input type="checkbox" name="focus" value="${id}" ${o.focus.includes(id)?'checked':''} ${['epidemiology','causal'].includes(id)?'disabled':''}><span>${title}</span></label>`).join('')}</div>
  <label for="style">How would you like the tutor to help?</label><select id="style">${['Worked examples and questions','Step-by-step explanations','Exam-style questions with feedback','Discuss my reasoning','Connect StatsDirect to R'].map(t=>`<option ${o.style===t?'selected':''}>${t}</option>`).join('')}</select></details></section>
  ${trainingHTML(state,coursePack)}
  <div class="actions"><button id="applyOptions" class="primary">Return to learning</button><span id="optionSaved" class="small" role="status">Changes are saved as you edit.</span></div>`;
  for(const id of ['statisticalSkills','qualifications','needs','priorKnowledge','targetDate','style'])$(id).oninput=()=>{o[id]=$(id).value;save();};
  $('optionProfile').onchange=()=>{state.profile=$('optionProfile').value;$('profile').value=state.profile;save();};
  $('optionStage').onchange=()=>{state.stage=$('optionStage').value;$('stage').value=state.stage;save();};
  document.querySelectorAll('[name=focus]').forEach(el=>el.onchange=()=>{o.focus=Array.from(document.querySelectorAll('[name=focus]:checked')).map(x=>x.value);save();});
  $('exampleCourse').onclick=()=>{if(native)post({action:'exampleCourse'});else notice(`Open ${words().app} to load the example provider course.`);};
  $('importCourse').onclick=()=>{if(native)post({action:'importCoursePack'});else notice(`Course pack import is available in ${words().application}.`);};
  bindTraining(state,{save});
  $('applyOptions').onclick=()=>{activity('Updated learning options');navigate('study');};
}
function renderSources(){
  $('main').innerHTML=`<div class="eyebrow">EXAMINATION EXAMPLES</div><h1>Learn from the way questions are asked</h1><p class="subtle">Published examples inform our choice of topics and question styles. Our practice questions use original wording and fictional data; they are not endorsed by the examining bodies.</p><section class="card">${examSources.map(source=>`<div class="review-line"><h2><a href="${esc(source.url)}" target="_blank" rel="noopener">${esc(source.title)} ↗</a></h2><p>${esc(source.note)}</p></div>`).join('')}</section><section class="card"><h2>Practise explaining a result</h2><p>For public-health examinations, build the ability to discuss evidence with a colleague or member of the public as well as answering knowledge questions. The tutor can role-play a fictional service manager and give formative feedback.</p><button id="communication" class="primary">Prepare a conversation exercise</button></section>`;
  $('communication').onclick=()=>{state.draft=communicationPrompt;state.lesson='precision';navigate('study');};
}
function updatePreview(){$('recordPreview').textContent=reviewText(reviewRecord(state));$('consent').checked=false;$('emailRecord').disabled=true;}
function exportPayload(){const record=reviewRecord(state);return {record,text:reviewText(record),html:recordHTML(record)};}
function exportRecord(){const payload=exportPayload();if(native)post({action:'export',format:$('recordFormat')?.value??'pdf',...payload});else{const url=URL.createObjectURL(new Blob([payload.text],{type:'text/plain'}));const a=document.createElement('a');a.href=url;a.download='StatsDirect-learning-record.txt';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}}
function navigate(view){if(independent()&&['study','options','sources'].includes(view))return;state.view=view;save();render();}
$('settings').onclick=configure;
$('options').onclick=()=>navigate('options');
$('profile').onchange=()=>{state.profile=$('profile').value;activity('Learning pathway changed to '+tracks[state.profile].title);render();};
$('stage').onchange=()=>{state.stage=$('stage').value;activity('R learning stage changed to '+stages[state.stage]);render();};
$('lessons').onclick=e=>{const button=e.target.closest('[data-lesson]');if(!button||button.disabled)return;teachingSupport();state.lessonDrafts??={};state.lessonDrafts[state.lesson]=state.draft;state.lesson=button.dataset.lesson;state.draft=state.lessonDrafts[state.lesson]??'';state.view='study';ensureLessonPrompt(state,allLessons());activity('Opened lesson: '+lesson().title);render();};
$('sourcesNav').onclick=()=>navigate('sources');
$('practiceNav').onclick=()=>navigate('practice');$('recordNav').onclick=()=>navigate('record');
window.statsDirectLearn={
  hostContract:contract.hostContract,
  bridgeGeneration(value){bridgeGeneration=value;},
  restore(value){try{loaded=false;pending=null;streamedText='';renderedStudy=null;state=value?restorePortfolio(value):newPortfolio();if(!state.lesson.startsWith('course:')&&!lessons.some(l=>l.id===state.lesson))state.lesson='epidemiology';labelStudyGuides(state,lessons);if(independent())state.view='practice';welcome();render();loaded=true;save();}catch(error){this.loadError(error.message);}},
  loadError(message){loaded=false;document.body.classList.remove('study-mode');$('workspaceContext').hidden=true;$('main').innerHTML='<h1>The learning record needs attention</h1><p id="loadError"></p>';$('loadError').textContent=message;notice('The existing record has not been overwritten.');},
  settings(value){
    const wasSigningIn=settings.signingIn;settings=value;hostClass();
    $('settings').disabled=Boolean(pending)||Boolean(value.disabled);
    $('settings').textContent=value.disabled?'Online tutor disabled':value.configured?'Tutor connection':value.signingIn?'Finish ChatGPT sign-in':'Use my ChatGPT';
    // Account events must not replace a textarea or a partly answered practice form.
    if($('tutorBadge'))$('tutorBadge').textContent=value.label;
    if($('send')){$('send').textContent=sendLabel();$('send').disabled=Boolean(pending)||Boolean(value.disabled);}
    if($('connectionPrivacy'))$('connectionPrivacy').textContent=privacyText();
    renderWorkspace();
    if(wasSigningIn&&!value.signingIn)notice(value.configured?'Connected to ChatGPT. Send your question when you are ready.':value.label);
  },
  recordSharingConsent(value){
    if(!loaded)throw Error('The learning record is not ready. No document context was shared.');
    state.sharingConsents.push(value);save();return {state};
  },
  workspace(value){workspace=value;renderWorkspace();refreshEvidenceChoices();},
  partial(value){if(value.id!==pending)return;streamedText=value.text;if(!streamTimer)streamTimer=setTimeout(()=>{streamTimer=null;renderStream();},80);},
  resumeReply(event){if(!loaded||!event?.value?.id)return;pending=event.value.id;streamedText=event.partial??'';if(event.action==='reply')this.reply(event.value);else if(event.action==='tutorError')this.tutorError(event.value);else render();},
  diagnostics(value){state.diagnostics=value;save();},
  toolActivity(value){if(value.id===pending&&$('chatStatus'))$('chatStatus').textContent=value.text;},
  saved(value){if(value===revision){$('saveStatus').textContent=`Saved on ${words().computer}`;if($('optionSaved'))$('optionSaved').textContent=`Saved on ${words().computer}`;}},
  notice,
  reply(value){if(value.id!==pending)return;pending=null;state.conversation=state.conversation.filter(c=>c.id!==value.id);if(value.workspaceSources?.length)activity('Tutor used: '+value.workspaceSources.join('; '));transcriptEntry(state,'assistant',value.text,'StatsDirect AI tutor',{id:value.id,lessonId:lesson().id,lessonTitle:lesson().title,model:value.model,responseID:value.responseID,promptVersion:value.promptVersion,diagnostics:value.diagnostics??[],learningOptions:structuredClone(state.learning),rExperience:state.stage,courseSources:value.courseSources??[],workspaceSources:value.workspaceSources??[]});save();render();},
  tutorError(value){if(value.id&&value.id!==pending)return;const id=pending;pending=null;if(streamedText&&id){state.conversation=state.conversation.filter(c=>c.id!==id);transcriptEntry(state,'assistant',streamedText+'\n\n[Reply stopped before completion.]','StatsDirect AI tutor',{id,lessonId:lesson().id,lessonTitle:lesson().title});}streamedText='';state.diagnostics={...state.diagnostics,requestID:value.id,lastError:'Tutor reply stopped or failed',events:value.diagnostics??[]};save();notice(value.message);render();},
  exportRecord,
  coursePack(value){coursePack=value;adoptCourse(state,value);if(loaded){if(!allLessons().some(l=>l.id===state.lesson))state.lesson='epidemiology';save();render();}},
  providerReview(value){try{addProviderReview(state,value);activity('Imported provider assessment response (not authenticated)');render();}catch(e){notice(e.message);}},
  evidence(value){state.evidence.push(value);activity('Attached analysis evidence: '+value.title);renderRecord();},
  showOptions(){if(loaded)navigate('options');}
};
hostClass();
if(native)post({action:'ready',hostContract:contract.hostContract});else{try{const stored=localStorage.getItem('statsdirect-learning-preview');window.statsDirectLearn.restore(stored?JSON.parse(stored):null);}catch(error){window.statsDirectLearn.loadError(error.message);}notice(`Browser preview. Use ${words().app} to sign in with ChatGPT, analyses, R sessions and email drafts.`);}
