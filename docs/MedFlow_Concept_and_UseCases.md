# MedFlow AI — Concept & Use Case Catalogue

> Tài liệu tham chiếu: Report 1 – Project Introduction; Report 2 – Project Management Plan; Report 3 – Software Requirement Specification (SRS); MedFlow_3_Iterations_Dependencies_by_UseCase.xlsx.

## 1. Concept dự án

**MedFlow – Clinical Workflow and Patient Routing System** là hệ thống web quản lý quy trình khám ngoại trú không cấp cứu, kết nối Patient, Patient Service Staff, Doctor, Diagnostic Services Staff, Pharmacy Staff, Hospital Admin và System Admin. Mục tiêu là tổ chức xuyên suốt đăng ký/đặt lịch → tiếp nhận → khám → chỉ định cận lâm sàng → trả kết quả → kết luận → đơn thuốc/cấp phát → hồ sơ sau khám.

**Phạm vi cốt lõi:** xác thực và phân quyền, hồ sơ bệnh nhân và người thân, gợi ý AI không mang tính chẩn đoán, đặt lịch, check-in do nhân viên tiếp nhận xác nhận, quản lý Visit/Room/Queue, khám lâm sàng, diagnostic, prescription/pharmacy, thanh toán QR, hồ sơ bệnh án, quản trị bệnh viện và quản trị hệ thống.

**Ngoài phạm vi bản v1:** cấp cứu, nội trú, LIS/RIS/PACS đầy đủ, BHYT và kế toán toàn diện, ERP nhà thuốc, tích hợp chính phủ production, app mobile native, tự check-in bằng QR/kiosk. AI chỉ hỗ trợ gợi ý chuyên khoa/dịch vụ/lịch, không chẩn đoán hay kê đơn.

## 2. Phương pháp Iterative & Incremental

Hệ thống chia thành **3 iteration** theo Report 2. Mỗi iteration bao gồm phân tích/thiết kế chi tiết, triển khai FE/BE, unit/API/UI/integration/E2E testing, sửa lỗi, review và bàn giao một increment hoạt động. Increment sau **kế thừa** toàn bộ increment trước, có regression test.

| Milestone | Thời gian | Số backlog item | Increment có thể demo |
|---|---|---:|---|
| M1 | Week 6-7 | 47 | Patient có thể đăng ký/đăng nhập, quản lý profile, tìm Doctor/Service, xem slot, đặt Appointment; Hospital/System Admin có dữ liệu nền và RBAC cơ bản để vận hành. |
| M2 | Week 8-9 | 38 | Patient có thể reschedule/cancel, đến quầy theo Appointment hoặc Walk-in, được tạo Visit, route vào phòng/Doctor, tham gia queue và Doctor ghi nhận khám lâm sàng. |
| M3 | Week 10-13* | 45 | Hoàn chỉnh Doctor → Diagnostic → Result → Return-to-Doctor → Diagnosis → Prescription/Dispensing/Payment → Patient Record; hoàn thiện admin, integrations, audit, AI optional và release hardening. |

### Quy tắc phụ thuộc giữa các iteration

1. **M1** cung cấp tài khoản, danh mục Doctor/Service/Slot và Appointment để làm dữ liệu nền cho các luồng sau.
2. **M2** tái sử dụng Appointment và User/Patient từ M1; bổ sung Visit, Queue và Clinical Examination, sau đó chạy regression cho M1.
3. **M3** dựa vào Visit, Queue và Doctor Consultation từ M2; bổ sung Diagnostic Order/Result, Return Consultation, Diagnosis, Prescription/Dispense/Payment và Patient Records; thực hiện system/UAT/regression cho toàn hệ thống.

## 3. Actors và trách nhiệm

| Actor | Phạm vi chính |
|---|---|
| Guest | Homepage, login, patient registration |
| Patient | Profile, family, appointment, queue, AI recommendation, payment, records |
| Patient Service Staff | Search patient, reception/check-in, walk-in, routing, examination queue |
| Doctor | Assigned queue, consultation, diagnostic orders, diagnosis, prescription, completion |
| Diagnostic Services Staff | Diagnostic orders, patient receiving, diagnostic queue, execution, results |
| Pharmacy Staff | Prescription lookup, medicine preparation, dispensing |
| Hospital Admin | Hospital structures, doctors, services, slots, policies, reports |
| System Admin | Staff accounts, RBAC, integrations, audit |
| External services | AI Recommendation, SePay payment, notifications (theo tích hợp được cấu hình) |

## 4. Use Case Catalogue (SRS UC01–UC60)

