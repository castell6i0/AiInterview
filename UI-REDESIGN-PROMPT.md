# Prompt: Thiết kế lại UI InterviewLab (Tech / Dark)

Bạn là senior front-end engineer làm việc trong repo ASP.NET Core MVC (.NET 8) `AiInterview`. Nhiệm vụ: chuyển toàn bộ giao diện sang phong cách tech/dark, làm từng pha, không đụng logic backend.

## 0. Cách làm việc

- Đọc kỹ file trước khi sửa. Không đoán cấu trúc.
- Làm tuần tự theo các pha ở mục 7. Hết mỗi pha: chạy `dotnet build AiInterview.sln`, chạy bộ test .NET và JS hiện có (tự tìm chúng trong repo), liệt kê file đã đổi, rồi DỪNG chờ tôi duyệt.
- Viết code nhỏ nhất đủ chạy (YAGNI): không thêm thư viện, không tạo abstraction chưa có chỗ dùng, không refactor ngoài phạm vi.
- Nếu chạy được app, chụp screenshot ở 360 / 768 / 1280px để tự kiểm tra. Nếu không chạy được, nói thẳng là chưa kiểm tra bằng mắt.
- Người dùng làm trên Windows + Visual Studio 2022. Cẩn thận cú pháp Razor: `@` trong CSS/JS inline phải escape bằng `@@`, không để lỗi syntax.

## 1. Ràng buộc cứng

- Chỉ được sửa: `src/AiInterview.Web/Views/**/*.cshtml`, `wwwroot/css`, `wwwroot/js`, và thêm file tĩnh trong `wwwroot`. Được thêm MỘT hàm thuần trình bày vào `Helpers/Ui.cs` (xếp hạng điểm, mục 5). Không sửa Controllers, Services, ViewModels, Application, Domain, Infrastructure, migrations, appsettings.
- Giữ nguyên mọi `asp-*`, `name`, `id`, `data-*` đang được JS hoặc model binding dùng. Riêng phòng phỏng vấn: `answerInput, micButton, nextButton, endButton, readButton, sessionTimer, questionText, answerPanel, draftStatus, codePanel, monacoEditor, interimTranscript, nextActionTitle, nextActionText, roomError, roomErrorMessage, roomAiSettings, voiceNotice, progressCount, ttsVoice, roomConfig`. Báo cáo: `rubricData`. Trước khi đổi tên hoặc xóa bất kỳ id/class nào, `grep` trong `wwwroot/js` và `Views`.
- Giữ tiếng Việt trong UI, giữ `lang="vi"`, skip-link, `aria-*`, `role`, `aria-live` hiện có.
- Không CDN, không font tải từ ngoài, không framework mới. Thư viện có sẵn: Bootstrap (xem phiên bản trong `wwwroot/lib/bootstrap`), Chart.js (`wwwroot/lib/chart`), Monaco (`wwwroot/lib/monaco`).
- Không làm hỏng luồng AJAX/redirect hiện có (`request-feedback.js`, `AjaxRedirectFilter`) và các script `speech.js`, `interview-room.js`, `interview-setup.js`, `ai-settings.js`.

## 2. Phong cách (design tokens)

Khai báo trong `:root` của `site.css`. Mọi chỗ khác dùng biến, không hard-code hex ngoài khối này. Đây là giá trị mặc định đề xuất; đổi accent chỉ cần sửa token.

| token | giá trị | dùng cho |
|---|---|---|
| `--bg` | `#0a0e14` | nền trang |
| `--surface-1` | `#111826` | thẻ, sidebar, panel |
| `--surface-2` | `#161f2e` | input, hàng hover, phần lồng trong thẻ |
| `--border` | `#1f2a3a` | viền 1px |
| `--text` | `#e6edf3` | chữ chính |
| `--muted` | `#8b98a9` | chữ phụ |
| `--accent` | `#3ddc97` | hành động chính, trạng thái tốt |
| `--on-accent` | `#04130c` | chữ trên nền accent |
| `--cyan` | `#22d3ee` | AI, giọng nói, thông tin |
| `--danger` | `#f2555a` | lỗi, xóa, điểm thấp |
| `--warn` | `#f5a524` | cảnh báo, điểm trung bình |

