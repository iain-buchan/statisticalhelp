// Independent evidence: verify the 258 equation pictures against the published Flare site.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const local=path.resolve('C:/Develop/SD3Help/StatsDirect/Output/ieb21/Website/generatedimages/equations');
const evidence=path.resolve(__dirname,'../evidence/FlareLiveBaseline');
const sha=b=>crypto.createHash('sha256').update(b).digest('hex');
(async()=>{
 const queue=fs.readdirSync(local).filter(n=>/\.svg$/i.test(n)).sort((a,b)=>a.localeCompare(b,undefined,{numeric:true}));
 const checks=[],errors=[];
 await Promise.all(Array.from({length:8},async()=>{for(;;){const name=queue.shift();if(!name)return;const url='https://statsdirect.com/help/generatedimages/equations/'+name;try{
  const response=await fetch(url,{signal:AbortSignal.timeout(30000)}),body=Buffer.from(await response.arrayBuffer()),original=fs.readFileSync(path.join(local,name));
  checks.push({name,url,status:response.status,liveBytes:body.length,localBytes:original.length,liveSha256:sha(body),localSha256:sha(original),same:response.ok&&body.equals(original)});
 }catch(error){errors.push({name,url,error:String(error)});}}}));
 checks.sort((a,b)=>a.name.localeCompare(b.name,undefined,{numeric:true}));
 fs.mkdirSync(evidence,{recursive:true});fs.writeFileSync(path.join(evidence,'equation-live-hashes.json'),JSON.stringify({checkedAtUtc:new Date().toISOString(),local,checks,errors},null,2));
 const mismatches=checks.filter(c=>!c.same);console.log(JSON.stringify({checked:checks.length,identical:checks.length-mismatches.length,mismatches,errors},null,2));
 if(errors.length||mismatches.length||checks.length!==258)process.exitCode=1;
})().catch(e=>{console.error(e);process.exitCode=1;});
