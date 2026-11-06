// Store DOM selection endpoints independently: Range alone loses backwards selection.
// Stable message IDs survive streaming, completion and full conversation re-renders.
function endpoint(container,node,offset){
  const element=node?.nodeType===1?node:node?.parentElement;
  const body=element?.closest('.message-body'),message=body?.closest('[data-message-id]');
  if(!body||!message||!container.contains(message))return null;
  const range=container.ownerDocument.createRange();
  range.selectNodeContents(body);range.setEnd(node,offset);
  // The end of a preceding node and start of the next have the same text
  // offset, but WebKit can copy an extra paragraph break for the former.
  // Retain which side of the boundary the learner actually selected.
  const end=node.nodeType===3?node.length:node.childNodes.length;
  return {id:message.dataset.messageId,offset:range.toString().length,backward:offset===end};
}
function resolve(container,point){
  if(!point)return null;
  const message=Array.from(container.querySelectorAll('[data-message-id]')).find(x=>x.dataset.messageId===point.id);
  const body=message?.querySelector('.message-body');if(!body)return null;
  const walker=container.ownerDocument.createTreeWalker(body,4); // SHOW_TEXT; offsets are DOM UTF-16 offsets.
  let remaining=point.offset,last=null,node;
  while((node=walker.nextNode())){
    if(remaining<node.length||(remaining===node.length&&point.backward))return [node,remaining];
    remaining-=node.length;last=node;
  }
  return last?[last,last.length]:[body,0];
}
export function conversationReading(container){
  if(!container)return null;
  const selection=container.ownerDocument.defaultView.getSelection();
  return {top:container.scrollTop,left:container.scrollLeft,
    follow:(!selection||selection.isCollapsed)&&container.scrollHeight-container.clientHeight-container.scrollTop<80,
    anchor:endpoint(container,selection?.anchorNode,selection?.anchorOffset),
    focus:endpoint(container,selection?.focusNode,selection?.focusOffset)};
}
export function restoreConversationReading(container,reading){
  if(!container)return;
  if(reading){
    const anchor=resolve(container,reading.anchor),focus=resolve(container,reading.focus);
    if(anchor&&focus){
      const selection=container.ownerDocument.defaultView.getSelection();
      selection.setBaseAndExtent(...anchor,...focus);
    }
    container.scrollLeft=reading.left;
    container.scrollTop=reading.follow?container.scrollHeight:reading.top;
  }else container.scrollTop=container.scrollHeight; // First display or explicit conversation switch.
}
