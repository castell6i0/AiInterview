# Kiểm tra giao diện khi bị giới hạn yêu cầu — 10/11/2026

## Phần đã có

- Câu hỏi AI và phần câu hỏi đang truyền được gán bằng `textContent`; không chèn HTML do AI trả về.
- Câu trả lời đang nhập được giữ trong tab bằng sessionStorage; các lần ghi draft được nối tiếp để bản cũ không ghi đè bản mới.
- API key không được ghi vào browser storage và bị xóa khỏi ô nhập khi rời/khôi phục trang.

## Phần bổ sung (RL-05 / API-ERR / một phần AI-01)

- `request-feedback.js` đọc Retry-After dạng giây hoặc HTTP date, dự phòng bằng `retryAfterSeconds`; 429 thiếu JSON vẫn hiện thông báo trung tính.
- Stream và Answer chờ đúng thời gian; các nút gửi bị tạm khóa nhưng câu trả lời vẫn sửa được. Hết thời gian người dùng chủ động thử lại; không tự gọi AI.
- Draft giữ dữ liệu trong tab, không cho thao tác gõ vượt qua thời gian chờ. Lỗi tạm thời dùng backoff tăng 2, 4, 8… tối đa 60 giây, đồng thời tôn trọng Retry-After dài hơn. Lỗi 400/413 không tự gửi lại cùng payload.
- Upload CV và các form cấu hình AI gửi bằng fetch với FormData, CSRF và giá trị nút vừa bấm. Khi nhận 429 không điều hướng hoặc reset form, nên file/model/key đang nhập vẫn còn; chỉ nút gửi bị tạm khóa. Các form AI dùng chung thời gian chờ.
- Nội dung thông báo dùng role=status hoặc vùng alert hiện có; không khóa điều hướng và không che giao diện.
- Fetch form nhận JSON `{ redirectUrl }` từ `AjaxRedirectFilter`; khi thành công mới điều hướng để giữ thông báo TempData.
- Lỗi SSE quota/rate_limit/system_concurrency được chuyển thành thời gian chờ, không báo mất kết nối hoặc yêu cầu đổi key nhầm.

## Tự động đã chạy

`node --test tests/browser/*.test.cjs`: **56/56 đạt**, gồm 42 kiểm thử cũ và 14 bổ sung. Mô phỏng đồng hồ/mạng/DOM, không gọi AI thật và không gửi email.

Các ca mới chứng minh: countdown và thử lại thủ công; câu trả lời/draft/file được giữ; gõ liên tục không vượt backoff; lần lưu sau gửi nội dung mới nhất; 413 dừng vòng lặp; CSRF/submitter được giữ; không gửi trùng khi bấm hai lần; HTTP-date; HTML lỗi không được render; redirect chỉ chấp nhận đường dẫn nội bộ; HTML/script trong câu hỏi AI hiển thị như văn bản.

Kiểm thử HTML/script chỉ chứng minh an toàn hiển thị. Nó không chứng minh nhà cung cấp AI luôn chống được mọi prompt injection hoặc luôn chấm đúng nghiệp vụ.

## Kịch bản kiểm tra thủ công khi demo

1. Dùng cấu hình test với giới hạn thấp, mở một buổi luyện và bấm nhận câu hỏi đến khi bị 429: thấy số giây, nút bật lại sau thời gian chờ, không tự phát sinh thêm lượt AI.
2. Trong lúc viết câu trả lời, gây 429 ở Draft rồi tiếp tục gõ: văn bản giữ nguyên, chỉ một lần lưu mới được gửi sau thời gian chờ.
3. Chọn một PDF/DOCX hợp lệ, gây 429 ở Upload: tên file vẫn còn; hết thời gian bấm lại được.
4. Nhập model và key thử, gây 429 ở Cài đặt AI: model/key không bị reset trong tab; thử lại sau countdown.
5. Tắt JavaScript: form vẫn dùng luồng POST/redirect thông thường; server tiếp tục chặn và hiện trang 429.

Chưa ghi nhận kiểm tra thủ công bằng trình duyệt người dùng trong đợt này; các kết quả đạt phía trên là kiểm thử tự động.
