const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../NexusFreelance.Web/Content/interactions.js'),'utf8');
function boot(script,ready){
 const role={value:'Client',events:{},addEventListener(n,fn){this.events[n]=fn;}};
 const button={dataset:{googleReady:String(ready)}};
 const node=()=>({setAttribute(){},append(){},addEventListener(){}});
 const document={querySelectorAll:()=>[],createElement:node,body:{appendChild(){}},getElementById:id=>id==='Role'?role:button};
 vm.runInNewContext(script,{document,window:{}});return{role,button};
}
function suite(script){
 for(const ready of [true,false]){
  const{role,button}=boot(script,ready);
  for(const selected of ['Client','Freelancer','Admin']){role.value=selected;role.events.change();assert.equal(button.disabled,!ready||selected==='Admin');if(ready&&selected!=='Admin')assert.ok(button.textContent.endsWith(selected));}
 }
}
suite(source);
const mutation=source.replace('!ready || role.value', 'role.value');assert.notEqual(mutation,source);assert.throws(()=>suite(mutation),assert.AssertionError);
console.log('PASS Google availability stays role-aware; readiness mutation rejected');
