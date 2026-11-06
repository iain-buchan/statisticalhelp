import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {questions} from './bank.mjs';
import {runR,archivedBank,archiveRequested} from './test-config.mjs';
// The archive comparison runs where an archived bank exists; a host that names one with STATSDIRECT_LEARN_ARCHIVE must supply it.
let archived=null,archiveError=null;
if(archiveRequested||existsSync(fileURLToPath(archivedBank))){
  try{const module=await import(archivedBank);archived=module.questions;if(!Array.isArray(archived))throw Error('the archive exports no questions array');}
  catch(error){archiveError=`${fileURLToPath(archivedBank)}: ${error.message}`;}
}

test('archived questions use the maintained content',{skip:!archived&&!archiveError?'no archived question bank on this host':false},()=>{
  assert.equal(archiveError,null,archiveError);
  for(const q of archived) assert.deepEqual(q,questions.find(current=>current.id===q.id));
});

test('reviewed numerical choices agree with independently expressed R calculations',()=>{
  // Literal choices bind these checks to what learners actually see, not only metadata.
  const fixtures=[
    ['UG-SENS-01','C','80%',0.8,'72/90'],
    ['UG-NNT-01','D','17',17,'ceiling(200/(28-16))'],
    ['PG-PPV-01','C','15.4%',100*90/585,'100*90/(90+495)'],
    ['PG-RROR-01','A','Risk ratio 1.50; odds ratio 2.25',1.5,'120/80'],
    ['PG-RROR-01','A','Risk ratio 1.50; odds ratio 2.25',2.25,'(120*120)/(80*80)'],
    ['PG-SE-01','D','It becomes half as large.',0.5,'sqrt(100/400)'],
    ['PH-STAND-01','C','400',400,'(75*200+25*1000)/100'],
    ['PH-CLUST-01','C','1.76',1.76,'1+19*0.04'],
    ['PH-SMR-01','C','1.25',1.25,'55/44'],
    ['RS-MULT-01','D','64%',0.6415140775914581,'pbinom(0,20,0.05,lower.tail=FALSE)'],
    ['PG-LR-01','C','40%',0.4,'6*0.1/(6*0.1+0.9)'],
    ['CORE-RATE-01','A','2 events per 100 person-years',2,'100*12/600'],
    ['AN-PREC-01','B','The service with 1,000 people',1,'as.numeric(diff(prop.test(100,1000,correct=FALSE)$conf.int) < diff(prop.test(10,100,correct=FALSE)$conf.int))'],
    ['LIB-RATES-01','A','Both adjusted rates are 400 per 100000 person-years.',400,'sum(c(0.75,0.25)*c(200,1000))'],
    ['LIB-APPRAISAL-01','C','Programme risk among all 100 allocated participants could be 20% to 40% before further assumptions.',0.2,'20/100'],
    ['LIB-APPRAISAL-01','C','Programme risk among all 100 allocated participants could be 20% to 40% before further assumptions.',0.4,'(20+20)/100'],
    ['LIB-MISSING-01','D','It ignores uncertainty about missing values and distorts variability and relationships.',1,'as.numeric(var(c(20,22,18,26,24,30,140/6,140/6)) < var(c(20,22,18,26,24,30)))'],
    ['LIB-VALIDATION-01','B','Correlation is unchanged, but mean prediction error becomes +10 points.',10,'mean(seq(10,80,10)+10-c(12,18,31,39,51,59,72,78))'],
    ['LIB-VALIDATION-01','B','Correlation is unchanged, but mean prediction error becomes +10 points.',0,'cor(c(12,18,31,39,51,59,72,78),seq(10,80,10)+10)-cor(c(12,18,31,39,51,59,72,78),seq(10,80,10))'],
    ['LIB-META-01','E','4:1',4,'(1/0.02^2)/(1/0.04^2)'],
  ];
  const output=runR(
    ['--vanilla','-e',`cat(sprintf('%.17g',c(${fixtures.map(f=>f[4]).join(',')})),sep='\n')`]);
  const results=output.trim().split(/\s+/).map(Number);
  fixtures.forEach(([id,key,choice,expected],i)=>{
    const q=questions.find(q=>q.id===id);
    assert.equal(q.correct,key,id);
    assert.equal(q.options.find(o=>o.id===key).text,choice,id);
    assert.ok(Math.abs(results[i]-expected)<1e-12,`${id}: ${results[i]} vs ${expected}`);
  });
  const pct=questions.find(q=>q.id==='AN-PCT-01');
  assert.equal(pct.correct,'A');
  assert.equal(pct.options.find(o=>o.id==='A').text,'2 percentage points; 20% relative reduction');
  assert.ok(Math.abs((0.10-0.08)*100-2)<1e-12);
  assert.ok(Math.abs((0.10-0.08)/0.10*100-20)<1e-12);
});

// Run after each actual bundled R script, in that script's environment.
