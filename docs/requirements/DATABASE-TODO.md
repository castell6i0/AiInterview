# DATABASE-TODO.md — Việc cần làm: Bảo mật SQL/Database

- **Dự án:** AiInterview / InterviewLab (đồ án chuyên ngành)
- **Ngày lập:** 09/11/2026
- **Người nhận:** AI agent triển khai
- **Trạng thái:** Đề xuất, **chưa sửa code**
- **Mức độ:** "Chuyên nghiệp vừa đủ" cho đồ án: làm các việc chứng minh được khi demo/bảo vệ, các việc chỉ cần khi chạy thật cho nhiều người được ghi vào mục "Hạn chế và hướng phát triển" của báo cáo.
- **Phạm vi:** `src/AiInterview.Infrastructure/Persistence`, `src/AiInterview.Web` (cấu hình, controller/service liên quan), `tests/AiInterview.Tests`, `README.md`, `docs`.
- **Quy ước:** Tài liệu **không chứa code**. Agent tự đọc code hiện có và chọn cách làm phù hợp, nhưng phải đáp ứng đúng mục tiêu và tiêu chí nghiệm thu.
- **Công cụ:** SQL Server + SSMS 22, EF Core migration.
- **Môi trường demo:** **localhost** (theo yêu cầu của thầy). Chưa deploy hosting/VPS.

---

## Phạm vi theo môi trường localhost

| Làm ngay | Làm bản gọn | Để khi deploy (ghi vào báo cáo) |
|---|---|---|
| DB-06, DB-08, DB-05, DB-07, DB-04, DB-16 | DB-01, DB-03 | DB-02, DB-09, DB-10, DB-11, DB-13, DB-14, DB-15; phần SQL login/chứng chỉ của DB-01 |
| DB-12 (nếu dư thời gian) | | |

---

## 0. Cách sử dụng

1. Đọc hết tài liệu trước khi sửa file.
2. Đọc lại code ở các khu vực được nhắc; số dòng có thể đã thay đổi.
3. Nếu code hiện tại đã làm một phần, ghi rõ "đã có" và chỉ bổ sung phần thiếu.
4. Không mở rộng sang viết lại schema hoặc đổi nghiệp vụ ngoài phạm vi.
5. Không đưa connection string thật, mật khẩu, API key hoặc dữ liệu CV/JD thật vào commit, log, test, tài liệu.
6. Mọi thay đổi schema phải có migration backward-compatible và có cách rollback.
7. Làm theo thứ tự ở mục 5; sau mỗi nhóm, chạy lại toàn bộ test.

---

## 1. Hiện trạng đã tốt (không làm lại)

- Không có raw SQL; toàn bộ truy vấn dùng EF Core LINQ (tham số hóa).
- Schema có unique index, filtered unique index, check constraint, `RowVersion`, `HasPrecision`.
- FK `NoAction` để giữ bằng chứng lịch sử và tránh multiple cascade path.
- Transaction `Serializable` ở các thao tác quản trị/lớp học.
- `AutoMigrate` và `Seed` mặc định tắt trong `appsettings.json`; seeder demo chỉ chạy khi `development=true`.
- Production dùng error page chung, không bật developer exception page.
- Connection string mặc định có `Encrypt=True`.

Rủi ro chính **không phải SQL injection**, mà là cấu hình/vận hành, race condition quota, dữ liệu phình to và kiểm tra quyền.

---

## 2. Nguyên tắc bắt buộc

1. Không làm giảm bảo mật hiện có (CSRF, phân quyền, mã hóa key, redact log).
2. Không tin dữ liệu từ client; giới hạn phải kiểm tra ở server và, nếu có thể, ở DB.
3. Không dùng tài khoản `sa`/`db_owner`/`sysadmin` cho ứng dụng chạy thật.
4. Không bật `AutoMigrate`/`Seed` ở production.
5. Migration phải backward-compatible; cột mới nên nullable hoặc có default.
6. Không trả chi tiết `SqlException`, tên bảng/cột hoặc câu SQL cho người dùng.
7. Không log nội dung CV/JD/câu trả lời/prompt/API key.
8. Không xóa database hoặc bảng để "sửa lỗi migration" khi chưa rõ nguyên nhân.
9. Mỗi nhóm thay đổi phải có test hoặc mô tả kiểm thử thủ công.
10. Cập nhật `README.md`/`docs` khi đổi cấu hình hoặc hành vi vận hành.

