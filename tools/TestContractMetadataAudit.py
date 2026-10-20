"""Exercise the shipped metadata-audit predicate with in-memory SQL rows only."""
from pathlib import Path
import json,re,subprocess,tempfile
from V3FlowE2E import SQLCMD
source=Path(__file__).with_name('ContractsAudit.sql').read_text(encoding='utf-8-sig')
predicate=source.split('WHERE m.Id IS NULL OR EXISTS (',1)[1].split(');\nINSERT @a',1)[0].strip()
def quote(x):return 'NULL' if x is None else "N'"+str(x).replace("'","''")+"'"
cases=[('exact','Plan','Criteria',5,100,0),('title-case','plan','Criteria',5,100,1),('title-space','Plan ','Criteria',5,100,1),('criteria-case','Plan','criteria',5,100,1),('criteria-space','Plan','Criteria ',5,100,1),('both-null','Plan',None,None,100,0),('null-empty','Plan','',None,100,1),('day','Plan','Criteria',6,100,1),('amount','Plan','Criteria',5,101,1)]
rows=[]
for name,title,criteria,day,amount,expected in cases:
 snap={'Title':'Plan','AcceptanceCriteria':None if name in ('both-null','null-empty') else 'Criteria','DueDay':None if name in ('both-null','null-empty') else 5,'Amount':100}
 rows.append('('+','.join([quote(name),quote(title),quote(criteria),'NULL' if day is None else str(day),str(amount),quote(json.dumps(snap)),str(expected)])+')')
query='DECLARE @cases TABLE(Name nvarchar(30),Title nvarchar(180),AcceptanceCriteria nvarchar(1000),DueDay int,Amount decimal(18,0),Snapshot nvarchar(max),Expected bit); INSERT @cases VALUES '+','.join(rows)+'; '
def evaluate(p):
 with tempfile.NamedTemporaryFile(mode='w',encoding='utf-8-sig',prefix='NexusAuditPredicate-',suffix='.sql',delete=False) as f:
  f.write('SET NOCOUNT ON; '+query+'SELECT COUNT(*) FROM @cases m CROSS APPLY(SELECT m.Snapshot AS value) j WHERE m.Expected<>CASE WHEN EXISTS('+p+') THEN 1 ELSE 0 END;')
 try:
  result=subprocess.run([SQLCMD,'-S',r'lpc:.\SQLEXPRESS','-d','NexusFreelanceMvp','-E','-C','-I','-b','-h','-1','-W','-i',f.name],capture_output=True,text=True,encoding='utf-8',errors='replace')
  assert result.returncode==0,result.stdout+result.stderr
  return result.stdout.strip()
 finally: Path(f.name).unlink()
assert evaluate(predicate)=='0'
print('CONTRACT_METADATA_AUDIT_CHECKS=9 PASS')
mutation=re.sub(r'CONVERT\(varbinary\(max\),(m\.(?:Title|AcceptanceCriteria)|JSON_VALUE\(j.value,\'\$\.(?:Title|AcceptanceCriteria)\'\))\)',r'\1',predicate)
assert mutation!=predicate
assert int(evaluate(mutation))>0
print('PASS collation/trailing-space weakening mutation rejected')
