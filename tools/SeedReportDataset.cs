using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;
using NexusFreelance.Web.Data;
using NexusFreelance.Web.Models;
using NexusFreelance.Web.Services;

// Explicit, offline demonstration data. Never contacts Gmail, Google or VNPay.
// The caller must first prepare the isolated candidate; the live DB is rejected.
class SeedReportDataset
{
    static AppDbContext db;
    static DateTime Now { get { return DateTime.UtcNow; } }
    static readonly Dictionary<string,UserAccount> users = new Dictionary<string,UserAccount>();
    static readonly StringBuilder portfolio = new StringBuilder();
    static string password;
    static int Main(string[] args)
    {
        try {
            Database.SetInitializer<AppDbContext>(null);
            password=Environment.GetEnvironmentVariable("NEXUS_DEMO_PASSWORD");
            if(string.IsNullOrEmpty(password) || password.Length<20 || args.Length!=1) throw new InvalidOperationException("A private demo password and generated-asset directory are required.");
            using(db=new AppDbContext()) {
                db.Database.Connection.Open();
                if(!System.Text.RegularExpressions.Regex.IsMatch(db.Database.Connection.Database,@"^NexusFreelanceSubmission20261020$") || db.Users.Any() || db.Projects.Any()) throw new InvalidOperationException("Only the freshly prepared isolated candidate may be seeded.");
                SkillExamSeeder.Seed(db);
                typeof(AppDbContext).Assembly.GetType("NexusFreelance.Web.Data.SkillCatalogSeed").GetMethod("Apply",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{db});
                foreach(var track in db.SkillTracks.Where(t=>!t.Code.StartsWith("NF_")).ToList()) { track.Industry="Công nghệ thông tin";track.IsActive=true; }
                db.SaveChanges();
                Accounts(); GroupsAndProfiles(); Marketplace(); Workflows(); CaseStudy();
                foreach(var u in db.Users.ToList()) if(!db.WalletAccounts.Any(w=>w.UserAccountId==u.Id)) db.WalletAccounts.Add(new WalletAccount {UserAccountId=u.Id});
                db.SaveChanges();
                Directory.CreateDirectory(args[0]);
                File.WriteAllText(Path.Combine(args[0],"portfolio.html"),PortfolioPage(),new UTF8Encoding(false));
                Console.WriteLine("CLEAN_SEED_OK users="+db.Users.Count()+" projects="+db.Projects.Count()+" contracts="+db.Contracts.Count()+" groups="+db.FreelancerGroups.Count()+" portfolio="+db.PortfolioItems.Count());
            }
            return 0;
        } catch(Exception ex) {
            var validation=ex as System.Data.Entity.Validation.DbEntityValidationException;
            if(validation!=null) foreach(var item in validation.EntityValidationErrors) foreach(var error in item.ValidationErrors) Console.Error.WriteLine(item.Entry.Entity.GetType().Name+"."+error.PropertyName+": "+error.ErrorMessage);
            Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);
            return 1;
        } finally { password=null; }
    }
    static UserAccount User(string key,string name,string role)
    {
        var u=new UserAccount {Name=name,Email=key+"@nexus.example.test",Role=role,PasswordHash=PasswordService.Hash(password),JoinedAtUtc=Now.AddMonths(-8),EmailVerifiedAtUtc=Now.AddMonths(-8),PasswordChangedAtUtc=Now};
        db.Users.Add(u);db.SaveChanges();users[key]=u;return u;
    }
    static void Accounts()
    {
        User("admin","Quản trị dữ liệu mẫu","Admin");
        var clients=new[]{new[]{"client.minhan","Nguyễn Minh An","Hương Việt Coffee","Đà Nẵng","Chuỗi cửa hàng cà phê cần chuẩn hóa kênh bán hàng, nội dung và báo cáo vận hành."},new[]{"client.thuha","Trần Thu Hà","An Tâm Care","Hà Nội","Đơn vị dịch vụ chăm sóc sức khỏe cần quy trình đặt lịch rõ ràng và thông tin dễ tiếp cận."},new[]{"client.quanghuy","Lê Quang Huy","Mộc Gia Living","TP. Hồ Chí Minh","Thương hiệu nội thất cần website, hình ảnh sản phẩm và quy trình xử lý đơn hàng đồng bộ."}};
        foreach(var row in clients) {
            var u=User(row[0],row[1],"Client");
            db.ClientProfiles.Add(new ClientProfile {UserAccountId=u.Id,ClientType="Business",CompanyName=row[2],Location=row[3],About=row[4]+" Hồ sơ doanh nghiệp minh họa cho bộ dữ liệu thực hành.",UpdatedAtUtc=Now});db.SaveChanges();
            var wallet=new WalletService(db);wallet.RequestMoney(u.Id,"Deposit",100000000m,"demo-opening-"+row[0],"Vốn minh họa nội bộ để thực hành ký quỹ; không phải giao dịch tiền thật hoặc thanh toán VNPay.");
            var request=db.MoneyRequests.Single(r=>r.UserAccountId==u.Id);
            wallet.ReviewMoney(request.Id,users["admin"].Id,true,"Khởi tạo số dư minh họa cho môi trường localhost, không ghi nhận thanh toán PSP.");
        }
        User("minh.frontend","Hoàng Đức Minh","Freelancer");User("linh.backend","Nguyễn Khánh Linh","Freelancer");User("trang.ux","Phạm Thu Trang","Freelancer");
        User("hai.data","Vũ Thanh Hải","Freelancer");User("phuc.seo","Đỗ Hữu Phúc","Freelancer");User("mai.content","Bùi Ngọc Mai","Freelancer");
        User("an.design","Lê Nhật An","Freelancer");User("vy.translation","Trần Thảo Vy","Freelancer");User("khoa.qa","Nguyễn Đăng Khoa","Freelancer");User("tung.pm","Phan Sơn Tùng","Freelancer");
    }
    static void Profile(string key,string role,string industry,string city,int years,int minimum,string skill,string title,string summary,string outcome)
    {
        var u=users[key];
        db.FreelancerProfiles.Add(new FreelancerProfile {UserAccountId=u.Id,CoreRole=role,Industry=industry,Location=city,YearsExperience=years,MinimumProjectBudget=minimum,IsAvailable=true,IsPublic=true,UpdatedAtUtc=Now,
            Bio="Tập trung vào "+role.ToLowerInvariant()+". Làm rõ yêu cầu trước khi báo giá, cập nhật tiến độ theo tuần và bàn giao kèm hướng dẫn sử dụng. "+summary});
        var item=new PortfolioItem {FreelancerId=u.Id,Title=title,Summary=summary,Role=role,Skills=skill,Outcome=outcome,Url="https://localhost:44391/Content/demo/portfolio.html#"+key,CreatedAtUtc=Now.AddDays(-20)};
        db.PortfolioItems.Add(item);db.SaveChanges();
        var track=db.SkillTracks.Single(t=>t.Code==skill);
        var claim=new SkillClaim {UserAccountId=u.Id,SkillTrackId=track.Id,Level=4,YearsExperience=years};db.SkillClaims.Add(claim);db.SaveChanges();
        var request=new SkillEvidenceRequest {SkillClaimId=claim.Id,Status="Pending",SubmittedAtUtc=Now};db.SkillEvidenceRequests.Add(request);db.SaveChanges();
        db.SkillEvidence.Add(new SkillEvidence {SkillEvidenceRequestId=request.Id,Type="Portfolio",PortfolioItemId=item.Id,Url=item.Url});db.SaveChanges();
        portfolio.Append("<article id=\"").Append(HttpUtility.HtmlAttributeEncode(key)).Append("\"><p class=\"eyebrow\">HỒ SƠ MINH HỌA · ").Append(HttpUtility.HtmlEncode(role)).Append("</p><h2>").Append(HttpUtility.HtmlEncode(title)).Append("</h2><p>").Append(HttpUtility.HtmlEncode(summary)).Append("</p><h3>Phạm vi và cách làm</h3><p>").Append(HttpUtility.HtmlEncode("Khảo sát yêu cầu, lập danh sách đầu việc, thực hiện bản nháp và nhận phản hồi trước khi hoàn thiện. Người phụ trách: "+u.Name+". Kỹ năng trọng tâm: "+track.Title+".")).Append("</p><h3>Kết quả và tiêu chí bàn giao</h3><p>").Append(HttpUtility.HtmlEncode(outcome)).Append("</p></article>");
    }
    static void GroupsAndProfiles()
    {
        Profile("minh.frontend","Frontend Developer","Công nghệ thông tin","Đà Nẵng",5,8000000,"FRONTEND_WEB","Giao diện đặt lịch cho dịch vụ chăm sóc sức khỏe","Thiết kế và triển khai các màn hình chọn dịch vụ, khung giờ và xác nhận đặt lịch trên máy tính và điện thoại.","Bàn giao giao diện responsive, trạng thái lỗi rõ ràng, hỗ trợ bàn phím và hướng dẫn cấu hình API.");
        Profile("linh.backend","Backend Developer","Công nghệ thông tin","Hà Nội",6,12000000,"BACKEND_DOTNET","API quản lý đặt lịch và phân quyền","Xây dựng API quản lý dịch vụ, lịch hẹn, người dùng và phân quyền theo vai trò với C# và SQL Server.","Bàn giao tài liệu API, kiểm thử phân quyền, giao dịch dữ liệu và hướng dẫn triển khai.");
        Profile("trang.ux","UI/UX Designer","Thiết kế & sáng tạo","Hà Nội",4,6000000,"NF_UX_DESIGN","Thiết kế trải nghiệm đặt lịch trong ba bước","Lập sơ đồ hành trình, wireframe và bộ giao diện cho khách hàng đặt lịch khám hoặc tư vấn.","Bộ màn hình có trạng thái trống, lỗi, tải dữ liệu và nguyên tắc sử dụng thành phần thống nhất.");
        Profile("hai.data","Data Analyst","Dữ liệu & AI","TP. Hồ Chí Minh",5,10000000,"NF_DATA_ANALYSIS","Báo cáo doanh thu và tồn kho bán lẻ","Làm sạch dữ liệu bán hàng, thống nhất mã sản phẩm và xây dựng bộ chỉ số theo ngày, chi nhánh và nhóm hàng.","Có bảng định nghĩa chỉ số, truy vấn SQL và các bước đối chiếu số liệu gốc với báo cáo.");
        Profile("phuc.seo","SEO Specialist","Marketing & truyền thông","Đà Nẵng",5,7000000,"NF_SEO","Kế hoạch SEO cho thương hiệu nội thất","Kiểm tra cấu trúc website, phân nhóm từ khóa và lên kế hoạch cải thiện các trang sản phẩm.","Bàn giao báo cáo kỹ thuật, bản đồ từ khóa và tiêu chí đo lường; không cam kết thứ hạng thiếu căn cứ.");
        Profile("mai.content","Content Writer","Nội dung & biên dịch","Hà Nội",4,4000000,"NF_CONTENT_WRITING","Bộ nội dung giới thiệu sản phẩm cà phê","Xây dựng giọng điệu thương hiệu, cấu trúc bài viết và nội dung giới thiệu từng dòng cà phê.","Bàn giao bài viết gốc, bảng thông điệp và lịch biên tập; chỉ số kết quả cần được đo sau khi xuất bản.");
        Profile("an.design","Graphic Designer","Thiết kế & sáng tạo","TP. Hồ Chí Minh",6,6000000,"NF_GRAPHIC_DESIGN","Bộ nhận diện quán cà phê địa phương","Phát triển logo, màu sắc, hệ chữ và bộ ứng dụng trên bao bì, thực đơn, bài đăng mạng xã hội.","Bàn giao tệp vector, bảng màu, quy tắc khoảng cách và tài liệu sử dụng nhận diện.");
        Profile("vy.translation","Biên dịch Anh – Việt","Nội dung & biên dịch","Huế",7,4000000,"NF_TRANSLATION","Bản địa hóa danh mục nội thất Anh – Việt","Dịch mô tả sản phẩm, thống nhất thuật ngữ vật liệu và hiệu đính theo ngữ cảnh bán lẻ.","Bàn giao bản dịch song ngữ, bảng thuật ngữ và danh sách câu hỏi cần khách hàng xác nhận.");
        Profile("khoa.qa","QA Engineer","Công nghệ thông tin","Đà Nẵng",4,6000000,"DATABASE_SQL","Kế hoạch kiểm thử luồng mua hàng","Lập test case cho giỏ hàng, đặt hàng, phân quyền và đối chiếu dữ liệu giữa giao diện với hệ thống.","Bàn giao bộ test case, báo cáo lỗi có bước tái hiện và kết quả kiểm thử hồi quy.");
        Profile("tung.pm","Project Manager","Kinh doanh & tư vấn","Hà Nội",8,10000000,"NF_PROJECT_MANAGEMENT","Kế hoạch triển khai sản phẩm theo từng mốc","Làm rõ phạm vi, phụ thuộc, trách nhiệm thành viên và kế hoạch nghiệm thu theo tuần.","Bàn giao lịch trình, sổ rủi ro, biên bản quyết định và biểu mẫu theo dõi thay đổi phạm vi.");
        Group("Sen Digital Studio","Nhóm phát triển sản phẩm web gồm giao diện, backend và thiết kế trải nghiệm. Chốt phạm vi và tiêu chí nghiệm thu trước khi triển khai.","minh.frontend","linh.backend","trang.ux");
        Group("Mộc Creative","Nhóm nội dung và nhận diện thương hiệu. Phối hợp SEO, biên tập và thiết kế để tạo bộ tài liệu nhất quán.","phuc.seo","mai.content","an.design");
        Group("DataCraft Analytics","Nhóm phân tích dữ liệu, kiểm thử và điều phối triển khai dashboard vận hành.","hai.data","khoa.qa","tung.pm");
    }
    static void Group(string name,string description,params string[] members)
    {
        var g=new FreelancerGroup {Name=name,Description=description,LeaderId=users[members[0]].Id,IsPublic=true,CreatedAtUtc=Now.AddMonths(-3)};db.FreelancerGroups.Add(g);db.SaveChanges();
        foreach(var key in members) db.FreelancerGroupMembers.Add(new FreelancerGroupMember {FreelancerGroupId=g.Id,UserAccountId=users[key].Id,Status="Active",InvitedAtUtc=Now.AddMonths(-3),RespondedAtUtc=Now.AddMonths(-3).AddHours(1)});
        db.FreelancerGroupEvents.Add(new FreelancerGroupEvent {FreelancerGroupId=g.Id,ActorId=g.LeaderId,Description="Hoàn tất đội hình và thống nhất cách cập nhật tiến độ, bàn giao và phân chia công việc.",CreatedAtUtc=Now.AddMonths(-3).AddDays(1)});db.SaveChanges();
    }
    static Project Project(string client,string title,string type,string industry,decimal budget,int days,string scope,params string[] roles)
    {
        var p=new Project {ClientId=users[client].Id,Title=title,ProjectType=type,Industry=industry,Budget=budget,PlatformFeeRate=0.10m,Status="Published",PublishedAtUtc=Now.AddDays(-3),CreatedAtUtc=Now.AddDays(-4),DeadlineUtc=Now.AddDays(days),Description=scope+"\n\nCách phối hợp: thống nhất phạm vi qua đề xuất, xác nhận hợp đồng và ký quỹ từng mốc trước khi thực hiện. Cập nhật tiến độ hai lần mỗi tuần. Bàn giao kèm tài liệu, tệp nguồn và hướng dẫn sử dụng trong phạm vi đã chốt."};
        db.Projects.Add(p);db.SaveChanges();foreach(var role in roles) db.RoleRequirements.Add(new RoleRequirement {ProjectId=p.Id,Title=role,RequiredCount=1});
        db.ActivityLogs.Add(new ActivityLog {ProjectId=p.Id,ActorId=p.ClientId,Action="Đăng dự án với ngân sách, phạm vi và yêu cầu bàn giao rõ ràng",CreatedAtUtc=p.CreatedAtUtc});db.SaveChanges();return p;
    }
    static string Plan(decimal total,int days)
    {
        var first=decimal.Floor(total*0.4m);
        return new JavaScriptSerializer().Serialize(new[]{new PaymentPlan.Row {Title="Thống nhất phương án và bàn giao bản đầu",Amount=first,DueDay=Math.Max(1,days/2),AcceptanceCriteria="Hoàn tất danh sách yêu cầu, bản nháp có thể kiểm tra và ghi nhận phản hồi bằng văn bản."},new PaymentPlan.Row {Title="Hoàn thiện và bàn giao phạm vi còn lại",Amount=total-first,DueDay=days,AcceptanceCriteria="Sản phẩm đáp ứng tiêu chí trong hợp đồng; bàn giao tệp nguồn, tài liệu sử dụng và danh sách các mục đã đối chiếu."}});
    }
    static Proposal Offer(Project p,string worker,int roleIndex,decimal amount,int days,string pitch,int? bid=null,string sharedPlan=null)
    {
        var role=db.RoleRequirements.Where(r=>r.ProjectId==p.Id).OrderBy(r=>r.Id).ToList()[roleIndex];
        var q=new Proposal {ProjectId=p.Id,RoleRequirementId=role.Id,ApplicantId=users[worker].Id,QuotedPrice=amount,DeliveryDays=days,MilestonePlan=sharedPlan??Plan(amount,days),Pitch=pitch,Status="Pending",GroupBidId=bid,CreatedAtUtc=Now.AddDays(-2)};
        db.Proposals.Add(q);db.SaveChanges();return q;
    }
    static void Marketplace()
    {
        var p=Project("client.minhan","Landing page giới thiệu cà phê rang mộc","SOLO","Công nghệ thông tin",18000000,21,"Cần landing page giới thiệu ba dòng cà phê, câu chuyện thương hiệu, bảng giá và biểu mẫu liên hệ. Ưu tiên tốc độ tải trên điện thoại, nội dung dễ đọc và có hướng dẫn tự cập nhật.","Frontend Developer");
        var q=Offer(p,"minh.frontend",0,16500000,18,"Tôi đề xuất triển khai giao diện theo hai mốc, kiểm tra trên điện thoại và bàn giao hướng dẫn cập nhật nội dung. Phạm vi gồm năm khu vực nội dung và một biểu mẫu liên hệ.");
        new ProjectWorkflowService(db).Shortlist(p.Id,q.Id,p.ClientId);
        db.NegotiationMessages.Add(new NegotiationMessage {ProposalId=q.Id,SenderId=p.ClientId,Body="Ngân sách đã gồm kiểm tra trên điện thoại chưa? Tôi sẽ cung cấp ảnh và nội dung trước khi chốt bản thiết kế.",CreatedAtUtc=Now.AddDays(-1)});
        db.NegotiationMessages.Add(new NegotiationMessage {ProposalId=q.Id,SenderId=q.ApplicantId,Body="Giá đã gồm kiểm tra responsive và hai vòng chỉnh sửa trong phạm vi. Có thể bắt đầu khi nhận đủ nội dung và hoàn tất ký quỹ mốc đầu.",CreatedAtUtc=Now.AddDays(-1).AddHours(1)});db.SaveChanges();
        p=Project("client.minhan","Thiết kế bộ nhận diện cho Hương Việt Coffee","SOLO","Thiết kế & sáng tạo",12000000,18,"Thiết kế logo, bảng màu, hệ chữ, tem túi cà phê và mẫu thực đơn. Bàn giao tệp vector cùng hướng dẫn sử dụng; nội dung thương hiệu do khách hàng cung cấp.","Graphic Designer");Offer(p,"an.design",0,11000000,16,"Tôi sẽ khảo sát đối tượng sử dụng, trình bày hai hướng nhận diện và hoàn thiện một phương án được chọn. Bàn giao gồm tệp nguồn và quy tắc sử dụng.");
        p=Project("client.quanghuy","Biên dịch danh mục nội thất Anh – Việt","SOLO","Nội dung & biên dịch",8000000,12,"Biên dịch khoảng 8.000 từ mô tả nội thất. Cần thống nhất thuật ngữ gỗ, bề mặt và kích thước; bàn giao bản song ngữ và bảng thuật ngữ để sử dụng về sau.","Biên dịch Anh – Việt");Offer(p,"vy.translation",0,7600000,10,"Tôi đề xuất thống nhất bảng thuật ngữ trước, dịch theo nhóm sản phẩm và hiệu đính toàn bộ tài liệu trước khi bàn giao.");
        Project("client.thuha","Website đặt lịch tư vấn An Tâm Care","TEAM","Công nghệ thông tin",65000000,42,"Xây dựng website để khách hàng xem dịch vụ, chọn lịch trống và nhận xác nhận. Quản trị có thể cập nhật dịch vụ, theo dõi lịch và xuất báo cáo. Không xử lý hồ sơ bệnh án trong phạm vi này.","Frontend Developer","Backend Developer","UI/UX Designer");
        p=Project("client.quanghuy","Bộ nội dung và kế hoạch SEO cho Mộc Gia Living","TEAM","Marketing & truyền thông",32000000,30,"Lập bản đồ từ khóa, viết 12 bài giới thiệu vật liệu và thiết kế 12 ảnh minh họa. Nội dung cần có nguồn cho thông tin kỹ thuật và tránh cam kết thứ hạng tìm kiếm.","SEO Specialist","Content Writer","Graphic Designer");
        Bid(p,"Mộc Creative",new[]{"phuc.seo","mai.content","an.design"},new[]{10000000m,12000000m,8000000m},28,false);
        Project("client.quanghuy","Dashboard tồn kho và doanh thu cho chuỗi bán lẻ","ENTERPRISE","Dữ liệu & AI",95000000,50,"Chuẩn hóa dữ liệu đơn hàng và tồn kho từ hai nguồn, xây bảng chỉ số theo chi nhánh và nhóm sản phẩm. Cần truy vấn có thể kiểm tra lại, tài liệu định nghĩa chỉ số và quy trình cập nhật dữ liệu.","Data Analyst","Backend Developer","QA Engineer");
        p=Project("client.minhan","Chuẩn hóa quy trình vận hành cửa hàng","SOLO","Kinh doanh & tư vấn",15000000,24,"Khảo sát quy trình mở ca, kiểm kê và bàn giao ca; đề xuất biểu mẫu và phân công trách nhiệm để người quản lý theo dõi nhất quán.","Project Manager");p.Status="Paused";db.SaveChanges();
        p=Project("client.thuha","Báo cáo lịch hẹn và chất lượng phục vụ","SOLO","Dữ liệu & AI",45000000,35,"Tổng hợp số lịch hẹn, tỷ lệ khách đến và nhu cầu theo khung giờ. Phạm vi giới hạn ở số liệu vận hành đã ẩn danh; đối chiếu số tổng trước khi bàn giao.","Data Analyst");p.Status="Draft";
        var draft=new AIJobDraft {ClientId=p.ClientId,Idea="Cần báo cáo vận hành lịch hẹn dễ đọc cho quản lý cơ sở.",Industry=p.Industry,ProjectType=p.ProjectType,Budget=p.Budget,DeadlineUtc=p.DeadlineUtc,CreatedAtUtc=Now.AddDays(-2),AppliedProjectId=p.Id};db.AIJobDrafts.Add(draft);db.SaveChanges();
        var version=new AIJobDraftVersion {AIJobDraftId=draft.Id,VersionNumber=1,Title=p.Title,Description=p.Description,Objectives="Theo dõi nhu cầu và điều phối khung giờ dựa trên số liệu vận hành.",Scope="Làm sạch dữ liệu lịch hẹn đã ẩn danh và xây báo cáo theo ngày, dịch vụ, cơ sở.",UserStories="Quản lý xem được số lịch hẹn và tỷ lệ khách đến để bố trí nhân sự.",RequiredSkills="SQL, làm sạch dữ liệu, thiết kế dashboard",Deliverables="Báo cáo, bảng định nghĩa chỉ số, hướng dẫn cập nhật và đối chiếu số liệu.",Milestones="Thống nhất chỉ số; bàn giao báo cáo và hướng dẫn.",Generator="Manual",CreatedAtUtc=draft.CreatedAtUtc};db.AIJobDraftVersions.Add(version);db.SaveChanges();db.AIJobRoleSuggestions.Add(new AIJobRoleSuggestion {AIJobDraftVersionId=version.Id,Title="Data Analyst",RequiredCount=1});db.SaveChanges();
    }
    static ProjectContract Solo(Project p,string worker,int role,decimal amount,int days)
    {
        var offer=Offer(p,worker,role,amount,days,"Tôi đã rà soát phạm vi và đề xuất hai mốc có tiêu chí nghiệm thu riêng. Báo giá gồm phần việc được giao, tài liệu bàn giao và các vòng phản hồi trong phạm vi hợp đồng.");
        new ProjectWorkflowService(db).AcceptProposal(p.Id,offer.Id,p.ClientId);
        return db.Contracts.Single(c=>c.ProposalId==offer.Id);
    }
    static ProjectContract Bid(Project p,string groupName,string[] workers,decimal[] amounts,int days,bool accept)
    {
        var g=db.FreelancerGroups.Single(x=>x.Name==groupName);
        var plan=Plan(amounts.Sum(),days);
        var b=new GroupBid {ProjectId=p.Id,FreelancerGroupId=g.Id,LeaderId=g.LeaderId,DeliveryDays=days,MilestonePlan=plan,Pitch="Nhóm đề xuất phân việc theo từng vai trò, cập nhật tiến độ tập trung và bàn giao theo hai mốc có tiêu chí rõ ràng. Trưởng nhóm điều phối phản hồi; mỗi thành viên xác nhận phạm vi và tỷ trọng trước khi ký quỹ.",Status="Pending",CreatedAtUtc=Now.AddDays(-2)};db.GroupBids.Add(b);db.SaveChanges();
        for(int i=0;i<workers.Length;i++) Offer(p,workers[i],i,amounts[i],days,b.Pitch,b.Id,plan);
        if(!accept)return null;
        new GroupBidService(db).Decide(b.Id,p.ClientId,true);return db.Contracts.Single(c=>c.GroupBidId==b.Id);
    }
    static void Sign(ProjectContract c,bool everyone)
    {
        var service=new ContractService(db);var v=service.Current(c);service.Confirm(c.Id,c.Project.ClientId,v.Id,v.Hash);
        if(everyone) foreach(var id in db.ContractMembers.Where(m=>m.ContractId==c.Id).Select(m=>m.UserAccountId).ToList())service.Confirm(c.Id,id,v.Id,v.Hash);
    }
    static void Workflows()
    {
        var p=Project("client.minhan","Tích hợp quản lý đơn hàng cho Hương Việt Coffee","SOLO","Công nghệ thông tin",24000000,28,"Triển khai API quản lý đơn và trạng thái giao hàng, phân quyền nhân viên và xuất báo cáo. Cần kiểm thử giao dịch SQL và tài liệu cấu hình.","Backend Developer");
        var c=Solo(p,"linh.backend",0,p.Budget,28);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(c,false);
        p=Project("client.quanghuy","Cổng bán hàng B2B Mộc Việt — hợp đồng độc lập","ENTERPRISE","Công nghệ thông tin",80000000,45,"Phát triển giao diện đặt hàng và API xử lý đơn theo hai hợp đồng độc lập. Hai phạm vi có tài liệu API chung; mỗi hợp đồng có mốc, xác nhận và ký quỹ riêng.","Frontend Developer","Backend Developer");
        var front=Solo(p,"minh.frontend",0,35000000,40);var back=Solo(p,"linh.backend",1,45000000,45);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(front,false);Sign(back,true);
        p=Project("client.thuha","Cổng đặt lịch An Nhiên — triển khai theo đội hình","TEAM","Công nghệ thông tin",72000000,42,"Nhóm thiết kế và phát triển quy trình đặt lịch ba bước, khu vực quản trị lịch trống và thông báo xác nhận. Cần thống nhất các trạng thái lỗi và phân quyền trước khi bàn giao.","Frontend Developer","Backend Developer","UI/UX Designer");
        c=Bid(p,"Sen Digital Studio",new[]{"minh.frontend","linh.backend","trang.ux"},new[]{24000000m,28000000m,20000000m},42,true);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(c,true);
        new WalletService(db).Fund(db.EscrowMilestones.Where(m=>m.ContractId==c.Id).OrderBy(m=>m.Id).First().Id,p.ClientId);WorkspaceContent(c,false);
        p=Project("client.minhan","Dashboard doanh thu cửa hàng — chờ nghiệm thu bản đầu","SOLO","Dữ liệu & AI",20000000,20,"Tổng hợp doanh thu theo ngày, sản phẩm và chi nhánh. Bản đầu gồm dữ liệu mẫu đã đối chiếu, bảng định nghĩa chỉ số và giao diện báo cáo.","Data Analyst");
        c=Solo(p,"hai.data",0,p.Budget,20);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(c,true);WorkspaceContent(c,false);Deliver(c,0,false);
        p=Project("client.quanghuy","Danh mục sản phẩm nội thất Mộc Việt — đã bàn giao","SOLO","Công nghệ thông tin",16000000,21,"Xây dựng danh mục sản phẩm có bộ lọc vật liệu, kích thước và không gian sử dụng. Bàn giao giao diện responsive, hướng dẫn cập nhật và tài liệu triển khai.","Frontend Developer");
        c=Solo(p,"minh.frontend",0,p.Budget,21);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(c,true);WorkspaceContent(c,true);Deliver(c,0,true);Deliver(c,1,true);
        new ProjectWorkflowService(db).AddReview(p.Id,users["minh.frontend"].Id,p.ClientId,5,"Bàn giao đúng phạm vi, có tài liệu sử dụng và xử lý phản hồi rõ ràng. Các mốc đã được đối chiếu trước khi nghiệm thu.");
        db.ClientReviews.Add(new ClientReview {ProjectId=p.Id,FreelancerId=users["minh.frontend"].Id,ClientId=p.ClientId,Rating=5,Comment="Phạm vi rõ ràng, phản hồi tập trung và xác nhận bàn giao theo từng mốc.",CreatedAtUtc=Now});
        db.PortfolioItems.Add(new PortfolioItem {FreelancerId=users["minh.frontend"].Id,ProjectId=p.Id,Title="Danh mục nội thất Mộc Việt đã nghiệm thu",Summary="Phát triển giao diện danh mục có bộ lọc và hướng dẫn cập nhật. Hồ sơ này liên kết trực tiếp với dự án mẫu đã hoàn tất.",Role="Frontend Developer",Skills="HTML, CSS, JavaScript, responsive",Outcome="Hai mốc được nghiệm thu; tệp bàn giao và đánh giá có thể đối chiếu trong không gian hợp đồng.",Url="https://localhost:44391/Content/demo/portfolio.html#minh.frontend",CreatedAtUtc=Now});
        db.ProjectMemories.Add(new ProjectMemory {ProjectId=p.Id,EditorId=p.ClientId,Version=1,Source="Manual",ExecutiveSummary="Hoàn thành danh mục sản phẩm và bàn giao hướng dẫn quản trị cho Mộc Gia Living.",TechStack="HTML, CSS, JavaScript, tài liệu triển khai",ChallengesAndSolutions="Thống nhất cách ghi vật liệu và kích thước trước khi nhập dữ liệu để tránh bộ lọc không nhất quán.",LessonsLearned="Chốt danh sách trường dữ liệu và tiêu chí bộ lọc trước khi dựng giao diện.",ReusableAssets="Biểu mẫu kiểm tra nội dung và checklist nghiệm thu responsive.",PortfolioDraft="Có thể giới thiệu phạm vi đã hoàn thành sau khi được khách hàng đồng ý công khai.",CreatedAtUtc=Now});db.SaveChanges();
        new WalletService(db).RequestMoney(users["minh.frontend"].Id,"Withdrawal",2000000,"demo-withdraw-minh","Yêu cầu rút số dư minh họa sau dự án đã hoàn tất; chờ quản trị đối soát.");
        p=Project("client.quanghuy","Rà soát SEO danh mục vật liệu — cần hòa giải phạm vi","SOLO","Marketing & truyền thông",9000000,16,"Rà soát cấu trúc trang và lập bản đồ từ khóa cho danh mục vật liệu. Hai bên cần thống nhất việc viết lại mô tả sản phẩm có thuộc phạm vi ban đầu hay không.","SEO Specialist");
        c=Solo(p,"phuc.seo",0,p.Budget,16);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(p.Id,p.ClientId);Sign(c,true);new WalletService(db).Fund(db.EscrowMilestones.Where(m=>m.ContractId==c.Id).OrderBy(m=>m.Id).First().Id,p.ClientId);
        new WalletService(db).Dispute(p.Id,p.ClientId,"Hai bên cần làm rõ phạm vi viết lại mô tả sản phẩm so với báo cáo rà soát SEO đã thống nhất; đề nghị quản trị hỗ trợ đối chiếu điều khoản.",c.Id);
    }
    static void WorkspaceContent(ProjectContract c,bool complete)
    {
        var w=db.Workspaces.Single(x=>x.ContractId==c.Id);
        var members=db.ContractMembers.Where(m=>m.ContractId==c.Id).OrderBy(m=>m.Id).ToList();
        var titles=new[]{"Rà soát phạm vi và thống nhất tiêu chí nghiệm thu","Thực hiện bản nháp theo vai trò đã phân công","Đối chiếu kết quả và hoàn thiện tài liệu bàn giao"};
        for(int i=0;i<titles.Length;i++) {
            var task=new WorkTask {WorkspaceId=w.Id,AssigneeId=members[i%members.Count].UserAccountId,Title=titles[i],Status="ToDo",Priority=i==1?"High":"Normal",DueAtUtc=Now.AddDays(3+i*3)};db.Tasks.Add(task);db.SaveChanges();
            db.TaskHistories.Add(new TaskHistory {WorkTaskId=task.Id,ActorId=c.CounterpartyId,EventType="Created",NewStatus="ToDo",CreatedAtUtc=Now});db.SaveChanges();
            var target=complete?3:i;
            var transitions=new[]{"Doing","Review","Done"};
            for(int step=0;step<target;step++) {var old=task.Status;task.Status=transitions[step];db.TaskHistories.Add(new TaskHistory {WorkTaskId=task.Id,ActorId=task.AssigneeId,EventType="StatusChanged",OldStatus=old,NewStatus=task.Status,CreatedAtUtc=Now});db.SaveChanges();}
        }
        db.WorkspaceMessages.Add(new WorkspaceMessage {WorkspaceId=w.Id,AuthorId=c.Project.ClientId,Body="Nhờ các bạn cập nhật tiến độ theo từng đầu việc. Khi bàn giao cần nêu rõ phạm vi, tệp đính kèm và các tiêu chí đã kiểm tra.",CreatedAtUtc=Now});
        db.WorkspaceMessages.Add(new WorkspaceMessage {WorkspaceId=w.Id,AuthorId=c.CounterpartyId,Body="Đã tiếp nhận. Tôi sẽ tổng hợp phản hồi vào một danh sách để theo dõi thay đổi và gửi bản bàn giao kèm checklist đối chiếu.",CreatedAtUtc=Now});
        db.ProjectMilestones.Add(new ProjectMilestone {WorkspaceId=w.Id,Title="Rà soát bản đầu cùng khách hàng",Status=complete?"Completed":"Planned",CreatedAtUtc=Now,DueAtUtc=Now.AddDays(5),CompletedAtUtc=complete?(DateTime?)Now:null});
        if(!complete) db.WorkspaceMeetings.Add(new WorkspaceMeeting {WorkspaceId=w.Id,Title="Trao đổi tiến độ — tạo phòng họp khi bắt đầu",Url="https://meet.google.com/landing",StartsAtUtc=Now.AddDays(2),CreatedAtUtc=Now});
        db.SaveChanges();
    }
    static void Deliver(ProjectContract c,int index,bool approve)
    {
        var m=db.EscrowMilestones.Where(x=>x.ContractId==c.Id).OrderBy(x=>x.Id).ToList()[index];
        var wallet=new WalletService(db);
        if(m.Status=="Planned")wallet.Fund(m.Id,c.Project.ClientId);
        var w=db.Workspaces.Single(x=>x.ContractId==c.Id);
        var bytes=Encoding.UTF8.GetBytes("# Biên bản bàn giao\n\nDự án: "+c.Project.Title+"\nMốc: "+m.Title+"\n\n## Phạm vi\n"+c.Project.Description+"\n\n## Đối chiếu\n"+m.AcceptanceCriteria+"\n\n## Cách nghiệm thu\nKhách hàng kiểm tra tệp, ghi nhận phản hồi và xác nhận đúng mốc trên website. Đây là tài liệu mẫu cho môi trường localhost.\n");
        var file=new WorkspaceFile {WorkspaceId=w.Id,CreatedById=c.CounterpartyId,CreatedAtUtc=Now};db.WorkspaceFiles.Add(file);db.SaveChanges();string hash;
        using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
        var version=new WorkspaceFileVersion {WorkspaceFileId=file.Id,VersionNumber=1,UploadedById=c.CounterpartyId,FileName="bien-ban-ban-giao-moc-"+(index+1)+".txt",ContentType="text/plain",Sha256=hash,ByteLength=bytes.Length,Data=bytes,CreatedAtUtc=Now};db.WorkspaceFileVersions.Add(version);db.SaveChanges();
        using(var tx=db.Database.BeginTransaction(IsolationLevel.ReadCommitted)) {
            wallet.SubmissionMilestone(c.ProjectId,m.Id,false,c.Id);
            db.Deliverables.Add(new Deliverable {WorkspaceId=w.Id,SubmittedById=c.CounterpartyId,EscrowMilestoneId=m.Id,Title=m.Title+" — tài liệu bàn giao",Url="https://localhost:44391/WorkspaceFiles/Download/"+version.Id,Status=approve?"Approved":"Submitted",CreatedAtUtc=Now});db.SaveChanges();
            if(approve)wallet.ReleaseUnderLock(m.Id);
            db.SaveChanges();tx.Commit();
        }
    }

    static void CaseStudy()
    {
        // Purposeful fixtures, not imported production identities or real credentials.
        foreach(var claim in db.SkillClaims.ToList()) {
            var request=db.SkillEvidenceRequests.Single(r=>r.SkillClaimId==claim.Id);
            if(claim.UserAccountId!=users["khoa.qa"].Id)
                new SkillEvidenceService(db).Review(request.Id,users["admin"].Id,true,"Dữ liệu minh họa đã đối chiếu portfolio nội bộ để thử luồng ghép nối, không xác nhận năng lực ngoài thực tế.");
            var profile=db.FreelancerProfiles.Single(x=>x.UserAccountId==claim.UserAccountId);
            if(claim.UserAccountId!=users["khoa.qa"].Id)profile.CoreSkillTrackId=claim.SkillTrackId;
        }
        db.SaveChanges();
        var roleSkills=new Dictionary<string,string>{{"Frontend Developer","FRONTEND_WEB"},{"Backend Developer","BACKEND_DOTNET"},{"UI/UX Designer","NF_UX_DESIGN"},{"Data Analyst","NF_DATA_ANALYSIS"},{"SEO Specialist","NF_SEO"},{"Content Writer","NF_CONTENT_WRITING"},{"Graphic Designer","NF_GRAPHIC_DESIGN"},{"Biên dịch Anh – Việt","NF_TRANSLATION"},{"QA Engineer","DATABASE_SQL"},{"Project Manager","NF_PROJECT_MANAGEMENT"}};
        foreach(var role in db.RoleRequirements.ToList()) {
            string code;if(!roleSkills.TryGetValue(role.Title,out code))continue;
            var track=db.SkillTracks.Single(t=>t.Code==code);
            if(!db.ProjectSkillRequirements.Any(x=>x.ProjectId==role.ProjectId && x.SkillTrackId==track.Id))db.ProjectSkillRequirements.Add(new ProjectSkillRequirement {ProjectId=role.ProjectId,SkillTrackId=track.Id,MinimumLevel=3});
        }
        db.SaveChanges();
        string[][] briefs={
          new[]{"Website đặt bàn nhà hàng chay","Công nghệ thông tin","Frontend Developer","Cần xem thực đơn, chọn bàn theo khung giờ, xác nhận và theo dõi trạng thái đặt bàn. Có kiểm tra số khách và hiển thị tốt trên điện thoại."},
          new[]{"API quản lý kho nguyên liệu","Công nghệ thông tin","Backend Developer","Quản lý nhập xuất theo lô, hạn dùng và cảnh báo tồn kho thấp. Cần phân quyền thủ kho và người duyệt, giao dịch SQL nhất quán."},
          new[]{"Thiết kế ứng dụng đặt lịch thú y","Thiết kế & sáng tạo","UI/UX Designer","Khảo sát hành trình chủ thú cưng, thiết kế chọn dịch vụ và lịch trống. Bàn giao wireframe, prototype và trạng thái lỗi."},
          new[]{"Phân tích tỷ lệ quay lại cửa hàng","Dữ liệu & AI","Data Analyst","Làm sạch dữ liệu bán hàng ẩn danh, thống nhất khái niệm khách quay lại, lập dashboard và hướng dẫn đối chiếu chỉ số."},
          new[]{"SEO website du lịch cộng đồng","Marketing & truyền thông","SEO Specialist","Rà soát cấu trúc trang, phân nhóm từ khóa theo địa điểm và nhu cầu, đề xuất lịch nội dung. Không cam kết thứ hạng thiếu căn cứ."},
          new[]{"Nội dung giới thiệu sản phẩm gốm","Nội dung & biên dịch","Content Writer","Viết 20 mô tả sản phẩm gốm, thống nhất giọng điệu và quy tắc kích thước, vật liệu, cách chăm sóc. Có hai vòng hiệu đính."},
          new[]{"Bộ nhận diện tiệm bánh thủ công","Thiết kế & sáng tạo","Graphic Designer","Phát triển logo, bảng màu, tem hộp và thực đơn. Bàn giao tệp vector, font có quyền sử dụng và hướng dẫn nhận diện."},
          new[]{"Biên dịch hướng dẫn sử dụng thiết bị","Nội dung & biên dịch","Biên dịch Anh – Việt","Dịch 6000 từ hướng dẫn sử dụng, lập bảng thuật ngữ và đánh dấu câu cần xác nhận kỹ thuật. Bàn giao song ngữ có hiệu đính."},
          new[]{"Điều phối số hóa quy trình bảo trì","Kinh doanh & tư vấn","Project Manager","Lập kế hoạch theo giai đoạn, ma trận trách nhiệm, sổ rủi ro và tiêu chí nghiệm thu cho quy trình yêu cầu bảo trì."},
          new[]{"Giao diện bán vé workshop","Công nghệ thông tin","Frontend Developer","Hiển thị lịch workshop, số chỗ, thông tin giảng viên và biểu mẫu đăng ký. Cần trạng thái hết chỗ, lỗi và xác nhận rõ ràng."},
          new[]{"API lịch thuê phòng học","Công nghệ thông tin","Backend Developer","Thiết kế API chống trùng giờ, phân quyền quản lý cơ sở và nhật ký chỉnh lịch. Bàn giao kiểm thử cạnh tranh và tài liệu API."},
          new[]{"Dashboard theo dõi chi phí vận hành","Dữ liệu & AI","Data Analyst","Tổng hợp chi phí theo tháng và đơn vị, kiểm tra khoản trùng và thiết kế chỉ số biến động. Dữ liệu đầu vào là bản ẩn danh."}
        };
        for(int i=0;i<briefs.Length;i++) {
            var b=briefs[i];var p=Project(i%2==0?"client.minhan":"client.thuha",b[0],"SOLO",b[1],10000000m+i*1000000m,18+i,b[3],b[2]);
            var code=roleSkills[b[2]];var track=db.SkillTracks.Single(t=>t.Code==code);db.ProjectSkillRequirements.Add(new ProjectSkillRequirement {ProjectId=p.Id,SkillTrackId=track.Id,MinimumLevel=3});
            p.PublishedAtUtc=Now.AddHours(-12-i*3);db.SaveChanges();
        }
        // Ready-to-execute cancellation fixture, all parties already signed, first milestone held.
        var cancel=Project("client.thuha","Tối ưu biểu mẫu đặt lịch — thỏa thuận dừng triển khai","SOLO","Công nghệ thông tin",10000000,15,"Hai bên cần thực hành yêu cầu chấm dứt, xác nhận và hoàn phần tiền chưa sử dụng. Không tự động tất toán; người kiểm thử thao tác trên hợp đồng.","Frontend Developer");
        var contract=Solo(cancel,"minh.frontend",0,cancel.Budget,15);new ProjectWorkflowService(db).LockTeamAndCreateWorkspace(cancel.Id,cancel.ClientId);Sign(contract,true);
        new WalletService(db).Fund(db.EscrowMilestones.Where(m=>m.ContractId==contract.Id).OrderBy(m=>m.Id).First().Id,cancel.ClientId);
        // Manual authored example is explicitly marked Manual; never presented as an AI response.
        var complete=db.Projects.Single(p=>p.Status=="Completed");
        db.SharedKnowledgeEntries.Add(new SharedKnowledgeEntry {ProjectId=complete.Id,AuthorId=complete.ClientId,Version=1,Title="Checklist danh mục sản phẩm có bộ lọc",Industry=complete.Industry,Summary="Kinh nghiệm tổng quát từ dự án mẫu, chủ dự án đồng ý chia sẻ cho việc tham khảo JD.",Practices="Thống nhất tên trường vật liệu, kích thước và mã sản phẩm trước khi nhập. Thiết kế trạng thái không có kết quả và nút xóa bộ lọc. Kiểm tra bàn phím và điện thoại trước bàn giao.",AcceptanceChecklist="Bộ lọc kết hợp cho kết quả đúng; xóa lọc về danh sách ban đầu; không tràn ngang ở 320px; có hướng dẫn cập nhật dữ liệu.",SkillTags="Frontend, responsive, bộ lọc, kiểm thử",Status="Published",CreatedAtUtc=Now});
        db.ExternalCertifications.Add(new ExternalCertification {UserAccountId=users["linh.backend"].Id,SkillTrackId=db.SkillTracks.Single(t=>t.Code=="BACKEND_DOTNET").Id,Name="Chứng nhận backend minh họa",Issuer="Tổ chức giả định dùng trong báo cáo",EvidenceUrl="https://localhost:44391/Content/demo/portfolio.html#linh.backend",IssuedOn=Now.AddMonths(-2).Date,Status="Pending",SubmittedAtUtc=Now,ProficiencyLevel=4});
        db.ExternalCertifications.Add(new ExternalCertification {UserAccountId=users["minh.frontend"].Id,SkillTrackId=db.SkillTracks.Single(t=>t.Code=="FRONTEND_WEB").Id,Name="Chứng nhận frontend minh họa",Issuer="Tổ chức giả định dùng trong báo cáo",EvidenceUrl="https://localhost:44391/Content/demo/portfolio.html#minh.frontend",IssuedOn=Now.AddMonths(-2).Date,Status="Verified",SubmittedAtUtc=Now.AddDays(-2),ReviewedAtUtc=Now.AddDays(-1),ReviewedById=users["admin"].Id,ReviewReason="Tình huống giả lập đã duyệt; không phải chứng chỉ thật.",ProficiencyLevel=4});
        db.SaveChanges();
        foreach(var cert in db.ExternalCertifications.Where(c=>c.Status=="Verified").ToList())
            db.AdminAuditLogs.Add(new AdminAuditLog {ActorId=cert.ReviewedById.Value,TargetUserId=cert.UserAccountId,Action="VerifyCertification",Reason="Certification #"+cert.Id+": "+cert.ReviewReason,CreatedAtUtc=cert.ReviewedAtUtc.Value});
        foreach(var p in db.Projects.Where(x=>x.Status=="Draft").ToList())p.PublishedAtUtc=null;
        db.SaveChanges();
    }

    static string PortfolioPage()
    {
        return "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Hồ sơ sản phẩm minh họa · NexusFreelance</title><style>body{margin:0;background:#f4f6fa;color:#17243c;font:17px/1.7 system-ui,sans-serif}main{max-width:920px;margin:auto;padding:40px 22px}a{color:#2256ba}h1{font-size:2.2rem;line-height:1.2}h2{line-height:1.35}article{background:white;border:1px solid #dce3ed;border-radius:18px;padding:28px;margin:24px 0;scroll-margin:24px}.eyebrow{font-size:12px;letter-spacing:.08em;color:#53708e;font-weight:700}h3{font-size:17px}</style><main><a href=\"/Talent\">← Chuyên gia và nhóm</a><h1>Hồ sơ sản phẩm minh họa</h1><p>Các tình huống dưới đây thuộc bộ dữ liệu thực hành NexusFreelance. Tên người và doanh nghiệp được dùng để minh họa luồng nghiệp vụ; nội dung không phải xác nhận chứng chỉ hoặc thành tích thực tế.</p>"+portfolio+"</main></html>";
    }
}