---

## 3. Việc cần làm

### Nhóm P0 — Làm trước

#### DB-01 — Connection string an toàn (bản localhost)

**Vấn đề:** Chuỗi kết nối LocalDB với `Trusted_Connection=True` phù hợp khi demo localhost, nhưng nhóm dùng chung code nên connection string/secret không được nằm trong repo hoặc ZIP.

**Việc cần làm:**
- Giữ LocalDB hoặc SQL Server cục bộ cho demo; không cần SQL login riêng.
- Không hardcode connection string thật trong source dùng chung; mỗi thành viên dùng user-secrets hoặc file local bị `.gitignore`.
- Tách cấu hình Development và Production; tài liệu chỉ dùng chuỗi mẫu có placeholder.
- Kiểm tra `DesignTimeDbContextFactory` không dùng chuỗi cứng khác với cấu hình chính.
- Ghi vào tài liệu: khi deploy phải dùng SQL login riêng (không `sa`), `Encrypt=True`, bỏ `TrustServerCertificate=True` nếu có chứng chỉ hợp lệ.

**Nghiệm thu:**
- Không còn connection string thật trong repo hoặc ZIP nộp bài.
- README hướng dẫn thành viên mới cấu hình database cục bộ.

#### DB-03 — Kiểm soát migration và seed (bản localhost)

**Việc cần làm:**
- Giữ `Database:AutoMigrate=false` và `Seed:Enabled=false` mặc định trong `appsettings.json`; chỉ bật ở `appsettings.Development.json`.
- Không dùng `EnsureCreated`; chỉ dùng migration.
- Tài khoản demo chỉ tạo ở Development; ghi rõ trong README tài khoản nào được seed và mật khẩu lấy từ user-secrets, không nằm trong repo.
- Ghi cách áp dụng migration bằng tay (`dotnet ef database update`) và cách rollback về migration trước.

**Nghiệm thu:**
- Chạy với cấu hình mặc định không tự migrate/seed.
- Có hướng dẫn migration và rollback ngắn trong `docs/`.

---

### Nhóm P1 — Nên làm

#### DB-06 — Quota AI nguyên tử

**Vấn đề:** `ReserveAttemptAsync` đếm số log trong một phút rồi mới ghi log. Hai request đồng thời có thể cùng đọc dưới ngưỡng và cùng vượt giới hạn.

**Việc cần làm:**
- Chọn cơ chế nguyên tử: transaction có khóa phù hợp, hoặc cập nhật nguyên tử trên bảng quota theo user + kỳ thời gian.
- Không dùng kiểu "đếm trước, ghi sau" mà thiếu khóa/transaction.
- Retry gọi AI cũng phải được đếm nếu có thể phát sinh chi phí.
- Request bị hủy trước khi gửi tới provider không bị tính trùng; vẫn giữ log "đã đặt chỗ" để điều tra.
- Nếu chỉ chạy một instance, ghi rõ trong tài liệu và giữ cấu trúc để thay thế sau.
- Log không chứa prompt, response hoặc API key.

**Nghiệm thu:**
- Test hai request đồng thời không vượt ngưỡng.
- Tài liệu nêu rõ mô hình triển khai được hỗ trợ (một instance hay nhiều).

#### DB-08 — Kiểm tra quyền trước khi load dữ liệu lớn

**Vấn đề:** `Interviews/Report` tải toàn bộ session kèm turns/evaluation rồi mới kiểm tra `StudentId`/quyền giảng viên: tốn bộ nhớ và tạo khác biệt thời gian có thể bị lợi dụng để dò ID.

