const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const baseline=require('./flare-baseline.cjs'),root=path.resolve(__dirname,'..'),folder=path.join(__dirname,'FlareBaseline');
const evidence=path.join(root,'evidence','ReviewImprovements-20261104','FlareBaseline'),run=path.join(evidence,'run-'+crypto.randomUUID());fs.mkdirSync(run,{recursive:true});
const checks=[];function check(name,passed,detail){checks.push({name,passed:!!passed,detail});console.log((passed?'PASS ':'FAIL ')+name);if(!passed)throw Error(name);}
function refuse(action,name){let refused=false;try{action();}catch{refused=true;}check(name,refused);}
try{
 const manifest=baseline.readManifest(),archive=fs.readFileSync(path.join(folder,manifest.archive.file));
 check('Three frozen targets retain404 authored topics and258 original equation images each',Object.keys(manifest.targets).length===3&&Object.values(manifest.targets).every(target=>target.authoredTopics===404&&target.equationSvgs===258));
 check('The complete archive has2964 reader files and excludes only three Flare build logs',manifest.files.length===2964&&manifest.excluded.length===3&&manifest.excluded.every(file=>file.endsWith('.mclog')));
 const extracted=baseline.frozenRoot({cacheDirectory:path.join(run,'cache')});
 check('Fresh extraction verifies every archived byte against the pinned inventory',baseline.verifyTree(extracted,manifest)===extracted,{archiveBytes:archive.length,files:manifest.files.length});
 check('Approved79 bibliography corrections and7 source-corrected SVG records stay explicit',baseline.referenceCorrections().length===79&&manifest.equationCorrections.count===7&&manifest.equationCorrections.records.every(row=>manifest.files.find(file=>file.path==='Website/generatedimages/equations/'+row.name)?.sha256===row.sourceCorrectedSha256));
 const prior={ci:process.env.CI,root:process.env.STATSDIRECT_FLARE_OUTPUT,website:process.env.STATSDIRECT_FLARE_WEBSITE};
 try{process.env.CI='true';process.env.STATSDIRECT_FLARE_OUTPUT=path.join(run,'not-a-local-install');process.env.STATSDIRECT_FLARE_WEBSITE=path.join(run,'not-a-live-baseline');check('CI ignores development overrides and resolves the immutable packaged fixture',baseline.resolveFlareTarget('Website')===path.join(root,'evidence','FlareBaselineCache',manifest.archive.sha256,'Website'));}
 finally{for(const [name,key]of [['CI','ci'],['STATSDIRECT_FLARE_OUTPUT','root'],['STATSDIRECT_FLARE_WEBSITE','website']])if(prior[key]===undefined)delete process.env[name];else process.env[name]=prior[key];}
 const changed=path.join(extracted,'DesktopHelp','contents.htm'),original=fs.readFileSync(changed);fs.appendFileSync(changed,'\n<!-- isolated integrity mutation -->');
 try{refuse(()=>baseline.frozenRoot({cacheDirectory:path.join(run,'cache')}),'A modified extracted baseline fails instead of silently changing comparison inputs');}finally{fs.writeFileSync(changed,original);}
 check('Restoring the exact test bytes recovers the original immutable inventory',baseline.verifyTree(extracted,manifest)===extracted);
 const malformed=path.join(run,'malformed');fs.mkdirSync(malformed);fs.writeFileSync(path.join(malformed,'manifest.json'),JSON.stringify(manifest));const corrupt=Buffer.from(archive);corrupt[corrupt.length-1]^=1;fs.writeFileSync(path.join(malformed,manifest.archive.file),corrupt);
 refuse(()=>baseline.frozenRoot({fixtureDirectory:malformed,cacheDirectory:path.join(run,'never-created')}),'A corrupted archive fails its pinned hash before extracting any file');
 check('Rejected archives create no output tree',!fs.existsSync(path.join(run,'never-created')));fs.unlinkSync(path.join(malformed,manifest.archive.file));
 const traversal=structuredClone(manifest);traversal.files[0].path='Website/../escape.txt';fs.writeFileSync(path.join(malformed,'manifest.json'),JSON.stringify(traversal));
 refuse(()=>baseline.frozenRoot({fixtureDirectory:malformed,cacheDirectory:path.join(run,'never-created')}),'A traversal-shaped manifest entry is rejected before extraction');
 const duplicate=structuredClone(manifest);duplicate.files[1].path=duplicate.files[0].path.toUpperCase();fs.writeFileSync(path.join(malformed,'manifest.json'),JSON.stringify(duplicate));
 refuse(()=>baseline.readManifest(malformed),'Duplicate/case-colliding archive paths are rejected');
}catch(error){checks.push({name:'Baseline integrity run completed',passed:false,detail:error.stack||String(error)});}
const report={checkedAtUtc:new Date().toISOString(),passed:checks.every(check=>check.passed),checks,run};fs.writeFileSync(path.join(evidence,'integrity-checks.json'),JSON.stringify(report,null,2));console.log(JSON.stringify({passed:report.passed,checks:checks.length,report:path.join(evidence,'integrity-checks.json')}));if(!report.passed)process.exitCode=1;
