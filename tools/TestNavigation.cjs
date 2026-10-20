// Route selection regression checks run the shipped script against a small DOM boundary.
// Usage: node tools/TestNavigation.cjs
const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../NexusFreelance.Web/Content/navigation.js'), 'utf8');
function selected(source, controller, action, role, keys) {
  const groups = keys.map(() => ({open:false}));
  const links = keys.map((key,i) => ({dataset:{navKey:key}, closest:()=>groups[i], setAttribute(name,value) {this[name]=value;}}));
  const document = {
    body:{dataset:{navController:controller,navAction:action,navRole:role}},
    getElementById:()=>null, querySelector:()=>null,
    querySelectorAll: selector => selector === '.sidebar-nav a[data-nav-key]' ? links : []
  };
  vm.runInNewContext(source, {document,location:{pathname:'/test'}});
  links.forEach((link,i)=>assert.equal(groups[i].open,link['aria-current']==='page','Only selected secondary section opens automatically'));
  return links.filter(x=>x['aria-current']==='page').map(x=>x.dataset.navKey);
}
const cases = [
  ['Projects','Manage','Client','projects'], ['Projects','Edit','Client','projects'],
  ['Projects','Create','Client','create'], ['Projects','Details','Freelancer','marketplace'],
  ['JobStudio','New','Client','create'], ['Workspace','Index','Client','contracts'],
  ['WorkspaceFiles','Download','Freelancer','contracts'], ['Payments','Return','Client','contracts'],
  ['Contracts','Details','Freelancer','contracts'], ['Skills','Certifications','Freelancer','certifications'],
  ['Skills','SubmitCertification','Freelancer','certifications'],
  ['Skills','Take','Freelancer','skills'], ['Admin','Questions','Admin','admin-questions'],
  ['Admin','Reports','Admin','admin-reports'], ['Wallet','Admin','Admin','wallet'],
  ['Home','HowItWorks','Client','guide'], ['Dashboard','Index','Client','overview'],
  ['SkillEvidence','Admin','Admin','evidence'], ['Negotiation','Index','Freelancer','proposals']
];
const keys = [...new Set(cases.map(x=>x[3]))];
function suite(source) {
  for (const [controller,action,role,key] of cases)
    assert.deepEqual(selected(source,controller,action,role,keys),[key],`${role}: ${controller}/${action}`);
}
suite(script);
const mutation=script.replace("action === 'create'", "action !== 'create'");
assert.notEqual(mutation,script,'Mutation must change the route condition');
assert.throws(()=>suite(mutation),assert.AssertionError,'Suite must detect incorrect Create/Manage selection');
console.log(`PASS ${cases.length} route cases; Create/Manage condition mutation rejected`);