**Việc cần làm:**
- Kiểm tra ownership/quyền bằng truy vấn nhẹ trước (chỉ lấy `StudentId`, trạng thái, quan hệ lớp).
- Chỉ load đầy đủ khi đã xác nhận quyền.
- Áp dụng nguyên tắc tương tự cho các endpoint khác có nguy cơ load nặng trước khi kiểm tra quyền.
- Trả `NotFound` thay vì `Forbidden` để không lộ sự tồn tại của tài nguyên (giữ nhất quán với hiện tại).

**Nghiệm thu:**
- Không load full session trước khi kiểm tra quyền.
- Test: user không sở hữu không truy cập được; giảng viên chỉ xem được session của lớp mình.

#### DB-05 — Giới hạn độ dài dữ liệu ở DB

**Vấn đề:** Nhiều cột text lớn chỉ được validate ở ứng dụng, DB không chặn; import/seed/lỗi code có thể ghi dữ liệu rất lớn làm phình database và backup.

**Việc cần làm:**
- Lập danh sách cột text lớn và giới hạn hợp lý theo nghiệp vụ: câu trả lời, draft, nội dung CV/JD, `AnalysisJson`/`SkillGapJson`, nhận xét giảng viên, tiêu đề/mô tả.
- Thêm `HasMaxLength`/kiểu dữ liệu phù hợp hoặc check constraint độ dài.
- Kiểm tra dữ liệu hiện có trước khi thêm constraint; nếu có bản ghi vượt ngưỡng thì xử lý riêng, không để migration fail.
- Không đặt giới hạn thấp hơn validation hiện có của ứng dụng.
- Ghi giới hạn vào tài liệu.

**Nghiệm thu:**
- DB từ chối dữ liệu vượt giới hạn.
- Migration áp dụng được trên dữ liệu cũ.
- Có test trường hợp vượt giới hạn; luồng nhập liệu bình thường không bị ảnh hưởng.

#### DB-07 — Chống lạm dụng dung lượng qua session/CV/JD

**Vấn đề:** Mỗi session copy snapshot CV/JD; tạo session và upload chưa bị giới hạn nên có thể làm phình database.

**Việc cần làm:**
- Phối hợp với `RATE-LIMIT-TODO.md` (policy `upload-cv`, `answer`, tạo session).
- Giới hạn số CV/JD đang lưu theo user; thông báo rõ khi đạt ngưỡng.
- Giới hạn số session chưa hoàn thành hoặc số session/ngày.
- Chỉ copy snapshot các trường cần thiết.
- Cân nhắc dọn session rỗng/không hoạt động quá lâu nếu nghiệp vụ cho phép.

**Nghiệm thu:**
- Không thể tạo vô hạn session/CV trong thời gian ngắn.
- Người dùng nhận thông báo rõ khi đạt giới hạn.

#### DB-04 — Không lộ chi tiết database qua lỗi và log

**Việc cần làm:**
- Rà soát mọi chỗ bắt `DbException`, `SqlException`, `DbUpdateException`: trả thông báo trung tính, log mã lỗi và ngữ cảnh cần thiết nhưng không log câu SQL/giá trị tham số nhạy cảm.
- Kiểm tra exception handler/error page cho cả request HTML và JSON/SSE.
- Không trả stack trace ngoài môi trường Development.
- `EnableSensitiveDataLogging` phải tắt ngoài Development.
- Thông báo lỗi upload/parse CV không chứa đường dẫn server hay chi tiết schema.

**Nghiệm thu:**
- Không response nào chứa tên bảng/cột/câu SQL/stack trace ở production.
- Log đủ để điều tra nhưng không chứa PII hay secret.

#### DB-16 — Giữ quy tắc "không raw SQL"

