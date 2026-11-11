# Hướng dẫn demo InterviewLab

Đây là kịch bản thao tác trên tính năng đã triển khai. Đã chạy thành công một buổi Gemini thật bằng văn bản gồm 5 câu chính và 8 câu phụ, phân tích CV/JD và tạo báo cáo; xem biên bản cụ thể trong `VERIFICATION.md`. Chuẩn bị key/quota hợp lệ cho lần trình bày mới và microphone để kiểm thử phần giọng nói. Không seed sẵn kết quả AI hoặc bài phỏng vấn hoàn chỉnh. **Microphone/TTS với người dùng thật vẫn chưa được nghiệm thu.**

## Chuẩn bị trước buổi trình bày

1. Làm phần Chạy nhanh trong README; mở `http://localhost:5088` và đăng nhập được ba vai trò với mật khẩu do bạn đặt.
2. Dùng dữ liệu giả dưới đây. Không đưa email, số điện thoại, địa chỉ hoặc CV cá nhân thật lên màn hình trình chiếu.
3. Chép mẫu CV vào Word, lưu **DOCX** hoặc xuất **PDF có text**, đặt tên `CV-sinh-vien-demo.docx`. Chép JD vào chức năng thêm JD. JD dưới đây là dữ liệu giả để demo, không phải tin tuyển dụng thật.
4. Vào **Cài đặt AI**, chọn nhanh model từ danh sách hoặc tự nhập tên, rồi bấm **Lưu & kiểm tra model**. Nếu giữ nguyên cấu hình đã lưu, dùng **Kiểm tra kết nối đã lưu**. Các gợi ý Gemini có hạn mức miễn phí cho tài khoản đủ điều kiện; dự án trả phí vẫn tính phí theo gói. Các gợi ý OpenAI cần số dư API. Kiểm tra tạo phản hồi mẫu ngắn, có thể dùng một ít hạn mức/phí API và không gửi CV/JD/câu trả lời. **Chỉ lưu** không gọi API và chưa xác nhận model tạo được nội dung. Kết quả kiểm tra thành công xác nhận tạo được mẫu lúc kiểm tra. Chuẩn bị đủ thời gian/quota cho phát sinh câu hỏi và chấm từng lượt; AI có thể hỏi thêm, nên số lần gọi lớn hơn số câu chính.
5. Kiểm tra microphone, loa/tai nghe và quyền website trên đúng trình duyệt sẽ trình bày. Thử cả vi-VN và en-US nếu demo song ngữ.

## CV giả dùng để thử

```text
HỒ SƠ ỨNG VIÊN DEMO — DỮ LIỆU GIẢ
Ứng viên: Sinh viên Demo
Mục tiêu: Thực tập Backend .NET
Học vấn: Năm 4, Kỹ thuật phần mềm, Trường Đại học Demo

Kỹ năng
- C#, OOP, ASP.NET Core MVC và Web API.
- SQL Server: bảng, khóa chính/khóa ngoại, JOIN, index cơ bản.
- Entity Framework Core, migrations, LINQ và dependency injection.
- Git: branch, commit, pull request; làm việc nhóm nhỏ.
- Đọc tài liệu kỹ thuật tiếng Anh; viết unit test cơ bản với xUnit.

Dự án học tập: Quản lý thư viện
- Nhóm 3 người, 8 tuần; tự phụ trách API sách và nghiệp vụ mượn/trả.
- Viết API REST cho tìm kiếm sách, phân trang, mượn và trả sách.
- Thiết kế bảng Book, BorrowRecord và Reader bằng SQL Server.
- Dùng transaction để giữ số lượng sách hợp lệ khi mượn.
- Viết unit test cho giới hạn số sách mượn và ngày trả dự kiến.
- Khó khăn: hai người cùng mượn cuốn cuối. Đã tìm hiểu optimistic
  concurrency; chưa có số liệu benchmark hoặc tải thực tế.

Kinh nghiệm
- Chưa có kinh nghiệm doanh nghiệp; đã thực hiện một đồ án nhóm.
- Chưa triển khai Docker, CI/CD hoặc Redis trong dự án này.
```

## JD giả dùng để thử

Tiêu đề: **Backend .NET Intern — Demo**. Công ty: **Công ty Demo**. Để trống link nguồn vì đây là dữ liệu tự tạo.

