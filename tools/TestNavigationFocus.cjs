// Closed <details> descendants can have layout rectangles while refusing focus.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../NexusFreelance.Web/Content/navigation.js'),'utf8');
let document;
function element(name) { return {name,dataset:{},events:{},addEventListener(k,fn){this.events[k]=fn;},setAttribute(k,v){this[k]=v;},getAttribute(k){return this[k];},removeAttribute(k){delete this[k];},getClientRects(){return [1];},closest(){return null;},focus(){document.activeElement=this;}}; }
const toggle=element('toggle'),close=element('close'),link=element('link'),summary=element('summary'),hidden=element('hidden-link');
const closed={querySelector:()=>summary};summary.closest=()=>closed;hidden.closest=()=>closed;hidden.focus=()=>{};
const sidebar=element('sidebar'),main=element('main'),header=element('header'),footer=element('footer'),skip=element('skip'),scrim=element('scrim');
sidebar.querySelector=()=>close;
sidebar.querySelectorAll=s=>s==='details[data-nav-section]'?[]:[close,link,summary,hidden];
sidebar.contains=e=>[close,link,summary,hidden].includes(e);
document={activeElement:null,events:{},body:{dataset:{},classList:{add(){},toggle(){}}},
 getElementById:id=>id==='navigation-toggle'?toggle:id==='app-sidebar'?sidebar:main,
 querySelector:s=>({'.navigation-scrim':scrim,'.header':header,'footer':footer,'.skip-link':skip}[s]||null),
 querySelectorAll:()=>[],addEventListener(k,fn){this.events[k]=fn;}};
vm.runInNewContext(source,{document,window:{matchMedia:()=>({matches:true,addEventListener(){}})},localStorage:{getItem:()=>null},location:{pathname:'/Dashboard'}});
toggle.events.click();assert.equal(document.activeElement,close);
const key=(key,shiftKey=false)=>document.events.keydown({key,shiftKey,preventDefault(){}});
key('Tab',true);assert.equal(document.activeElement,summary,'Shift+Tab must reach visible summary, not a hidden link in closed details');
key('Tab');assert.equal(document.activeElement,close,'Tab wraps after last visible disclosure control');
key('Escape');assert.equal(document.activeElement,toggle);assert.equal(main.inert,false);
console.log('PASS mobile focus trap with closed groups; Tab/Shift+Tab/Escape; background restored');

