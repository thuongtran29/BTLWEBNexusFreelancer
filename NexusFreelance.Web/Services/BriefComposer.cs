using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using NexusFreelance.Web.ViewModels;

namespace NexusFreelance.Web.Services
{
    // Transparent local composition: curated work patterns, never presented as a model response.
    public static class BriefComposer
    {
        private static int sequence = Environment.TickCount & int.MaxValue;
        private sealed class WorkPattern
        {
            public string Topic, Users, Workflow, Outputs, Acceptance;
            public string[] Roles;
        }
        private static bool Has(string text, params string[] words) => words.Any(text.Contains);
        private static WorkPattern Pattern(JobIdeaForm form)
        {
            var text=(form.Idea+" "+form.AdditionalInstructions).ToLowerInvariant();
            var p=new WorkPattern();
            if(Has(text,"thú y","phòng khám","đặt lịch","lịch hẹn")) {
                p.Topic="hệ thống đặt lịch và quản lý hồ sơ";p.Users="người đặt lịch và nhân viên phụ trách";
                p.Workflow="tạo lịch hẹn, kiểm tra thời gian còn trống, cập nhật hồ sơ và theo dõi trạng thái lịch";
                p.Outputs="Luồng đặt lịch, danh sách lịch hẹn và hồ sơ liên quan";
                p.Acceptance="Kiểm tra trùng lịch, thiếu thông tin, đổi/hủy lịch và quyền xem hồ sơ";p.Roles=new[]{"Backend Developer","Frontend Developer"};
            } else if(Has(text,"giỏ hàng","tồn kho","đơn hàng","thương mại điện tử","bán sản phẩm")) {
                p.Topic="hệ thống bán hàng và xử lý đơn";p.Users="người mua và nhân viên quản lý cửa hàng";
                p.Workflow="tìm sản phẩm, thêm giỏ hàng, xác nhận đơn và cập nhật tồn kho";p.Outputs="Danh mục sản phẩm, giỏ hàng và quy trình xử lý đơn";
                p.Acceptance="Kiểm tra số lượng, sản phẩm hết hàng, tổng tiền và trạng thái đơn";p.Roles=new[]{"Backend Developer","Frontend Developer"};
            } else if(Has(text,"dịch ","biên dịch","thuật ngữ","translation","localization")) {
                p.Topic="bản dịch có kiểm soát thuật ngữ";p.Users="người đọc bản dịch và người duyệt nội dung";
                p.Workflow="thống nhất thuật ngữ, dịch bản mẫu, hiệu đính và kiểm tra đối chiếu bản gốc";p.Outputs="Bản dịch, bảng thuật ngữ và bảng ghi các điểm cần xác nhận";
                p.Acceptance="Đối chiếu nghĩa, số liệu, tên riêng được phép và định dạng với bản gốc";p.Roles=new[]{"Translator","Editor"};
            } else if(Has(text,"seo","tìm kiếm tự nhiên")) {
                p.Topic="kế hoạch tối ưu tìm kiếm theo nội dung";p.Users="người tìm kiếm và người quản lý nội dung";
                p.Workflow="khảo sát nội dung, xác định ý định tìm kiếm, lập danh sách tối ưu và kiểm tra sau cập nhật";p.Outputs="Báo cáo kiểm tra SEO, bản đồ nội dung và danh sách tối ưu theo ưu tiên";
                p.Acceptance="Mỗi đề xuất chỉ rõ trang/mục áp dụng, lý do và cách kiểm tra; không cam kết thứ hạng";p.Roles=new[]{"SEO Specialist","Content Writer"};
            } else if(form.Industry=="Marketing & truyền thông" || Has(text,"quảng cáo","chiến dịch","marketing")) {
                p.Topic="kế hoạch truyền thông và nội dung chiến dịch";p.Users="khách hàng mục tiêu và người vận hành chiến dịch";
                p.Workflow="xác định thông điệp, chuẩn bị nội dung theo kênh, duyệt mẫu và theo dõi chỉ số được thống nhất";p.Outputs="Thông điệp, lịch nội dung, mẫu truyền thông và bảng theo dõi";
                p.Acceptance="Đối chiếu thông điệp, đối tượng, quyền sử dụng tài sản và chỉ số có thể thu thập";p.Roles=new[]{"Digital Marketer","Content Writer"};
            } else if(Has(text,"video","phim","dựng clip")) {
                p.Topic="bộ video theo kịch bản được duyệt";p.Users="người xem mục tiêu và người duyệt kịch bản";
                p.Workflow="chốt kịch bản, dựng bản nháp, nhận phản hồi và xuất các phiên bản yêu cầu";p.Outputs="Kịch bản, video xuất và tệp dựng có thể chỉnh sửa theo thỏa thuận";
                p.Acceptance="Kiểm tra khung hình, âm thanh, phụ đề và quyền sử dụng tư liệu";p.Roles=new[]{"Video Editor","Content Writer"};
            } else if(Has(text,"logo","nhận diện","branding","đồ họa")) {
                p.Topic="bộ thiết kế nhận diện nhất quán";p.Users="người sử dụng bộ nhận diện và người duyệt thiết kế";
                p.Workflow="chốt định hướng, trình bày phương án, hiệu chỉnh và đóng gói tài sản";p.Outputs="Phương án thiết kế, tệp nguồn và hướng dẫn màu sắc/kiểu chữ";
                p.Acceptance="Kiểm tra độ rõ ở các kích thước, màu in/hiển thị và định dạng tệp nguồn";p.Roles=new[]{"Graphic Designer"};
            } else if(form.Industry=="Thiết kế & sáng tạo" || Has(text,"ui/ux","figma","prototype")) {
                p.Topic="thiết kế trải nghiệm và giao diện";p.Users="người dùng mục tiêu và người triển khai giao diện";
                p.Workflow="xác định luồng sử dụng, vẽ wireframe, thử prototype và bàn giao thiết kế";p.Outputs="Luồng màn hình, prototype và hệ thành phần có hướng dẫn";
                p.Acceptance="Kiểm tra đủ trạng thái trống/lỗi/thành công, màn hình nhỏ và khả năng thao tác";p.Roles=new[]{"UI/UX Designer"};
            } else if(form.Industry=="Dữ liệu & AI" || Has(text,"dashboard","phân tích dữ liệu","học máy")) {
                var ml=Has(text,"học máy","machine learning","dự đoán","mô hình ai");
                p.Topic=ml?"mô hình thử nghiệm có đánh giá dữ liệu":"báo cáo phân tích dữ liệu có thể đối chiếu";p.Users="người ra quyết định và người kiểm tra dữ liệu";
                p.Workflow="kiểm tra nguồn, làm sạch dữ liệu, phân tích và đánh giá kết quả";p.Outputs="Quy trình xử lý, báo cáo/chỉ số và hướng dẫn tái lập";
                p.Acceptance="Đối chiếu nguồn, dữ liệu thiếu, phép tính và giới hạn của kết luận";p.Roles=ml?new[]{"AI / ML Engineer","Data Analyst"}:new[]{"Data Analyst"};
            } else if(form.Industry=="Nội dung & biên dịch") {
                p.Topic="bộ nội dung theo đối tượng đọc";p.Users="người đọc mục tiêu và người biên tập";
                p.Workflow="lập dàn ý, viết bản mẫu, kiểm chứng thông tin và hiệu đính";p.Outputs="Dàn ý, bản nội dung chỉnh sửa được và danh sách nguồn tham khảo";
                p.Acceptance="Kiểm tra đúng brief, ngôn ngữ, nguồn thông tin và tính nhất quán";p.Roles=new[]{"Content Writer","Editor"};
            } else if(form.Industry=="Kỹ thuật & kiến trúc") {
                p.Topic="hồ sơ thiết kế kỹ thuật theo đầu vào được xác nhận";p.Users="người duyệt hồ sơ và đơn vị triển khai";
                p.Workflow="đối chiếu đầu vào, dựng bản vẽ/mô hình, kiểm tra phối hợp và bàn giao";p.Outputs="Bản vẽ/mô hình, ghi chú giả định và tệp nguồn theo thỏa thuận";
                p.Acceptance="Kiểm tra đơn vị đo, kích thước, lớp dữ liệu và điểm cần người có thẩm quyền xác nhận";p.Roles=new[]{"Architect","CAD Designer"};
            } else if(form.Industry=="Kinh doanh & tư vấn") {
                p.Topic="phương án nghiệp vụ và kế hoạch triển khai";p.Users="người vận hành và người ra quyết định";
                p.Workflow="khảo sát quy trình, phân tích vướng mắc, đề xuất phương án và đối chiếu với bên phụ trách";p.Outputs="Sơ đồ quy trình, phân tích phương án và kế hoạch hành động";
                p.Acceptance="Mỗi đề xuất có căn cứ, trách nhiệm, phụ thuộc và cách theo dõi kết quả";p.Roles=new[]{"Business Analyst","Business Consultant"};
            } else {
                p.Topic="sản phẩm theo brief đã xác nhận";p.Users="người sử dụng và người vận hành được Client xác định";
                p.Workflow="làm rõ đầu vào, xây luồng sử dụng, triển khai bản mẫu và kiểm tra đầu ra";p.Outputs="Bản mẫu chức năng, sản phẩm chỉnh sửa được và hướng dẫn sử dụng";
                p.Acceptance="Đối chiếu từng yêu cầu trong brief, tình huống nhập sai và tiêu chí bàn giao";p.Roles=new[]{"Full-stack Developer","UI/UX Designer"};
            }
            if(form.ProjectType=="SOLO") {
                var software=p.Roles.Any(r=>r.Contains("Developer"));
                p.Roles=new[]{software?"Full-stack Developer":p.Roles[0]};
            }
            return p;
        }
        public static JobEditForm Compose(JobIdeaForm form)
        {
            if(form==null || string.IsNullOrWhiteSpace(form.Idea))throw new InvalidOperationException("Cần mô tả dự án để soạn JD.");
            var p=Pattern(form);var n=Interlocked.Increment(ref sequence)&int.MaxValue;
            var focus=new[]{"Kiểm tra các tình huống ngoại lệ trước khi duyệt đầu ra.","Ưu tiên luồng thao tác có thể dùng thử và lấy phản hồi sớm.","Làm rõ đầu vào, phụ thuộc và trách nhiệm phê duyệt ở từng bước.","Chú trọng khả năng vận hành và tái sử dụng tài liệu sau bàn giao."}[n%4];
            var opening=new[]{"Mục đích triển khai", "Kết quả cần cung cấp", "Bối cảnh và công việc", "Phạm vi chuyên môn"}[(n/4)%4];
            var knowledge=string.IsNullOrWhiteSpace(form.SharedPracticeHints)?"":"\nBài học chung tham khảo: "+Clip(form.SharedPracticeHints,240);
            var title=Clip(form.Idea.Trim().Split(new[]{'\n','\r'},StringSplitOptions.RemoveEmptyEntries)[0],170);
            return new JobEditForm {
                Title=title,
                Description=opening+": "+Clip(form.Idea.Trim(),450)+"\nTổ chức công việc cho "+p.Topic+"; "+p.Workflow+".\n"+focus,
                Objectives="- Hoàn thành "+p.Topic+" theo yêu cầu: "+Clip(form.Idea.Trim(),180)+".\n- "+focus,
                Scope="Trong phạm vi: "+p.Workflow+".\nYêu cầu cụ thể: "+Clip(form.Idea.Trim(),220)+".\nNgoài phạm vi đề xuất: phần bổ sung chưa được duyệt.\nCần xác nhận: dữ liệu đầu vào, ưu tiên, điều kiện nghiệm thu và "+Clip(form.AdditionalInstructions??"các phụ thuộc",160)+".",
                UserStories="- Là "+p.Users+", cần "+p.Workflow+" để hoàn thành công việc đã nêu.\n- Client cần xem bản mẫu và phản hồi trước từng lần bàn giao.",
                RequiredSkills=string.Join("\n",p.Roles.Select(r=>"- "+r+": thực hiện phần chuyên môn trong luồng "+Clip(p.Workflow,110)+"; cung cấp bằng chứng liên quan để đối chiếu.")),
                Deliverables="- "+p.Outputs+".\n- Tiêu chí: "+p.Acceptance+".\n- "+focus+knowledge,
                Milestones="1. Xác nhận brief, đầu vào và tiêu chí: "+Clip(p.Acceptance,150)+".\n2. Duyệt bản mẫu cho "+p.Topic+"; "+focus+"\n3. Kiểm tra, bàn giao "+Clip(p.Outputs,180)+" và hướng dẫn theo thỏa thuận.",
                Roles=p.Roles.Take(6).Select(r=>new JobRoleForm{Title=r,RequiredCount=1}).ToList()
            };
        }
        private static string Clip(string s,int n)=>(s??"").Substring(0,Math.Min(n,(s??"").Length));
    }
}
