# Kết quả kiểm tra bản bàn giao — 10/11/2026

| Kiểm tra | Kết quả thực tế |
|---|---|
| Build trong lượt test cuối | Thành công, không có cảnh báo compiler/analyzer |
| .NET toàn solution, `AIINTERVIEW_TEST_SQL=1` | **399/399 đạt, không skip**; gồm database LocalDB thật, migration/rollback, giới hạn dữ liệu, quota đồng thời, quyền truy cập và các regression cũ |
| JavaScript | **56/56 đạt**; gồm autosave/backoff, countdown HTTP/SSE 429, giữ file/form/answer, hiển thị AI như văn bản |
| Script secrets trên Windows PowerShell 5.1 | **9/9 đạt**; fixture giả ngoài source, không dùng user-secrets thật |
| Khởi động ứng dụng thật bằng Kestrel | Đạt; trang chủ, đăng nhập, đăng ký, hướng dẫn và script mới trả 200 |
| Pipeline MVC thật | Vượt policy auth trả JSON 429 + Retry-After; body Answer quá lớn trả JSON 413 trước nghiệp vụ |
| `dotnet list package --vulnerable --include-transitive` | Không báo package có lỗ hổng trong toàn solution sau khi pin Pkcs 10.0.13 |
| Quét source | Không khớp mẫu credential, không có private config; chỉ có mẫu LocalDB công khai không chứa mật khẩu |
| Kiểm tra raw SQL | Đạt, 2 ngoại lệ hạ tầng được review bằng SHA-256; không có SQL mới ngoài allowlist |

Đã dùng dữ liệu giả. Không gọi API AI thật, không gửi mail thật, không chạy migration vào database người dùng. Smoke HTTP không chứng minh thư đến hộp thư hoặc quota bên nhà cung cấp còn hoạt động; đây là các phần phụ thuộc tài khoản/mạng.

Một lần test ban đầu bị thiếu instance test `AiInterviewDev`; đã tạo và khởi động instance test rồi chạy lại toàn bộ. Một test malformed JSON ban đầu kỳ vọng boolean thay vì JsonException; đã sửa đúng hợp đồng hiện có (provider bắt exception và trả lỗi an toàn), sau đó toàn bộ test đạt. Không bỏ qua test để đạt kết quả.

Chưa xác minh thu hồi/rotate key cũ, đổi mật khẩu tài khoản thật, spending limit hoặc lịch sử Git cũ. Chưa demo trực tiếp microphone/giọng đọc bằng thiết bị; script liên quan vẫn qua regression. Các giới hạn production và các mục hoãn được ghi rõ trong SECURITY-REVIEW-2026-11-10.vi.md.

Source mới không chứa thông tin gửi mail riêng. Người vận hành cấu hình user-secrets trên máy, hoặc dùng Migrate-LocalSecrets.ps1 chuyển file local cũ; xem MIGRATE-LOCAL-SECRETS.vi.md. Người dùng thông thường thao tác trên website, không cần sửa source.
