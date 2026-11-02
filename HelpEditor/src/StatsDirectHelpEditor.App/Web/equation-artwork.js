// Original self-contained equation artwork is reusable only while the exact
// source still matches. New/edited equations continue through offline MathJax.
export const equationArtworkCss=`
.help-equation.help-equation-artwork{display:inline!important;vertical-align:baseline!important;overflow:visible!important}
.help-equation-artwork>img.help-equation-original{display:inline;vertical-align:baseline;border:0;padding:0;margin:0;max-width:none;height:auto}
.help-equation-accessible{position:absolute!important;width:1px!important;height:1px!important;padding:0!important;margin:-1px!important;overflow:hidden!important;clip:rect(0,0,0,0)!important;white-space:nowrap!important;border:0!important}
`;
export function canonicalEquationSource(equation) {
  let result='';
  // The captured catalog is immutable. Namespace migration changes authoring
  // identity, not the equation artwork's original source identity.
  const namespace=uri=>uri==='https://raw.githubusercontent.com/iain-buchan/statisticalhelp/master/HelpEditor/Schema/StatsDirectHelp.xsd'?'http://www.madcapsoftware.com/Schemas/MadCap.xsd':uri||'';
  const part=value=>{value=String(value).replace(/\r\n?/g,'\n');result+=value.length+':'+value;};
  const element=node=>{
    result+='E';part(namespace(node.namespaceURI));part(node.localName);
    const attributes=Array.from(node.attributes).filter(a=>a.namespaceURI!=='http://www.w3.org/2000/xmlns/'&&a.name!=='xmlns').sort((a,b)=>{
      const an=namespace(a.namespaceURI),bn=namespace(b.namespaceURI);return an<bn?-1:an>bn?1:a.localName<b.localName?-1:a.localName>b.localName?1:0;
    });
    result+=attributes.length+':';for(const a of attributes){part(namespace(a.namespaceURI));part(a.localName);part(a.value);}
    let text='';const flush=()=>{if(text){result+='T';part(text);text='';}};
    for(const child of node.childNodes)if(child.nodeType===3||child.nodeType===4)text+=child.nodeValue;else if(child.nodeType===1){flush();element(child);}
    flush();result+='Z';
  };
  element(equation);return result;
}

export async function loadEquationArtwork(base=new URL('./equation-artwork/',import.meta.url)) {
  const response=await fetch(new URL('catalog.json',base));if(!response.ok)throw new Error('Equation artwork catalogue could not be loaded.');
  const catalog=await response.json();if(catalog.schemaVersion!==1||!Array.isArray(catalog.entries))throw new Error('Unsupported equation artwork catalogue.');
  const entries=new Map();
  for(const entry of catalog.entries){
    if(!/^[a-f0-9]{64}\.svg$/.test(entry.file)||entry.file!==entry.artworkSha256+'.svg')throw new Error('Invalid equation artwork content address.');
    entries.set(entry.canonicalSource,entry);
  }
  return {create(source,document=window.document,prepareLatex=value=>value){
    const entry=entries.get(canonicalEquationSource(source));if(!entry)return null;
    const span=document.createElement('span');span.className='help-equation help-equation-artwork help-math-ignore';span.dataset.equationSourceKey=entry.sourceKey;
    const image=document.createElement('img');image.className='help-equation-original';image.src=new URL(entry.file,base).href;image.alt='';image.setAttribute('aria-hidden','true');
    // Match Flare's replacement image styling without introducing a larger
    // inline text strut on the wrapper around a 12pt equation image.
    if(source.hasAttribute('style'))image.setAttribute('style',source.getAttribute('style'));
    const accessible=document.createElement('span');accessible.className='help-equation-accessible help-equation-live';
    if(source.children.length)for(const child of source.children)accessible.append(document.importNode(child,true));else {
      const text=source.textContent.trim(),delimiter=text.startsWith('$$')?'$$':'$';
      accessible.textContent=text.length>=delimiter.length*2&&text.startsWith(delimiter)&&text.endsWith(delimiter)?delimiter+prepareLatex(text.slice(delimiter.length,-delimiter.length))+delimiter:text;
    }
    // An absolute placeholder after an oversized image can create a blank
    // line. Place its accessible source first without changing image layout.
    span.append(accessible,image);return span;
  },count:entries.size};
}
