// Development-only renderer; the built application uses the checked-in ICO.
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('../../../tests/browser-runtime.cjs');
(async()=>{
  const browser=await chromium.launch({channel:'msedge',headless:true});
  try{
    const svg=fs.readFileSync(path.join(__dirname,'HelpEditor.svg'),'utf8'),frames=[];
    const page=await browser.newPage({deviceScaleFactor:1});
    for(const size of [16,20,24,32,40,48,64,96,128,256]){
      await page.setViewportSize({width:size,height:size});
      await page.setContent('<style>html,body{margin:0;width:100%;height:100%;background:transparent}svg{display:block;width:100%;height:100%}</style>'+svg);
      const png=await page.screenshot({omitBackground:true});
      frames.push({size,png});
      if(size===256)fs.writeFileSync(path.join(__dirname,'HelpEditor.png'),png);
    }
    const directory=Buffer.alloc(6+frames.length*16);directory.writeUInt16LE(1,2);directory.writeUInt16LE(frames.length,4);
    let offset=directory.length;
    frames.forEach(({size,png},i)=>{const p=6+i*16;directory[p]=directory[p+1]=size===256?0:size;directory.writeUInt16LE(1,p+4);directory.writeUInt16LE(32,p+6);directory.writeUInt32LE(png.length,p+8);directory.writeUInt32LE(offset,p+12);offset+=png.length;});
    fs.writeFileSync(path.join(__dirname,'HelpEditor.ico'),Buffer.concat([directory,...frames.map(f=>f.png)]));
    console.log('Created HelpEditor.ico with '+frames.length+' RGBA sizes (16–256px).');
  }finally{await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
