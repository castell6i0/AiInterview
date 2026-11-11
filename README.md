# Bản cập nhật bảo mật localhost — 10/11/2026

Đối chiếu hai tài liệu yêu cầu: [kết quả kiểm tra và phần còn cần chủ tài khoản thực hiện](docs/SECURITY-REVIEW-2026-11-10.vi.md).

- Bản source này **không chứa API key, mật khẩu Gmail, mật khẩu seed, database hay key ring**. Mọi đoạn hướng dẫn ZIP RIÊNG có email sẵn ở tài liệu cũ đã được thay thế bởi chính sách này.
- `Configure.ps1`, `Configure-Email.ps1` và công cụ nhập EduHouse lưu vào **.NET user-secrets ngoài thư mục source**, không tạo file local chứa mật khẩu nữa. User-secrets chỉ dùng Development và không phải kho mã hóa.
- Chạy `Run.ps1` hoặc mở solution, đặt `AiInterview.Web` là startup project. Development dùng mẫu LocalDB công khai, tự áp dụng migration. Không có mật khẩu demo mặc định; thiết lập qua `Configure.ps1` nếu cần tài khoản demo.
- Thiếu SMTP vẫn đăng ký được nhưng email chưa xác thực; AI hệ thống yêu cầu email đã xác thực. Key cá nhân được nhập ngay trên website.
- Mặc định mỗi người được tối đa 100 request/ngày UTC và 2000 request/tháng UTC bằng key hệ thống; retry và kiểm tra kết nối cũng được tính. Trang quản trị thống kê không lưu nội dung phỏng vấn.
- Các bước chuyển cấu hình, kiểm thử và giới hạn triển khai nằm trong tài liệu bảo mật. Không gửi file local hoặc ZIP cũ chứa cấu hình riêng cho người khác.

---

> **Bản ZIP RIÊNG ngày 20/10/2026: đã kiểm tra toàn bộ, giữ sửa LocalDB, danh sách model có hạn mức miễn phí và cấu hình Gmail có sẵn.** Người nhận không cần chạy Configure-Email.ps1 hoặc chỉnh sửa source. Giải nén, chạy `CHAY-WEB.cmd` ở thư mục ngoài cùng, rồi mở http://localhost:5088. Máy cần .NET/ASP.NET Core 8 và SQL Server LocalDB. Mở liên kết xác nhận email trên chính máy đang chạy website. Xem `HUONG-DAN-CHAY.txt` ở thư mục ngoài cùng. ZIP chứa thông tin gửi mail được chủ dự án cho phép chia sẻ riêng; không đăng công khai.
# InterviewLab — Hệ thống luyện phỏng vấn IT với AI

Đồ án ASP.NET Core MVC **.NET 8**, SQL Server, Entity Framework Core và Identity dành cho sinh viên IT Việt Nam. Sinh viên luyện phỏng vấn bằng văn bản hoặc giọng nói, theo CV/JD, nhận báo cáo có dẫn chứng và theo dõi tiến bộ. Giảng viên quản lý lớp, giao bài và nhận xét; quản trị viên quản lý danh mục, người dùng, câu hỏi và rubric.

Mã nguồn tích hợp **Gemini và OpenAI API** qua `IAiService`. Sau khi đăng nhập, người dùng vào **Cài đặt AI** (`/AiSettings`) để chọn nhà cung cấp, nhập API key và model riêng; lưu xong áp dụng ngay cho tài khoản đó. Cấu hình hệ thống qua `AI:Provider` vẫn dùng cho tài khoản chưa chọn kết nối cá nhân. Không có chế độ giả lập AI trong ứng dụng; tạo câu hỏi/chấm điểm cần key, model và quota hợp lệ. Không gửi API key qua cuộc trò chuyện hoặc đưa vào file ZIP chia sẻ.

**Kiểm chứng ngày 13/10/2026:** **281 kiểm thử .NET đạt**, gồm **44 SQL thật**, **26 kiểm thử JavaScript** và **22 kiểm tra HTTP** cho cài đặt cá nhân. Trang Cài đặt AI đã xem trên desktop/mobile, không tràn ngang ở lần đo mobile. Migration thứ 5 đã áp dụng vào database phát triển và bản Visual Studio, giữ nguyên tài khoản/buổi cũ. Đợt kiểm tra mới dùng key giả, không gọi AI hoặc gửi thư thật.

**Bằng chứng AI/email ngày 12/10/2026:** buổi QA/Tester tiếng Việt có CV đã hoàn thành **5 câu chính**, chấm và mở báo cáo thành công: **11/11 lần gọi Gemini thật thành công ngay lần đầu**, **29/29 trích dẫn khớp câu trả lời**, điểm tính lại **3,36/10**, readiness **34**. Gmail từ bản EduHouse đã được nhập riêng trên máy và xác minh kết nối TLS/xác thực SMTP thành công; chưa gửi thư hoặc kiểm tra hộp nhận. Tại đợt này chưa gọi OpenAI thật. Giọng đọc vẫn cần người nghe kiểm tra trên thiết bị thực tế. Phạm vi cụ thể nằm trong [VERIFICATION.md](docs/VERIFICATION.md).

**Cập nhật 17/10/2026 (bản sửa LocalDB):** tự chuẩn bị và chờ SQL LocalDB sẵn sàng trước khi cập nhật database ở môi trường Development, kể cả khi chạy F5. Không xóa hay chuyển database đang dùng. Máy vẫn cần cài SQL Server LocalDB. Danh sách chọn nhanh AI chỉ còn model Gemini có hạn mức miễn phí theo điều kiện tài khoản; vẫn có ô tự nhập. **Lưu & kiểm tra model** thử tạo mẫu bằng cấu hình vừa lưu, **Chỉ lưu** không gọi API. Xem [hướng dẫn](docs/AI-SETTINGS.vi.md).