Danh mục dưới đây theo **bảng Use Cases mục I.4.2 của SRS**. Một số UC có thể chưa có issue 1:1 vì backlog được chia theo màn hình/chức năng nhỏ hoặc công việc kỹ thuật; bảng “Issue mapping” cho biết liên kết thực tế theo workbook.

| UC | Use case trong SRS | GitHub milestone có issue ánh xạ |
|---|---|---|
| UC01 | View Homepage | M1 - Iteration 1 |
| UC02 | Login | M1 - Iteration 1 |
| UC03 | Register Patient Account | M1 - Iteration 1 |
| UC04 | Logout | M1 - Iteration 1 |
| UC05 | Manage Personal Profile | M1 - Iteration 1 |
| UC06 | Manage Family Profile | M1 - Iteration 1, M2 - Iteration 2 |
| UC07 | Receive AI Specialty / Service Recommendation | Chưa có ánh xạ UC trực tiếp trong file issues |
| UC08 | Receive AI Appointment Schedule Recommendation | Chưa có ánh xạ UC trực tiếp trong file issues |
| UC09 | Search Doctor | M1 - Iteration 1 |
| UC10 | Search Examination Service | M1 - Iteration 1 |
| UC11 | View Available Appointment Slots | M1 - Iteration 1 |
| UC12 | Book Appointment | M1 - Iteration 1 |
| UC13 | Reschedule Appointment | M2 - Iteration 2 |
| UC14 | Cancel Appointment | M2 - Iteration 2 |
| UC15 | Track Appointment Status | M1 - Iteration 1, M2 - Iteration 2 |
| UC16 | Track Examination Queue | M2 - Iteration 2 |
| UC17 | Receive Notifications and Reminders | M2 - Iteration 2 |
| UC18 | Pay by QR | M3 - Iteration 3 |
| UC19 | View Medical Record | M3 - Iteration 3 |
| UC20 | View Examination Result | Chưa có ánh xạ UC trực tiếp trong file issues |
| UC21 | View Prescription | M3 - Iteration 3 |
| UC22 | Search Patient | M2 - Iteration 2 |
| UC23 | Manage Patient Profile at Reception | M2 - Iteration 2 |
| UC24 | Check-in Patient with Appointment | M2 - Iteration 2 |
| UC25 | Create Walk-in Visit | M2 - Iteration 2 |
| UC26 | Assign Doctor to Visit | M2 - Iteration 2 |
| UC27 | Assign Examination Room to Visit | M2 - Iteration 2 |
| UC28 | Manage Examination Queue | M2 - Iteration 2, M3 - Iteration 3 |
| UC29 | Print Examination Ticket | M2 - Iteration 2 |
| UC30 | View Assigned Queue | M2 - Iteration 2 |
| UC31 | View Patient Examination | M2 - Iteration 2 |
| UC32 | View Medical History | M2 - Iteration 2 |
| UC33 | Record Clinical Examination | M2 - Iteration 2 |
| UC34 | Create Diagnostic Order | M3 - Iteration 3 |
| UC35 | View Diagnostic Result | M3 - Iteration 3 |
| UC36 | Record Diagnosis | M3 - Iteration 3 |
| UC37 | Create Prescription | M3 - Iteration 3 |
| UC38 | Complete Visit | M3 - Iteration 3 |
| UC39 | Review and Confirm Diagnostic Order | M3 - Iteration 3 |
| UC40 | Receive Diagnostic Patient | M3 - Iteration 3 |
| UC41 | Manage Diagnostic Queue | M3 - Iteration 3 |
| UC42 | Update Service Execution Status | M3 - Iteration 3 |
| UC43 | Update Diagnostic Result | M3 - Iteration 3 |
| UC44 | Search Prescription | M3 - Iteration 3 |
| UC45 | Prepare Medicines According to Prescription | M3 - Iteration 3 |
| UC46 | Confirm Medicine Dispensing | M3 - Iteration 3 |
| UC47 | Manage Department | M1 - Iteration 1, M2 - Iteration 2 |
| UC48 | Manage Examination Room | M1 - Iteration 1, M2 - Iteration 2 |
| UC49 | Manage Doctor | M1 - Iteration 1, M2 - Iteration 2 |
| UC50 | Manage Examination Service | M1 - Iteration 1, M2 - Iteration 2 |
| UC51 | Manage Diagnostic Service | M3 - Iteration 3 |
| UC52 | Manage Service Pricing | M3 - Iteration 3 |
| UC53 | Manage Examination Slots | M1 - Iteration 1, M2 - Iteration 2 |
| UC54 | Configure Appointment Policy | M1 - Iteration 1 |
| UC55 | Configure Queue Policy | M2 - Iteration 2 |
| UC56 | View Operational Report | M3 - Iteration 3 |
| UC57 | Manage Staff Accounts | M1 - Iteration 1, M2 - Iteration 2 |
| UC58 | Manage Roles and Permissions | M1 - Iteration 1, M2 - Iteration 2 |
| UC59 | Manage Integration Configuration | M3 - Iteration 3 |
| UC60 | View Audit Log | M3 - Iteration 3 |

