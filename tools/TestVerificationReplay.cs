using System;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

// Writes only uniquely named, disposable localhost fixtures and removes them in finally.
class TestVerificationReplay
{
    static string email="otp-replay-"+Guid.NewGuid().ToString("N")+"@example.test";
    static int uid, count;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
    static AppDbContext Open(){return new AppDbContext();}
    static void Prepare(string code,int minutes=10,int attempts=0)
    {
        using(var db=Open()){
            db.Database.ExecuteSqlCommand("DELETE FROM RegistrationCodes WHERE UserAccountId=@p0",uid);
            db.Database.ExecuteSqlCommand("UPDATE Users SET EmailVerifiedAtUtc=NULL WHERE Id=@p0 AND Email=@p1",uid,email);
            var salt=PasswordRecoveryService.NewToken();
            db.RegistrationCodes.Add(new RegistrationCode{UserAccountId=uid,Salt=salt,CodeHash=PasswordRecoveryService.HashToken(salt+code),CreatedAtUtc=DateTime.UtcNow.AddMinutes(-20),ExpiresAtUtc=DateTime.UtcNow.AddMinutes(minutes),FailedAttempts=attempts});db.SaveChanges();
        }
    }
    static bool Verify(string code,string role="Client"){using(var db=Open())return new EmailCodeService(db).Verify(email,role,code);}
    static int Main()
    {
        Database.SetInitializer<AppDbContext>(null);
        try {
            using(var db=Open()){
                if(db.Database.Connection.DataSource!=@".\SQLEXPRESS" || db.Database.Connection.Database!="NexusFreelanceMvp")throw new Exception("Local demo database required.");
                var user=new UserAccount{Name="Disposable OTP replay test",Email=email,Role="Client",PasswordHash=PasswordService.Hash(Guid.NewGuid().ToString("N")+"Aa1!"),JoinedAtUtc=DateTime.UtcNow};db.Users.Add(user);db.SaveChanges();uid=user.Id;
            }
            Prepare("172639");
            using(var stale=Open()){
                stale.Users.Single(u=>u.Id==uid);stale.RegistrationCodes.Single(c=>c.UserAccountId==uid);
                Check(Verify("172639"),"first context verifies once");
                Check(!new EmailCodeService(stale).Verify(email,"Client","172639"),"stale tracked context cannot replay a consumed code");
            }
            Prepare("283740");
            using(var gate=new Barrier(2)){
                Func<bool> worker=()=>{gate.SignalAndWait();return Verify("283740");};
                var a=Task.Run(worker);var b=Task.Run(worker);Task.WaitAll(a,b);
                Check((a.Result?1:0)+(b.Result?1:0)==1,"two simultaneous contexts produce exactly one successful confirmation");
            }
            Check(!Verify("283740"),"another machine cannot replay after success");
            Prepare("394851",-1);Check(!Verify("394851"),"expired code rejected");
            Prepare("405962",10,5);Check(!Verify("405962"),"five failed attempts disable even a correct code");
            Prepare("516073");Check(!Verify("516073","Freelancer"),"code is scoped to the selected profile role");
            Check(!Verify("111111"),"wrong code rejected");
            using(var db=Open()){
                var salt=PasswordRecoveryService.NewToken();
                db.RegistrationCodes.Add(new RegistrationCode{UserAccountId=uid,Salt=salt,CodeHash=PasswordRecoveryService.HashToken(salt+"516073"),CreatedAtUtc=DateTime.UtcNow,ExpiresAtUtc=DateTime.UtcNow.AddMinutes(10)});db.SaveChanges();
            }
            Check(Verify("516073"),"valid code remains usable after one failed attempt");
            using(var db=Open())Check(db.RegistrationCodes.Where(c=>c.UserAccountId==uid).All(c=>c.ConsumedAtUtc.HasValue),"successful confirmation consumes remaining codes");
            Console.WriteLine("VERIFICATION_REPLAY_CHECKS="+count);return 0;
        } catch(Exception ex){Console.WriteLine("FAIL "+ex.Message);return 1;}
        finally {
            if(uid>0)using(var db=Open()){
                db.Database.ExecuteSqlCommand("DELETE c FROM RegistrationCodes c JOIN Users u ON u.Id=c.UserAccountId WHERE u.Id=@p0 AND u.Email=@p1",uid,email);
                db.Database.ExecuteSqlCommand("DELETE FROM Users WHERE Id=@p0 AND Email=@p1",uid,email);
            }
            Console.WriteLine("Disposable OTP fixtures cleaned; existing accounts untouched.");
        }
    }
}
