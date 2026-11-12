# 0036 — View Diagnostic Result API Contract (`#138` / `M3-030` / `UC35`)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#138` (`M3-030`) là bước đọc kết quả cận lâm sàng cho `DIAGNOSTIC_STAFF` sau UC43. `#134` đã có `GET /api/v1/diagnostic/results` và `GET /api/v1/diagnostic/results/{idOrCode}` phục vụ bàn nhập kết quả: eligibility, `CanEnterResult`, `CanAmendResult`, điểm phục vụ. `#136` thêm amend. Cả hai GET đó giữ nguyên.

Schema chỉ có `diagnostic_results` và `diagnostic_result_versions.report_file_url`. `diagnostic_result_files` đã gộp vào URL này (`database/README.md`). Không có bảng ảnh, video, PACS, giải phẫu, vi sinh. Màn `SCR-DIA-08` (`diagnostic-result-view-page.tsx`) đang mock và chưa gắn route; contract BE không theo mock đó.

## Decision

1. **Endpoint đọc mới** trên `DiagnosticResultsController`, `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`:
   - `GET /api/v1/diagnostic/results/view`
   - `GET /api/v1/diagnostic-results/view`
   - `GET /api/v1/diagnostic/results/{idOrCode}/view`
   - `GET /api/v1/diagnostic-results/{idOrCode}/view`
   - `GET /api/v1/diagnostic/orders/{idOrCode}/result/view`
   - `GET /api/v1/diagnostic-orders/{idOrCode}/result/view`
   - Route literal `results/view` phải thắng `results/{idOrCode}`. Mã phiếu `VIEW` mở bằng `.../{idOrCode}/view`.

2. **Không sửa contract Enter/Amend.** `GET` list/detail của `#134` và `POST|PUT|PATCH .../amend` của `#136` giữ nguyên.

3. **Auth hai lớp, chỉ đọc.** Layer 2 tái sử dụng `EnsureDiagnosticStaffAccessAsync(..., requireResultWritePermission: false)` và `EnsureOrderHospitalOwnership`. Quyền đọc giữ `DIAGNOSTIC_RESULT_WRITE | QUEUE_MANAGE | DIAGNOSTIC_ORDER`. Không thêm permission, không seed. Bác sĩ, bệnh nhân, role khác: `403` ở layer 1. Phiếu khác bệnh viện: `403`, cùng câu với Enter. Không có quyền diagnostic: `403`.

4. **Chỉ phiếu đã có dòng `diagnostic_results`.** `HasResult == true` gồm nháp (`DRAFT`, `PRELIMINARY`, `PENDING_VERIFY`), đã khóa (`FINALIZED`, `VERIFIED`) và phiếu hủy nhưng còn dòng kết quả. Phiếu chưa có dòng kết quả không vào list. Detail của phiếu đó: `404` "Phiếu chỉ định chưa có kết quả cận lâm sàng." Không thấy phiếu: `404` cùng `NotFoundException("DiagnosticOrder", key)` của Enter. List rỗng: `200` và `Items` rỗng.

5. **Đọc không chặn vòng đời.** Dịch vụ, khoa, phòng, bệnh nhân inactive, visit đã đóng, hồ sơ đã khóa vẫn đọc được. Không `409`. Không ghi `audit_logs`. Không tăng `queue_entries.state_version`. Không gọi `SaveChanges` trên nhánh GET.

6. **Tài liệu = URL đã lưu.** `DiagnosticResultDocumentDto`: `ReportFileUrl`, `HasDocument`, `FileName` (segment cuối của URL http/https, không I/O). URL rỗng → `HasDocument = false`, `ReportFileUrl = null`. Server không tải URL.

7. **Nội dung detail.** Latest version: `ResultText`, `Conclusion`, `CorrectionReason`, người nhập / người duyệt, thời điểm. Kèm toàn bộ version và lịch sử audit đã có (`DIAGNOSTIC_RESULT_ENTERED|FINALIZED|AMENDED`). Bệnh nhân dùng `DiagnosticOrderPatientSummaryDto` (có `Allergies`). Lâm sàng dùng `DiagnosticOrderClinicalSummaryDto` (có mã chẩn đoán lượt khám). Không trả `CanEnterResult`, `CanAmendResult`, eligibility ghi, danh sách điểm phục vụ.

8. **List.** Query: `ResultStatus`, `Category`, `Keyword`, `VisitId`, `VisitCode`, `Limit` (mặc định 50, 1–100). `COMPLETED` = đã finalize, cùng nghĩa Enter. `PENDING` → `400`. Summary đếm toàn bộ kết quả trong bệnh viện trước filter và trước `Limit`. Item sort `EnteredAt` giảm dần, rồi `OrderId` giảm dần.

## Consequences

- **Duoc**: Staff đọc đủ bản ghi đã lưu mà không đụng cửa sổ ghi của `#134`/`#136`.
- **Mat**: Một DTO và vài route mới. Màn mock ảnh/video/giải phẫu chưa có dữ liệu để map.
- **Rui ro con lai**: `GET /results/view` nuốt mã phiếu đúng bằng `VIEW`. Đọc PHI không có audit; các GET hiện tại cũng không audit.