> **Lưu ý về UC31:** Bảng UC trong nội dung SRS xác định UC31 = **View Patient Examination**, mặc dù mục lục phần Use Case Specifications có dòng tên khác. Sử dụng mã và tên từ bảng UC chính để truy vết issue.

## 5. Module / Function theo từng milestone

### M1 — Foundation + Appointment Booking
- **Authentication & Role Landing:** Login, Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard, Logout, Register Patient Account, Verify Patient Account, View Diagnostic Dashboard, View Doctor Dashboard, View Hospital Admin Dashboard, View Patient Home, View Pharmacy Dashboard, View Reception Dashboard, View System Admin Dashboard
- **Profile Management:** Create Family Profile, Update Family Profile, Update Personal Profile, View Family Profile Detail, View Family Profile List, View Personal Profile, View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile
- **Appointment Management:** Book Appointment, Review Appointment Booking, Search Doctor, Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail, Search Examination Service, View Appointment Detail, View Appointment List, View Available Appointment Slots, View Doctor Detail, View Examination Service Detail
- **Hospital Configuration:** Create Department, Create Doctor, Create Examination Room, Create Examination Service, Update Department, Update Doctor, Update Examination Room, Update Examination Service, View Department List, View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service, View Doctor List (Admin), View Examination Room List, View Examination Service List (Admin)
- **Scheduling & Operations:** Configure Appointment Policy, Create Examination Slot, Update Examination Slot, View Examination Slot List, View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy
- **System Administration:** Assign Permissions, Create Staff Account, Enforce Role-based Access, Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions, View Role List, View Staff Account List
- **Cross-module:** Patient/Staff/Admin routes; session expiration; cross-account patient data access, Re-run all M1 features with seeded demo data, browser smoke, CI, Register/Login → role authorization → search doctor/service → available slot → booking → appointment detail; slot capacity/no duplicate booking
- **Quality:** All delivered module functions

### M2 — Reception + Visit + Queue + Basic Consultation
- **Profile Management:** Remove Family Profile
- **Appointment Management:** Cancel Appointment, Receive Appointment Reminder, Reschedule Appointment, Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder, Track Appointment Status
- **Reception & Check-in:** Check-in Scheduled Patient, Create Patient Profile at Reception, Create Walk-in Visit, Search Patient / Appointment, Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit, Update Patient Profile at Reception, View Check-in Result, View Patient Detail
- **Visit & Routing:** Assign Doctor to Visit, Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket, Assign Examination Room, Print Examination Ticket, Transfer Patient to Compatible Room
- **Queue Management:** Apply Clinical Priority, Call Next Eligible Patient, Cancel Queue Entry, Handle Early Arrival / Safe Backfill, Handle Late Arrival / Missed Turn, Track Patient Queue, Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry, Update Queue Status, View Doctor Assigned Queue, View Examination Queue
- **Consultation:** Open Consultation Workspace, Open Consultation Workspace; View Medical History; Record Clinical Examination, Record Clinical Examination, View Medical History
- **Hospital Configuration:** Deactivate Department, Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service, Deactivate Doctor, Deactivate Examination Room, Deactivate Examination Service
- **Scheduling & Operations:** Close Examination Slot, Close Examination Slot; Configure Queue Policy, Configure Queue Policy
- **System Administration:** Create Role, Deactivate Staff Account, Update Role, Update Staff Account, Update Staff Account; Deactivate Staff Account; Create Role; Update Role
- **Cross-module:** Early/backfill, missed turn, priority, cancellation, cross-room routing, M1 appointment → reschedule/cancel where allowed → check-in OR walk-in → Visit → room/doctor → queue → call next → clinical exam, Repeat full M1 booking and M2 reception/consultation with integration environment
- **Quality:** All delivered module functions

