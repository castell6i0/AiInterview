> **Cập nhật 10/11/2026:** xem [đối chiếu bảo mật](SECURITY-REVIEW-2026-11-10.vi.md). Cấu hình bí mật hiện lưu bằng user-secrets ngoài source; ZIP mới không kèm mật khẩu/email/key. Những ghi nhận kiểm thử theo ngày cũ bên dưới là lịch sử, không xác nhận cho bản hiện tại.

# Chọn nhà cung cấp AI và giọng đọc

## Chuyển Gemini hoặc OpenAI

Người dùng nhập key ngay tại **Cài đặt AI** trên website. Danh sách **Chọn model có hạn mức miễn phí** chỉ gợi ý Gemini; có thể tự sửa **Tên model · có thể tự nhập**, rồi bấm **Lưu & kiểm tra model** để lưu và thử tạo mẫu bằng cấu hình vừa chọn. **Chỉ lưu** không gọi API. Hạn mức miễn phí phụ thuộc tài khoản; dự án trả phí vẫn tính phí theo gói đang dùng. Kết nối OpenAI cũ được giữ để quản lý, không có trong danh sách model miễn phí. Xem [hướng dẫn cài đặt AI](AI-SETTINGS.vi.md).

Các bước dưới đây dành cho người vận hành cấu hình kết nối dùng chung.

1. Trong thư mục có `AiInterview.sln`, chạy `Configure.ps1`.
2. Ở câu hỏi nhà cung cấp, nhập `OpenAI` hoặc `Gemini`. Enter giữ lựa chọn hiện tại; source mới mặc định dùng Gemini.
3. Nhập API key của đúng nhà cung cấp. Key được nhập ẩn; Enter giữ key đã có của **nhà cung cấp vừa chọn**. Key của nhà cung cấp khác không bị xóa và không được dùng thay thế.
4. Enter ở mật khẩu mẫu và connection string nếu muốn giữ nguyên tài khoản/kết nối hiện có. Chọn model được tài khoản của bạn cấp quyền.
5. Khởi động lại ứng dụng, mở **Cài đặt AI** để xem đúng tên nhà cung cấp và model. Bấm **Kiểm tra kết nối đã lưu** để thử tạo phản hồi mẫu ngắn, rồi chạy một buổi ngắn với dữ liệu mẫu trước khi dùng CV cá nhân.

