# Đối chiếu bảo vệ database — 10/11/2026

Phạm vi: một tiến trình ứng dụng trên localhost, SQL Server/LocalDB và EF Core migration. Không thay database người dùng bằng database mới, không xóa dữ liệu để xử lý lỗi migration.

## Những phần đã có và phần bổ sung

| Yêu cầu | Kết quả trong source |
|---|---|
| DB-01 | Factory design-time đọc cùng các lớp cấu hình như ứng dụng; bỏ connection string riêng hardcode. Cấu hình Development cung cấp mẫu LocalDB tích hợp Windows, không có tài khoản/mật khẩu SQL. Cấu hình máy thật đặt ngoài source. |
| DB-03 | Mặc định AutoMigrate/Seed tắt; tài khoản demo chỉ tạo trong Development và mật khẩu lấy từ cấu hình ngoài source. Vẫn dùng migration, không chuyển sang EnsureCreated. |
| DB-05 | Thêm 24 CHECK constraint cho cột văn bản lớn. Kiểm tra bằng DATALENGTH để cả dấu cách cuối chuỗi cũng được tính. |
| DB-06 | Hạn mức AI được kiểm tra và đặt chỗ trong khóa theo user ở một instance; retry được tính. Schema bổ sung nguồn key, thời điểm gửi và index phục vụ quota ngày/tháng. |
| DB-07 | Giới hạn số CV/JD, buổi đang mở và buổi/ngày. Khóa dùng chung bao quanh thao tác đếm và ghi, tránh hai request đồng thời cùng vượt ngưỡng. |
| DB-08 | Report truy vấn quyền bằng giá trị StudentId trước; chỉ tải snapshot/turn/evaluation sau khi có quyền. Lặp lại điều kiện quyền trong truy vấn đầy đủ để tránh đổi quyền giữa hai bước. Tài nguyên không thuộc quyền trả 404. |
| DB-04 | Các lỗi xóa CV/JD, cập nhật tài khoản và lớp đã có thông báo trung tính. Lỗi DB chưa xử lý được đưa qua middleware lỗi chung; không đưa exception SQL vào response. Không bật sensitive-data logging. |
| DB-16 | scripts/Check-RawSql.ps1 phát hiện raw SQL mới hoặc thay đổi trong source; hai ngoại lệ hạ tầng được khóa bằng SHA-256 và có lý do review. |
| AUTH-02 | Mã lớp mới dùng 8 byte ngẫu nhiên (16 chữ số hex); mã lớp cũ tiếp tục dùng được. |

## Giới hạn lưu trữ

Các khóa trong nhóm `StorageLimits`, có kiểm tra cấu hình khi khởi động:

| Khóa | Mặc định | Miền hợp lệ |
|---|---:|---:|
| MaxCvsPerUser | 10 | 1–1.000 |
| MaxJdsPerUser | 20 | 1–1.000 |
| MaxActiveSessionsPerUser | 10 | 1–1.000 |
| MaxSessionsPerDay | 30 | 1–10.000 |

Buổi đang mở là Created, InProgress hoặc AwaitingQuestion. Kết thúc buổi chuyển sang ReadyForEvaluation và giải phóng hạn mức buổi đang mở; vẫn tính trong tổng buổi/ngày. Ngày tính theo UTC, làm mới lúc 07:00 UTC+7. Hạn mức không tự xóa dữ liệu cũ. CV/JD đang được lịch sử tham chiếu không bị xóa; khi đạt ngưỡng và không còn bản chưa dùng để xóa, người dùng liên hệ quản trị viên để điều chỉnh giới hạn.

Khóa lưu trữ dùng 257 semaphore cố định, không lưu danh sách user vô hạn. Các tài khoản có thể trùng nhóm khóa và chờ nhau ngắn hạn, nhưng bộ đếm/quota vẫn riêng từng tài khoản. Cơ chế này bảo vệ request đồng thời trong **một process**. Chưa bảo đảm hạn mức khi chạy nhiều process/instance cùng database; lúc đó cần khóa hoặc bộ đếm nguyên tử trong SQL/Redis.

