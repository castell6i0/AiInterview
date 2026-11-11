# Bàn giao InterviewLab bằng link website (không gửi source cho người dùng)

## Phần người dùng

Sau khi website được chạy trên một máy chủ công khai, người dùng chỉ cần mở **https://ten-mien-cua-ban/** trên trình duyệt; **không cần Visual Studio, SQL Server, API key trong source, hay bất kỳ file ZIP nào**. Họ có thể đăng ký, đăng nhập, chọn buổi phỏng vấn, trả lời và xem kết quả trên web. Nếu quản trị viên đã cấu hình API AI dùng chung cho máy chủ, sinh viên cũng **không cần tự nhập key**.

**Lưu ý:** `http://localhost:5088` chỉ mở được từ chính máy đang chạy ứng dụng, **không phải link công khai**. Source ZIP không tự tạo domain, host, email gửi thư hoặc tài khoản API AI. Người vận hành website phải làm các bước bên dưới **một lần**, không phải người dùng.

## Phần người vận hành website (một lần)

1. Chuẩn bị host chạy **ASP.NET Core 8** và **SQL Server** có thể truy cập từ ứng dụng. Có thể dùng Windows Server + IIS (ASP.NET Core Hosting Bundle), Azure App Service có SQL Server, hoặc hạ tầng tương đương. Bật domain và chứng chỉ **HTTPS**.
2. Publish **`src/AiInterview.Web`** bằng `dotnet publish src/AiInterview.Web/AiInterview.Web.csproj -c Release -o publish`; chuyển thư mục publish lên host, cấu hình process/IIS App Pool/HTTPS và tự khởi động khi máy chủ reboot. Không phát source ZIP hoặc file cấu hình chứa mật khẩu cho người dùng.
3. Tại host, cấu hình các thiết lập **bí mật phía máy chủ** bằng environment variables hoặc secret store, **không đưa key vào trang HTML/client hoặc file ZIP**:

   | Biến môi trường | Cách dùng |
   |---|---|
   | `ASPNETCORE_ENVIRONMENT` | `Production` |
   | `ConnectionStrings__DefaultConnection` | SQL Server thật, quyền đủ chạy và sử dụng DB |
   | `AllowedHosts` | Tên domain hợp lệ, ví dụ `interview.example.com` |
   | `AI__Provider` | `Gemini` hoặc `OpenAI` |
   | `Gemini__ApiKey` hoặc `OpenAI__ApiKey` | Key của **người vận hành** dùng chung cho sinh viên |
   | `Gemini__Model` hoặc `OpenAI__Model` | Model mà tài khoản API thật sự sử dụng được |
   | `Seed__Enabled` | `true` để tạo role và danh mục bắt buộc khi khởi tạo DB |

4. **Khởi tạo database** bằng lệnh `dotnet AiInterview.Web.dll --migrate` trong thư mục publish, sau khi đặt đủ biến môi trường. Trên môi trường Production, thao tác này tạo role/danh mục khi `Seed__Enabled=true`, **không** tạo sẵn tài khoản demo/mật khẩu admin. Nếu muốn tài khoản quản trị, cần tạo/cấp quyền qua quy trình triển khai do người vận hành kiểm soát; không tự nâng quyền tài khoản đăng ký công khai.
5. **Khuyến nghị bật xác minh email cho website công khai** bằng SMTP với `Email__Enabled=true`, `Email__Host`, `Email__Port`, `Email__Security`, `Email__Username`, `Email__Password`, `Email__FromAddress`, `Email__FromName` và `Email__PublicBaseUrl=https://ten-mien-cua-ban`. Cấu hình này chỉ có trên máy chủ. Khi chưa có SMTP, nút đăng ký vẫn bấm được, người dùng đăng nhập được nhưng email **chưa được xác minh**; người khác có thể đăng ký bằng địa chỉ email không thuộc họ. Cân nhắc kỹ trước khi mở đăng ký công khai.
6. Duy trì **ASP.NET Core Data Protection keys** lâu dài và dùng chung giữa các instance nếu chạy nhiều máy. Nếu mất key ring, dữ liệu key AI cá nhân đã mã hóa và phiên đăng nhập có thể không đọc được. Sao lưu SQL, giữ bảo mật connection string/API key/SMTP password, cấu hình logging, giới hạn sử dụng và quota API.
7. Kiểm tra trên thiết bị/mạng **khác máy chủ**: mở HTTPS/domain, đăng ký, đăng nhập, tạo buổi luyện, nhận câu hỏi thực tế, gửi trả lời và xem báo cáo. Nếu dùng email xác nhận, kiểm tra cả link email có dẫn đúng domain công khai. Kiểm tra tài khoản sinh viên không thể truy cập trang admin.

## Giao cho người dùng

Chỉ gửi địa chỉ HTTPS như **`https://ten-mien-cua-ban/`** và hướng dẫn sử dụng `/Home/Help`. **Không gửi source, appsettings chứa key, connection string hay thư mục publish**. Nếu AI báo lỗi, quản trị viên kiểm tra key/quota/model của cấu hình máy chủ; mỗi người dùng có thể chọn key cá nhân trong **Cài đặt AI** nếu chủ website cho phép.

> Bản ZIP chỉ là mã nguồn đã sửa, **không phải website đã được host lên Internet**. Chưa có domain/máy chủ thì chưa có link công khai để gửi cho người khác.