| Cấu hình | Gemini | OpenAI |
|---|---|---|
| `AI:Provider` | `Gemini` | `OpenAI` |
| Key | `Gemini:ApiKey` | `OpenAI:ApiKey` |
| Model | `Gemini:Model` | `OpenAI:Model` |
| Model mặc định | `gemini-3.1-flash-lite` | `gpt-4.1-mini` |
| Lấy key | [Google AI Studio](https://aistudio.google.com/apikey) | [OpenAI API keys](https://platform.openai.com/settings/organization/api-keys) |

`gpt-4.1-mini` có tài liệu chính thức tại [OpenAI Docs](https://developers.openai.com/api/docs/models/gpt-4.1-mini). Hạn mức, quyền truy cập và phí cần kiểm tra trên tài khoản của bạn. Phạm vi gọi API thật đã thực hiện được ghi trong [VERIFICATION.md](VERIFICATION.md). Bộ test adapter chỉ chứng minh xử lý giao thức, không chứng minh độ hay hoặc độ ổn định của model trong mọi buổi phỏng vấn.

Việc đổi nhà cung cấp không xóa lịch sử học tập. Ứng dụng không tự gửi nội dung sang nhà cung cấp khác khi gặp lỗi. Khi buổi đang dở được tiếp tục sau khi đổi nhà cung cấp, hãy đọc phần xác nhận gửi dữ liệu hiển thị trong ứng dụng.

Bản bàn giao 10/11/2026 không chứa cấu hình bí mật. Script cấu hình lưu vào .NET user-secrets ngoài source; biến môi trường ưu tiên cao hơn. Key cá nhân trong SQL tiếp tục được mã hóa. Không chia sẻ file local cũ, thư mục UserSecrets, key ring hoặc backup database.

## Khi AI lỗi giữa buổi

Câu trả lời đã được xác nhận lưu và các lượt chấm thành công vẫn giữ trong database. Nếu hết tín dụng hoặc chạm giới hạn chi tiêu, kiểm tra mục thanh toán/hạn mức của nhà cung cấp và xử lý trước khi thử lại; thử liên tục không khôi phục hạn mức. Nếu gọi quá nhanh, dịch vụ đang bận hoặc mạng bị gián đoạn, chờ theo thông báo rồi dùng nút thử lại ngay trong buổi đang dở.

Trang **Cài đặt AI** báo có key chỉ xác nhận có cấu hình. Nút **Kiểm tra kết nối đã lưu** tạo phản hồi mẫu `{"ready":true}` qua cùng kiểu kết nối streaming của phòng phỏng vấn và chỉ báo thành công khi nhận đủ nội dung đúng cấu trúc. Yêu cầu không chứa CV/JD/câu trả lời, có thể dùng một ít hạn mức/phí API, giới hạn đầu ra 512 token, thời gian 45 giây, dữ liệu nhận 65.536 ký tự và không tự thử lại. Thành công ở bước này xác nhận có thể tạo mẫu tại thời điểm kiểm tra, không bảo đảm đủ hạn mức hoặc chất lượng cho cả buổi. Xem [hướng dẫn cài đặt AI](AI-SETTINGS.vi.md).

OpenAI có thể trả HTTP 200 để bắt đầu luồng nhưng gửi lỗi trong luồng thay vì nội dung. Lỗi `credit_balance_exhausted` / `insufficient_quota` cần kiểm tra Billing/Limits trên OpenAI Platform; không xử lý như dịch vụ tạm ngừng. Xem [mã lỗi OpenAI](https://developers.openai.com/api/docs/guides/error-codes).

## Chọn giọng đọc tiếng Việt hoặc tiếng Anh

Trong phòng luyện, mở **Giọng đọc và tốc độ** dưới câu hỏi. Chỉ các giọng cùng ngôn ngữ buổi luyện được hiển thị. Chọn giọng nghe rõ nhất và tốc độ phù hợp; lựa chọn lưu trên trình duyệt, riêng cho tiếng Việt và tiếng Anh.

Ứng dụng đợi danh sách giọng tải xong trước khi đọc, không tự dùng giọng tiếng Anh cho buổi tiếng Việt khi thiếu giọng. Nếu không có giọng phù hợp, thử Microsoft Edge hoặc cài giọng đọc đúng ngôn ngữ trong Windows. Giọng đọc do trình duyệt và thiết bị cung cấp; đổi Gemini sang OpenAI **không tự đổi hệ thống giọng đọc thành OpenAI TTS**.

Câu hỏi chỉ được đọc sau khi nhận đầy đủ và được server kiểm tra. Nội dung đọc bỏ ký hiệu Markdown, không đọc nguyên khối code, chuẩn hóa một số thuật ngữ, và chia đoạn tại khoảng trắng để tránh cắt từ. Câu hỏi hiển thị trên màn hình vẫn giữ nguyên.

**13 kiểm thử TTS tự động** đã kiểm tra việc chọn giọng, thời điểm tải giọng, lưu lựa chọn, chia đoạn, hủy phát cũ và lỗi trình duyệt bằng đối tượng kiểm thử. Chưa có xác nhận nghe thực tế của người dùng cho bản sửa này. Hãy nghe thử cả tiếng Việt và tiếng Anh trên chính máy sử dụng để chọn giọng phù hợp.

## Mở trình duyệt khi chạy Visual Studio

`src/AiInterview.Web/Properties/launchSettings.json` đã đặt `launchBrowser: true`. Chọn `AiInterview.Web` làm startup project, chọn profile **AiInterview**, nhấn F5 hoặc Ctrl+F5 để Visual Studio mở ứng dụng tại `http://localhost:5088`.

Với `Run.ps1`/`dotnet run`, có thể vẫn cần mở địa chỉ đó bằng tay. Chỉ chạy một phiên trên cổng 5088.