## Giới hạn văn bản trong SQL

Đơn vị là số code unit UTF-16, thống nhất với `.Length` trong .NET. Không thu nhỏ kiểu cột cũ. Các cột JSON chừa đủ chỗ cho trường hợp serializer escape ký tự Unicode thành `\uXXXX`, nên có giới hạn lớn hơn nội dung hiển thị.

| Dữ liệu | Giới hạn |
|---|---:|
| Nội dung CV / snapshot CV | 50.000 |
| Nội dung JD / snapshot JD | 30.000 |
| Phân tích CV JSON | 5.000.000 |
| SkillGap JSON / snapshot SkillGap | 4.500.000 |
| Câu hỏi buổi phỏng vấn | 5.000 |
| Câu trả lời / bản nháp / câu trả lời cải thiện | 20.000 |
| SpeechEventsJson | 1.000.000 |
| StrengthsJson / ImprovementsJson mỗi trường | 150.000 |
| ChangesExplanation / lý do kết quả | 8.000 |
| CodeReviewJson | 250.000 |
| Feedback từng tiêu chí | 5.000 |
| Evidence từng tiêu chí | 2.000 |
| SummaryJson | 1.100.000 |
| FillerDetailsJson | 20.000 |
| Câu hỏi ngân hàng / hướng dẫn bài tập | 4.000 |
| Nhận xét giảng viên | 3.000 |

Tiêu đề, tên, mô tả ngắn và URL đã có giới hạn độ dài trước đợt này. Bảng giới hạn chuẩn nằm trong `DatabaseTextLimits.cs`; EF model metadata có test kiểm tra mọi chuỗi nghiệp vụ không giới hạn kiểu cột đều có CHECK tương ứng.

## Migration và dữ liệu cũ

Migration mới: `20261110030924_AddSecurityLimitsAndAiUsage`.

- Thêm `AiRequestLogs.KeySource` với default `Legacy`, `DispatchedAt` nullable và index `(UserId, KeySource, CreatedAt)`; log cũ không bị gán nhầm sang key hệ thống hay cá nhân.
- Trước mỗi CHECK độ dài, migration kiểm tra có dòng cũ vượt ngưỡng hay không. Nếu không có, tạo constraint trusted bằng `WITH CHECK`.
- Nếu đã có dòng quá dài, giữ nguyên dữ liệu và tạo constraint bằng `WITH NOCHECK`. Constraint vẫn được bật để từ chối lần ghi mới sai giới hạn; chỉ dữ liệu lịch sử chưa được xác thực. Migration không cắt nội dung và không xóa dòng. Thông báo chỉ nói có dữ liệu cũ cần review, không in nội dung CV/JD/câu trả lời.
- Người quản trị rà soát constraint `is_not_trusted = 1` trong SSMS, xử lý bản ghi cũ theo nghiệp vụ có backup, rồi dùng `WITH CHECK CHECK CONSTRAINT` để xác thực lại. Không disable constraint để nhập thêm dữ liệu quá giới hạn.
- Backup trước khi migrate/rollback. Rollback chỉ bỏ các CHECK và cột/index mới của log AI; CV/JD/session vẫn được giữ. Hai trường mới của log AI sẽ mất nếu rollback, nên xuất hoặc backup trước khi cần giữ thống kê.

