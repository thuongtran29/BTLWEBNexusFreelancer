import {spawn} from 'node:child_process';
import {mkdtemp,readFile,writeFile,mkdir,rm,access} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const args=new Map();for(let i=2;i<process.argv.length;i+=2)args.set(process.argv[i],process.argv[i+1]);
if(!args.has('--output')||!args.has('--fixture')||!args.has('--audit-web-root'))throw Error('Required: --output <audit-results-folder> --fixture <RealtimeAuditE2E.json> --audit-web-root <isolated-44392-web>');
const root=path.resolve(args.get('--output')),out=path.join(root,'browser');
const audit=path.resolve(args.get('--audit-web-root'));
if(audit.toLowerCase().startsWith('d:\\freelancerbtlweb')||!(await readFile(path.join(audit,'Web.config'),'utf8')).includes('Initial Catalog=NexusFreelanceAudit20261019;'))throw Error('Only isolated audit site/database allowed');
await mkdir(root,{recursive:true});
const base='https://localhost:44392';
const fixture=JSON.parse(await readFile(path.resolve(args.get('--fixture')),'utf8'));
const cooldown=fixture.cooldownUntilUtc ? Math.max(0,Date.parse(fixture.cooldownUntilUtc)-Date.now()) : 0;
if(cooldown>0){if(cooldown>70000)throw Error('Invalid fixture cooldown');console.log('Waiting for real send-quota window after synthetic backlog fixture.');await new Promise(resolve=>setTimeout(resolve,cooldown));}
const demo=await readFile('D:/FreelancerBTLWEB/_local/TAI_KHOAN_TEST.txt','utf8');
const password=/^Mật khẩu: (.+)$/m.exec(demo)?.[1]?.trim();if(!password)throw Error('Local test credential unavailable');
const profile=await mkdtemp(path.join(tmpdir(),'nexus-realtime-'));
const child=spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--ignore-certificate-errors','--remote-debugging-port=0',`--user-data-dir=${profile}`,'about:blank'],{stdio:'ignore'});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));let ws,id=0;
const pending=new Map(),roles=new Map(),events=[],errors=[],checks=[],views=[],allPages=[];
function check(ok,label){if(!ok)throw Error(label);checks.push(label);console.log('PASS '+label);}
async function call(method,params={},sessionId){return new Promise((resolve,reject)=>{const n=++id,timer=setTimeout(()=>{pending.delete(n);reject(Error('CDP timeout '+method));},45000);pending.set(n,{resolve:r=>{clearTimeout(timer);resolve(r);},reject:e=>{clearTimeout(timer);reject(e);}});ws.send(JSON.stringify({id:n,method,params,...(sessionId?{sessionId}:{})}));});}
class Page {
 constructor(session,role,context){this.s=session;this.role=role;this.context=context;}
 async cmd(method,params={}){return call(method,params,this.s);}
 async eval(expression){const r=await this.cmd('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;}
 async wait(expression,label,timeout=20000){const start=Date.now();while(Date.now()-start<timeout){try{if(await this.eval(expression))return;}catch(error){if(error.message.includes('SyntaxError'))throw error;}await sleep(150);}throw Error('Wait: '+label);}
 async nav(route){await this.cmd('Page.navigate',{url:base+route});await sleep(150);await this.wait('document.readyState==="complete"','page '+route,90000);await this.eval('document.fonts.ready.then(()=>true)');}
 async click(selector){
  for(let i=0;i<8;i++){const p=await this.eval(`(()=>{let e=document.querySelector(${JSON.stringify(selector)}),d;for(let p=e;p;p=p.parentElement)if(p.tagName==='DETAILS'&&!p.open)d=p;if(!d)return null;let s=d.querySelector('summary');s.scrollIntoView({block:'center',behavior:'instant'});let r=s.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);if(!p)break;await this.mouse(p);await sleep(50);}
  const p=await this.eval(`(()=>{let e=document.querySelector(${JSON.stringify(selector)});if(!e)throw Error('Missing '+${JSON.stringify(selector)});e.scrollIntoView({block:'center',behavior:'instant'});let r=e.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2};})()`);await this.mouse(p);
 }
 async mouse(p){await this.cmd('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});await this.cmd('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});}
 async fill(selector,data){await this.eval(`(()=>{let f=document.querySelector(${JSON.stringify(selector)});if(!f)throw Error('Missing form');for(let[k,v]of Object.entries(${JSON.stringify(data)})){let e=[...f.elements].find(x=>x.name===k);if(!e)throw Error('Missing '+k);e.value=String(v);if(k==='Description'&&document.querySelector('.CodeMirror'))document.querySelector('.CodeMirror').CodeMirror.setValue(String(v));e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));}})()`);}
 async send(text){await this.fill('form.chat-compose',{[this.scope==='workspace'?'Body':'body']:text});await this.click('form.chat-compose button[type=submit]');await this.wait('document.querySelector("[data-live-send-status]").textContent.includes("Đã gửi")','AJAX send '+this.role);}
 async capture(name){const data=await this.cmd('Page.captureScreenshot',{format:'png',captureBeyondViewport:false});await writeFile(path.join(out,name+'.png'),Buffer.from(data.data,'base64'));}
 async measure(width){await this.cmd('Emulation.setDeviceMetricsOverride',{width,height:950,deviceScaleFactor:1,mobile:false});const r=await this.eval('({width:innerWidth,scroll:document.documentElement.scrollWidth})');check(r.scroll<=width+1,this.role+' no horizontal overflow at '+width);views.push({role:this.role,...r});await this.capture(this.role+'-'+width);}
}
async function page(role,context){const ctx=context || (await call('Target.createBrowserContext',{disposeOnDetach:true})).browserContextId;const t=await call('Target.createTarget',{url:'about:blank',browserContextId:ctx});const a=await call('Target.attachToTarget',{targetId:t.targetId,flatten:true});const p=new Page(a.sessionId,role,ctx);allPages.push(p);roles.set(p.s,role);await p.cmd('Page.enable');await p.cmd('Runtime.enable');await p.cmd('Network.enable');await p.cmd('Emulation.setFocusEmulationEnabled',{enabled:true});await p.cmd('Emulation.setDeviceMetricsOverride',{width:1440,height:1000,deviceScaleFactor:1,mobile:false});return p;}
async function login(p,key,role){await p.nav('/Account/Login');await p.fill('form[action="/Account/Login"]',{Email:key+'@nexus.example.test',Password:password,Role:role});await p.click('form[action="/Account/Login"] button[type=submit]');await p.wait('document.body.dataset.accountId && document.body.dataset.accountId!=="0" && !document.querySelector("#GoogleLoginButton")','login '+role,60000);check(true,role+' real browser login');}
async function online(p,on){await p.cmd('Network.emulateNetworkConditions',{offline:!on,latency:0,downloadThroughput:-1,uploadThroughput:-1});}
let newProject, recovering=false;
try {
 let parts;for(let i=0;i<80;i++){try{parts=(await readFile(path.join(profile,'DevToolsActivePort'),'utf8')).trim().split('\n');break;}catch(_){await sleep(100);}}
 if(!parts)throw Error('Chrome did not start');ws=new WebSocket('ws://127.0.0.1:'+parts[0]+parts[1]);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j;});
 ws.onmessage=e=>{const m=JSON.parse(e.data),p=pending.get(m.id);if(p){pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result);return;}const role=roles.get(m.sessionId);if(m.method==='Runtime.exceptionThrown')errors.push({role,error:m.params.exceptionDetails.text});if(m.method==='Network.eventSourceMessageReceived')events.push({role,event:m.params.eventName,at:Date.now()});if(m.method==='Network.responseReceived'&&m.params.response.status>=500&&!recovering)errors.push({role,status:m.params.response.status,url:m.params.response.url});};
 await mkdir(out,{recursive:true});const c=await page('Client'),f=await page('Freelancer'),a=await page('Admin');
 await login(c,'client.quanghuy','Client');await login(f,'linh.backend','Freelancer');await login(a,'admin','Admin');
 const cid=fixture.conversationId,wid=fixture.workspaceId;await c.nav('/Messages/Thread/'+cid);await f.nav('/Messages/Thread/'+cid);await a.nav('/Notifications');
 const cn=await page('Client-notifications',c.context);await cn.nav('/Notifications');
 await c.wait('document.querySelector("[data-live-status]").dataset.state==="connected"','Client SSE state');
 await f.wait('document.querySelector("[data-live-status]").dataset.state==="connected"','Freelancer SSE state');
 await c.eval('window.__sameDocument="yes";window.auditXss=false');await f.eval('window.__sameDocument="yes"');
 const tag='UI realtime '+Date.now(),payload=tag+' <img src=x onerror="window.auditXss=true">';
 await f.send(payload);await c.wait('document.querySelector(".chat-thread").textContent.includes('+JSON.stringify(payload)+')','Incoming direct message');
 check(await c.eval('window.__sameDocument==="yes" && !window.auditXss && !document.querySelector(".chat-thread img[src=x]")'),'Incoming message uses literal text, no reload or XSS');
 const draft='Bản nháp giữ khi mất mạng '+tag;await c.fill('form.chat-compose',{body:draft});await online(c,false);await sleep(200);await c.click('form.chat-compose button[type=submit]');
 check(await c.eval('document.querySelector("textarea").value==='+JSON.stringify(draft)+' && document.querySelector("[data-live-send-status]").textContent.includes("chưa gửi")'),'Offline send preserves draft and does not pretend success');
 const missed=[tag+' mất mạng 1',tag+' mất mạng 2',tag+' mất mạng 3'];for(const text of missed)await f.send(text);
 await online(c,true);await c.wait(JSON.stringify(missed)+'.every(t=>document.querySelector(".chat-thread").textContent.includes(t))','Catch up missed messages');
 check(await c.eval('document.querySelector("textarea").value==='+JSON.stringify(draft)),'Reconnect preserves unsent draft');
 check(await c.eval('(()=>{let ids=[...document.querySelectorAll("[data-message-id]")].map(x=>x.dataset.messageId);return ids.length===new Set(ids).size})()'),'Reconnect does not duplicate rendered messages');
 await c.send(draft);await f.wait('document.querySelector(".chat-thread").textContent.includes('+JSON.stringify(draft)+')','Client reply reaches freelancer');check(await f.eval('window.__sameDocument==="yes"'),'Client and Freelancer exchange in both directions without reload');
 await cn.wait('document.querySelectorAll("form[data-ajax-read]").length>0','New notifications arrive on open page');await cn.click('form[data-ajax-mark-all] button');
 await c.cmd('Page.bringToFront');
 await c.wait(`!document.querySelector('a[href="/Notifications"] .notification-count')`,'Cross-tab unread total');check(true,'Mark-all-read updates bell in another Client tab');
 await c.nav('/Projects/Create');await c.fill('form[action="/Projects/Create"]',{Title:tag+' phản ánh',Description:'Dự án kiểm thử realtime cho quy trình báo cáo và quản trị; không phải công việc thật.',Industry:'Công nghệ thông tin',ProjectType:'SOLO',Budget:1000000});await c.click('form[action="/Projects/Create"] button[type=submit]');await c.wait('location.pathname.startsWith("/Projects/Manage/")','Create report fixture');newProject=Number(await c.eval('location.pathname.split("/").pop()'));
 await c.fill('form[action="/Projects/AddRequirement"]',{Title:'Backend Developer',RequiredCount:1});await c.click('form[action="/Projects/AddRequirement"] button[type=submit]');await sleep(300);await c.wait('document.readyState==="complete" && document.body.textContent.includes("Backend Developer")','Role saved');
 await c.click('form[action^="/Projects/Publish/"] button[type=submit]');await sleep(300);if(await c.eval('!!document.querySelector("dialog.confirmation-dialog[open]")'))await c.click('dialog.confirmation-dialog[open] .btn-primary');await c.wait(`!document.querySelector('form[action^="/Projects/Publish/"]')`,'Publish fixture');
 await f.nav('/Projects/Details/'+newProject);await f.fill('form[action="/Reports/Submit"]',{Category:'Other',Details:'Phản ánh minh họa để kiểm thử thông báo gửi ngay tới quản trị viên. '+tag});await f.click('form[action="/Reports/Submit"] button[type=submit]');await f.wait('location.pathname.startsWith("/Reports/Mine")','Report submitted');
 await a.wait('document.querySelector(".notification-list").textContent.includes('+JSON.stringify('về dự án #')+'+'+newProject+')','Admin receives report notification');check(true,'Admin receives new report with sender, project and source without reload');
 await a.eval(`window.__reportNotification=[...document.querySelectorAll('[data-notification-id]')].find(x=>x.textContent.includes('về dự án #${newProject}')).dataset.notificationId`);
 const nid=await a.eval('window.__reportNotification');await a.click('[data-notification-id="'+nid+'"] form[action^="/Notifications/Open"] button');await a.wait('location.pathname.startsWith("/Admin/Reports")','Admin opens notified moderation queue');check(true,'Admin notification opens the authorized moderation function');
 for(const p of [c,f,a]){await p.nav('/Workspace/Index/'+wid);p.scope='workspace';}
 await f.send(tag+' trao đổi nhóm');for(const p of [c,a])await p.wait('document.querySelector(".chat-thread").textContent.includes('+JSON.stringify(tag+' trao đổi nhóm')+')','Workspace live '+p.role);
 check(await a.eval(`!document.querySelector('form[action="/Workspace/SendMessage"]')`),'Admin receives live workspace evidence with no send control');
 check(await c.eval(`!!document.querySelector('form[action="/Workspace/SendMessage"]')`),'Client can discuss project scope while work management stays restricted');
 for(const p of [c,f,a])for(const width of [375,768,1440])await p.measure(width);
 const rotationStart=Date.now();while(!events.some(x=>x.event==='rotate')&&Date.now()-rotationStart<28000)await sleep(200);
 check(events.some(x=>x.event==='rotate'),'Stream rotation/reconnect observed during interaction');
 if(process.env.NEXUS_RT_RECYCLE==='true'){
  const draft='Giữ bản nháp khi IIS khởi động lại '+tag;await c.fill('form.chat-compose',{Body:draft});recovering=true;
  await writeFile(path.join(root,'recycle-ready.txt'),'Audit browser ready; dedicated 44392 IIS only.');
  let ready=false;for(let i=0;i<240;i++){try{await access(path.join(root,'recycle-restarted.txt'));ready=true;break;}catch(_){}await sleep(250);}if(!ready)throw Error('Audit recycle coordinator did not respond');
  await c.wait('document.querySelector("[data-live-status]").dataset.state==="connected"','Reconnect after IIS restart',120000);
  check(await c.eval('document.querySelector("form.chat-compose textarea").value==='+JSON.stringify(draft)),'IIS restart preserves the draft without reloading the page');
  await f.send(tag+' sau khi IIS khởi động lại');await c.wait('document.querySelector(".chat-thread").textContent.includes('+JSON.stringify(tag+' sau khi IIS khởi động lại')+')','Post-restart message delivery');recovering=false;
  check(true,'Realtime reconnects after IIS restart and delivers newly persisted messages');
 }
 // A second tab sharing Client cookies logs out, revoking the other tab's live UI.
 await cn.nav('/Dashboard');await cn.click('form[action="/Account/Logout"] button');await sleep(150);if(await cn.eval('!!document.querySelector("dialog.confirmation-dialog[open]")'))await cn.click('dialog.confirmation-dialog[open] .btn-primary');await cn.wait('document.body.dataset.accountId==="0"','Client logout');
 await c.wait('document.querySelector("[data-live-status]").dataset.state==="stopped"','Other tab closed after logout');
 check(await c.eval('document.querySelector(".chat-thread").textContent.includes("đăng xuất") && document.querySelector("form.chat-compose button").disabled'),'Logout in another tab closes live access and clears private message display');
 check(events.some(x=>x.event==='state'&&x.role==='Client')&&events.some(x=>x.event==='state'&&x.role==='Freelancer')&&events.some(x=>x.event==='state'&&x.role==='Admin'),'Actual SSE frames observed for all three roles');
 check(errors.length===0,'No unexpected JavaScript exception or HTTP 500');
 console.log('REALTIME_BROWSER_CHECKS='+checks.length+' VIEWPORTS='+views.length);
} finally {
 const states=[];for(const p of allPages)try{states.push({role:p.role,...await p.eval(`({route:location.pathname,hidden:document.hidden,status:document.querySelector('[data-live-status]')?.textContent,unread:document.querySelector('a[href="/Notifications"]')?.getAttribute('aria-label'),feedback:document.querySelector('[data-live-send-status]')?.textContent,notificationFeedback:document.querySelector('#notification-feedback')?.textContent})`)});await p.capture('last-'+p.role);}catch(_){}
 await writeFile(path.join(out,'last-pages.json'),JSON.stringify(states,null,2));
 await mkdir(out,{recursive:true});await writeFile(path.join(out,'results.json'),JSON.stringify({checks,views,errors,events,newProject},null,2));ws?.close();child.kill();await sleep(300);
 if(profile.startsWith(path.join(tmpdir(),'nexus-realtime-')))await rm(profile,{recursive:true,force:true,maxRetries:4,retryDelay:250});
}
