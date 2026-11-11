> **Cập nhật 10/11/2026:** xem [đối chiếu bảo mật](SECURITY-REVIEW-2026-11-10.vi.md). Cấu hình bí mật hiện lưu bằng user-secrets ngoài source; ZIP mới không kèm mật khẩu/email/key. Những ghi nhận kiểm thử theo ngày cũ bên dưới là lịch sử, không xác nhận cho bản hiện tại.

# Nhập API key trong InterviewLab

Sau khi đăng nhập, chọn **Cài đặt AI** ở thanh bên hoặc mở `/AiSettings`. Trang này dành cho mọi tài khoản đã đăng nhập; thay đổi chỉ áp dụng cho tài khoản đang dùng.

## Bắt đầu

1. Chọn **Google Gemini · Có hạn mức miễn phí** trong phần **Thêm kết nối cá nhân**.
2. Tạo key tại [Google AI Studio](https://aistudio.google.com/apikey), rồi dán vào ô **API key**. Để dùng hạn mức miễn phí, chọn dự án thuộc gói miễn phí của Google AI Studio.
3. Chọn trong **Chọn model có hạn mức miễn phí** hoặc tự sửa ô **Tên model · có thể tự nhập**. Danh sách chỉ gợi ý model Gemini có free tier; model riêng đã lưu vẫn được giữ dù không nằm trong gợi ý. Model tự nhập ngoài danh sách không được xác nhận miễn phí.
4. Bấm **Lưu & kiểm tra model**. Web lưu rồi thử tạo mẫu bằng đúng key/model vừa lưu. Nếu chỉ muốn lưu mà chưa gọi API, bấm **Chỉ lưu**. Không cần khởi động lại web.
5. Đọc kết quả rồi **Quay lại việc đang làm** hoặc **Tiếp tục luyện phỏng vấn**. Nút **Kiểm tra kết nối đã lưu** ở đầu trang giúp kiểm tra lại cấu hình đang dùng. Nếu đi từ buổi luyện/CV, đường quay lại trang đó được giữ.

Danh sách tham khảo đối chiếu ngày 16/10/2026 gồm Gemini 3.5 Flash-Lite, 3.8 Flash, 3.6 Flash và 3.1 Flash-Lite có hạn mức miễn phí cho tài khoản đủ điều kiện theo [bảng giá Google](https://ai.google.dev/gemini-api/docs/pricing). Dự án trả phí vẫn áp dụng giá của gói trả phí; chọn nhãn này không chuyển gói thanh toán và không bảo đảm còn quota. Không có gợi ý OpenAI hoặc model trả phí trong danh sách chọn nhanh. Gợi ý không tự gọi API hoặc tự đổi lựa chọn đã lưu.

Nếu đã có key OpenAI từ bản cũ, mục **Nâng cao · Kết nối cũ có phí** cho phép quản lý lại kết nối đó. Kết nối OpenAI đang dùng vẫn được giữ nguyên cho đến khi bạn chủ động lưu Gemini; ứng dụng không xóa key hoặc tự chuyển nhà cung cấp. OpenAI cũ chỉ có ô model tự nhập và nhãn có phí, không có danh sách gợi ý miễn phí.

Model tự nhập cần hỗ trợ tạo văn bản và JSON có cấu trúc. Danh sách không phải toàn bộ model tài khoản có quyền sử dụng. Bấm **Lưu & kiểm tra model** để xác nhận tại thời điểm dùng; nếu lỗi, lựa chọn vẫn được lưu để bạn chỉnh lại trên web.

Ô API key luôn để trống khi mở lại trang. Nút **Hiện key** chỉ hiện nội dung bạn vừa nhập; không lấy lại key đã lưu. Khi đổi nhà cung cấp trong ô chọn, nội dung key chưa lưu được xóa để tránh lưu nhầm sang bên khác. Bạn có thể lưu key/model cho cả hai nhà cung cấp rồi chọn một để sử dụng.

## Giữ, thay và xóa key

| Thao tác | Kết quả |
|---|---|
| Lưu với ô key để trống | Giữ key đã lưu của nhà cung cấp đang chọn; nếu chưa có key dùng được thì cần nhập key |
| Nhập key mới rồi lưu | Thay key của nhà cung cấp đang chọn và chuyển sang kết nối cá nhân đó |
| Chọn nhà cung cấp đã lưu key, để trống key rồi lưu | Chuyển sang key/model của nhà cung cấp đó |
| Xóa key của nhà cung cấp chưa chọn | Kết nối đang dùng giữ nguyên |
| Xóa key cá nhân đang dùng | Kết nối đó thiếu key; ứng dụng yêu cầu nhập lại, không tự dùng key hệ thống |
| Chuyển sang cấu hình hệ thống | Dùng nhà cung cấp/model/key do người vận hành đặt sẵn; các key cá nhân vẫn được giữ để chuyển lại |

Lịch sử học tập không bị xóa khi đổi hoặc xóa key. Xóa trong InterviewLab chỉ gỡ bản key ứng dụng lưu; muốn thu hồi key ở nhà cung cấp, thực hiện trong tài khoản Google/OpenAI của bạn.

Nếu chưa từng chọn kết nối cá nhân, tài khoản dùng cấu hình hệ thống hiện có. Nếu hệ thống chưa có key, trang hướng dẫn nhập key cá nhân. Mỗi tài khoản chỉ đọc/đổi cài đặt của chính mình; không có thao tác sửa key người khác ở trang này.

## Kiểm tra kết nối xác nhận điều gì?

Nút **Kiểm tra kết nối đã lưu** sử dụng cấu hình ở phần **Đang sử dụng**, không dùng key/model chưa bấm lưu. Ứng dụng yêu cầu AI tạo một phản hồi mẫu ngắn theo cấu trúc định sẵn, qua cùng kiểu kết nối nhận nội dung từng phần của phòng phỏng vấn. Lần kiểm tra này có thể sử dụng một ít hạn mức hoặc phát sinh phí API; **không gửi CV, JD hay câu trả lời phỏng vấn**.

Kết quả thành công có nghĩa key và model đã tạo được phản hồi mẫu hợp lệ **tại thời điểm kiểm tra**. Kết quả này không bảo đảm chất lượng toàn bộ buổi phỏng vấn hoặc đủ hạn mức cho các lượt tiếp theo. Nếu có lỗi, đọc nguyên nhân trên web, xử lý rồi kiểm tra lại trước khi quay về buổi đang dở.

Về kỹ thuật, yêu cầu mẫu dùng `POST` đến [OpenAI Responses API](https://developers.openai.com/api/reference/resources/responses/methods/create) hoặc [Gemini streamGenerateContent](https://ai.google.dev/api/generate-content), bật chế độ streaming và yêu cầu JSON `{"ready":true}`. Server chỉ báo thành công khi nhận đủ phản hồi và kiểm tra đúng cấu trúc, không chỉ dựa vào mã HTTP thành công. Mỗi lần bấm gửi một yêu cầu, không tự thử lại; giới hạn đầu ra 512 token, thời gian 45 giây và dữ liệu nhận 65.536 ký tự.

| Thông báo | Việc cần làm |
|---|---|
| Key chưa hợp lệ hoặc chưa có quyền | Kiểm tra đúng nhà cung cấp, key còn hiệu lực và quyền model |
| Không tìm thấy model | Kiểm tra tên model trong tài khoản nhà cung cấp rồi lưu lại |
| Hết tín dụng hoặc chạm giới hạn chi tiêu/hạn mức tài khoản | Kiểm tra mục thanh toán và giới hạn của nhà cung cấp; bổ sung tín dụng hoặc điều chỉnh giới hạn phù hợp trước khi thử lại. Bấm lại liên tục không khôi phục hạn mức |
| Gọi quá nhanh hoặc nhà cung cấp đang bận | Chờ theo thông báo rồi thử lại; không cần tạo buổi mới |
| Chưa kết nối được hoặc mất quá lâu | Kiểm tra mạng; cấu hình đã lưu vẫn được giữ |
| Không đọc được key đã lưu | Nhập lại key hoặc nhờ người vận hành khôi phục đúng khóa mã hóa |

Với OpenAI, lỗi `credit_balance_exhausted` nghĩa tài khoản API đã hết tín dụng. Mở OpenAI Platform, kiểm tra **Billing** và **Limits**, rồi xử lý số dư/hạn mức hoặc chủ động chọn Gemini tại trang này. Không cần tạo buổi phỏng vấn mới; sau khi kết nối dùng được, quay lại buổi đang dở.

## Đổi nhà cung cấp và dữ liệu học tập

Lựa chọn cá nhân được áp dụng ở yêu cầu tiếp theo. Buổi đang dở lưu nhà cung cấp bạn đã đồng ý lúc tạo; khi đổi sang nhà cung cấp khác, phòng luyện yêu cầu xác nhận lại trước khi gửi nội dung buổi đó. Nếu một tab tạo buổi hoặc phân tích CV/JD đang mở từ trước khi đổi, cần kiểm tra và xác nhận lại nhà cung cấp mới.

Key và model cá nhân không đổi cấu hình hệ thống, tài khoản khác hoặc các bản báo cáo đã lưu. AI không tự chuyển nhà cung cấp khi gặp lỗi.

## Cập nhật database

Bản này thêm migration **`20261013105437_AddPersonalAiSettings`**, tạo bảng `UserAiSettings` liên kết một-một tùy chọn với `AspNetUsers`. Bảng lưu nguồn cấu hình đang chọn, tên nhà cung cấp, model, hai key đã mã hóa, thời điểm cập nhật và `RowVersion` để kiểm soát thay đổi đồng thời.

Dừng ứng dụng, mở PowerShell tại thư mục có `AiInterview.sln`, giữ nguyên cấu hình cục bộ và chạy:

```powershell
.\Run.ps1 -MigrateOnly
.\Run.ps1
```

Migration không sao chép key hệ thống vào từng tài khoản. Tài khoản cũ tiếp tục dùng cấu hình hệ thống cho đến khi chọn kết nối cá nhân. Bộ sơ đồ/SQL bàn giao trước tính năng này là ảnh chụp phiên bản cũ; [ERD.md](ERD.md) và migrations trong source phản ánh bảng mới.

## Lưu trữ và sao lưu cho người vận hành

API key cá nhân được ASP.NET Core Data Protection bảo vệ trước khi ghi vào SQL, gắn với mã tài khoản và nhà cung cấp. Trang chỉ nhận trạng thái có key/không có key, không trả lại key đã lưu. Các thao tác thay đổi yêu cầu đăng nhập, có chống giả mạo biểu mẫu và giới hạn tần suất. Dùng HTTPS khi triển khai ngoài localhost.

**Sao lưu database cùng Data Protection key ring.** Ứng dụng hiện dùng nơi lưu khóa mặc định của ASP.NET Core theo môi trường. Khi chạy Windows có hồ sơ người dùng, nơi thường dùng là `%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, được bảo vệ bằng DPAPI. Không đưa key ring vào ZIP source hoặc xóa khóa cũ còn cần để giải mã. Xem [cơ chế lưu khóa của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-8.0).

Khi chuyển máy, tài khoản chạy dịch vụ hoặc thư mục triển khai, cần giữ đúng danh tính ứng dụng và quyền giải mã key ring. Chỉ chép database và các tệp khóa chưa đảm bảo giải mã được trên môi trường mới. Với hosting/container, người vận hành cần cấu hình kho khóa bền vững ngoài thư mục publish; các instance dùng chung cấu hình bảo vệ phù hợp. Xem [cấu hình Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-8.0). Nếu không khôi phục được khóa, người dùng nhập lại API key; ứng dụng không tự chuyển sang key hệ thống.

Bản bàn giao 10/11/2026 không chứa cấu hình bí mật. Script cấu hình lưu vào .NET user-secrets ngoài source; biến môi trường ưu tiên cao hơn. Key cá nhân trong SQL tiếp tục được mã hóa. Không chia sẻ file local cũ, thư mục UserSecrets, key ring hoặc backup database.
