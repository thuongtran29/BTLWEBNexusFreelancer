"""Exercise the shipped cleanup predicate against memory-only SQL rows."""
from pathlib import Path
from V3FlowE2E import sql

source = Path(__file__).with_name('HideTestSkillCatalog.sql').read_text(encoding='utf-8')
predicate = source.split('UPDATE dbo.SkillTracks SET IsActive=0\nWHERE ', 1)[1].split(';', 1)[0]
rows = """
(1,1,'E2E_A',N'Admin Skills E2E A'),
(2,1,'TIMER_A',N'Timer test A'),
(3,1,'V3E_ABCDEF1234_0',N'V3 proof abcdef1234 0'),
(4,1,'V3E_ABCDEF1234X0',N'V3 proof abcdef1234 0'),
(5,1,'V3E_ABCDEG1234_0',N'V3 proof abcdeg1234 0'),
(6,1,'V3E_ABCDEF1234_0',N'Customer skill'),
(7,1,'NF_UX_DESIGN',N'Thiết kế trải nghiệm & giao diện'),
(8,0,'V3E_ABCDEF1234_0',N'V3 proof abcdef1234 0'),
(9,1,'V3E_ABCDEF1234_9',N'V3 proof abcdef1234 9'),
(10,1,'V3E_ABCDEF1234_1',N'V3 proof abcdef1234 0')
"""
def matched(where):
    return sql('SET NOCOUNT ON; DECLARE @tracks TABLE(Id int,IsActive bit,Code varchar(40),Title nvarchar(120)); INSERT @tracks VALUES '+rows+'; SELECT Id FROM @tracks WHERE '+where+' ORDER BY Id;').splitlines()
assert matched(predicate) == ['1','2','3'], 'Only exact owned historical patterns may match'
mutated = predicate.replace(" AND SUBSTRING(Code,15,1)='_'", '')
assert mutated != predicate and matched(mutated) != ['1','2','3'], 'Negative delimiter case must detect the old defect'
print('PASS actual cleanup predicate: 3 exact matches, 7 protected rows; delimiter mutation rejected')
