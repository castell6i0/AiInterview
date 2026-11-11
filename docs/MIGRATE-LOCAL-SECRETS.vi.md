# Chuyển cấu hình riêng ra khỏi source

`Migrate-LocalSecrets.ps1` là tiện ích chạy thủ công, không tự chạy khi mở website hoặc giải nén ZIP.

Chạy từ thư mục gốc project:

```powershell
.\Migrate-LocalSecrets.ps1
```

Mặc định chuyển đúng `src\AiInterview.Web\appsettings.Local.json`. Để chuyển cấu hình từ một bản source cũ, truyền đường dẫn rõ ràng:

```powershell
.\Migrate-LocalSecrets.ps1 -LocalPath 'C:\DUONG-DAN-CU\AiInterview\src\AiInterview.Web\appsettings.Local.json'
```

Tiện ích đọc JSON trong bộ nhớ, hợp nhất vào user-secrets của `AiInterview-Local-2026` nằm ngoài source, và không in giá trị cấu hình. Giá trị đã có trong user-secrets được ưu tiên, kể cả giá trị rỗng/null; các khóa còn thiếu mới được thêm từ file local. Cả kiểu khóa `Email:Password` và object lồng nhau đều được hỗ trợ.

Sau khi kiểm tra file nguồn/đích chưa bị thay đổi trong lúc xử lý, tiện ích ghi file tạm ở cạnh user-secrets và thay thế nguyên tử. Nó đọc lại để xác minh dữ liệu đã lưu, kiểm tra lại file local, rồi mới xóa **đúng file appsettings.Local.json đã cung cấp**. Nếu JSON lỗi hoặc phát hiện thay đổi đồng thời, nó giữ file nguồn và báo lỗi trung tính. Không xóa thư mục, không quét hoặc sửa các bản source/ZIP khác.

Khởi động lại ứng dụng ở Development để đọc user-secrets. Biến môi trường vẫn có ưu tiên cao hơn. User-secrets lưu dưới dạng văn bản trên máy; không chia sẻ thư mục đó. Chuyển vị trí lưu không thu hồi API key đã lộ, không đổi mật khẩu tài khoản cũ và không xóa bí mật khỏi ZIP/bản sao đã gửi trước đây.

`Configure.ps1` cũng kiểm tra thay đổi đồng thời trước khi lưu, tương tự script cấu hình email, để không ghi đè dữ liệu được sửa trong một cửa sổ khác.

Kiểm thử an toàn:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\scripts\SecretStore.Tests.ps1
```

**9/9 kiểm thử đạt** trên Windows PowerShell. Mọi đường dẫn user-secrets được thay bằng fixture tạm trong bản sao script; chỉ dùng dữ liệu giả. Có kiểm tra hợp nhất/ưu tiên khóa, file đích chưa tồn tại, JSON hỏng, thay đổi đồng thời ở nguồn/đích, không xóa nhầm file bên cạnh, giữ cấu hình khác, không in giá trị nhạy cảm và dọn file tạm. Chưa chạy tiện ích chuyển đổi trên cấu hình thật của người dùng.
