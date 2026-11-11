# Đối chiếu RATE-LIMIT-TODO và DATABASE-TODO — 10/11/2026

Phạm vi: ứng dụng localhost, **một tiến trình web**. Bản làm gốc là source đã gửi ngày 20/10/2026. Không sửa database người dùng; kiểm thử SQL dùng database mới có GUID và xóa đúng database do fixture tạo. Các yêu cầu gốc nằm trong `docs/requirements/`.

## Kết quả theo yêu cầu

| Mục | Trạng thái trong source mới | Bằng chứng / giới hạn |
|---|---|---|
| SEC-01 | Hoàn tất phần đóng gói/cấu hình; **chưa hoàn tất thao tác tài khoản** | Script cấu hình lưu user-secrets ngoài source. ZIP loại local config, key ring, dữ liệu và backup. Không có lịch sử Git đi kèm source để quét; kiểm tra working tree bằng `Test-SourceSecrets.ps1`. Chưa thu hồi key cũ/đổi mật khẩu tài khoản thật/chưa xác nhận spending limit. |
| SEC-02 | Đã vá package | Pin Pkcs 10.0.13, giữ .NET 8 và MailKit hiện có; xem kết quả kiểm thử bên dưới. |
| RL-01 | Đã bổ sung | `RateLimiting` có defaults, bật/tắt, cửa sổ, số request, hàng đợi; ValidateOnStart. Giới hạn service và cooldown email được cấu hình. |
| RL-02, RL-03 | Đã bổ sung | Các policy trong bảng dưới, phân vùng user/IP, IPv4-mapped IPv6 chuẩn hóa, toàn cục + đồng thời stream. |
| RL-04 | Đã bổ sung | 429 JSON cho AJAX/JSON/SSE, HTML cho form thường, Retry-After và no-store. Log chỉ nhóm thao tác/endpoint/hash phân vùng/thời gian chờ, không query/body/IP thô. |
| RL-05 | Đã bổ sung | Giữ dữ liệu form/file/nháp; countdown, chặn thử lại sớm, autosave backoff. Xem CLIENT-RATE-LIMIT.vi.md. |
| QUOTA-01, AUTH-01 | Đã bổ sung/siết lại | Chính sách B: đăng ký khi thiếu SMTP vẫn chưa xác thực, key hệ thống yêu cầu email xác thực và tài khoản không bị khóa; key cá nhân không chuyển ngầm sang hệ thống. |
| QUOTA-02 | Đã bổ sung | Log provider/model/key source/status/token/chi phí biết được, index user–key source–UTC; thống kê quản trị theo người/ngày. Không log prompt/response. Chi phí thiếu giá hoặc usage được để “chưa biết”, không coi là 0. |
| SIZE-01 | Đã bổ sung | Giới hạn body theo endpoint, kể cả chunked; 413 an toàn trước khi thực thi nghiệp vụ. |
| API-ERR | Đã có retry và xử lý provider; bổ sung phân biệt key nguồn | HTTP/SSE lỗi an toàn; quota/rate limit hiển thị thời gian chờ; mỗi retry được ghi quota. Không gọi nhà cung cấp thật trong đợt kiểm thử này. |
| AI-01 | Bổ sung kiểm thử cấu trúc và hiển thị | Dữ liệu CV/JD/câu trả lời tách khỏi chỉ dẫn; schema/rubric được kiểm tra, HTML hiển thị thành văn bản. Kiểm thử mô phỏng không chứng minh LLM không bao giờ bị prompt injection. |
| DB-01, DB-03 | Đã điều chỉnh bản localhost | Connection mặc định Production trống, mẫu Development LocalDB công khai không chứa credential. Design-time đọc cùng nguồn cấu hình runtime. Production từ chối bật AutoMigrate/Seed. |
| DB-06 | Đã bổ sung nguyên tử trong một instance | Singleton khóa đến khi reservation được lưu; nhiều DbContext đồng thời không vượt quota. Retry có tính phí được đếm; trước khi gửi bị hủy có trạng thái riêng. Chưa hỗ trợ nhiều tiến trình web. |
| DB-08 | Đã sửa | Report chỉ truy vấn chủ sở hữu/quyền lớp trước khi tải turns/evaluation; không có quyền trả 404. |
| DB-05 | Đã bổ sung | 24 CHECK giới hạn text; giữ bản ghi cũ quá dài, không tự cắt/xóa. CHECK không trusted khi có dữ liệu cũ vi phạm; hướng dẫn xử lý riêng trong DATABASE-SECURITY.vi.md. |
| DB-07 | Đã bổ sung | CV/JD/session có giới hạn và khóa thao tác đếm–ghi cho một instance. |
| DB-04 | Đã bổ sung | Middleware trả lỗi HTML/JSON trung tính; không log exception text/SQL/body; tắt category log EF có thể chứa SQL. SSE giữ lỗi an toàn riêng. |
| DB-16 | Đã bổ sung | `scripts/Check-RawSql.ps1` phát hiện API SQL mới; hai ngoại lệ hạ tầng được review và khóa bằng hash. Không nối input người dùng vào SQL. |
| AUTH-02 | Đã bổ sung | Mã lớp mới dùng 8 byte ngẫu nhiên. |