- Bootstrap: nếu bản trong `wwwroot/lib/bootstrap` ≥ 5.3 thì đặt `data-bs-theme="dark"` trên `<html>` ở `_Layout`. Nếu < 5.3 thì ghi đè các biến `--bs-*` (body, card, form, table, alert, dropdown, modal, border).
- Font: nội dung dùng `"Segoe UI", system-ui, sans-serif`. Số liệu, nhãn, timer, terminal dùng `"Cascadia Code", "JetBrains Mono", Consolas, ui-monospace, monospace`. Không tải font.
- Hình khối: bán kính 6px (nút, input), 8px (thẻ), 999px chỉ cho badge. Viền 1px `--border`. Không bóng tổng quát; glow chỉ ở `:focus-visible` và khi hover nút primary.
- Tương phản chữ thường ≥ 4.5:1 trên nền thật. Đo bằng công cụ, không ước chừng.
- Dọn `site.css`: gộp rule trùng (`.question-text`, `.room-answer textarea`, `.header-status` hiện khai báo hai lần), bỏ rule chết. Không đổi tên class mà view đang dùng, trừ khi sửa hết view dùng nó.

## 3. Không "AI slop" (bắt buộc)

Cấm:

- Gradient tím/hồng/xanh-tím, gradient trên chữ, blob hoặc orb mờ.
- Glassmorphism, `backdrop-filter` chồng nhiều lớp.
- Emoji làm icon hoặc nằm trong chữ. Icon là SVG inline, nét 1.5px, cùng một bộ, không dùng ký tự văn bản. Các ký tự hiện tại trong sidebar (◫ ◎ ↗ ▤ ◇ ⚙ ♧) phải thay hết, mỗi mục menu một icon riêng, không lặp.
- Lưới 3 thẻ "icon – tiêu đề – một câu" giống hệt nhau, bố cục căn giữa toàn bộ, thẻ lồng thẻ.
- Số liệu, tên, review, phần trăm giả. Mọi con số trên trang phải đến từ model. Dữ liệu minh họa (terminal ở landing, đăng nhập, đăng ký) phải là nội dung phỏng vấn IT thật, cụ thể, có ghi chú "Ví dụ minh họa".
- Câu chữ marketing rỗng ("mở khóa tiềm năng", "trải nghiệm liền mạch", "được hỗ trợ bởi AI"). Chữ UI ngắn, cụ thể, nói người dùng làm được việc gì. Chỉ sửa chữ khi câu đó rỗng nghĩa; không đổi nghĩa chức năng.
- Quá hai màu nhấn, neon cầu vồng, viền phát sáng khắp nơi.
- Nhiều hơn một animation "chủ đạo" trong một viewport (không tính nền ASCII).

Tạo thứ bậc bằng cỡ chữ, khoảng cách, độ đậm; không bằng cách thêm khung hoặc bóng.

## 4. Hệ nút theo chức năng

Chỉ 5 biến thể. Mọi nút trong toàn bộ view phải thuộc đúng một biến thể; cùng chức năng thì cùng class, cùng màu.

| Chức năng | class | Màu |
|---|---|---|
| Hành động chính: Bắt đầu, Gửi, Lưu, Tạo, Đăng nhập, Đăng ký | `btn btn-primary` | `--accent` đặc, chữ `--on-accent` |
| Điều hướng và phụ: xem thêm, quay lại, quản lý, hướng dẫn, hủy | `btn btn-secondary` | viền `--border`, chữ `--text`, nền trong suốt |
| AI và giọng nói: mic, đọc câu hỏi, kiểm tra kết nối AI | `btn btn-ai` (class mới) | `--cyan` |
| Nguy hiểm: xóa, kết thúc buổi | `btn btn-danger` | `--danger` |
| Bỏ qua, cảnh báo | `btn btn-warning` | `--warn` |

- Đủ trạng thái: hover, active, `:focus-visible`, disabled, loading (`.is-loading` có spinner, gắn khi form submit; nối vào `request-feedback.js`, không viết script trùng).
- Nút mic đang ghi âm (`.mic-button.recording`) dùng `--danger`; viết lại bằng token, bỏ `!important`.
- Link trong đoạn văn không phải nút.
- Sau khi xong, lệnh này phải trả về rỗng (hoặc liệt kê ngoại lệ có lý do):
  `grep -rnE "btn-(soft|outline-primary|outline-secondary|outline-danger|outline-warning|light|success|info)" src/AiInterview.Web/Views src/AiInterview.Web/wwwroot/js`

## 5. Thống kê

Phạm vi: tìm bằng `grep -rn "new Chart\|metric-number\|heatmap-cell\|progress" src/AiInterview.Web/Views`. Gồm ít nhất: Dashboard/Index, Dashboard/History, Interviews/Report, Interviews/Compare, Lecturer/Details, Lecturer/Student, Admin/Index, AiUsage/Index, Classes/Details.

