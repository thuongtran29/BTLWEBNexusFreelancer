// Deterministic matching/threshold checks, then transactional PSP checks on a named audit DB.
// No network payments; all callback fields and credentials are synthetic fixtures.
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;
using NexusFreelance.Web.ViewModels;

internal static class TestPlatformExtensions
{
    private static int checks;
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static void Throws(Action run, string name) { try { run(); } catch(InvalidOperationException) { Check(true,name); return; } throw new Exception(name); }
    private static Dictionary<string,string> Callback(WalletTopup order, string transaction, string amount = null, string status = "00", string code = "00")
    {
        var fields = new Dictionary<string,string> { {"vnp_TmnCode","AUDIT123"}, {"vnp_TxnRef",order.TxnRef}, {"vnp_Amount",amount ?? (order.Amount*100).ToString("0")}, {"vnp_TransactionStatus",status}, {"vnp_ResponseCode",code}, {"vnp_TransactionNo",transaction}, {"vnp_CurrCode","VND"} };
        Sign(fields); return fields;
    }
    private static void Sign(Dictionary<string,string> fields) { fields["vnp_SecureHash"]=VnPayCodec.Hmac(VnPaySettings.Load().Secret,VnPayCodec.Canonical(fields)); }
    public static int Main(string[] args)
    {
        try {
            Check(!SkillVerificationService.HasPassed(4,5),"Exact 80 percent does not earn a new certificate");
            Check(SkillVerificationService.HasPassed(81,100),"Above 80 percent earns a certificate");
            Check(SkillVerificationService.HasPassed(201,250),"Actual 80.4 percent passes regardless of rounded display");
            Check(!SkillVerificationService.HasPassed(0,0) && !SkillVerificationService.HasPassed(6,5),"Invalid exam counts cannot pass");
            var project=new Project { Industry="Công nghệ thông tin", Requirements=new List<RoleRequirement>{new RoleRequirement{Title="Backend Developer"}} };
            var required=new List<ProjectSkillRequirement>{new ProjectSkillRequirement{SkillTrackId=1,MinimumLevel=3,Skill=new SkillTrack{Title="Backend"}}};
            var weak=new[]{new VerifiedSkillRow{SkillId=1,Title="Backend",Level=1,Evidence="Nội bộ"}};
            var strong=new[]{new VerifiedSkillRow{SkillId=1,Title="Backend",Level=3,Evidence="Minh chứng"}};
            var below=SmartMatchingService.Score(project,required,weak,"Backend Developer",project.Industry,8,"Fixture","freelancer",weak);
            var above=SmartMatchingService.Score(project,required,strong,"Backend Developer",project.Industry,8,"Fixture","freelancer",strong);
            Check(below.Covered==0 && below.Score==0,"Role match cannot override missing minimum skill level");
            Check(above.Covered==1 && above.Score==100,"Verified skill, level, role and industry produce explainable score");
            Check(SmartMatchingService.Score(project,required,strong.Concat(weak),"Backend",project.Industry,1,"Group","group").Covered==1,"Group skill union counts a skill once");
            VerifiedRoleSemantics();
            if(args.Contains("--roles")) { Console.WriteLine("VERIFIED_ROLE_CHECKS="+checks); return 0; }
            Database.SetInitializer<AppDbContext>(null);
            using(var db=new AppDbContext()) {
                if(!db.Database.Connection.Database.StartsWith("NexusFreelanceAudit",StringComparison.Ordinal)) throw new Exception("Refusing PSP writes outside isolated audit database");
                if(args.Contains("--policy")) { EvidencePolicy(db); Console.WriteLine("PLATFORM_POLICY_CHECKS="+checks); return 0; }
                Environment.SetEnvironmentVariable("NEXUS_VNPAY_ENABLED","true");
                Environment.SetEnvironmentVariable("NEXUS_VNPAY_TMN_CODE","AUDIT123");
                Environment.SetEnvironmentVariable("NEXUS_VNPAY_HASH_SECRET","SyntheticFixtureSecretOnly-12345678901234567890");
                Environment.SetEnvironmentVariable("NEXUS_VNPAY_MODE","sandbox");
                Environment.SetEnvironmentVariable("NEXUS_PUBLIC_BASE_URL","https://localhost:44392");
                var client=db.Users.Single(u=>u.Email=="client.minhan@nexus.example.test").Id;
                var service=new WalletTopupService(db);
                Throws(()=>service.Create(client,0),"Zero top-up rejected");
                Throws(()=>service.Create(client,10000.5m),"Fractional VND rejected");
                Throws(()=>service.Create(client,9999),"Top-up below PSP minimum rejected");
                var amount=10000+new Random().Next(1,5000);
                var before=db.WalletAccounts.AsNoTracking().Where(w=>w.UserAccountId==client).Select(w=>w.Balance).Single();
                var order=service.Create(client,amount);
                Check(service.Create(client,amount).Id==order.Id,"Repeated checkout reuses pending order");
                var url=service.CheckoutUrl(order,"127.0.0.1");
                Check(url.Contains("vnp_BankCode=VNPAYQR") && url.Contains("vnp_Amount="+(amount*100)),"Checkout opens official VNPay QR for the exact amount");
                Check(db.WalletAccounts.AsNoTracking().Where(w=>w.UserAccountId==client).Select(w=>w.Balance).Single()==before,"Creating or opening QR does not credit the wallet");
                var tx=DateTime.UtcNow.Ticks.ToString().Substring(3);
                Check(service.ReceiveIpn(Callback(order,tx,"100"))=="04","Signed wrong amount rejected");
                var tampered=Callback(order,tx); tampered["vnp_SecureHash"]=new string('a',128);
                Check(service.ReceiveIpn(tampered)=="97","Invalid signature rejected");
                var wrongCurrency=Callback(order,tx); wrongCurrency["vnp_CurrCode"]="USD"; Sign(wrongCurrency);
                Check(service.ReceiveIpn(wrongCurrency)=="04","Signed wrong currency rejected");
                var results=new string[2];
                Parallel.For(0,2,i=>{ using(var other=new AppDbContext()) results[i]=new WalletTopupService(other).ReceiveIpn(Callback(order,tx)); });
                Check(results.Count(s=>s=="00")==1 && results.Count(s=>s=="02")==1,"Concurrent IPN credits exactly once across independent contexts");
                Check(db.WalletAccounts.AsNoTracking().Where(w=>w.UserAccountId==client).Select(w=>w.Balance).Single()==before+amount,"Wallet increases by the confirmed amount once");
                Check(db.WalletEntries.Count(e=>e.Reference=="vnpay-topup:"+order.Id)==1,"Exactly one deposit ledger entry exists");
                Check(service.ReceiveIpn(Callback(order,tx))=="02","Further callback replay cannot credit again");
                var next=service.Create(client,amount+1);
                Check(service.ReceiveIpn(Callback(next,tx))=="97","Provider transaction cannot credit a different top-up order");
                var failed=Callback(next,"9999999999999",null,"02","24");
                Check(service.ReceiveIpn(failed)=="00" && db.WalletEntries.Count(e=>e.Reference=="vnpay-topup:"+next.Id)==0,"Canceled checkout records no credit");
                var funding=db.EscrowMilestones.Include(m=>m.Contract.Project).Where(m=>m.Contract.Status=="AwaitingEscrow" && m.Status=="Planned" && !db.PaymentIntents.Any(p=>p.EscrowMilestoneId==m.Id && p.Status=="Pending")).OrderBy(m=>m.Id).FirstOrDefault();
                if(funding==null) throw new Exception("Audit needs a confirmed unfunded contract for PSP isolation regression");
                var escrowService=new PspPaymentService(db);
                var escrow=escrowService.Checkout(funding.Id,funding.Contract.Project.ClientId,"127.0.0.1");
                var cross=Callback(order,tx,(escrow.Amount*100).ToString("0"));cross["vnp_TxnRef"]=escrow.TxnRef;Sign(cross);
                Check(escrowService.ReceiveIpn(cross)=="97","A top-up PSP receipt cannot also fund an escrow contract");
                var escrowTxn=DateTime.UtcNow.Ticks.ToString().Substring(3);cross["vnp_TransactionNo"]=escrowTxn;Sign(cross);
                Check(escrowService.ReceiveIpn(cross)=="00","Signed independent PSP receipt funds its exact escrow milestone");
                var another=service.Create(client,amount+2);
                Check(service.ReceiveIpn(Callback(another,escrowTxn))=="97","An escrow PSP receipt cannot also credit a wallet top-up");
                var own = new Project { ClientId=client, Title="Audit knowledge own", Description="Internal audit", ProjectType="SOLO", Status="Completed", Budget=10000, Industry="Công nghệ thông tin", CreatedAtUtc=DateTime.UtcNow };
                var otherProject = new Project { ClientId=db.Users.Single(u=>u.Email=="client.thuha@nexus.example.test").Id, Title="Audit knowledge other", Description="Private other owner", ProjectType="SOLO", Status="Completed", Budget=10000, Industry="Công nghệ thông tin", CreatedAtUtc=DateTime.UtcNow };
                db.Projects.Add(own); db.Projects.Add(otherProject); db.SaveChanges();
                var ownMemory=new ProjectMemory{ProjectId=own.Id,EditorId=client,Version=1,Source="ClientEdited",ExecutiveSummary="API backend audit own knowledge",LessonsLearned="Test before release",CreatedAtUtc=DateTime.UtcNow};
                var otherMemory=new ProjectMemory{ProjectId=otherProject.Id,EditorId=otherProject.ClientId,Version=1,Source="ClientEdited",ExecutiveSummary="PRIVATE-OTHER-OWNER-MARKER",CreatedAtUtc=DateTime.UtcNow};
                db.ProjectMemories.Add(ownMemory);db.ProjectMemories.Add(otherMemory);db.SaveChanges();
                var idea=new JobIdeaForm{Idea="API backend audit",Industry=own.Industry,UseProjectKnowledge=true,KnowledgeContext="FORGED-INPUT",KnowledgeReferences="FORGED-ID"};
                JobPrompt.AttachKnowledge(db,client,idea);
                Check(idea.KnowledgeReferences.Contains("Memory #"+ownMemory.Id) && !idea.KnowledgeContext.Contains("PRIVATE-OTHER-OWNER-MARKER") && !idea.KnowledgeContext.Contains("FORGED-INPUT"),"Knowledge retrieval enforces client ownership and overwrites forged context");
                idea.UseProjectKnowledge=false;idea.UseSharedKnowledge=false;JobPrompt.AttachKnowledge(db,client,idea);
                Check(idea.KnowledgeContext=="" && idea.KnowledgeReferences=="","Knowledge opt-out sends no memory context");
            }
            Console.WriteLine("PLATFORM_SERVICE_CHECKS="+checks); return 0;
        } catch(Exception ex) { Console.Error.WriteLine("FAIL "+ex.GetType().Name+": "+ex.Message); return 1; }
    }
    private static void VerifiedRoleSemantics()
    {
        var evidence=new[] {
            new VerifiedSkillRow{SkillId=1,Title="Backend",Level=3,Evidence="Verified fixture"},
            new VerifiedSkillRow{SkillId=2,Title="Frontend",Level=2,Evidence="Verified fixture"},
            new VerifiedSkillRow{SkillId=3,Title="Design",Level=4,Evidence="Verified fixture"}
        };
        var form=new FreelancerProfileForm{CoreRole="Legacy title",CoreSkillTrackId=1,SecondarySkillTrackIds=new List<int>{2}};
        ProfileService.ValidateRoles(form,evidence);
        Check(form.CoreRole=="Legacy title","Verified role validation preserves the professional title");
        form.SecondarySkillTrackIds=new List<int>{2,2};
        Throws(()=>ProfileService.ValidateRoles(form,evidence),"Duplicate secondary roles rejected");
        form.SecondarySkillTrackIds=new List<int>{1};
        Throws(()=>ProfileService.ValidateRoles(form,evidence),"Primary role cannot also be secondary");
        form.SecondarySkillTrackIds=new List<int>{99};
        Throws(()=>ProfileService.ValidateRoles(form,evidence),"Unverified secondary role rejected");
        form.CoreSkillTrackId=99;form.SecondarySkillTrackIds=new List<int>();
        Throws(()=>ProfileService.ValidateRoles(form,evidence),"Unverified primary role rejected");
        form.CoreSkillTrackId=null;form.SecondarySkillTrackIds=new List<int>{2};
        Throws(()=>ProfileService.ValidateRoles(form,evidence),"Secondary selection requires primary selection");
        form.SecondarySkillTrackIds=null;
        ProfileService.ValidateRoles(form,new VerifiedSkillRow[0]);
        Check(true,"Professional profile can be saved before any role is verified");

        var profile=new FreelancerProfile{UserAccountId=7,CoreRole="Design",CoreSkillTrackId=1};
        var secondary=new[] {
            new FreelancerSecondaryRole{UserAccountId=7,SkillTrackId=2},
            new FreelancerSecondaryRole{UserAccountId=7,SkillTrackId=2},
            new FreelancerSecondaryRole{UserAccountId=8,SkillTrackId=3}
        };
        var selected=SmartMatchingService.SelectedRoles(profile,secondary,evidence);
        Check(selected.Select(r=>r.SkillId).SequenceEqual(new[]{1,2}),"Role resolver includes main and secondary once and ignores another user's selections");
        Check(MatchingService.VerifiedRolePoints("Backend",selected)==30,"Future snapshot uses exact selected primary title");
        Check(MatchingService.VerifiedRolePoints("Frontend",selected)==30,"Future snapshot uses exact selected secondary title");
        Check(MatchingService.VerifiedRolePoints("Frontend Developer",selected)==24,"Future snapshot keyword score uses verified role titles");
        Check(MatchingService.VerifiedRolePoints("Frontend Backend",selected)==12,"Future snapshot takes best individual role rather than combining or stacking roles");
        Check(MatchingService.VerifiedRolePoints("Frontend",new VerifiedSkillRow[0])==0,"Future snapshot cannot earn role bonus without active selected evidence");
        Check(MatchingService.VerifiedRolePoints("",selected)==0,"Empty requirement does not generate a snapshot role bonus");
        Check(SmartMatchingService.SelectedRoles(profile,secondary,new VerifiedSkillRow[0]).Count==0,"Stored role IDs never count without live evidence");
        Check(SmartMatchingService.SelectedRoles(null,secondary,evidence).Count==0,"Evidence without a profile does not create selected roles");

        var project=new Project{Industry="IT",Requirements=new List<RoleRequirement>{new RoleRequirement{Title="Frontend Developer"}}};
        var required=new List<ProjectSkillRequirement>{new ProjectSkillRequirement{SkillTrackId=2,MinimumLevel=2,Skill=new SkillTrack{Title="Frontend"}}};
        var withSecondary=SmartMatchingService.Score(project,required,evidence,"Unrelated self title","IT",7,"Fixture","freelancer",selected);
        Check(withSecondary.Score==100 && withSecondary.Reasons.Any(r=>r.Contains("Frontend") && r.IndexOf("vai trò",StringComparison.OrdinalIgnoreCase)>=0),"Selected verified secondary title contributes role bonus with explanation");
        var noSelection=SmartMatchingService.Score(project,required,evidence,"Frontend Developer","IT",7,"Fixture","freelancer");
        Check(noSelection.Score==80 && noSelection.Covered==1,"Free-text professional title gives no role bonus while verified ability still counts");
        var primaryOnly=SmartMatchingService.SelectedRoles(profile,new FreelancerSecondaryRole[0],evidence);
        Check(SmartMatchingService.Score(project,required,evidence,"Frontend Developer","IT",7,"Fixture","freelancer",primaryOnly).Score==80,"Unselected verified skills do not become roles automatically");
        var duplicates=selected.Concat(selected).ToArray();
        Check(SmartMatchingService.Score(project,required,evidence,"Anything","IT",7,"Fixture","group",duplicates).Score==100,"Multiple selected roles cannot stack role bonus or skill coverage");
        var stale=SmartMatchingService.Score(project,new List<ProjectSkillRequirement>(),new VerifiedSkillRow[0],"Frontend Developer","IT",7,"Fixture","freelancer",selected);
        Check(stale.Score==0,"Stale selected roles cannot match a project with no declared skills");
        var forged=new[]{new VerifiedSkillRow{SkillId=1,Title="Frontend Developer",Level=5}};
        Check(SmartMatchingService.Score(project,required,evidence,"Frontend Developer","IT",7,"Fixture","freelancer",forged).Score==80,"Role scoring resolves trusted title from evidence rather than supplied selection title");
        var onlyRole=SmartMatchingService.Score(project,new List<ProjectSkillRequirement>(),evidence,"Anything","IT",7,"Fixture","freelancer",selected);
        Check(onlyRole.Score==70 && onlyRole.Reasons.Any(r=>r.Contains("chưa đủ cơ sở")),"Role-only recommendations disclose absent project skill requirements");
    }