## Endpoint → policy (mọi MVC endpoint còn lại vẫn chịu global)

Policy chọn bằng metadata controller/action, không dựa trên URL do client tự gõ. Không cần gắn attribute riêng từng action; giảm nguy cơ bỏ sót khi thêm POST quản trị. Static file được phục vụ trước limiter, không bị tính.

| Policy | Endpoint | Request / cửa sổ | Partition |
|---|---|---:|---|
| auth | POST Account/Login, Register | 20 / 60 giây | IP |
| email | POST Account/ResendConfirmation | 3 / 300 giây, cooldown 60 giây/tài khoản | IP + đặt chỗ SQL theo tài khoản |
| confirm-email | Account/ConfirmEmail | 10 / 60 giây | IP |
| ai-settings | POST AiSettings/* | 10 / 60 giây | user |
| ai-stream | POST Interviews/Stream | 10 / 60 giây, tối đa 2 đồng thời | user |
| upload-cv | POST Profile/Upload | 5 / 600 giây | user |
| answer | POST Interviews/Answer | 30 / 60 giây | user |
| draft | POST Interviews/Draft | 120 / 60 giây | user |
| class-join | POST Classes/Join | 5 / 60 giây | user |
| admin-write | POST Admin/*, Lecturer/* | 60 / 60 giây | user |
| session-create | POST Interviews/Create, Dashboard/PracticeWeak | 10 / 60 giây | user |
| ai-analysis | POST Profile/AnalyzeCv, CompareGap | 10 / 60 giây | user |
| global | Toàn bộ endpoint động | 300 / 60 giây | user, chưa đăng nhập dùng IP |

Mỗi rule ở `RateLimiting:Policies:<policy>` có `Enabled`, `PermitLimit`, `WindowSeconds`, `QueueLimit` (mặc định 0). Khởi động lại sau khi chỉnh rate limit. `StreamConcurrencyEnabled`, `StreamConcurrencyLimit`, `StreamConcurrencyQueueLimit`, `ConcurrencyRetryAfterSeconds` cấu hình limiter đồng thời. `EmailAccountCooldownSeconds=60` giữ chống spam theo tài khoản. Queue lớn làm request chờ, nên giữ 0 trong demo. Identity vẫn khóa sau 5 lần sai trong 10 phút.

## Quota, dữ liệu và kích thước

`AiQuota:SystemKeyEnabled` là công tắc khẩn cấp; đổi trong `appsettings.json` đang được theo dõi sẽ áp dụng cho lượt gọi tiếp theo, không cần deploy lại. Nếu bị biến môi trường ghi đè, phải đổi nguồn ghi đè và khởi động lại. Một request đang gửi không thể được thu hồi khỏi nhà cung cấp.

- `RequireConfirmedEmail=true`; `SystemRequestsPerDay=100`; `SystemRequestsPerMonth=2000` (0 tắt quota tháng); `SystemMaxConcurrentRequests=2`.
- Kỳ quota là UTC; ngày mới bắt đầu 07:00 UTC+7. Phân biệt `System`, `Personal`, `Legacy` (log cũ không suy đoán nguồn key). Retry/probe đều có reservation; `cancelled_before_send` không bị tính quota. Các reservation chưa biết kết quả được tính thận trọng.
- `Gemini:MaxRequestsPerMinute=20`, `OpenAI:MaxRequestsPerMinute=20` vẫn là lớp bảo vệ service. Personal không bị trừ quota ngày/tháng của key hệ thống.
- `StorageLimits`: `MaxCvsPerUser=10`, `MaxJdsPerUser=20`, `MaxActiveSessionsPerUser=10`, `MaxSessionsPerDay=30`.
- `RequestBodyLimits:DefaultBytes=262144`, `UploadBytes=6291456`; file CV thực tế tối đa 5 MiB. Stream/Join/Mark 8 KiB; Answer/Draft/CreateJd 128 KiB; Create/PracticeWeak 64 KiB; Profile.Save 32 KiB; AiSettings.Save 16 KiB. Các giá trị trong `Endpoints` có thể cấu hình và được kiểm tra khi khởi động.

Giới hạn áp dụng phía server. Người dùng không thể tăng quota bằng sửa form. Xem DATABASE-SECURITY.vi.md để biết migration/rollback và giới hạn từng cột.

## Secret và việc chủ tài khoản còn phải làm

Các script nhập ẩn thông tin và ghi vào `%APPDATA%/Microsoft/UserSecrets/AiInterview-Local-2026/secrets.json`, không đưa bí mật lên command line. Không gửi thư mục UserSecrets hoặc file cấu hình cũ. Environment variables ưu tiên cao hơn user-secrets. API key cá nhân trong SQL tiếp tục được Data Protection mã hóa. Có thể chuyển file local cũ bằng `Migrate-LocalSecrets.ps1` (xem MIGRATE-LOCAL-SECRETS.vi.md); script giữ ưu tiên user-secrets, xác minh bản ghi rồi chỉ xóa đúng file local đã chỉ định. Tài khoản seed mới cũng không được đánh dấu email đã xác thực.

**Chưa thể đánh dấu toàn bộ SEC-01 đạt:** cần chủ tài khoản thu hồi/thay key Gemini, OpenAI và Gmail app password đã từng chia sẻ trong ZIP/chat; đặt mật khẩu mới cho tài khoản demo thật, đặt hạn mức phù hợp trong tài khoản nhà cung cấp. Đổi `Seed:Password` chỉ dùng cho tài khoản mới, không đổi mật khẩu tài khoản đã có. Bản ZIP cũ đã chứa bí mật vẫn phải coi là đã lộ; tạo ZIP mới không thu hồi được bí mật cũ.

Không có repository `.git` trong bản nguồn nhận được, nên không thể khẳng định lịch sử cũ sạch. Bộ quét hiện tại báo đường dẫn/quy tắc, không in giá trị; có giới hạn của phương pháp dò mẫu. Khi đưa lên Git, thêm gitleaks/secret scanning của nền tảng và kiểm tra cả lịch sử.

## Kiểm thử và tái hiện

Kết quả cuối của bản bàn giao được ghi tại `SECURITY-VERIFICATION.vi.md`. Không dùng API key hoặc gửi email thật trong đợt này. Bài SQL dùng SQL Server LocalDB thật; bài HTTP dùng ASP.NET TestServer; bài JS dùng môi trường DOM mô phỏng. Danh sách demo thủ công nằm trong CLIENT-RATE-LIMIT.vi.md.

```powershell
.\Test-SourceSecrets.ps1
.\scripts\Check-RawSql.ps1
dotnet test AiInterview.sln
# Bật SQL integration: xem DATABASE-SECURITY.vi.md để khởi động instance test.
$env:AIINTERVIEW_TEST_SQL='1'
dotnet test AiInterview.sln
node --test tests/browser/*.test.cjs
dotnet list AiInterview.sln package --vulnerable --include-transitive
.\Package-Source.ps1 -Destination 'DUONG_DAN_ZIP_MOI.zip'
```

Nguồn vá dependency: [Microsoft advisory Pkcs](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx), [gói Pkcs 10.0.13](https://www.nuget.org/packages/System.Security.Cryptography.Pkcs/10.0.13). Package vẫn hỗ trợ .NET 8; việc quét NuGet không thay thế cập nhật Windows/.NET runtime của máy demo.

## Hạn chế và hướng phát triển

Theo phạm vi localhost trong hai tài liệu: chưa triển khai limiter phân tán/Redis, trusted proxy headers, CSP report-only, CAPTCHA, 2FA admin, kiểm thử tải, cảnh báo ngân sách 70/90/100%, audit log bất biến toàn hệ thống, retention job, TDE/Always Encrypted, tài khoản migrate/runtime riêng, tối ưu index theo tải thật và quy trình restore database + key ring đã diễn tập. Chưa xác nhận hosting/production an toàn để mở công khai.

Trang thống kê quản trị là bản gọn (user/ngày/nguồn key/token/chi phí biết được), chưa có dashboard đầy đủ top user và tổng lượt bị HTTP limiter chặn. Phân quyền nằm ở ứng dụng, chưa có SQL row-level security. Session/CV/JD/log không tự xóa; operator cần chính sách lưu trữ trước khi triển khai thật. Không đánh dấu các phần này là đã làm.