**Việc cần làm:**
- Thêm quy tắc code review: mọi raw SQL phải được giải thích, tham số hóa và có test.
- Cấm nối chuỗi input người dùng vào SQL.
- Thêm bước kiểm tra (grep hoặc CI) phát hiện `FromSqlRaw`, `ExecuteSqlRaw`, `SqlCommand` ngoài ý muốn.
- Rà soát định kỳ các truy vấn nhận `Id` từ route/query để bảo đảm luôn lọc theo user/role.
- Ghi quy tắc vào `README`/`docs`.

**Nghiệm thu:**
- Raw SQL mới sẽ bị phát hiện ở review/CI.
- Có checklist rà soát định kỳ.

---

### Nhóm P2 — Làm nếu dư thời gian

#### DB-12 — Audit log và rà soát phân quyền dữ liệu

- Ghi audit log cho: đổi role, khóa/mở khóa tài khoản, xóa/sửa dữ liệu quan trọng, đổi cấu hình AI/quota.
- Nội dung log: actor, hành động, đối tượng, thời gian, kết quả; không ghi PII/secret.
- Chỉ Admin xem được; có phân trang và lọc; không sửa/xóa được từ giao diện thường.
- Rà soát mọi truy vấn có `Id` từ route/query để bảo đảm lọc theo user/role.
- Ghi rõ trong tài liệu: phân quyền hoàn toàn ở tầng ứng dụng, chưa có row-level security.

#### DB-10 — Backup/restore gắn với Data Protection (để khi deploy)

- Ghi quy trình backup database **cùng** Data Protection key ring.
- Cố định application name của Data Protection.
- Thử restore một lần trên môi trường sạch và ghi kết quả.
- Khi mất key ring: người dùng nhận thông báo nhập lại key, ứng dụng không crash, không tự chuyển sang key hệ thống.

#### DB-15 — Seed, tài khoản demo và mật khẩu mẫu (để khi deploy)

- Production không tạo tài khoản demo; nên dừng khởi động nếu phát hiện seed demo ngoài Development.
- Không để mật khẩu seed thật trong repo; dùng user-secrets/biến môi trường.
- Seed role/danh mục phải idempotent và không ghi đè dữ liệu người dùng.
- Ghi tài liệu tài khoản nào được tạo tự động và khi nào.

---

## 4. Chỉ ghi vào báo cáo "Hạn chế và hướng phát triển"

Không triển khai trong phạm vi đồ án, nhưng nêu rõ lý do và hướng mở rộng:

| Mục | Lý do chưa làm | Hướng phát triển |
|---|---|---|
| DB-01 (phần SQL login, `Encrypt`, chứng chỉ) | Demo localhost | Dùng SQL login riêng, secret manager |
| DB-02: tách tài khoản migrate/runtime | Chạy demo một môi trường | Tạo hai login, runtime chỉ có DML |
| DB-10, DB-15: backup key ring, tài khoản demo | Chưa deploy | Quy trình backup/restore và seed an toàn khi lên production |
| DB-09: TDE / Always Encrypted | Không có dữ liệu thật ở mức nhạy cảm cao | Bật TDE cho DB và backup; Always Encrypted cho cột nhạy cảm |
| DB-11: retention job | Dữ liệu ít | Job dọn log AI, session cũ theo chính sách |
| DB-13: tối ưu index | Chưa có dữ liệu lớn | Đo truy vấn báo cáo/dashboard rồi thêm index có chủ đích |
| DB-14: pool/timeout/retry | Tải thấp | Đặt `CommandTimeout`, retry lỗi transient có idempotency |

---

## 5. Thứ tự thực hiện

1. **Chuẩn bị:** tạo nhánh riêng, chạy build/test baseline, đọc `AppDbContext`, migration, cấu hình.
2. **P0 (bản localhost):** DB-01 → DB-03.
3. **P1:** DB-06 → DB-08 → DB-05 → DB-07 → DB-04 → DB-16.
4. **P2 (nếu còn thời gian):** DB-12. DB-10 và DB-15 để khi deploy.
5. **Hoàn thiện:** chạy toàn bộ test, cập nhật README/docs, viết mục "Hạn chế và hướng phát triển".

---

