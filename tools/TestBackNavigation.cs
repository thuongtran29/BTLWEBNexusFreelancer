using System;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.ViewModels;
class TestBackNavigation
{
    static int checks;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
    static int Main(){
        Check(PageNavigation.Back("Home","Index",null,null)==null,"public root has no back loop");
        Check(PageNavigation.Back("Dashboard","Index","Client",null)==null,"dashboard is a root");
        Check(PageNavigation.Back("Admin","Index","Admin",null)==null,"admin root has no back loop");
        Check(PageNavigation.Back("Projects","Edit","Client",null,12).Path=="~/Projects/Manage/12","edit returns to matching project");
        Check(PageNavigation.Back("Projects","Details","Freelancer",null,12).Path=="~/","freelancer returns to marketplace");
        Check(PageNavigation.Back("Projects","Details","Client",null,12).Path=="~/Projects/Mine","client returns to own project list");
        Check(PageNavigation.Back("Contracts","Details","Client",new ContractDetailsPage{Contract=new ProjectContract{ProjectId=12}}).Path=="~/Contracts?projectId=12","contract preserves project context");
        Check(PageNavigation.Back("Workspace","Index","Client",new Workspace{ProjectId=12,ContractId=34}).Path=="~/Contracts/Details/34","workspace returns to own contract");
        Check(PageNavigation.Back("Admin","Questions","Admin",null).Path=="~/Admin","admin child returns to admin");
        Check(PageNavigation.Back("Account","ConfirmRegistration",null,null).Path=="~/Account/Login","verification returns to login");
        Check(PageNavigation.Back("Negotiation","Thread","Freelancer",null).Path=="~/Proposals/Mine","proposal conversation has a stable parent");
        Check(PageNavigation.Back("https://attacker.test","outside","Client",null).Path=="~/Dashboard","unknown route cannot produce an external redirect");
        Console.WriteLine("BACK_NAVIGATION_CHECKS="+checks);return 0;
    }
}
