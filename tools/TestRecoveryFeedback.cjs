const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../NexusFreelance.Web/Content/recovery-form.js'),'utf8');
function boot(){
 const events={},windowEvents={},attrs={};
 const button={disabled:false,textContent:'Gửi liên kết đặt lại mật khẩu'};
 const status={hidden:true,textContent:''};
 const email={value:'user@example.test',validity:{valueMissing:false,typeMismatch:false},setCustomValidity(value){this.message=value;}};
 const form={dataset:{},addEventListener(name,fn){events[name]=fn;},setAttribute(k,v){attrs[k]=v;},removeAttribute(k){delete attrs[k];},querySelector(s){return s==='button[type="submit"]'?button:s==='[data-recovery-status]'?status:email;}};
 const document={querySelectorAll(){return[form];}};
 vm.runInNewContext(source,{document,window:{addEventListener(name,fn){windowEvents[name]=fn;}}});
 return{events,windowEvents,attrs,form,button,status,email};
}
let count=0;
function check(name,fn){fn();count++;console.log('PASS '+name);}
check('valid submit immediately announces processing and prevents duplicate requests',()=>{
 const x=boot();let stopped=false;
 x.events.submit({defaultPrevented:false,preventDefault(){stopped=true;}});
 assert.equal(stopped,false);assert.equal(x.button.disabled,true);assert.equal(x.attrs['aria-busy'],'true');assert.equal(x.status.hidden,false);assert.match(x.status.textContent,/Đang xử lý/);
 x.events.submit({defaultPrevented:false,preventDefault(){stopped=true;}});assert.equal(stopped,true);
});
check('back navigation restores an actionable form',()=>{
 const x=boot();x.events.submit({defaultPrevented:false,preventDefault(){}});x.windowEvents.pageshow();
 assert.equal(x.button.disabled,false);assert.equal(x.status.hidden,true);assert.equal(x.attrs['aria-busy'],undefined);
});
check('empty and malformed email have visible Vietnamese feedback, corrected input clears it',()=>{
 const x=boot();x.email.validity.valueMissing=true;x.events.invalid({target:x.email});assert.match(x.email.message,/nhập email/i);assert.equal(x.button.disabled,false);assert.equal(x.status.hidden,false);
 x.email.validity.valueMissing=false;x.email.validity.typeMismatch=true;x.events.invalid({target:x.email});assert.match(x.email.message,/hợp lệ/);
 x.events.input({target:x.email});assert.equal(x.email.message,'');assert.equal(x.status.hidden,true);
});
check('cancelled submits do not leave controls disabled',()=>{
 const x=boot();x.events.submit({defaultPrevented:true,preventDefault(){}});assert.equal(x.button.disabled,false);
});
console.log('RECOVERY_FEEDBACK_CHECKS='+count);