- **Thang điểm:** kiểm tra thang thực tế trong code (báo cáo đang hiển thị điểm /10, readiness 0–100). Ngưỡng cho điểm 0–10: `<5` `--danger`, `5–6.9` `--warn`, `7–8.4` `--cyan`, `≥8.5` `--accent`. Readiness chia 10 rồi dùng cùng ngưỡng. Viết MỘT hàm trong `Helpers/Ui.cs` trả về band (`low|mid|good|top`), dùng khắp nơi, kèm class CSS `.score-low/.score-mid/.score-good/.score-top`. Màu không phải kênh duy nhất: kèm nhãn chữ ("Cần cải thiện", "Khá", "Tốt", "Xuất sắc").
- **KPI `.kpi`:** nhãn mono nhỏ viết hoa, giá trị mono lớn, đơn vị rõ ràng. Chênh lệch so với kỳ trước dùng ▲/▼ + màu + chữ cho screen reader; chỉ hiện khi có kỳ trước thật. Có tooltip hoặc chú thích nếu số là kết quả của một công thức.
- **Biểu đồ:** tạo `wwwroot/js/chart-theme.js` đặt `Chart.defaults` theo token (màu chữ, lưới, font mono, tooltip, legend). Radar: trục cố định 0–10, nhãn tiêu chí tiếng Việt. Line (tiến bộ): trục y 0–10, trục x là ngày, có tiêu đề trục và đơn vị. Có legend khi hơn một series. Animation vào 400ms, tắt khi reduced motion.
- Mỗi biểu đồ có `<details>` "Xem bảng số liệu" chứa bảng thật.
- Trạng thái rỗng: nói còn thiếu gì và có nút làm bước tiếp theo. Không vẽ biểu đồ trống.
- Biểu đồ nằm trong tab đang ẩn: gọi `chart.resize()` khi tab hiện.

## 6. Animation

Quy tắc chung: ≤250ms (trừ nền ASCII và terminal), `ease-out`, chỉ animate `transform` và `opacity`. Tắt toàn bộ khi `prefers-reduced-motion: reduce`. Animation chạy vô hạn chỉ gồm: nền ASCII, con trỏ terminal, sóng âm ở phòng giọng nói.

### 6.1 Nền ASCII

- Một `<canvas id="asciiBg" aria-hidden="true">` trong `_Layout` (cả nhánh đã đăng nhập và public), `position:fixed; inset:0; z-index:-1; pointer-events:none`. Script `wwwroot/js/ascii-bg.js`.
- Ký tự theo chủ đề IT: các token như `SELECT *`, `O(n log n)`, `git commit`, `async/await`, `404`, `HashMap`, `{}`, `=>`, `0x1F`, `try/catch` trôi chậm. Font mono, màu `--accent` với alpha thấp.
- Độ đậm theo trang: `.public-page` 0.14, `.workspace` 0.05, trang phòng phỏng vấn: tắt (đặt class hoặc `data-bg="off"` trên `body`).
- Hiệu năng: giới hạn 30fps bằng rAF + delta, tạm dừng khi `visibilitychange` ẩn, xử lý `devicePixelRatio`, debounce resize, giảm mật độ ở ≤767px. Khi `reduce`: vẽ một khung tĩnh hoặc tắt.
- Công tắc "Hiệu ứng nền" trong header, lưu `localStorage` (bọc try/catch), mặc định bật.
- Nội dung luôn nằm trên bề mặt đặc `--surface-1`; không để chữ đọc trực tiếp trên canvas.

### 6.2 Đăng ký và Đăng nhập (`Account/Register`, `Account/Login`, dùng chung `.auth-shell`)

Panel trái `.auth-art` thành cửa sổ terminal (thanh tiêu đề 3 chấm + tên).

- **Đăng ký:** tự gõ một câu hỏi phỏng vấn IT thật bằng tiếng Việt, ví dụ "Giải thích sự khác nhau giữa INNER JOIN và LEFT JOIN", rồi một câu trả lời ngắn, rồi thanh điểm mẫu chạy từ 0 lên giá trị mẫu, nhãn "Ví dụ minh họa". Luân phiên 2–3 câu khác nhau, mỗi vòng khoảng 12 giây.
- **Đăng nhập:** log chạy lặp `> connecting…  > loading profile…  > ready` kèm con trỏ nhấp nháy.
- **Form:** ô nhập glow viền khi focus; nút submit vào trạng thái loading; khi sai mật khẩu hoặc validation lỗi, khung form rung một lần (translateX ±4px, 300ms) và ô lỗi viền `--danger`. Hiệu ứng lỗi kích hoạt từ `.validation-summary-errors` / `.field-validation-error` có sẵn khi trang render lại; không thêm AJAX. Ổ khóa mở khi đăng nhập thành công chỉ làm nếu không kéo dài redirect quá 400ms.
- Mobile ≤767px: `.auth-art` đang ẩn; giữ ẩn và không chạy animation của nó.
- Nội dung terminal là ví dụ viết sẵn cố định, không gọi AI hay backend.

### 6.3 Chuyển trang và chuyển mục

