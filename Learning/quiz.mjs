import * as bank from './bank.mjs';
// The host build supplies bank.mjs: bankVersion, tracks and questions are required; bankPublication, the record of where the content came from, is optional and null when the host keeps none.
const {bankVersion, questions, tracks} = bank;
const bankPublication = bank.bankPublication ?? null;

export function createSession(track, mode, id = crypto.randomUUID(), providerPack = null) {
  if (!tracks[track] || !['learn','test'].includes(mode)) throw new Error('Choose a valid learning path and mode.');
  const selected=providerPack?.questions ?? questions.filter(q=>q.track===track || q.track==='core');
  if(!selected.length)throw Error('This course has no practice questions.');
  return {id, track, mode, bankVersion:providerPack?.version??bankVersion, contentPublication:providerPack?null:structuredClone(bankPublication), course:providerPack?.course??null, startedAt:new Date().toISOString(), completedAt:null,
    questionIds:selected.map(q=>q.id),
    questionSnapshots:structuredClone(selected), answers:[], hints:[], explanations:[], reflection:''};
}

export function recordAssistance(session, questionId, kind) {
  if (session.mode !== 'learn' || session.completedAt) throw new Error('Assistance is unavailable in this test.');
  if (!session.questionIds.includes(questionId) || !['hints','explanations'].includes(kind)) throw new Error('Invalid assistance request.');
  if (!session[kind].includes(questionId)) session[kind].push(questionId);
}

export function recordAnswer(session, questionId, choice, confidence, reasoning = '') {
  const q = sessionQuestion(session,questionId);
  if (session.completedAt || session.questionIds[session.answers.length] !== questionId) throw new Error('This question is not awaiting an answer.');
  if (!q.options.some(o=>o.id===choice) || !['unsure','fairly','confident'].includes(confidence)) throw new Error('Choose an answer and your confidence.');
  session.answers.push({questionId, questionVersion:q.version, choice, confidence,
    reasoning:String(reasoning).slice(0,1500), correct:choice===q.correct, answeredAt:new Date().toISOString(),
    assisted:session.hints.includes(questionId)||session.explanations.includes(questionId)});
  if (session.answers.length===session.questionIds.length) session.completedAt = new Date().toISOString();
}

export function learnerResult(session) {
  if (!session.completedAt) throw new Error('Finish the session to see the result.');
  const score = session.answers.filter(a=>a.correct).length;
  const revisit = session.answers.filter(a=>!a.correct || a.confidence==='unsure' || a.assisted);
  return {score,total:session.questionIds.length,assisted:session.answers.filter(a=>a.assisted).length,
    revisit:revisit.map(a=>{ const q=sessionQuestion(session,a.questionId); return {
      topic:q.topic,objective:q.objective,help:q.help,
      reason:!a.correct?(a.confidence==='confident'?'Confident but incorrect':'Incorrect answer'):
        a.assisted?'Answered with teaching support':'Correct, but uncertain'}; })};
}

export function reviewBundle(session) {
  const result = learnerResult(session);
  return {schemaVersion:1,prototype:true,submissionStatus:'not_submitted',
    universityReview:{status:'not_connected',reviewer:null,decision:null},
    intendedRouting:{initial:'support@statsdirect.com',future:'chil@liverpool.ac.uk',active:false},
    learner:{id:null,replyEmail:null},
    session:{...session,reflection:session.reflection.slice(0,1500)},
    provisionalResult:result,
    markingMethod:'Published answer keys; 1 for correct and 0 otherwise. Reasoning retained but not graded.',
    questionSnapshots:session.questionIds.map(id=>structuredClone(sessionQuestion(session,id))),
    teachingSource:'Original StatsDirect teaching material with per-record review metadata retained; not official examination items.',
    accreditation:'No CPD credit or University approval is claimed.'};
}

export function sessionQuestion(session,id) { return session.questionSnapshots?.find(q=>q.id===id) ?? questions.find(q=>q.id===id); }
