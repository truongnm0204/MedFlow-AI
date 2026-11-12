# 0035 — Update Diagnostic Result API Contract (`#136` / `M3-028` / `UC43`)

- Ngay: 2026-11-12
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#136` (`M3-028`) là bước sửa kết quả cận lâm sàng sau khi đã khóa. `#134` (`EnterDiagnosticResultAsync`) từ chối mọi lần ghi sau finalize bằng `409` (ADR 0033). Sửa bản nháp vẫn đi Enter. Không thêm cột database: `diagnostic_result_versions.version_no` và `correction_reason` đã có.

## Decision

1. **Không gọi `EnterDiagnosticResultAsync`.** Route Enter (`POST|PUT|PATCH .../result`) giữ nguyên và vẫn `409` khi kết quả đã khóa.

2. **Endpoint mới (`DiagnosticResultsController`, `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`)**:
   - `POST|PUT|PATCH /api/v1/diagnostic/orders/{idOrCode}/result/amend`
   - `POST|PUT|PATCH /api/v1/diagnostic-orders/{idOrCode}/result/amend`
   - `POST|PUT|PATCH /api/v1/diagnostic/results/{idOrCode}/amend`
   - `POST|PUT|PATCH /api/v1/diagnostic-results/{idOrCode}/amend`
   - Body identifier: `POST|PUT /api/v1/diagnostic/results/amend`, `POST|PUT /api/v1/diagnostic/orders/result/amend`
   - Response tái sử dụng `EnterDiagnosticResultResponseDto` với `ActionOutcome = "DIAGNOSTIC_RESULT_AMENDED"`.

3. **Auth hai lớp**, cùng Enter: `DIAGNOSTIC_RESULT_WRITE` và `EnsureOrderHospitalOwnership`. Bác sĩ không sửa kết quả.

4. **Chỉ amend kết quả đã khóa.** Chưa finalize → `409`. `CorrectionReason` hoặc `AmendmentReason` bắt buộc, 5–500 ký tự, cấm `<>`. `Notes` không thay lý do. `ResultText` 3–5000, `Conclusion` 3–2000. `ReportFileUrl` null = giữ URL bản latest; `""` = xóa; có giá trị = thay (cùng rule URL của Enter).

5. **Version mới.** `VersionNo = max + 1`, `IsFinalized = true`, `EnteredBy*` và `VerifiedBy*` = staff đang sửa. Header `diagnostic_results.status` giữ `FINALIZED` hoặc `VERIFIED`. `FinalizedAt` và `FinalizedByStaffId` không đổi. Không status `AMENDED`.

6. **Không mở lại lifecycle.** Order và queue giữ `COMPLETED`. Chỉ `queue_entries.state_version + 1`. Không ghi `visits.status`, `PresenceStatus`, `BlockedReason`. `ExpectedStateVersion` lệch → `409`. Nội dung sau quy tắc URL trùng latest → `409` "Không có thay đổi nội dung kết quả cận lâm sàng." và không thêm version.

7. **Cửa sổ sửa (`409`)**: order/queue `CANCELLED`; queue `SKIPPED`; visit `COMPLETED|DONE|CANCELLED|NO_SHOW`; hồ sơ `FINALIZED|LOCKED|CLOSED`; bệnh nhân, dịch vụ, khoa, điểm phục vụ hoặc phòng không active.

8. **Eligibility.** `CanAmendResult` và `AmendDisabledReason` trên list/detail. `CanEnterResult` và `DisabledReason` giữ nghĩa Enter (đã khóa → không nhập mới).

9. **Audit** `DIAGNOSTIC_RESULT_AMENDED`, `EntityType = DIAGNOSTIC_RESULT`. GET detail trả action này trong lịch sử.

## Consequences

- **Duoc**: Sửa kết quả đã khóa mà bác sĩ vẫn thấy latest version và `AllMandatoryResultsReady` không tụt, vì header vẫn finalized.
- **Mat**: `PUT .../result` không phải amend. FE `#135` phải gọi `/amend`.
- **Rui ro con lai**: Enter vẫn chưa chặn queue `SKIPPED`. Amend thì chặn. Không vá Enter trong `#136`.