**Cập nhật 16/10/2026:** kiểm tra kết nối nay yêu cầu AI tạo một mẫu ngắn và xác minh nội dung nhận đủ, thay cho chỉ đọc thông tin model. Lần chẩn đoán OpenAI thật nhận lỗi `credit_balance_exhausted` thuộc nhóm `insufficient_quota` trong luồng phản hồi; HTTP 200 không có nghĩa đã tạo được câu hỏi. Ứng dụng phân biệt hết tín dụng với lỗi dịch vụ tạm thời. Cần xử lý số dư/hạn mức OpenAI API hoặc chọn Gemini có hạn mức phù hợp; chưa xác minh thành công một buổi OpenAI hoàn chỉnh.

Bản sửa bổ sung chọn **Gemini/OpenAI**, thay đổi chủ đề và dạng câu hỏi, kiểm tra câu gần trùng với 20 câu chính gần đây cùng vị trí/ngôn ngữ. Khi đủ số câu chính, server kết thúc phần hỏi mà không gọi AI thêm. Phần chấm dùng mã dẫn chứng ngắn rồi đối chiếu lại nguyên văn trên server để giảm kích thước schema. Giọng đọc chỉ phát câu đã được kiểm tra, có chọn giọng đúng ngôn ngữ và tốc độ; profile Visual Studio đã bật `launchBrowser: true`.

UI được chỉnh theo luồng rõ ràng: tổng quan ưu tiên buổi đang dở; tạo buổi có hai nhóm chính và phần CV/JD tùy chọn; phòng luyện luôn ghi bước cần làm; báo cáo có liên kết nhanh tới điểm, kế hoạch và từng câu. Chữ, tương phản, khoảng cách và điều hướng được thống nhất; đồng hồ chỉ bắt đầu khi có câu hỏi và dừng khi kết thúc phần trả lời.

## Chạy nhanh trên Windows

Phần cài đặt dưới đây dành cho người phát triển/vận hành website. **Người sử dụng chỉ mở địa chỉ website**, làm theo **Hướng dẫn sử dụng** (`/Home/Help`), không cần tải source hoặc chạy lệnh. Xem [hướng dẫn người dùng](docs/USER-GUIDE.vi.md) và [hướng dẫn triển khai cho người vận hành](docs/TRIEN-KHAI-WEBSITE.vi.md).

**Cập nhật 14/10/2026:** dashboard chỉ rõ bước kết nối AI khi cần; trang cài đặt giữ đường quay lại buổi/CV, model nằm trong phần nâng cao; lỗi key có nút sửa ngay trên web. Đã đạt **287 .NET, 29 JavaScript, 31 kiểm tra HTTP**. Xem [phạm vi kiểm chứng](docs/VERIFICATION.md).

1. Cài **.NET SDK 8 hoặc mới hơn**, **ASP.NET Core Runtime 8** và **SQL Server LocalDB** hoặc SQL Server. Nếu dùng Visual Studio, cài workload **ASP.NET and web development**; mở `AiInterview.sln`, chọn `AiInterview.Web` làm startup project.
2. Giải nén toàn bộ source vào một thư mục của bạn. Mở PowerShell ngay tại thư mục chứa `AiInterview.sln`.
3. Chạy cấu hình:

   ```powershell
   .\Configure.ps1
   ```

   Đặt mật khẩu cho ba tài khoản mẫu, chọn connection string và model. **API key trong script là cấu hình dùng chung của hệ thống**: có thể nhấn Enter để bỏ qua trên máy mới rồi nhập key riêng trên web sau khi đăng nhập. Các trường key/mật khẩu/connection string được nhập ẩn. Mật khẩu tối thiểu 10 ký tự, có chữ hoa, chữ thường, số và ký tự đặc biệt. Nhấn Enter để giữ cấu hình cũ; trên máy mới Enter ở connection string dùng LocalDB mặc định. Muốn gửi email xác nhận đăng ký, chạy thêm `.\Configure-Email.ps1` để thiết lập Gmail gửi thư xác nhận theo hướng dẫn bên dưới.
4. Khởi động:

   ```powershell
   .\Run.ps1
   ```

5. Khi cửa sổ báo `Now listening`, mở **http://localhost:5088**. Đăng nhập → **Cài đặt AI** → chọn Gemini → nhập API key → chọn model có hạn mức miễn phí hoặc tự nhập → **Lưu & kiểm tra model**. Giữ cửa sổ PowerShell đang chạy; nhấn `Ctrl+C` để dừng. `Run.ps1` không tự mở trình duyệt.

Nếu Windows chặn chạy script đã tải về, dùng lệnh sau trong **chính cửa sổ PowerShell hiện tại**, rồi chạy lại script:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Connection string mặc định:

```text
Server=(localdb)\MSSQLLocalDB;Database=AiInterview;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=True;TrustServerCertificate=True
```

Bạn có thể thay bằng SQL Server/SQL Express đang dùng trong `Configure.ps1`. `TrustServerCertificate=True` phục vụ môi trường phát triển cục bộ; khi triển khai hãy dùng chứng chỉ SQL Server được tin cậy.

### Chạy bằng Visual Studio

1. Mở **AiInterview.sln**. Vào **View → Solution Explorer** nếu chưa thấy danh sách project.
2. Mở nhóm **src**, chuột phải project **AiInterview.Web** → **Set as Startup Project**. Chọn đúng project, không chọn solution hoặc thư mục src.
3. Chọn profile **AiInterview** cạnh nút chạy, nhấn **F5** (debug) hoặc **Ctrl+F5** (chạy). Profile đã bật **`launchBrowser: true`**, nên Visual Studio tự mở trình duyệt tại **http://localhost:5088**.

