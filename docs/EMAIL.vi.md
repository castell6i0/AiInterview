> **Cập nhật 10/11/2026:** xem [đối chiếu bảo mật](SECURITY-REVIEW-2026-11-10.vi.md). Cấu hình bí mật hiện lưu bằng user-secrets ngoài source; ZIP mới không kèm mật khẩu/email/key. Những ghi nhận kiểm thử theo ngày cũ bên dưới là lịch sử, không xác nhận cho bản hiện tại.

# Email đăng ký và đăng nhập

Khi SMTP và địa chỉ website công khai đã được thiết lập, InterviewLab gửi email xác nhận cho tài khoản tự đăng ký mới. Người dùng phải mở liên kết trong thư để xác nhận địa chỉ trước khi đăng nhập. Khi **chưa có SMTP**, nút đăng ký vẫn hoạt động: tài khoản mới đăng nhập ngay bằng mật khẩu nhưng **chưa xác minh email** (`EmailConfirmed` vẫn `false`). Để tránh người khác đăng ký bằng email không thuộc về họ trên website công khai, nên cấu hình email và HTTPS trước khi mời người dùng đăng ký.

Sau khi đăng nhập thành công, hệ thống gửi email thông báo đăng nhập. Đây là thông báo, không phải mã OTP hoặc bước xác thực thứ hai. Hiện chưa có chức năng quên/đặt lại mật khẩu qua email.

## Cấu hình Gmail trên máy của bạn

1. Dùng tài khoản Gmail hoặc Google Workspace mà bạn quản lý. Bật xác minh hai bước, sau đó [tạo Google App Password](https://myaccount.google.com/apppasswords) cho InterviewLab. Nhập App Password riêng này, không dùng mật khẩu đăng nhập Google. Google yêu cầu xác minh hai bước; một số tài khoản tổ chức hoặc tài khoản có Advanced Protection có thể không tạo được App Password. Xem [hướng dẫn chính thức của Google](https://support.google.com/accounts/answer/185833?hl=vi).
2. Mở PowerShell trong thư mục dự án và chạy:

   ```powershell
   .\Configure-Email.ps1
   ```

3. Nhập email người gửi và App Password (bỏ các khoảng trắng phân nhóm khi dán). Mật khẩu được nhập ẩn; script không in các giá trị cũ trong cấu hình. Enter giữ giá trị cũ hoặc giá trị mặc định; lần đầu bắt buộc có email và App Password. Ctrl+C hủy trước khi lưu.
4. Giữ tên người gửi `InterviewLab`, hoặc nhập tên khác. Đặt `PublicBaseUrl` theo địa chỉ người nhận sẽ dùng để mở website. Mặc định `http://localhost:5088` chỉ phù hợp khi mở liên kết trên chính máy đang chạy ứng dụng. Khi đưa lên máy chủ, dùng địa chỉ gốc HTTPS, ví dụ `https://interview.example.com`, không thêm đường dẫn, tham số hay dấu `#`.
5. Khởi động lại ứng dụng bằng `.\Run.ps1`.

Script đặt `Email:Enabled=true`, máy chủ `smtp.gmail.com`, cổng `587`, chế độ `StartTls`; `Username` và `FromAddress` đều là địa chỉ người gửi. Cấu hình này theo [hướng dẫn Gmail SMTP của Google](https://support.google.com/a/answer/176600?hl=vi). HTTP chỉ được chấp nhận cho địa chỉ loopback như `localhost`, `127.0.0.1` hoặc `[::1]`.

Các giá trị được lưu trong .NET user-secrets ngoài thư mục source, giữ các mục cấu hình khác. Script từ chối ghi đè nếu cấu hình thay đổi trong lúc nhập. Chỉ dùng Development; môi trường triển khai dùng kho bí mật hoặc biến môi trường. Không gửi thư mục UserSecrets cho người khác.

## Nhập cấu hình từ bản EduHouse đã cung cấp

Nếu có bản `EduHouse_fixed20.zip` của bạn trên máy, có thể nhập cấu hình Gmail thay vì gõ lại bằng `Configure-Email.ps1`. Dừng ứng dụng, mở PowerShell tại thư mục solution InterviewLab và chạy:

```powershell
.\Import-EmailFromEduHouse.ps1 -ArchivePath "E:\Edu House\EduHouse_fixed20.zip" -PublicBaseUrl "http://localhost:5088"
.\Run.ps1 -MigrateOnly
.\Run.ps1
```

`-ArchivePath` bắt buộc; thay đường dẫn nếu ZIP nằm ở nơi khác. `-PublicBaseUrl` mặc định là `http://localhost:5088`; khi triển khai, đặt địa chỉ gốc HTTPS mà người nhận mở được.

Script đọc `MailUser`/`MailPassword` từ cấu hình trong ZIP mà không giải nén, rồi lưu thành cấu hình email cục bộ của InterviewLab. Các mục AI/SQL/seed được giữ nguyên; script từ chối XML/giá trị không hợp lệ hoặc các cấu hình email mâu thuẫn. Mật khẩu không được in ra. Cơ chế Gmail SMTP cổng 587/TLS được giữ, ứng dụng tiếp tục dùng .NET 8 và MailKit cùng luồng xác nhận đăng ký/thông báo đăng nhập hiện có.

Bản ZIP source InterviewLab không chứa thông tin Gmail riêng. Trên máy khác, chạy lại lệnh nhập với ZIP EduHouse của bạn hoặc dùng `Configure-Email.ps1`. Nhập cấu hình thành công chưa chứng minh SMTP đăng nhập được hoặc thư đã đến hộp nhận.

## Kiểm tra hộp thư thật

Sau khi cấu hình, tự đăng ký bằng một hộp thư bạn sở hữu. Kiểm tra thư xác nhận (cả thư rác), mở liên kết, rồi đăng nhập và kiểm tra thư thông báo. Nếu chưa nhận thư xác nhận, dùng chức năng gửi lại trên trang đăng nhập. Hãy kiểm tra địa chỉ gốc của website nếu liên kết mở sai máy hoặc không truy cập được.

Nếu gửi thư thất bại, kiểm tra App Password, quyền dùng App Password của tài khoản, kết nối ra `smtp.gmail.com:587` và thư rác của người nhận. Sau khi đổi mật khẩu tài khoản Google, cần tạo App Password mới theo [hướng dẫn Google](https://support.google.com/accounts/answer/185833?hl=vi).

Trên máy hiện tại, cả bản phát triển và bản source dùng Visual Studio đã được nhập cấu hình Gmail từ ZIP EduHouse, kiểm tra **kết nối TLS/xác thực SMTP thành công**; không cần nhập lại. Lần kiểm tra dừng sau xác thực, không gửi thư. Vì vậy chưa xác minh Gmail nhận nội dung thư hoặc thư đến hộp nhận; bước đăng ký/xác nhận/đăng nhập bằng hộp thư của bạn ở trên sẽ kiểm tra phần đó.