```text
TIN TUYỂN DỤNG GIẢ PHỤC VỤ DEMO
Vị trí: Backend .NET Intern

Tham gia phát triển API cho ứng dụng quản lý đơn hàng. Phối hợp với
frontend và QA để làm rõ yêu cầu, viết code và kiểm thử tính năng.

Yêu cầu:
- Nắm C#, OOP, ASP.NET Core Web API, REST và HTTP status code.
- Biết SQL Server, JOIN, index, transaction, tránh truy vấn N+1.
- Sử dụng Entity Framework Core, dependency injection và Git.
- Biết unit test, giải thích được một dự án đã tự thực hiện.
- Đọc được tài liệu tiếng Anh và trình bày ý tưởng rõ ràng.

Điểm cộng: Docker, CI/CD, caching với Redis và xử lý đồng thời.
Không yêu cầu kinh nghiệm làm việc chính thức. Khi chưa biết một
công nghệ, ứng viên cần nêu rõ và trình bày kế hoạch học phù hợp.
```

Kỳ vọng hợp lý của màn so CV/JD là nhận ra bằng chứng về .NET/SQL/Git và thiếu bằng chứng về Docker/CI/CD/Redis. Kết quả và cách diễn đạt phụ thuộc model; không buộc AI trả một đoạn văn cố định.

## Kịch bản trình bày 20–30 phút

| Thời lượng | Thao tác | Điểm cần giải thích |
|---|---|---|
| 2 phút | Trang đầu → đăng nhập sinh viên → Tổng quan | Sinh viên luyện theo mục tiêu và theo dõi tiến bộ |
| 3 phút | Hồ sơ · CV · JD → upload CV → xem text → đồng ý và phân tích | Tách trích text tại server khỏi gửi text cho AI; không lưu file gốc |
| 2 phút | Thêm JD → chọn CV → đồng ý và phân tích khoảng trống | Câu hỏi có bối cảnh ứng tuyển; “thiếu bằng chứng” không khẳng định ứng viên không có kỹ năng |
| 7–10 phút | Tạo buổi Backend .NET/Intern/Việt/văn bản/5 câu chính, chọn CV+JD, persona Phản biện | AI hỏi thật, follow-up tối đa 2 lần; quá trình sinh câu hỏi có stream |
| 3 phút | Hoàn thành buổi → Xem báo cáo | Điểm có trọng số, bằng chứng, viết lại, verdict, readiness và kế hoạch |
| 2 phút | Tổng quan → Lịch sử → Timeline → so sánh khi đã có 2 buổi | Heatmap/gợi ý dựa trên các buổi hoàn chỉnh; buổi thiếu dữ liệu được ghi rõ |
| 3 phút | Giảng viên → tạo lớp/giao bài → sinh viên tham gia → xem dashboard và CSV | Chia sẻ theo lớp và quyền của giảng viên; bài nộp kiểm tra đúng cấu hình |
| 2 phút | Admin → câu hỏi/tag → rubric | Ngân hàng câu hỏi có thể mở rộng; rubric mới không sửa lịch sử |

Để vừa thời gian, tạo sẵn một buổi **đã thực hiện thật** trước buổi bảo vệ và mở báo cáo của nó khi cần. Không mô tả dữ liệu tạo tay như kết quả Gemini. Nếu chưa đủ quota/thời gian, trình bày trạng thái đang thực hiện và giới hạn xác minh một cách rõ ràng.

### Buổi văn bản và báo cáo

1. Chọn **Luyện phỏng vấn**, vị trí Backend .NET, Intern, tiếng Việt, văn bản, 5 câu, loại Tổng hợp hoặc Theo JD. Chọn CV/JD và đồng ý gửi dữ liệu.
2. Bấm **Bắt đầu phỏng vấn**. Câu hỏi thật có thể khác mỗi lần; chỉ dùng đáp án minh họa dưới đây nếu phù hợp câu đang được hỏi.
3. Một câu trả lời có cấu trúc để demo: “Trong dự án thư viện, em dùng transaction khi tạo bản ghi mượn và giảm số lượng sách để hai thay đổi cùng thành công hoặc cùng rollback. Tuy nhiên transaction đơn thuần chưa giải quyết mọi trường hợp đồng thời; em cần chọn isolation level hoặc dùng rowversion và thử lại khi có xung đột. Em chưa benchmark nên chưa có số liệu tốc độ để khẳng định.”
4. Một câu trả lời còn yếu để quan sát follow-up: “Em có dùng index vì truy vấn sẽ nhanh hơn, nhưng em chưa giải thích được trường hợp index làm thao tác ghi chậm đi.” AI quyết định có hỏi thêm hay không; persona không đảm bảo một câu hỏi cố định.
5. Ở một lượt, nhập nháp, đợi trạng thái lưu thành công rồi tải lại trang. Xác nhận còn đúng câu hỏi và phần nháp.
6. Trả lời đủ các câu chính, xử lý câu phụ nếu có, rồi bấm **Xem báo cáo của tôi**. Quan sát trạng thái chấm từng lượt; không bấm lặp lại liên tục.
7. Mở tiêu chí để đối chiếu dẫn chứng với chính câu trả lời. Phân biệt điểm nội dung và cách nói. Mở phần sử dụng AI để xem số lần gọi/token; chi phí có thể chưa xác định.