Nếu hiện **“A project with an Output Type of Class Library cannot be started directly”**, Visual Studio đang chọn một project thư viện làm startup project. Thực hiện lại bước 2. Domain, Application và Infrastructure là thư viện được Web sử dụng; giữ nguyên loại project của chúng.

Solution liệt kê Web trước các project thư viện. Với solution đã mở trước đó, Visual Studio có thể giữ lựa chọn startup riêng trên máy; vẫn cần chọn **Set as Startup Project** một lần. Cấu hình database/API key thực hiện theo phần trên. Nếu đã chạy bằng Run.ps1, dừng phiên đó trước khi chạy một phiên khác trên cùng cổng.

### Tài khoản mẫu

| Vai trò | Email | Mật khẩu |
|---|---|---|
| Quản trị viên | `admin@example.test` | Mật khẩu bạn đã nhập trong `Configure.ps1` |
| Giảng viên | `lecturer@example.test` | Như trên |
| Sinh viên | `student@example.test` | Như trên |

Không có mật khẩu mặc định trong source. Nếu chưa đặt `Seed:Password`, hệ thống vẫn tạo role và danh mục nhưng **không tạo tài khoản mẫu**. Đăng ký công khai chỉ tạo sinh viên. Seed không đổi mật khẩu tài khoản đã tồn tại, không nâng quyền một tài khoản cũ trùng email và không ghi đè các câu hỏi đã được quản trị viên sửa.

Tài khoản tự đăng ký mới **chỉ phải xác nhận email khi SMTP và địa chỉ website công khai đã được cấu hình hợp lệ**. Nếu chưa cấu hình email, người dùng vẫn đăng ký và đăng nhập được bằng email/mật khẩu ngay trên website, nhưng **địa chỉ email chưa được xác minh** (không tự đánh dấu `EmailConfirmed=true`). Khi cấu hình SMTP, các đăng ký mới quay về luồng xác nhận email thông thường. Tài khoản cũ và tài khoản cấp bởi quản trị viên/seed giữ cách đăng nhập hiện có.

Profile chạy mặc định là `Development`: tự áp dụng migration và seed. Trên database mới, seed gồm 3 role, 7 vị trí, 63 câu hỏi, các tag kỹ năng và rubric 5 tiêu chí. Mở lại ứng dụng không tạo thêm các bản ghi seed trùng. Chỉ tài khoản demo bị giới hạn ở Development; role/danh mục cũng có thể seed ở môi trường khác nếu người vận hành chủ động bật `Seed:Enabled`.

## Email xác nhận đăng ký và thông báo đăng nhập

Chạy `.\Configure-Email.ps1` tại thư mục solution để nhập địa chỉ Gmail/Google Workspace gửi thư, **Google App Password** và địa chỉ gốc của website. App Password được nhập ẩn và lưu vào cấu hình cục bộ; nhập trên máy riêng, không gửi qua chat. Script dùng `smtp.gmail.com`, cổng `587`, `StartTls`. Xem [hướng dẫn email](docs/EMAIL.vi.md) để chuẩn bị Gmail và cấu hình URL.

Nếu có bản EduHouse đã cung cấp, có thể nhập cấu hình Gmail bằng lệnh sau thay cho nhập tay. Script chỉ đọc cấu hình trong ZIP, lưu vào file local và giữ nguyên cấu hình AI/SQL/seed:

```powershell
.\Import-EmailFromEduHouse.ps1 -ArchivePath "E:\Edu House\EduHouse_fixed20.zip" -PublicBaseUrl "http://localhost:5088"
```

Thay đường dẫn/URL cho đúng máy; khởi động lại ứng dụng sau khi nhập. Bản source thông thường không chứa Gmail/App Password. Riêng ZIP RIÊNG này đã kèm cấu hình SMTP của chủ dự án; người nhận không cần nhập lại.

Sau khi tự đăng ký, người dùng mở liên kết xác nhận trong email rồi đăng nhập. Liên kết có hiệu lực **24 giờ**; có thể gửi lại sau **60 giây**, tối đa **3 yêu cầu gửi lại/IP trong 5 phút**. Sau mỗi lần đăng nhập thành công, ứng dụng gửi email thông báo; **không có bước OTP đăng nhập**. Nếu thư thông báo gửi lỗi, phiên đăng nhập vẫn dùng được.

Bản source thông thường đặt **`Email:Enabled=false`**. Riêng ZIP RIÊNG này bật email qua `appsettings.Local.json` có sẵn, nên đăng ký mới yêu cầu xác nhận email. **Nút đăng ký vẫn hoạt động** ngay cả khi SMTP chưa được thiết lập: tài khoản được tạo theo vai trò sinh viên, không xác minh quyền sở hữu email và người dùng có thể đăng nhập trực tiếp. Với website công khai, nên bật SMTP/HTTPS trước khi chia sẻ để tránh người khác đăng ký bằng địa chỉ email không thuộc họ. Không xem chế độ không xác minh là bảo đảm danh tính email. MailKit bắt buộc TLS và giới hạn thời gian gửi **15 giây**. Trên máy hiện tại, cấu hình riêng nhập từ EduHouse đã kết nối TLS và xác thực SMTP thành công; chưa gửi thư hoặc kiểm tra việc thư tới hộp nhận.

Khi cập nhật bản mới, dừng ứng dụng đang chạy, mở đúng solution mới, cấu hình email riêng trên máy rồi build và áp dụng migration trước khi khởi động lại:

```powershell
.\Configure-Email.ps1
.\Run.ps1 -MigrateOnly
.\Run.ps1
```