Web là server-rendered nhiều trang. Không thêm htmx hoặc Turbo.

1. **Giữa các trang (CSS View Transitions):** `@view-transition { navigation: auto; }`. `.sidebar`, `.app-header`, `#asciiBg` có `view-transition-name` riêng để đứng yên. `.page-content` có `view-transition-name: page`, vào bằng fade + dịch 8px trong 180–200ms. Thử đặt `view-transition-name` cho `.nav-item.active` để vạch active trượt giữa các mục; nếu hiển thị lỗi thì bỏ. Dự phòng cho trình duyệt không hỗ trợ: `@supports not (view-transition-name: x)` → `.page-content { animation: pageIn .2s ease-out }`. Tắt khi `reduce`.
2. **Trong trang:** thẻ và KPI vào lần lượt, trễ 40ms mỗi phần tử, tối đa 8 phần tử đầu; `<details>` mở/đóng mượt; tab đổi bằng fade 150ms. Dùng một class chung `.reveal` + biến `--i`, không viết riêng cho từng trang.
3. **Khi chờ:** thanh tiến trình 2px màu `--accent` ở đầu viewport, chạy khi click link nội bộ và khi submit form. Mở rộng `request-feedback.js`, không tạo script thứ hai làm cùng việc.

## 7. Các pha

**Pha 0 — Nền tảng.** Tokens, Bootstrap dark, dọn `site.css`, hệ nút (mục 4) áp cho toàn bộ view, bộ icon SVG + sidebar, nền ASCII + công tắc, chuyển trang (6.3), `chart-theme.js`.
Nghiệm thu: mọi trang đã chuyển dark và không vỡ bố cục; grep nút sạch; sidebar không còn ký tự icon văn bản.

**Pha 1 — Public.** `Home/Index`, `Account/*` (Login, Register, CheckEmail, ConfirmEmail, AccessDenied), animation 6.2. Ở landing, thay `.hero-orbit` bằng terminal xem trước một buổi phỏng vấn thật; giữ nguyên các mục nội dung và liên kết hiện có.

**Pha 2 — Phòng phỏng vấn (`Interviews/Room`).**
- Chế độ giọng nói: avatar AI ở giữa khung, sóng âm bằng CSS (các cột `<i>`, dựa trên `.wave`), 3 trạng thái `data-state="idle|speaking|listening"` đặt lên container, cập nhật từ các sự kiện sẵn có trong `speech.js` / `interview-room.js` (chỉ thêm dòng set attribute, không đổi logic). Transcript tạm (`interimTranscript`) hiển thị rõ.
- Chế độ văn bản: câu hỏi dạng dòng lệnh `> `, vùng trả lời dạng editor mono.
- Timer dùng mono. Tiến độ dạng ô phân đoạn theo số câu chính, `progressCount` giữ nguyên và hiển thị đủ "x / n".
- Monaco: đổi sang theme `vs-dark` hoặc theme tùy biến khớp token. Nền ASCII tắt ở trang này.

**Pha 3 — Dashboard, Create, History, Report, Compare, Replay, Profile, AiSettings, AiUsage, Classes.**
- KPI và biểu đồ theo mục 5.
- Report chia 4 tab bằng Bootstrap tabs: Tổng quan (điểm, mạnh/yếu, dẫn chứng), Từng câu, Kế hoạch ôn tập, Nhận xét giảng viên. Các hash cũ `#report-score`, `#report-practice`, `#report-answers` phải mở đúng tab và cuộn tới mục. Radar `resize()` khi tab hiện.
- Create: giữ cấu trúc `setup-layout`, `flow-steps`, `setup-summary`; chỉ đổi giao diện.

**Pha 4 — Lecturer/\*, Admin/\*.** Bảng dark gọn (hàng ≥44px, header bảng tối), badge trạng thái và vai trò dùng một bộ chung, thống kê theo mục 5, form quản trị cùng style.

## 8. Nghiệm thu chung

- `dotnet build AiInterview.sln` không lỗi, không thêm warning mới; test .NET và JS hiện có vẫn đạt.
- Không tràn ngang ở 360 / 768 / 1280px.
- Tab đi qua mọi điều khiển, focus nhìn thấy rõ; tương phản chữ ≥ 4.5:1.
- `prefers-reduced-motion: reduce`: không animation nào chạy, trạng thái đổi tức thì.
- Tắt JS: các trang ngoài phòng phỏng vấn vẫn đọc được và form vẫn gửi được (trừ biểu đồ và nền).
- Không còn emoji, ký tự icon văn bản, hay hex hard-code ngoài khối token.
- Cuối mỗi pha báo cáo ≤ 10 dòng: file đã đổi, điều chưa kiểm tra được, rủi ro.
