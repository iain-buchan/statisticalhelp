// One-time import into an independent authoring working copy. No upstream writes.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import {fileURLToPath} from 'node:url';
const repo=path.resolve(process.argv[2]||'');
if(!process.argv[2])throw Error('Pass the StatsDirect source repository path.');
const output=path.dirname(fileURLToPath(import.meta.url));
const source=path.join(repo,'Learning/upstream/Learn');
const lessonsText=fs.readFileSync(path.join(source,'lessons.json'),'utf8');
const lessons=JSON.parse(lessonsText);
const bankText=fs.readFileSync(path.join(source,'bank.mjs'),'utf8');
const expectedImport="import lessons from '../Content/Learn/lessons.json' with {type:'json'};";
if(!bankText.includes(expectedImport))throw Error('Review the changed bank module import before extracting it.');
const moduleText=bankText.replace(expectedImport,'const lessons='+JSON.stringify(lessons)+';');
const bank=await import('data:text/javascript;base64,'+Buffer.from(moduleText).toString('base64'));
const destination=path.join(output,'starter-course.json');
if(fs.existsSync(destination))throw Error('An authoring course already exists; it will not be overwritten.');
const allQuestions=JSON.parse(JSON.stringify(bank.questions));
const course={schemaVersion:2,id:'statsdirect-health-foundations',version:'2026-11-03-draft',title:'Health and biomedical statistics — StatsDirect foundations',
 provider:{name:'StatsDirect course author',reviewEmail:'support@statisticalhelp.org',assessmentType:'self',assessmentRequirements:'Original practice exercises and MCQs. Review the learning record and practical reasoning against the course objectives.',cpdStatement:'Learning evidence may support a provider review. StatsDirect does not award or guarantee CPD credits.'},
 documents:[],lessons,questions:allQuestions,resourceURLs:[],
 authoring:{audience:'Health and biomedical undergraduates; masters students; postgraduate UK and US professional exam candidates; healthcare commissioning and payer analysts progressing from Excel to R.',
  learningObjectives:'Define the question and study design; choose and interpret appropriate analyses; explain uncertainty, bias and assumptions; connect StatsDirect menus with readable R.',
  reviewStatus:'Imported draft — course author review required',pilotContext:'Liverpool and Manchester NHS workforce development',
  originalQuestionPolicy:'Use original questions and fictional or appropriately licensed/de-identified data; no official exam questions or endorsement implied.',
  importedAtUtc:new Date().toISOString(),sourceBankVersion:bank.bankVersion,sourceTracks:bank.tracks,
  provenance:{repository:'https://github.com/iain-buchan/statsdirect',source:'Learning/upstream/Learn',upstream:JSON.parse(fs.readFileSync(path.join(repo,'Learning/upstream.lock.json'),'utf8')).commit}}};
fs.mkdirSync(path.join(output,'Baseline'),{recursive:true});
for(const name of ['lessons.json','bank.mjs'])fs.copyFileSync(path.join(source,name),path.join(output,'Baseline',name));
fs.copyFileSync(path.join(repo,'Learning/upstream/LICENSE'),path.join(output,'LICENSE'));
fs.writeFileSync(destination,JSON.stringify(course,null,2)+'\n');
const hash=file=>crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const provenance={sourceRepository:course.authoring.provenance.repository,upstreamRepository:'https://github.com/iain-buchan/statsdirect-mac',upstreamCommit:course.authoring.provenance.upstream,license:'MIT',lessons:lessons.length,questions:allQuestions.length,bankVersion:bank.bankVersion,
 files:['lessons.json','bank.mjs'].map(name=>({path:'Learning/upstream/Learn/'+name,sha256:hash(path.join(source,name))})),workingCopy:'starter-course.json',upstreamFilesChanged:false};
fs.writeFileSync(path.join(output,'provenance.json'),JSON.stringify(provenance,null,2)+'\n');
console.log(JSON.stringify({destination,lessons:lessons.length,questions:allQuestions.length,upstreamFilesChanged:false}));
