"""HTTP acceptance helper for legacy local V3 fixtures, without bypassing signatures."""
from SmokeE2E import forms
from V3FlowE2E import sql

def confirm_project(smoke, project_id, *actors):
    ids=[int(x) for x in sql(f"SELECT Id FROM Contracts WHERE ProjectId={int(project_id)} AND Status='AwaitingSignatures'").splitlines()]
    assert ids, 'Expected an unsigned fixture contract'
    for cid in ids:
        for actor in actors:
            page=f'/Contracts/Details/{cid}'
            markup=smoke.get(actor,page).text
            consent=[f for f in forms(markup) if '/Contracts/Confirm/' in f['action']]
            assert len(consent)==1, 'Every fixture party must explicitly receive a current consent form'
            response=smoke.post(actor,consent[0]['action'],consent[0]['fields'])
            assert response.status_code==302
        assert sql(f"SELECT Status FROM Contracts WHERE Id={cid}")=='AwaitingEscrow', 'All current parties must have accepted'

def dispute_contract(project_id):
    return int(sql(f"SELECT Id FROM Contracts WHERE ProjectId={int(project_id)} AND Status='Active'"))
