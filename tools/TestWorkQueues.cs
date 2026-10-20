using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

public sealed class ReadSet<T> : DbSet<T>, IQueryable<T> where T : class
{
    private readonly IQueryable<T> data;
    public ReadSet(IEnumerable<T> rows) { data=rows.AsQueryable(); }
    Type IQueryable.ElementType { get { return data.ElementType; } }
    Expression IQueryable.Expression { get { return data.Expression; } }
    IQueryProvider IQueryable.Provider { get { return data.Provider; } }
    IEnumerator<T> IEnumerable<T>.GetEnumerator() { return data.GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() { return data.GetEnumerator(); }
}
public static class TestWorkQueues
{
    private static int count;
    private static void Check(bool ok,string label) { if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label); }
    public static int Main()
    {
        var contracts=new[]{"AwaitingSignatures","Active","Disputed","Completed","AwaitingEscrow","Terminated","Declined"}
            .Select((state,index)=>new ProjectContract{Id=index+1,Status=state,CurrentVersion=2,CounterpartyId=22,Project=new Project{ClientId=11}}).ToList();
        var current=new ContractVersion{ContractId=1,VersionNumber=2,Hash=new string('A',64)};
        using(var db=new AppDbContext())
        {
            db.Contracts=new ReadSet<ProjectContract>(contracts);
            db.ContractMembers=new ReadSet<ContractMember>(new[]{new ContractMember{ContractId=1,UserAccountId=33},new ContractMember{ContractId=2,UserAccountId=33}});
            db.ContractAcceptances=new ReadSet<ContractAcceptance>(new[]{
                new ContractAcceptance{UserAccountId=11,Version=current,AcceptedHash=current.Hash},
                new ContractAcceptance{UserAccountId=22,Version=new ContractVersion{ContractId=1,VersionNumber=1,Hash="old"},AcceptedHash="old"},
                new ContractAcceptance{UserAccountId=33,Version=current,AcceptedHash="wrong"}});
            db.EscrowMilestones=new ReadSet<EscrowMilestone>(new[]{2,3,4}.Select(id=>new EscrowMilestone{ContractId=id,Status="Submitted"}));
            Check(ContractInbox.VisibleTo(db,11,"Client").Count()==7,"owner sees own contracts");
            Check(ContractInbox.VisibleTo(db,22,"Freelancer").Count()==7,"counterparty sees own contracts");
            Check(ContractInbox.VisibleTo(db,33,"Freelancer").Count()==2,"member sees only contracted membership");
            Check(!ContractInbox.VisibleTo(db,99,"Client").Any(),"unrelated account sees no contracts");
            Check(ContractInbox.VisibleTo(db,99,"Admin").Count()==7,"trusted admin role can inspect contracts");
            var query=contracts.AsQueryable();
            Check(!ContractInbox.Filter(db,query,"confirm",11,false).Any(),"current correct signature removes personal action");
            Check(ContractInbox.Filter(db,query,"confirm",22,false).Single().Id==1,"old version signature does not complete new version");
            Check(ContractInbox.Filter(db,query,"confirm",33,false).Single().Id==1,"wrong hash is not treated as confirmation");
            Check(ContractInbox.Filter(db,query,"review",11,false).Select(c=>c.Id).SequenceEqual(new[]{2}),"review excludes disputed and completed contracts even with submitted milestones");
            Check(ContractInbox.Filter(db,query,"fund",11,false).Single().Id==5,"funding queue includes only awaiting escrow");
            Check(ContractInbox.Filter(db,query,"active",11,false).Single().Id==2,"active queue excludes disputed work");
            Check(ContractInbox.Filter(db,query,"issues",11,false).Single().Id==3,"disputes have a separate work queue");
            Check(ContractInbox.Filter(db,query,"closed",11,false).Select(c=>c.Id).SequenceEqual(new[]{4,6,7}),"all terminal states remain searchable");
        }
        Console.WriteLine("WORK_QUEUE_TESTS_PASSED checks="+count);return 0;
    }
}