`Run.ps1 -MigrateOnly` build và áp dụng migration vào database đã cấu hình. Giữ lại cấu hình cục bộ và dữ liệu SQL đang dùng; không thay bằng thư mục bản giải nén cũ.

## Nhập API key trên web

Mọi tài khoản đã đăng nhập đều có mục **Cài đặt AI** (`/AiSettings`). Chọn Gemini, nhập key, chọn model có hạn mức miễn phí hoặc tự nhập rồi bấm **Lưu & kiểm tra model**. **Chỉ lưu** áp dụng cấu hình mà chưa gọi API. OpenAI đã lưu từ bản cũ vẫn được quản lý trong mục nâng cao có nhãn phí; không có model trả phí trong danh sách chọn nhanh. Thay đổi có hiệu lực ở yêu cầu tiếp theo, không cần khởi động lại. Key cá nhân được mã hóa trước khi lưu SQL và không hiển thị lại trên trang.

- Để trống ô API key khi lưu sẽ giữ key đã lưu của nhà cung cấp đang chọn; nếu chưa có key dùng được, phải nhập key.
- **Xóa key** chỉ xóa key của nhà cung cấp tương ứng. Nếu đó là kết nối cá nhân đang dùng, thao tác AI sẽ yêu cầu nhập lại; ứng dụng không tự chuyển sang key hệ thống.
- Muốn trở lại kết nối dùng chung, bấm **Chuyển sang cấu hình hệ thống**. Các key cá nhân khác vẫn được giữ; cấu hình này không ảnh hưởng tài khoản khác.
- **Kiểm tra kết nối đã lưu** kiểm tra cấu hình đang dùng ở đầu trang, không dùng nội dung chưa lưu trong ô nhập. Web yêu cầu AI tạo một mẫu ngắn, có thể dùng một ít hạn mức/phí API và không gửi CV/JD/câu trả lời. Thành công xác nhận tạo được mẫu hợp lệ tại thời điểm kiểm tra; các lượt phỏng vấn tiếp theo vẫn phụ thuộc hạn mức và tình trạng dịch vụ.

Buổi đang dở yêu cầu xác nhận lại khi đổi nhà cung cấp. Trang tạo buổi hoặc phân tích CV/JD đang mở trước khi đổi cũng cần xác nhận đúng nhà cung cấp mới trước khi gửi dữ liệu. Xem [hướng dẫn Cài đặt AI](docs/AI-SETTINGS.vi.md) để quản lý key và sao lưu khóa mã hóa.

## Cấu hình Gemini hoặc OpenAI dùng chung

Người vận hành chạy `Configure.ps1`, chọn **OpenAI** hoặc **Gemini** trước khi nhập key và model. Nhấn Enter ở tên nhà cung cấp để giữ lựa chọn hiện tại. Chuyển nhà cung cấp giữ nguyên key của nhà cung cấp còn lại, mật khẩu mẫu, kết nối SQL và lịch sử học tập. Cần khởi động lại ứng dụng sau khi đổi **cấu hình hệ thống**; tài khoản đang dùng kết nối cá nhân tiếp tục dùng lựa chọn riêng.

Buổi đang dở ghi nhớ nhà cung cấp đã được đồng ý lúc tạo. Nếu đổi nhà cung cấp, phòng luyện yêu cầu xác nhận trước khi gửi CV/JD và câu trả lời của buổi cũ đến nhà cung cấp mới. Lịch sử đã lưu được giữ lại.