    private static void EvidencePolicy(AppDbContext db)
    {
        var user=new UserAccount{Name="Scoped evidence audit",Email="evidence-audit-"+Guid.NewGuid().ToString("N")+"@example.test",PasswordHash="disabled-audit-password",Role="Freelancer",JoinedAtUtc=DateTime.UtcNow,EmailVerifiedAtUtc=DateTime.UtcNow};
        db.Users.Add(user);db.SaveChanges();
        try {
            var admin=db.Users.Single(u=>u.Email=="admin@nexus.example.test").Id;
            var valid=new ExternalCertification{UserAccountId=user.Id,SkillTrackId=17,ProficiencyLevel=2,Name="Scoped valid",Issuer="Audit",EvidenceUrl="https://example.com/valid",IssuedOn=DateTime.Today.AddYears(-1),ExpiresOn=DateTime.Today,Status="Verified",SubmittedAtUtc=DateTime.UtcNow};
            var expired=new ExternalCertification{UserAccountId=user.Id,SkillTrackId=17,ProficiencyLevel=5,Name="Scoped expired",Issuer="Audit",EvidenceUrl="https://example.com/expired",IssuedOn=DateTime.Today.AddYears(-1),ExpiresOn=DateTime.Today.AddDays(-1),Status="Verified",SubmittedAtUtc=DateTime.UtcNow};
            db.ExternalCertifications.Add(valid);db.ExternalCertifications.Add(expired);
            db.SkillClaims.Add(new SkillClaim{UserAccountId=user.Id,SkillTrackId=17,Level=5,YearsExperience=1});
            var attempt=new SkillExamAttempt{UserAccountId=user.Id,SkillTrackId=16,QuestionCount=10,CorrectCount=9,Percent=90,Passed=true,SubmittedAtUtc=DateTime.UtcNow};
            db.SkillExamAttempts.Add(attempt);db.SaveChanges();
            var badge=new EarnedSkillBadge{UserAccountId=user.Id,SkillTrackId=16,EvidenceAttemptId=attempt.Id,IssuedAtUtc=DateTime.UtcNow};
            db.EarnedSkillBadges.Add(badge);db.SaveChanges();
            var matcher=new SmartMatchingService(db);var evidence=matcher.Evidence(new[]{user.Id})[user.Id];
            Check(evidence.Single(e=>e.SkillId==17).Level==2,"Certificate remains valid through its full expiry day; expired or self-declared higher levels do not override it");
            Check(evidence.Single(e=>e.SkillId==16).Level==1,"Internal certificate contributes foundation level only");
            var profiles=new ProfileService(db);
            var form=new FreelancerProfileForm{CoreRole="Legacy professional title",Industry="Công nghệ thông tin",CoreSkillTrackId=16,SecondarySkillTrackIds=new List<int>{17}};
            profiles.SaveProfile(user.Id,form);profiles.SaveProfile(user.Id,form);
            var saved=profiles.Load(user.Id);
            Check(saved.Profile.CoreRole==form.CoreRole && saved.Profile.CoreSkillTrackId==16 && saved.Profile.SecondarySkillTrackIds.SequenceEqual(new[]{17}),"Primary and secondary verified roles persist without overwriting legacy professional title");
            Check(db.Set<FreelancerSecondaryRole>().Count(r=>r.UserAccountId==user.Id)==1,"Repeated profile save does not duplicate secondary roles");
            var profile=db.FreelancerProfiles.Single(p=>p.UserAccountId==user.Id);
            var labels=matcher.SelectedRoleLabels(new[]{profile},new Dictionary<int,List<VerifiedSkillRow>>{{user.Id,evidence}})[user.Id];
            Check(labels.Contains("Chính: ") && labels.Contains("Phụ: ") && !labels.Contains(form.CoreRole),"Shared role label helper uses selected evidence titles, not free text");
            var snapshotRole=evidence.Single(e=>e.SkillId==17).Title;
            var snapshots=new MatchingService(db);
            var snapshotBefore=snapshots.CreateSnapshot(user.Id,new Project{ProjectType="TEAM"},new RoleRequirement{Title=snapshotRole},1);
            var previousExplanation=snapshotBefore.Explanation;
            Check(snapshotBefore.CoreRolePoints==30 && previousExplanation.Contains("phụ: "+snapshotRole),"New proposal snapshot scores selected verified secondary role and records its title");
            var track=db.SkillTracks.Single(s=>s.Id==17);var wasActive=track.IsActive;
            try {
                track.IsActive=false;db.SaveChanges();
                Check(matcher.Evidence(new[]{user.Id})[user.Id].All(e=>e.SkillId!=17) && profiles.Load(user.Id).HasInactiveRoleSelections,"Inactive skill tracks disappear from evidence and invalidate saved role selections");
                Throws(()=>profiles.SaveProfile(user.Id,form),"Inactive secondary role rejected despite otherwise valid certificate");
            } finally { track.IsActive=wasActive;db.SaveChanges(); }
            form.CoreSkillTrackId=17;form.SecondarySkillTrackIds=new List<int>{16};profiles.SaveProfile(user.Id,form);
            Check(profiles.Load(user.Id).Profile.SecondarySkillTrackIds.SequenceEqual(new[]{16}),"Switching primary and secondary replaces selections");
            form.CoreSkillTrackId=16;form.SecondarySkillTrackIds=new List<int>{17};profiles.SaveProfile(user.Id,form);
            badge.RevokedAtUtc=DateTime.UtcNow;badge.RevokedById=admin;badge.RevocationReason="Scoped audit revocation";valid.ExpiresOn=DateTime.Today.AddDays(-1);db.SaveChanges();
            evidence=matcher.Evidence(new[]{user.Id})[user.Id];
            Check(evidence.Count==0,"Revoked internal badge and expired external certificates provide no verified skill; self declaration alone does not qualify");
            Check(matcher.SelectedRoleLabels(new[]{profile})[user.Id]=="" && profiles.Load(user.Id).HasInactiveRoleSelections,"Revoked/expired primary and secondary disappear from role labels and show profile warning");
            var snapshotAfter=snapshots.CreateSnapshot(user.Id,new Project{ProjectType="TEAM"},new RoleRequirement{Title=snapshotRole},1);
            Check(snapshotAfter.CoreRolePoints==0 && snapshotAfter.BadgePoints==0,"Future snapshot excludes expired and revoked roles and abilities");
            Check(snapshotBefore.CoreRolePoints==30 && snapshotBefore.Explanation==previousExplanation,"Earlier generated snapshot retains its original points and explanation");
            form.CoreRole="Attempted overwrite";
            Throws(()=>profiles.SaveProfile(user.Id,form),"Profile cannot re-save revoked or expired role selections");
            Check(profile.CoreRole=="Legacy professional title","Rejected role update leaves tracked professional title unchanged");
            form.CoreRole="Legacy professional title";form.CoreSkillTrackId=null;form.SecondarySkillTrackIds=new List<int>();profiles.SaveProfile(user.Id,form);
            Check(!db.Set<FreelancerSecondaryRole>().Any(r=>r.UserAccountId==user.Id),"Clearing roles removes persisted secondary selections");
            var ids=Enumerable.Range(100000,501).Concat(new[]{user.Id}).ToArray();
            Check(matcher.Evidence(ids).Count==ids.Length,"Evidence queries batch large rosters without SQL parameter overflow");
        } finally {
            db.Set<FreelancerSecondaryRole>().RemoveRange(db.Set<FreelancerSecondaryRole>().Where(r=>r.UserAccountId==user.Id));
            db.FreelancerProfiles.RemoveRange(db.FreelancerProfiles.Where(p=>p.UserAccountId==user.Id));
            db.EarnedSkillBadges.RemoveRange(db.EarnedSkillBadges.Where(b=>b.UserAccountId==user.Id));
            db.SkillExamAttempts.RemoveRange(db.SkillExamAttempts.Where(a=>a.UserAccountId==user.Id));
            db.SkillClaims.RemoveRange(db.SkillClaims.Where(c=>c.UserAccountId==user.Id));
            db.ExternalCertifications.RemoveRange(db.ExternalCertifications.Where(c=>c.UserAccountId==user.Id));
            db.Users.Remove(user);db.SaveChanges();
        }
    }
}
