> **Chạy F5 Visual Studio:** cấu hình dịch vụ của máy này nằm trong `_local/integrations.dat`, được mã hóa và ở ngoài web root. Giữ thư mục `_local` khi chạy; công cụ `CauHinhDichVu.cmd` sẽ lưu cấu hình riêng tại đây. ZIP chia sẻ không chứa tệp này; máy khác cần nhập khóa của họ.
>
> Kết quả sửa và kiểm thử F5 mới nhất: `docs/FIX_DICH_VU_VISUAL_STUDIO_20261020.md` trong ZIP, hoặc `_local/FIX_DICH_VU_VISUAL_STUDIO_20261020.md` trong bản đang chạy.

# NexusFreelance — ASP.NET MVC 5

Hệ thống kết nối Client với Freelancer cá nhân/nhóm: hồ sơ năng lực, dự án và chào giá, ghép nối theo vị trí, nhiều hợp đồng độc lập, xác nhận hai phía, Workspace, ký quỹ, bàn giao/nghiệm thu, tranh chấp và ví. Có Admin, hội thoại/thông báo realtime và AI Job Studio dùng kho tri thức sau dự án.

## Công nghệ
ASP.NET MVC 5.2.9, C#, .NET Framework 4.8, Entity Framework 6.5.1, Razor, JavaScript/jQuery, SQL Server 2022 trở lên. Chạy trên Windows với Visual Studio có workload ASP.NET/web, .NET Framework 4.8 Developer Pack và IIS Express.

## Cài đặt từ bản chia sẻ
1. Giải nén toàn bộ thư mục; mở `NexusFreelance.sln` bằng Visual Studio.
2. Trong SSMS, mở kết nối mới tới `master`, chạy **Database/NexusFreelance_BanChot.sql** với mã hóa UTF-8. Script tạo DB `NexusFreelanceSubmission` và không ghi đè DB đã có. SQL gồm schema và bộ dữ liệu minh họa; không dùng `.bak` trên máy tác giả.
3. Trong `NexusFreelance.Web/Web.config`, sửa tên SQL instance theo máy của bạn nếu khác `.\SQLEXPRESS`. Bản chia sẻ đã trỏ tới DB `NexusFreelanceSubmission` đúng tên của script.
4. Restore NuGet và Build. Build/F5 tự tạo khóa ứng dụng riêng trong App_Data ở lần đầu; không dùng khóa máy khác. Có thể chạy `powershell -NoProfile -ExecutionPolicy Bypass -File tools\BuildFromSource.ps1`.
5. Chạy bằng F5 tại `https://localhost:44391/`. Chỉ chạy một phiên IIS Express trên cổng này. Nếu môi trường thiếu cấu hình IIS Express, chạy `tools\StartLocalIisExpress.ps1` sau khi build.
6. Xem tài khoản minh họa trong `Submission/TAI_KHOAN_DEMO.md`; chọn đúng vai trò. Tài khoản `.test` chỉ phục vụ demo, không nhận email thật.

## Dịch vụ ngoài
Bản chia sẻ **không chứa** Gmail app password, Google OAuth secret, Gemini/OpenAI API key, VNPay hash secret, machineKey của máy tác giả, thư xác minh hoặc tệp riêng của người dùng.

Mở `CauHinhDichVu.cmd` trên máy của bạn để cấu hình Gmail, Google OAuth, Gemini và VNPay sandbox. Chỉ chọn dịch vụ có thông tin hợp lệ. Google dùng OAuth Web client với callback `https://localhost:44391/Account/GoogleCallback`. Bật SMTP rồi mới thử đăng ký email thật hoặc quên mật khẩu; giữ project Gemini ở hạn mức/billing bạn dự định sử dụng. Không đổi sang provider trả phí tự động.

Kiểm tra: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\TestExternalServices.ps1 -Probe`. Thêm `-SendTestMail` sẽ gửi một thư kiểm tra tới chính người gửi đã cấu hình. Thông tin được mã hóa bằng DPAPI theo tài khoản Windows; không commit hoặc chia sẻ tệp riêng. `StartLocalDemo.ps1` dùng hộp thư thử trên máy, không gửi tới Gmail.

VNPay thật cần merchant và endpoint IPN HTTPS công khai đã đăng ký. QR chuyển khoản MB sinh cục bộ; Admin phải đối chiếu tiền thực nhận mới ghi có. Dữ liệu tiền trong SQL là minh họa, không phải tiền ngân hàng; không dùng DB demo cho thanh toán production.

## Tài liệu
- `Submission/BaoCaoTongKet_NexusFreelance_BanChot.docx`: báo cáo tổng kết; thông tin bìa/nhóm/lớp cần người thực hiện điền.
- `Submission/SO_TAY_BAN_CHOT_DU_LIEU_CHUC_NANG_TEST_CASE.md`: chức năng, phân quyền, luồng và dữ liệu test.
- `Submission/TEST_CASES_BAN_CHOT.csv`: danh mục test case; chưa chạy không được ghi là PASS.
- `Submission/Chuong_2_Phan_tich_he_thong`: phân tích và sơ đồ use case.
- `Submission/Anh_Chuong_4_5_BanMoi`: ảnh, chú thích và tài liệu tham khảo chương 4–5.
- `Submission/So_do_lop_Tong_quan`: sơ đồ lớp tổng quan.
- `docs/PROJECT_REST_API.md`, `docs/OPERATIONS_RECOVERY.md`: API và vận hành.
- `docs/KIEM_THU_DICH_VU_VA_BAN_CHIA_SE_20261020.md`: kết quả kiểm tra mới nhất và giới hạn kiểm thử.

Chọn nội dung phù hợp để ghép báo cáo cuối đúng số trang yêu cầu; các bộ chương và ảnh là tài liệu tham khảo, không cộng toàn bộ vào một báo cáo vượt giới hạn.

## Kiểm tra mã nguồn
Build Debug/Release: `tools/BuildFromSource.ps1`. Kiểm tra Razor: `tools/CheckOffline.ps1`. Các bộ `TestAuthentication.ps1`, `TestAccountIntegrations.ps1`, `TestGeminiProvider.ps1`, `TestVnPayGateway.ps1` chạy kiểm tra có kiểm soát. Những E2E/seed/reset cũ trong tools chỉ chạy trên DB kiểm thử riêng và theo hướng dẫn; không chạy hàng loạt trên dữ liệu đang dùng.

## Chia sẻ bằng link
Để người khác tải mã nguồn, tải ZIP sạch này lên Google Drive/OneDrive hoặc đưa nội dung thư mục sạch lên kho Git phù hợp rồi sao chép liên kết. Đây là **link mã nguồn**; website chạy trực tuyến cần hosting Windows/IIS, SQL Server, HTTPS và cấu hình dịch vụ riêng.

Giữ `_local`, App_Data, `.vs`, bin/obj, tệp khóa và bản sao lưu riêng ngoài kho chia sẻ. File `.gitignore` đã loại các nhóm này. `MANIFEST_SHA256.txt` dùng kiểm tra tính toàn vẹn của bản nguồn được đóng gói.
# BTLWEBNexusFreelancer
