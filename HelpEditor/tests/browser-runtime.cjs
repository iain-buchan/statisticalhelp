const fs=require('node:fs'),path=require('node:path'),os=require('node:os');
// Normal developer installation first; use this PC's bundled test runtime when available.
try{module.exports=require('playwright');}
catch(error){
 if(error.code!=='MODULE_NOT_FOUND')throw error;
 const bundled=process.env.STATSDIRECT_PLAYWRIGHT_PATH||path.join(os.homedir(),'.cache','codex-runtimes','codex-primary-runtime','dependencies','node','node_modules','playwright');
 if(!fs.existsSync(bundled))throw new Error('Install Playwright for developer browser checks, or set STATSDIRECT_PLAYWRIGHT_PATH. The editor itself does not need Node or Playwright.');
 module.exports=require(bundled);
}