## 6. Kế hoạch kiểm thử

**Tự động:**
- Quyền: user A không đọc/sửa được session, CV, JD, báo cáo của user B; giảng viên chỉ xem session lớp mình.
- Quota: hai request đồng thời không vượt ngưỡng.
- Độ dài: ghi dữ liệu vượt giới hạn bị DB từ chối.
- Migration: áp dụng được trên database có dữ liệu cũ.
- Lỗi DB: response không chứa tên bảng/cột/câu SQL/stack trace.
- Seed: production không tạo tài khoản demo.

**Thủ công (dùng SSMS 22):**
- Chạy migration bằng tài khoản có quyền và xác nhận ứng dụng chạy sau đó.
- Tạo nhiều session/upload liên tục, xác nhận bị chặn theo giới hạn.
- Giả lập lỗi DB, xác nhận log và response không lộ dữ liệu nhạy cảm.
- Restore database + key ring sang môi trường sạch, kiểm tra key cá nhân (nếu làm DB-10).

---

## 7. Checklist trước khi nộp/demo

- [ ] Connection string và mật khẩu seed không nằm trong repo/ZIP.
- [ ] `AutoMigrate=false`, `Seed=false` mặc định; chỉ bật ở Development.
- [ ] README có hướng dẫn cấu hình database cục bộ và migration.
- [ ] `EnableSensitiveDataLogging` tắt ngoài Development.
- [ ] Không trả chi tiết SQL/stack trace cho người dùng.
- [ ] Quota AI nguyên tử (hoặc đã ghi rõ chỉ một instance).
- [ ] Report kiểm tra quyền trước khi load dữ liệu lớn.
- [ ] Giới hạn độ dài cột và giới hạn số CV/session đã bật.
- [ ] Đã có bước kiểm tra raw SQL.
- [ ] Toàn bộ test pass; đã smoke test các luồng chính.
- [ ] Mục "Hạn chế và hướng phát triển" đã viết.

---

## 8. Definition of Done

Một việc chỉ hoàn thành khi: có thay đổi code/cấu hình/migration tương ứng; có test tự động hoặc mô tả kiểm thử thủ công; build và test hiện có pass; migration backward-compatible và có rollback; không có secret/connection string thật trong diff; tài liệu được cập nhật; không làm giảm bảo mật hiện có.

---

## 9. Câu hỏi mở cần chốt

1. ~~Môi trường demo~~: **đã chốt localhost** theo yêu cầu của thầy.
2. **Số instance:** mặc định một instance khi demo localhost; DB-06 vẫn làm để quota đúng khi có request đồng thời.
3. **Thời gian lưu dữ liệu:** log AI, session, CV/JD giữ bao lâu? (chỉ cần ghi vào báo cáo nếu không làm retention)
4. **Audit log:** có làm trong phạm vi đồ án không, hay chỉ ghi vào hướng phát triển?

---

## 10. Phụ lục — Khu vực cần đọc trước

- `src/AiInterview.Infrastructure/Persistence/AppDbContext.cs`
- `src/AiInterview.Infrastructure/Persistence/Migrations/*`
- `src/AiInterview.Infrastructure/Persistence/DatabaseSeeder.cs`, `SeedQuestions.cs`
- `src/AiInterview.Web/Program.cs`, `DesignTimeDbContextFactory.cs`
- `src/AiInterview.Web/appsettings.json`, `appsettings.Development.json`
- `src/AiInterview.Web/Services/AdminService.cs`, `InterviewService.cs`, `ProfileService.cs`
- `src/AiInterview.Web/Controllers/InterviewsController.cs`
- `src/AiInterview.Infrastructure/AI/GeminiAiService.cs`, `OpenAiService.cs`
- `src/AiInterview.Domain/Entities/Models.cs`
- `README.md`, `docs/*`

---

**Ghi chú cuối:** Ưu tiên thay đổi nhỏ, có test, có thể rollback; không gộp toàn bộ cải tiến vào một lần thay đổi lớn.