| Nhà cung cấp | Nơi lấy API key | Model mặc định trong source |
|---|---|---|
| Gemini | [Google AI Studio](https://aistudio.google.com/apikey) | `gemini-3.1-flash-lite` |
| OpenAI | [OpenAI API keys](https://platform.openai.com/settings/organization/api-keys) | [`gpt-4.1-mini`](https://developers.openai.com/api/docs/models/gpt-4.1-mini) |

Source giữ **Gemini làm mặc định** để cấu hình hiện có tiếp tục chạy. Key Gemini không dùng được cho OpenAI. Adapter OpenAI có kiểm thử giao thức; lần chẩn đoán API thật đã nhận lỗi hết tín dụng, chưa xác minh một buổi OpenAI hoàn chỉnh. Model khả dụng, hạn mức và chi phí phụ thuộc tài khoản từng nhà cung cấp. Trang **Cài đặt AI** hiển thị nhà cung cấp/model, nguồn key đang dùng và có nút kiểm tra khả năng tạo phản hồi mẫu. Không tự chuyển sang nhà cung cấp khác khi lỗi.

Để chuyển sang OpenAI bằng cấu hình .NET:

```powershell
dotnet user-secrets set "AI:Provider" "OpenAI" --project src/AiInterview.Web
dotnet user-secrets set "OpenAI:ApiKey" "YOUR_OPENAI_KEY" --project src/AiInterview.Web
dotnet user-secrets set "OpenAI:Model" "gpt-4.1-mini" --project src/AiInterview.Web
```

Để quay lại Gemini, đặt `AI:Provider` thành `Gemini`. Chi tiết hướng dẫn và phạm vi kiểm chứng: [AI-CONFIGURATION.vi.md](docs/AI-CONFIGURATION.vi.md).

`Configure.ps1` lưu cấu hình vào `src/AiInterview.Web/appsettings.Local.json`. File này được bỏ qua bởi Git, loại khỏi publish và ZIP source thông thường; `App_Data` cũng bị loại khỏi publish. Riêng bản ZIP RIÊNG có file local chỉ chứa cấu hình email đã được chủ dự án cho phép chia sẻ; không kèm key AI hay dữ liệu tài khoản. Đây là file cấu hình chứa bí mật ở dạng văn bản, không phải kho mã hóa; chỉ chia sẻ bản riêng cho người nhận được cho phép và không đưa `App_Data` vào ZIP.

Thứ tự ưu tiên **cấu hình hệ thống** khi chạy Development: **biến môi trường → user-secrets → appsettings.Local.json → appsettings.Development.json → appsettings.json**. Vì vậy, nếu sửa local mà không có tác dụng, kiểm tra biến môi trường, user-secrets và xem tài khoản có đang dùng key cá nhân hay không. Khởi động lại ứng dụng sau khi thay file cấu hình hệ thống; lựa chọn cá nhân trên web áp dụng ngay.

Cách thay thế bằng user-secrets (chạy tại thư mục solution; thay các giá trị mẫu trên máy riêng):

```powershell
dotnet user-secrets set "AI:Provider" "Gemini" --project src/AiInterview.Web
dotnet user-secrets set "Gemini:ApiKey" "YOUR_KEY" --project src/AiInterview.Web
dotnet user-secrets set "Gemini:Model" "MODEL_AVAILABLE_TO_YOUR_ACCOUNT" --project src/AiInterview.Web
dotnet user-secrets set "Seed:Password" "YOUR_OWN_STRONG_PASSWORD" --project src/AiInterview.Web
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "YOUR_SQL_CONNECTION_STRING" --project src/AiInterview.Web
```

Các lệnh có giá trị bí mật có thể nằm trong lịch sử terminal; `Configure.ps1` là cách nhập ẩn thuận tiện hơn. User-secrets chỉ được nạp trong Development. Trong môi trường triển khai, dùng kho bí mật của hệ thống hosting hoặc biến môi trường như `AI__Provider`, `OpenAI__ApiKey`, `OpenAI__Model`, `Gemini__ApiKey`, `Gemini__Model`, `ConnectionStrings__DefaultConnection`.

| Cấu hình | Ý nghĩa |
|---|---|
| `Gemini:MaxRequestsPerMinute` / `OpenAI:MaxRequestsPerMinute` | Hạn mức mỗi user, mặc định 20 lần gọi/phút, gồm lần thử lại |
| `Gemini:TimeoutSeconds` / `OpenAI:TimeoutSeconds` | Thời gian chờ mỗi lần gọi, mặc định 90 giây |
| `Gemini:MaxRetries` / `OpenAI:MaxRetries` | Số lần thử lại lỗi có thể phục hồi/JSON sai, mặc định 2 |
| `Gemini:MaxOutputTokens` / `OpenAI:MaxOutputTokens` | Giới hạn đầu ra, cấu hình bàn giao là 16384 |
| `Gemini:IsFreeTier` | Chỉ bật nếu xác định tài khoản/model thực sự thuộc gói miễn phí |
| `Gemini:InputPricePerMillionTokensUsd`, `Gemini:OutputPricePerMillionTokensUsd` | Bảng giá để tính chi phí ước tính; mặc định chưa đặt |
| `Database:AutoMigrate` | Tự áp dụng migration khi khởi động; mặc định chỉ bật trong Development |
| `Seed:Enabled` | Tạo dữ liệu mẫu/danh mục; mặc định chỉ bật trong Development |

Khi chưa có đủ token hoặc bảng giá, báo cáo hiển thị **chi phí chưa xác định**, không tự xem là miễn phí. Token được ghi theo từng lần gọi, kể cả lần thử lại; không ghi nội dung CV, JD hoặc transcript vào nhật ký yêu cầu AI.

## Chức năng và cách dùng

### Sinh viên

- **Hồ sơ · CV · JD:** cập nhật trường/chuyên ngành/năm học; upload PDF có text hoặc DOCX. Giới hạn 5 MB, tối đa 50 trang PDF và 50.000 ký tự text; chưa hỗ trợ OCR cho PDF scan. Chỉ text trích xuất được lưu, không giữ file CV gốc. Phân tích kỹ năng/dự án/kinh nghiệm là thao tác AI riêng có xác nhận gửi dữ liệu.
- **JD:** dán nội dung tin tuyển dụng, thêm link nguồn nếu có. Ứng dụng không tự tải nội dung từ link. Chọn CV để so sánh bằng chứng kỹ năng với JD; lưu cả CV đã dùng cho lần so sánh. Khi tạo buổi với đúng cặp CV/JD đã phân tích, kết quả skill gap được chụp vào buổi để định hướng câu hỏi. Đổi sang CV khác cần phân tích lại khoảng trống cho cặp mới.
- **Luyện phỏng vấn:** chọn vị trí, Intern/Fresher, Việt/Anh, văn bản/giọng nói, loại kỹ thuật/HR/dự án/JD/tổng hợp/code, persona và 5–10 câu chính. Phỏng vấn dự án cần CV, phỏng vấn theo JD cần JD. Xác nhận gửi CV/JD được chọn và câu trả lời đến nhà cung cấp đang cấu hình. Chủ đề và dạng câu hỏi được luân phiên; câu gần trùng trong buổi hoặc với 20 câu chính gần đây cùng vị trí/ngôn ngữ bị từ chối để tạo lại.
- **Phòng phỏng vấn:** bấm Bắt đầu; trả lời từng câu; AI có thể hỏi thêm tối đa 2 lượt trong một nhóm câu chính trước câu chính cuối. Đủ số câu chính thì chuyển sang báo cáo, không cần AI tạo lời kết. Có đồng hồ, tự lưu nháp lên server, bỏ qua, kết thúc sớm, thử lại khi AI lỗi và tiếp tục từ lịch sử. Chỉ tin nháp đã hiển thị lưu thành công; nội dung chưa kịp gửi khi mất kết nối có thể chưa có trên server.
- **Giọng nói:** Web Speech API với `vi-VN`/`en-US`, có đọc câu hỏi bằng TTS. Mở **Giọng đọc và tốc độ** để chọn giọng và tốc độ phù hợp; ứng dụng chỉ chọn giọng cùng ngôn ngữ, chuẩn hóa ký hiệu kỹ thuật phổ biến khi đọc và chờ câu hỏi được kiểm tra xong mới phát. Nếu thiếu giọng phù hợp, giao diện thông báo. Quyền microphone và hỗ trợ của trình duyệt quyết định khả năng nhận dạng; vẫn có thể nhập văn bản. Browser có thể dùng dịch vụ nhận dạng của nhà cung cấp; ứng dụng không lưu audio.
- **Báo cáo:** điểm 5 tiêu chí, trích dẫn câu trả lời, điểm mạnh/yếu, câu trả lời viết lại, lý do sửa, verdict, readiness và kế hoạch luyện tập. Phần cách nói được tính từ transcript/sự kiện trình duyệt, độc lập với điểm nội dung.
- **Tổng quan/Lịch sử:** lọc theo vị trí/ngôn ngữ/cấp độ khi có hỗ trợ, biểu đồ điểm và delivery, heatmap kỹ năng, gợi ý câu hỏi và nút Luyện điểm yếu. Timeline cho đánh dấu câu tốt/cần luyện, so sánh hai buổi cạnh nhau.
- **Lớp của tôi:** nhập mã lớp và đồng ý chia sẻ lịch sử với giảng viên; làm bài tập được giao, nộp buổi đủ điều kiện, đọc nhận xét.

### Giảng viên

Tạo lớp, chia sẻ mã lớp, giao bài có hạn nộp theo UTC+7 và cấu hình phỏng vấn cố định. Xem dashboard lớp, tiến bộ từng sinh viên, kỹ năng yếu, báo cáo và thêm nhận xét. Cảnh báo hiện khi không luyện ít nhất 14 ngày hoặc 3 buổi hoàn chỉnh gần nhất đều dưới 5 điểm. Xuất **CSV UTF-8 mở được bằng Excel**, chưa có xuất workbook `.xlsx` riêng.

Buổi nộp bài phải hoàn thành đầy đủ sau khi bài được tạo, đúng vị trí/cấp độ/ngôn ngữ/loại/số câu và phiên bản rubric. Mỗi buổi chỉ nộp một lần cho một bài tập; có thể luyện buổi khác để nộp lại. Bài nộp sau hạn được ghi là trễ. Lớp đóng giữ lịch sử nhưng không nhận tham gia/nộp bài mới.

### Quản trị viên

Tạo tài khoản, sửa họ tên/email, quản lý vai trò và trạng thái khóa; phiên đăng nhập cũ bị thu hồi sau đổi quyền/thông tin. Chỉ xóa được tài khoản chưa có dữ liệu học tập/giảng dạy; tài khoản đã có lịch sử có thể bị khóa để bảo toàn dữ liệu. Không được tự xóa/khóa hoặc làm mất quản trị viên cuối đang hoạt động. Quản lý vị trí, câu hỏi, tag kỹ năng, loại công ty và rubric. Xóa vị trí đã được dùng sẽ chuyển sang ngừng hoạt động để giữ lịch sử. Thay trọng số/tiêu chí tạo **phiên bản rubric mới**; buổi luyện và bài tập cũ vẫn dùng phiên bản của chúng.

## Cách tính điểm và giới hạn diễn giải

Mỗi lượt có 5 điểm 0–10: chính xác, độ sâu, cấu trúc, liên quan, ngôn ngữ. Trọng số ban đầu lần lượt là **30/20/20/20/10**; tổng có trọng số được tính trên server. Các lượt chính/phụ cùng nhóm lấy trung bình, sau đó cộng điểm các nhóm và chia cho **số câu chính đã chọn**. Câu bỏ qua/chưa hỏi đóng góp 0, không giúp tăng điểm bằng cách kết thúc sớm.

Readiness là `round(điểm tổng × 10)`, một ước lượng luyện tập cho vị trí đã chọn. Verdict do code xác định: từ 8,5 là Strong Hire; từ 7 là Hire; từ 5 là Lean No Hire; thấp hơn là No Hire. AI giải thích bằng dẫn chứng, không được tự thay tổng điểm. Kết quả thiếu câu/kết thúc sớm được gắn tạm thời. Biểu đồ và heatmap tiến bộ chính chỉ dùng buổi hoàn chỉnh, không tạm thời; dashboard xét tối đa 100 buổi gần nhất, chọn điểm yếu từ tối đa 20 buổi cùng bối cảnh.

Delivery là **ước tính** từ sự kiện browser: số từ tách bằng khoảng trắng, tốc độ từ/phút, thời gian trước khi nói, số khoảng dừng ít nhất 2 giây và từ đệm. Với tiếng Việt, đây không phải tách từ ngôn ngữ học; “like” có thể là từ đệm hoặc từ dùng bình thường. Khi không có telemetry giọng nói, số đo nói hiển thị “—”, không bịa số. Không phân tích âm sắc/ngữ điệu hay lưu bản thu.

Code được soạn bằng Monaco và gửi nhà cung cấp AI đã chọn review về đúng/sai, độ phức tạp, trường hợp biên và cách viết. **Không thực thi code, không có Judge0**. Đây là hệ thống luyện tập, không cung cấp đáp án để gian lận phỏng vấn thật.

## Kiến trúc source

```text
AiInterview.sln
Configure.ps1 / Configure-Email.ps1 / Import-EmailFromEduHouse.ps1 / Run.ps1
src/
  AiInterview.Domain/          Entity và enum nghiệp vụ
  AiInterview.Application/     DTO, interface AI/CV, prompt/rubric, delivery analytics
  AiInterview.Infrastructure/  Gemini/OpenAI REST/SSE, PdfPig/OpenXML, EF/Identity/migrations/seed
  AiInterview.Web/             MVC controllers/views/viewmodels, service nghiệp vụ, JS/CSS
tests/AiInterview.Tests/       Kiểm thử quy tắc, protocol, CV, telemetry, persistence, CSV
docs/                         ERD, hướng dẫn demo, hồ sơ kiểm chứng
```

Controller xử lý HTTP/phân quyền, service xử lý nghiệp vụ; EF Core là lớp truy cập dữ liệu. Bootstrap, Chart.js và Monaco được đóng gói trong `wwwroot/lib`, không cần tải CDN khi sử dụng. Các nhà cung cấp AI vẫn cần Internet, NuGet cần Internet cho lần restore đầu.

`IAiService` nằm ở Application; Gemini và OpenAI có adapter riêng, được chọn qua `AI:Provider`. Muốn thêm nhà cung cấp khác thì thêm adapter, ánh xạ schema/streaming/usage và đăng ký DI trong `Program.cs`. `ISpeechToText` hiện là **hợp đồng JavaScript** trong `speech.js`, adapter hiện tại dùng Web Speech API; STT phía server chưa được triển khai.

Xem [ERD và giải thích dữ liệu](docs/ERD.md), [kịch bản demo và dữ liệu giả](docs/DEMO.vi.md), [kiểm chứng/nghiệm thu](docs/VERIFICATION.md).

## Build, test và migration

```powershell
# Chỉ restore và build, không mở web, không thay đổi database
.\Run.ps1 -NoLaunch

# Restore, build, áp dụng migrations + seed theo cấu hình, không mở web
.\Run.ps1 -MigrateOnly

# Chạy toàn bộ kiểm thử
dotnet test AiInterview.sln --nologo
```

Kiểm thử JavaScript cho các tình huống bất đồng bộ (cần Node.js; không cần Node để chạy web):

```powershell
node --test tests/browser/*.test.cjs
```

Bộ giọng đọc bổ sung **13 kiểm thử chức năng**: chờ danh sách giọng tải xong, chọn đúng ngôn ngữ, lưu giọng/tốc độ, xử lý thiếu giọng, chia câu không cắt từ, ngừng callback cũ và lỗi trình duyệt. Chỉ đọc câu hỏi đã được server kiểm tra, không đọc mảnh stream chưa hoàn tất. **Chất lượng phát âm thực tế vẫn phụ thuộc giọng của trình duyệt/thiết bị và cần người nghe xác nhận.** Trong phòng luyện, mở **Giọng đọc và tốc độ** để chọn giọng phù hợp; nếu máy thiếu giọng đúng ngôn ngữ, ứng dụng thông báo thay vì dùng nhầm giọng mặc định.

Các bài kiểm thử phòng luyện kiểm tra transcript cuối khi dừng microphone, khóa thao tác khi gửi, callback nhận dạng đến muộn, lưu nháp tuần tự, Monaco tải chậm, SSE chia nhỏ, browser từ chối lưu nháp cục bộ, không đọc nội dung chưa được kiểm tra và tiến độ hoàn tất. Chúng dùng môi trường browser giả lập để kiểm tra quy tắc, không thay cho thử microphone thật.

Năm migration `InitialCreate`, `AddSkillGapSnapshot`, `AddSessionAiProvider`, `AddAccountEmailConfirmation` và `AddPersonalAiSettings` được giao trong `src/AiInterview.Infrastructure/Persistence/Migrations`. Migration thứ ba ghi nhà cung cấp AI của từng buổi; các buổi cũ được gán Gemini. Migration thứ tư thêm quy tắc xác nhận email và thời điểm yêu cầu gửi thư, giữ nguyên trạng thái xác nhận của tài khoản cũ. Migration **`20261013105437_AddPersonalAiSettings`** tạo bảng `UserAiSettings` để lưu kết nối cá nhân đã mã hóa; tài khoản chưa thiết lập vẫn dùng cấu hình hệ thống. Áp dụng bằng `Run.ps1 -MigrateOnly` trước khi chạy bản mới. Dùng CLI EF khi cần xuất script hoặc phát triển migration mới:

```powershell
dotnet tool restore
dotnet ef migrations script --idempotent --project src/AiInterview.Infrastructure --startup-project src/AiInterview.Web --output migration.sql
```

Lệnh xuất script không áp dụng thay đổi vào DB. Nếu dùng `dotnet ef database update`, cần lưu ý `DesignTimeDbContextFactory` đọc cùng thứ tự cấu hình runtime; đặt `ASPNETCORE_ENVIRONMENT=Development` khi dùng cấu hình Development/user-secrets. Dùng `Run.ps1 -MigrateOnly` để áp dụng đúng chuỗi kết nối runtime đã cấu hình. Không xóa database để xử lý lỗi migration khi chưa xác định nguyên nhân.

## Lỗi thường gặp

| Hiện tượng | Nhóm lỗi | Cách kiểm tra |
|---|---|---|
| Không tìm thấy `dotnet` hoặc thiếu `Microsoft.AspNetCore.App 8` | Môi trường | Cài SDK và ASP.NET Core Runtime 8, mở lại terminal; kiểm tra `dotnet --list-sdks`, `dotnet --list-runtimes` |
| Restore thất bại | Mạng/NuGet | Kiểm tra mạng/proxy và thông báo package bị lỗi; chạy lại khi truy cập NuGet được |
| Không kết nối được SQL Server/LocalDB | Cấu hình | Kiểm tra instance hoạt động, connection string, quyền tạo database/đăng nhập; dùng Configure để chọn đúng server |
| “Address already in use” tại 5088 | Môi trường | Dừng lần chạy cũ do bạn mở; không khởi động hai app cùng cổng |
| Không đăng nhập được tài khoản mẫu | Cấu hình/tài khoản | Đặt Seed:Password trước khi seed lần đầu; đổi giá trị này không reset tài khoản cũ; sau 5 lần sai có thể bị khóa 10 phút |
| Đăng ký báo chờ thiết lập email | Cấu hình email | Chạy `Configure-Email.ps1` trên máy riêng rồi khởi động lại; tài khoản cũ vẫn đăng nhập được |
| Tài khoản mới chưa đăng nhập được hoặc chưa nhận thư | Xác nhận email | Kiểm tra hộp thư/thư rác, mở liên kết xác nhận; gửi lại sau ít nhất 60 giây. Xem `docs/EMAIL.vi.md` nếu SMTP hoặc URL sai |
| Đã có key nhưng AI vẫn lỗi | Dịch vụ/cấu hình | Mở Cài đặt AI, kiểm tra nguồn key/model và bấm Kiểm tra kết nối đã lưu; bước này thử tạo mẫu và hiển thị nguyên nhân lỗi trước khi quay lại buổi |
| Key cá nhân không đọc được sau khi chuyển máy/thư mục | Khóa mã hóa | Khôi phục đúng Data Protection key ring và danh tính ứng dụng hoặc nhập lại key tại Cài đặt AI; xem docs/AI-SETTINGS.vi.md |
| Hết tín dụng hoặc chạm giới hạn chi tiêu | Hạn mức tài khoản | Kiểm tra Billing/Limits của nhà cung cấp và xử lý số dư/hạn mức, hoặc chủ động chuyển nhà cung cấp trong Cài đặt AI. Bấm thử lại liên tục không khôi phục hạn mức |
| Gọi quá nhanh, timeout hoặc JSON AI không hợp lệ | Dịch vụ | Giữ nguyên buổi, chờ theo thông báo rồi thử lại. Câu trả lời đã lưu và các lượt đã chấm được giữ |
| Microphone không hoạt động | Browser/thiết bị | Dùng localhost hoặc HTTPS, cấp quyền microphone, kiểm tra thiết bị và hỗ trợ Web Speech; chuyển sang nhập văn bản |
| Giọng đọc không phù hợp hoặc không có giọng | Browser/thiết bị | Mở **Giọng đọc và tốc độ**, chọn giọng Việt/Anh phù hợp và điều chỉnh tốc độ; giọng khả dụng phụ thuộc trình duyệt và hệ điều hành |
| Buổi cũ yêu cầu xác nhận sau đổi AI | Cấu hình/đồng ý dữ liệu | Kiểm tra tên nhà cung cấp mới và xác nhận trong phòng luyện nếu muốn gửi dữ liệu buổi đó để tiếp tục |
| PDF không trích xuất được | Định dạng | Dùng PDF chọn/copy được chữ hoặc DOCX; PDF scan/mật khẩu/file hỏng cần xuất lại |
| Không có buổi để nộp bài | Quy tắc nghiệp vụ | Mở bài tập và tạo buổi theo cấu hình của bài; hoàn thành mọi câu chính, không kết thúc sớm |
| Sửa config mà giá trị không đổi | Ưu tiên cấu hình | Kiểm tra environment/user-secrets ghi đè, rồi khởi động lại app |

Khi gửi ảnh lỗi để được hỗ trợ: gửi trang đang thao tác, các bước tái hiện, phần thông báo có **file và dòng**, cùng phiên bản SDK/SQL/browser. Che key, mật khẩu, connection string có mật khẩu và nội dung CV thật. Lỗi `CS...` kèm file/dòng thường là build/code; lỗi runtime cần đối chiếu stack trace với file/dòng và thông báo gốc trước khi kết luận. Lỗi SQL/quota/quyền microphone thường là môi trường hoặc cấu hình, không phải bằng chứng code sai.

## Triển khai ngoài máy cá nhân

Dùng `dotnet publish src/AiInterview.Web -c Release` và cấu hình host ASP.NET Core phù hợp. Đặt Production, HTTPS, host hợp lệ trong `AllowedHosts`, connection string và API key hệ thống ở cấu hình bí mật. Dùng SQL Server riêng với tài khoản ít quyền cần thiết. **Sao lưu database cùng Data Protection key ring** và bảo toàn danh tính ứng dụng/quyền giải mã khi chuyển máy hoặc thư mục; chỉ sao lưu SQL không đủ để khôi phục API key cá nhân. Xem [vận hành khóa mã hóa](docs/AI-SETTINGS.vi.md). Áp dụng migration có kiểm soát trước khi mở ứng dụng, không bật tự migrate trên nhiều instance cùng lúc.

Đặt `Seed:Enabled=false`, `Database:AutoMigrate=false` cho vận hành thông thường; cấp tài khoản quản trị theo quy trình của môi trường, không công bố tài khoản demo. Nếu bootstrap role/danh mục bằng seed, chủ động bật seed cho bước migration rồi tắt lại; seed Production không tạo tài khoản demo.

Khi chạy nhiều instance, cần quota/khóa dùng chung để bảo vệ hạn mức tổng của cùng API key; limiter trong source dùng semaphore trong một tiến trình kết hợp nhật ký SQL và không thay thế một bộ điều phối quota phân tán. Nếu đặt sau reverse proxy, cấu hình HTTPS/forwarded headers và kiểm thử SSE không bị buffering/timeout, giới hạn theo đúng IP khách. Kiểm thử lại đăng nhập, quyền truy cập, upload, microphone và nhà cung cấp AI đã chọn tại URL triển khai thực tế trước khi nghiệm thu.