Chạy tại thư mục chứa solution, sau khi cấu hình môi trường và connection qua user-secrets/biến môi trường:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --project src/AiInterview.Infrastructure --startup-project src/AiInterview.Web
```

Rollback về migration liền trước (không dùng cho việc sửa lỗi kết nối):

```powershell
dotnet ef database update 20261013105437_AddPersonalAiSettings --project src/AiInterview.Infrastructure --startup-project src/AiInterview.Web
```

Không chạy các lệnh trên database không có quyền quản lý. Production phải cấu hình connection riêng và chạy migration có chủ đích bằng tài khoản phù hợp; không bật seed tài khoản mẫu.

## Kiểm tra raw SQL và quyền

```powershell
pwsh -File scripts/Check-RawSql.ps1
```

Hai ngoại lệ đã review:

1. LocalDbStartup: câu `SELECT 1` cố định để kiểm tra LocalDB sẵn sàng, không ghép input vào SQL.
2. Migration mới: DDL dùng tên bảng/cột/predicate cố định trong migration, không có input của người dùng; cần SQL để preflight và giữ lại dữ liệu quá giới hạn cũ. Có kiểm thử migrate/rollback và từ chối dữ liệu mới vượt ngưỡng.

Mọi file raw SQL thay đổi đều làm hash không khớp và khiến kiểm tra thất bại. Không cập nhật hash chỉ để bỏ qua cảnh báo: phải review lý do, đảm bảo giá trị động được tham số hóa, có test. Script chỉ in tên file cần review, không in câu SQL hoặc tham số.

Checklist review định kỳ: route/query ID có predicate owner/role; giảng viên chỉ đọc sinh viên trong lớp mình; kiểm tra quyền trước Include dữ liệu lớn; upload/AI không ghi nội dung nhạy cảm vào log; không dùng input để ghép SQL; migration không xóa/thu ngắn dữ liệu cũ âm thầm. Phân quyền hiện nằm ở ứng dụng, chưa có SQL row-level security.

## Kiểm thử và giới hạn công bố

`DatabaseSecurityTests` gồm kiểm tra metadata mọi cột text lớn; SQL migrate có CV cũ quá dài và rollback/re-upgrade; chặn CV/JD mới vượt ngưỡng (kể cả dấu cách cuối chuỗi); Report bị từ chối chỉ chạy truy vấn scalar không tải turns/snapshot; chủ sở hữu/giảng viên đúng lớp truy cập được và mất quyền sau khi xóa membership; đồng thời tạo CV/JD/session chỉ một request qua khi hạn mức là một; kết thúc buổi giải phóng quota active nhưng không giải phóng quota ngày. Các SQL test chỉ chạy trên database ngẫu nhiên do fixture tự tạo, không truy cập database thật của người dùng.

Các kiểm thử quyền CV/JD/session khác và seed đã có trong suite hiện hữu. Kết quả chạy toàn bộ suite được tổng hợp trong tài liệu kiểm chứng của bản bàn giao; tài liệu này không thay thế kết quả test thực tế.

SQL test cần instance riêng `AiInterviewDev`. Kiểm tra và khởi động bằng SQLLocalDB đi kèm SQL Server/Visual Studio; nếu `info` báo instance chưa tồn tại mới chạy `create`. Không xóa/reset instance đang có:

```powershell
SqlLocalDB.exe info AiInterviewDev
# Chỉ khi instance chưa tồn tại:
SqlLocalDB.exe create AiInterviewDev
SqlLocalDB.exe start AiInterviewDev
$env:AIINTERVIEW_TEST_SQL = '1'
dotnet test tests/AiInterview.Tests
```

Fixture tạo database với tên `AiInterview_Test_` và GUID riêng cho mỗi test, kiểm tra database chưa tồn tại trước khi tạo, và chỉ xóa đúng database do fixture đó tạo khi kết thúc. Instance test không phải database ứng dụng.

Chưa triển khai DB-12 audit log đầy đủ; giữ trong hướng phát triển. Khi deploy cần tách tài khoản migrate/runtime, login SQL quyền tối thiểu, chứng chỉ hợp lệ với Encrypt=True, backup DB kèm Data Protection key ring, kiểm thử restore, retention job, đo truy vấn/index theo dữ liệu thật, kiểm soát timeout/retry và hạn mức nhiều instance. Không coi giới hạn localhost hiện tại là cơ chế vận hành production hoàn chỉnh.
