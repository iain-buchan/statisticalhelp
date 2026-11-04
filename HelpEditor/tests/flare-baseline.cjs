// Shared immutable Flare-output fixture. Node built-ins only: no SDK, Flare,
// PowerShell, npm archive package or network request is needed to read it.
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),zlib=require('node:zlib');
const project=path.resolve(__dirname,'..'),fixture=path.join(__dirname,'FlareBaseline');
const targets=['DesktopHelp','Website','GitHub'];
const sha=value=>crypto.createHash('sha256').update(value).digest('hex');
const safeName=name=>typeof name==='string'&&name.length<300&&!/[\\:\x00-\x1f]/.test(name)&&targets.includes(name.split('/')[0])&&name.split('/').every(part=>part&&!['.','..'].includes(part));
function noLinks(file){for(let current=path.resolve(file);;current=path.dirname(current)){try{if(fs.lstatSync(current).isSymbolicLink())throw Error('Linked fixture/cache path is not supported: '+current);}catch(error){if(error.code!=='ENOENT')throw error;}if(path.dirname(current)===current)break;}}
function readManifest(folder=fixture){
 const manifest=JSON.parse(fs.readFileSync(path.join(folder,'manifest.json'),'utf8'));
 if(manifest.schemaVersion!==1||manifest.archive?.file!=='flare-20261101.tar.gz'||!/^[a-f0-9]{64}$/.test(manifest.archive.sha256)||!Number.isSafeInteger(manifest.archive.bytes)||!Number.isSafeInteger(manifest.archive.tarBytes)||manifest.archive.tarBytes>128*1024*1024||!Array.isArray(manifest.files)||manifest.files.length>10000)throw Error('Invalid frozen Flare manifest');
 const seen=new Set();for(const file of manifest.files){if(!safeName(file.path)||seen.has(file.path.toLowerCase())||!/^[a-f0-9]{64}$/.test(file.sha256)||!Number.isSafeInteger(file.bytes)||file.bytes<0)throw Error('Invalid/duplicate frozen Flare file');seen.add(file.path.toLowerCase());}
 if(!Array.isArray(manifest.authoredTopicPaths)||manifest.authoredTopicPaths.length!==404||new Set(manifest.authoredTopicPaths).size!==404)throw Error('Frozen authored-topic inventory is invalid');
 for(const name of targets)if(manifest.targets?.[name]?.authoredTopics!==404)throw Error('Frozen target must contain exactly404 authored topics: '+name);
 for(const target of targets)for(const topic of manifest.authoredTopicPaths)if(!safeName(target+'/'+topic)||!seen.has((target+'/'+topic).toLowerCase()))throw Error('Frozen authored topic is missing from '+target);
 return manifest;
}
function verifyTree(folder,manifest){
 noLinks(folder);
 const expected=new Map(manifest.files.map(file=>[file.path,file]));
 function walk(directory){for(const entry of fs.readdirSync(directory,{withFileTypes:true})){const file=path.join(directory,entry.name),relative=path.relative(folder,file).split(path.sep).join('/');if(entry.isSymbolicLink())throw Error('Linked frozen fixture entry: '+relative);if(entry.isDirectory())walk(file);else{const known=expected.get(relative);if(!known||entry.isFile()!==true||fs.statSync(file).size!==known.bytes||sha(fs.readFileSync(file))!==known.sha256)throw Error('Frozen Flare fixture changed: '+relative);expected.delete(relative);}}}
 walk(folder);if(expected.size)throw Error('Frozen Flare fixture files are missing: '+[...expected.keys()].slice(0,3).join(', '));return folder;
}
function unpack(archive,manifest,destination){
 if(archive.length!==manifest.archive.bytes||sha(archive)!==manifest.archive.sha256)throw Error('Frozen Flare archive hash/size mismatch');
 const tar=zlib.gunzipSync(archive,{maxOutputLength:manifest.archive.tarBytes});if(tar.length!==manifest.archive.tarBytes)throw Error('Frozen Flare archive length mismatch');
 const expected=new Map(manifest.files.map(file=>[file.path,file])),entries=[];let offset=0;
 const text=(header,start,length)=>header.subarray(start,start+length).toString('utf8').replace(/\0.*$/s,'');
 while(offset+512<=tar.length){const header=tar.subarray(offset,offset+512);if(header.every(byte=>byte===0))break;
  let sum=0;for(let index=0;index<512;index++)sum+=index>=148&&index<156?32:header[index];
  const name=text(header,0,100),size=parseInt(text(header,124,12).trim(),8),check=parseInt(text(header,148,8).trim(),8),kind=text(header,156,1),magic=text(header,257,6);
  const known=expected.get(name);if(!safeName(name)||!known||kind!=='0'||magic!=='ustar'||sum!==check||size!==known.bytes||offset+512+size>tar.length)throw Error('Unexpected frozen Flare archive entry');
  const data=tar.subarray(offset+512,offset+512+size);if(sha(data)!==known.sha256)throw Error('Frozen Flare entry hash mismatch: '+name);
  entries.push({name,data});expected.delete(name);offset+=512+Math.ceil(size/512)*512;
 }
 if(expected.size||tar.subarray(offset).some(byte=>byte!==0))throw Error('Incomplete frozen Flare archive');
 // Nothing is extracted until all headers, paths, sizes and content hashes pass.
 noLinks(destination);fs.mkdirSync(destination,{recursive:false});for(const {name,data}of entries){const file=path.resolve(destination,...name.split('/'));if(!file.startsWith(destination+path.sep))throw Error('Invalid fixture containment');noLinks(file);fs.mkdirSync(path.dirname(file),{recursive:true});noLinks(file);fs.writeFileSync(file,data,{flag:'wx'});}
 return verifyTree(destination,manifest);
}
function frozenRoot(options={}){
 const folder=options.fixtureDirectory||fixture,manifest=readManifest(folder),cacheRoot=path.resolve(options.cacheDirectory||path.join(project,'evidence','FlareBaselineCache'));
 const archive=fs.readFileSync(path.join(folder,manifest.archive.file));if(archive.length!==manifest.archive.bytes||sha(archive)!==manifest.archive.sha256)throw Error('Frozen Flare archive hash/size mismatch');
 noLinks(cacheRoot);fs.mkdirSync(cacheRoot,{recursive:true});noLinks(cacheRoot);
 const destination=path.join(cacheRoot,manifest.archive.sha256);
 if(fs.existsSync(destination))return verifyTree(destination,manifest);
 const stage=path.join(cacheRoot,'.extract-'+process.pid+'-'+crypto.randomUUID());
 try{unpack(archive,manifest,stage);try{fs.renameSync(stage,destination);}catch(error){if(!fs.existsSync(destination))throw error;verifyTree(destination,manifest);}return destination;}
 finally{if(fs.existsSync(stage)){if(path.dirname(stage)!==cacheRoot||!path.basename(stage).startsWith('.extract-'+process.pid+'-')||fs.lstatSync(stage).isSymbolicLink())throw Error('Unsafe fixture staging cleanup');fs.rmSync(stage,{recursive:true});}}
}
const strict=()=>process.env.STATSDIRECT_REQUIRE_FROZEN_FLARE==='1'||!!process.env.CI&&process.env.CI!=='false';
function resolveFlareRoot(){
 if(!strict()&&process.env.STATSDIRECT_FLARE_OUTPUT)return path.resolve(process.env.STATSDIRECT_FLARE_OUTPUT);
 if(fs.existsSync(path.join(fixture,'manifest.json'))||fs.existsSync(path.join(fixture,'flare-20261101.tar.gz')))return frozenRoot();
 if(strict())throw Error('Frozen Flare baseline is required in CI; restore tests/FlareBaseline');
 const legacy='C:/Develop/SD3Help/StatsDirect/Output/ieb21';if(!fs.existsSync(legacy))throw Error('No frozen Flare archive or explicit development baseline is available');console.warn('Developer fallback: using local Flare output because the frozen fixture is absent');return legacy;
}
function resolveFlareTarget(target){if(!targets.includes(target))throw Error('Unknown Flare target');const override=process.env[target==='Website'?'STATSDIRECT_FLARE_WEBSITE':target==='DesktopHelp'?'STATSDIRECT_FLARE_DESKTOP':'STATSDIRECT_FLARE_GITHUB'];return !strict()&&override?path.resolve(override):path.join(resolveFlareRoot(),target);}
function referenceCorrections(){const manifest=readManifest(),entry=manifest.referenceCorrections,file=fs.readFileSync(path.join(fixture,'reference-link-corrections.json'));if(sha(file)!==entry.sha256)throw Error('Frozen reference-correction manifest changed');const value=JSON.parse(file);if(!Array.isArray(value)||value.length!==79)throw Error('Expected79 approved reference corrections');return value;}
module.exports={resolveFlareRoot,resolveFlareTarget,frozenRoot,readManifest,verifyTree,referenceCorrections};