### M3 — Clinical Completion + Admin + Release Validation
- **Queue Management:** Create Return-to-Doctor Queue Entry
- **Consultation:** Complete Visit, Create Additional Diagnostic Order, Record Diagnosis & Clinical Conclusion, Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit, Review Diagnostic Results
- **Diagnostic Management:** Create Diagnostic Order, Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result, Enter Diagnostic Result, Manage Diagnostic Queue, Receive Diagnostic Patient, Review & Confirm Diagnostic Order, Update Diagnostic Result, Update Diagnostic Service Status, View Diagnostic Result, View Diagnostic Results for Doctor, View Diagnostic Worklist
- **Prescription & Pharmacy:** Confirm Medicine Dispensing, Create Prescription, Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result, Prepare Medicines, Search Prescription, Select Medicine Batch (FEFO), View Dispensing Result
- **Patient Records:** View Diagnostic Result, View Medical Record Detail, View Medical Record List, View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription, View Prescription
- **Payment:** Generate QR Payment, Track Payment Result, View Charges & Payment, View Charges & Payment; Generate QR Payment; Track Payment Result
- **Hospital Configuration:** Create Diagnostic Service, Deactivate Diagnostic Service, Update Diagnostic Service, Update Service Price, View Diagnostic Service List, View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price, View Service Pricing
- **Scheduling & Operations:** View Operational Reports
- **System Administration:** Search & Filter Audit Log, Update Integration Configuration, View Audit Log, View Integration Configuration, View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log
- **AI Recommendation:** Receive Appointment Schedule Recommendation, Receive Specialty / Service Recommendation, Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation
- **Cross-module Hardening:** Activity & Audit Completeness, End-to-End Regression & UAT Fixes, Unified Loading / Empty / Error States, Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes, Unsaved Change Protection
- **Cross-module:** Cross-role patient clinical access; concurrency on queue/slot; endpoint performance smoke, Doctor exam → diagnostic order → diagnostic queue → result → return-doctor queue → diagnosis → visit completion, Doctor prescription → pharmacy search → FEFO batch prepare → dispense → stock transaction; QR pending/success/fail, Full booking → check-in/walk-in → queue → doctor → diagnostic → return → prescription → payment → records; UAT evidence, Visit complete → patient sees own record/result/prescription; AI recommendation optional fallback
- **Quality:** All delivered module functions

## 6. Business rules quan trọng phải kiểm thử

- Username/email duy nhất; access control theo vai trò, tài khoản và patient/visit ownership.
- Chỉ được booking/reschedule khi slot còn capacity; không được có appointment active trùng thời gian.
- Booking thành công tạo queue reservation; queue number không tự đổi hay tái sử dụng tùy tiện.
- Appointment khác Visit; check-in lặp không được tạo thêm active Visit; walk-in có thể không có Appointment.
- Queue chỉ được gọi khi bệnh nhân đủ điều kiện, có mặt và các prerequisite đã thỏa.
- Diagnostic Result phải gắn Diagnostic Order; Diagnostic Order phải thuộc Visit hợp lệ.
- Return-to-Doctor chỉ đủ điều kiện khi các kết quả bắt buộc sẵn sàng và bệnh nhân quay lại khu vực bác sĩ.
- Medical Record finalized cần bảo tồn lịch sử/chỉnh sửa truy vết.
- Prescription phải thuộc Visit và được bác sĩ có trách nhiệm phát hành; chỉ dispense với prescription hợp lệ.
- Không được dispense lô thuốc quá hạn/blocked; mọi thay đổi tồn kho có Stock Transaction.
- Payment transaction reference cần duy nhất để tránh ghi nhận thanh toán trùng.

## 7. Quy tắc truy vết UC ↔ Issue

- Mỗi issue phát triển được tách **FE** và **BE**, nhưng cùng trỏ về UC scope nếu SRS định nghĩa.
- Các issue kiểm thử truy vết module/UC, điều kiện cần hoàn thành FE/BE hoặc E2E tương ứng.
- Dependency được giữ nguyên ở cột `Dependencies (UC code + name)` trong file Excel. Nếu phần việc không có UC trực tiếp, ghi `Cross-cutting task` thay vì tự đặt mã UC.
- Khi requirement thay đổi: tạo issue `type:change-requirement`, cập nhật SRS/UC, kiểm tra tác động FE, BE, testing và iteration.

## 8. Tài liệu nguồn

- `Report-1_Project-Introduction.docx` — concept, scope và exclusions.
- `Report-2_Project_Management_Plan.docx` — 3 iteration (Weeks 6–11) + Verification/Validation (Weeks 12–13), trách nhiệm và quản lý chất lượng.
- `Report-3_Software_Requirement_Specification.docx` — danh mục UC, actors, business rules, permissions, workflows.
- `MedFlow_3_Iterations_Dependencies_by_UseCase.xlsx` — issue breakdown 355 mục và dependency cụ thể.