### Buổi giọng nói và tiếng Anh

Tạo một buổi khác với hình thức giọng nói. Bấm microphone, cấp quyền, nói câu trả lời, dừng và kiểm tra transcript trước khi gửi. Bật tự đọc câu hỏi hoặc bấm Đọc câu hỏi. Đổi ngôn ngữ ở buổi mới sang tiếng Anh để thử en-US; không đổi nhận dạng giữa chừng trong một buổi.

Câu nói tiếng Anh giả để kiểm tra nhận dạng, khi câu hỏi phù hợp:

> In my library project, I implemented the borrowing API. I used a transaction to keep the loan record and stock update consistent. I still need to test concurrent requests and measure the result before choosing a final concurrency strategy.

Thử ngắt nghỉ hơn 2 giây và dùng một từ đệm tự nhiên để quan sát sự kiện. Số đo phụ thuộc browser, transcript cuối cùng và telemetry, không có số chuẩn bắt buộc. Thử từ chối quyền microphone ở một buổi riêng: hệ thống phải vẫn cho nhập văn bản, không tự tạo số đo nói.

### Lớp và bài tập

1. Đăng nhập `lecturer@example.test`, chọn Lớp giảng dạy → tạo **Lớp Demo Kỹ thuật phần mềm**, ghi lại mã lớp.
2. Tạo bài **Luyện Backend .NET Intern**, hạn nộp trong tương lai theo UTC+7, tiếng Việt, kỹ thuật, 5 câu.
3. Đăng nhập sinh viên, vào Lớp của tôi, nhập mã và xác nhận quyền xem lịch sử của giảng viên.
4. Mở bài tập, tạo buổi từ bài tập để lấy đúng cấu hình và rubric. Hoàn thành đầy đủ rồi quay lại trang bài tập để nộp. Buổi luyện kết thúc sớm/đã nộp/nằm trước ngày giao sẽ không hợp lệ.
5. Giảng viên mở sinh viên trong lớp, xem báo cáo/tiến bộ, để lại nhận xét, xuất CSV. Sinh viên mở báo cáo để thấy nhận xét.
6. Nếu muốn demo nộp trễ, dùng bài tập thử phù hợp quy định ứng dụng và thực hiện sau hạn; không sửa giờ hệ thống hoặc database để tạo bằng chứng giả.

### Admin và code

Admin thêm câu hỏi “Giải thích optimistic concurrency trong EF Core” với tag `EF Core, Concurrency`, cấp độ Intern và loại công ty phù hợp. Tạo một rubric tên mới, thay trọng số rồi kiểm tra rubric cũ vẫn tồn tại. Không khóa tài khoản admin đang dùng để trình bày.

Để demo code, tạo buổi loại Coding; khi có câu code, Monaco hiển thị trình soạn thảo. Viết lời giải theo câu hỏi thật, gửi và xem review trong báo cáo. Giải thích rõ hệ thống **không chạy code**; độ đúng được AI nhận xét chứ không phải kết quả chạy test case.

## Nhánh demo khi AI lỗi

Nếu thiếu key, bị từ chối model, timeout hoặc hết quota, đọc thông báo và giữ nguyên buổi. Với lỗi hết tín dụng/giới hạn tài khoản, xử lý thanh toán hoặc hạn mức trước khi thử lại; với lỗi gọi quá nhanh hoặc dịch vụ đang bận, chờ theo thông báo. Xác nhận câu trả lời đã gửi còn trong lịch sử; không tạo liên tục buổi mới để né lỗi. Phần hồ sơ, trích xuất CV, JD, lớp, admin và cấu trúc buổi vẫn có thể thao tác nếu database hoạt động. Không trình bày một báo cáo không có câu trả lời là đã kiểm chứng AI chấm điểm.

## Lời giới thiệu ngắn

“InterviewLab giúp sinh viên IT luyện phỏng vấn theo vị trí, CV và tin tuyển dụng. AI từ Gemini hoặc OpenAI theo lựa chọn của bạn đóng vai người phỏng vấn, có thể hỏi sâu vào câu trả lời. Sau buổi, hệ thống đưa ra điểm theo rubric, trích dẫn bằng chứng và đề xuất luyện tiếp. Số liệu cách nói được tính riêng từ sự kiện trình duyệt. Giảng viên có thể giao bài, theo dõi lớp và phản hồi; dữ liệu cá nhân được phân quyền theo người dùng và lớp đã tham gia.”
