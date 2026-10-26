-- MedFlow AI FINAL v4.1 Priority 1 -- CONNECTED SAMPLE DATA for PostgreSQL
-- Compatible only with: MedFlowAI_FINAL_v4_1_Priority1_PostgreSQL.sql
-- Synthetic demonstration data. Not for production or clinical use.
-- RUN ORDER: (1) create an EMPTY database and run schema .sql, (2) run this seed .sql.
-- This script must be run on a fresh/empty MedFlow schema; do not run it twice.
-- Roles: 7; Users: 14 total (exactly 2 accounts per role).
-- DEMO PASSWORD (LOCAL users): Demo@12345
-- Password hashes use ASP.NET Core Identity PasswordHasher (PBKDF2-SHA256) —
-- pre-rehashed from the original Argon2id upstream values for compatibility
-- with MedFlow.API's existing PasswordHasher<User> usage.
-- Email domain .test and endpoint domain .invalid are deliberately non-routable.
-- All patients, diagnostics, medications, addresses, licenses and identifiers are fictional.
-- Clinical data and ICD descriptions are placeholders and must not guide real care.
BEGIN;
-- Safety guard: refuse to seed a database that already has core users.
DO $medflow_seed$ BEGIN IF EXISTS (SELECT 1 FROM "users") THEN RAISE EXCEPTION 'Seed requires empty MedFlow tables: users is not empty'; END IF; END $medflow_seed$;

-- roles: 7 row(s)
INSERT INTO "roles" ("id", "code", "name") VALUES
  (1, 'PATIENT', 'Bệnh nhân'),
  (2, 'DOCTOR', 'Bác sĩ'),
  (3, 'RECEPTION', 'Nhân viên tiếp đón'),
  (4, 'DIAGNOSTIC_STAFF', 'Nhân viên cận lâm sàng'),
  (5, 'PHARMACY_STAFF', 'Nhân viên nhà thuốc'),
  (6, 'HOSPITAL_ADMIN', 'Quản trị bệnh viện'),
  (7, 'SYSTEM_ADMIN', 'Quản trị hệ thống');

-- permissions: 14 row(s)
INSERT INTO "permissions" ("id", "code") VALUES
  (1, 'PATIENT_PROFILE_READ'),
  (2, 'APPOINTMENT_BOOK'),
  (3, 'PATIENT_CHECKIN'),
  (4, 'VISIT_ASSIGN'),
  (5, 'QUEUE_MANAGE'),
  (6, 'MEDICAL_RECORD_WRITE'),
  (7, 'DIAGNOSTIC_ORDER'),
  (8, 'DIAGNOSTIC_RESULT_WRITE'),
  (9, 'PRESCRIPTION_ISSUE'),
  (10, 'MEDICINE_DISPENSE'),
  (11, 'HOSPITAL_CONFIG'),
  (12, 'USER_MANAGE'),
  (13, 'AUDIT_READ'),
  (14, 'PAYMENT_VIEW');

-- users: 14 row(s)
INSERT INTO "users" ("id", "email", "password_hash", "auth_provider", "status", "email_verified_at", "failed_login_attempts", "locked_until") VALUES
  (1, 'patient01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (2, 'patient02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (3, 'doctor01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (4, 'doctor02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (5, 'reception01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (6, 'reception02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (7, 'diagnostic_staff01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (8, 'diagnostic_staff02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (9, 'pharmacy_staff01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (10, 'pharmacy_staff02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (11, 'hospital_admin01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (12, 'hospital_admin02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (13, 'system_admin01@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL),
  (14, 'system_admin02@medflow.test', 'AQAAAAIAAYagAAAAEBo+09S/ZoecEwLG1Q1yVDz5Gi4qVjfVdQsnM3f0nlTyc6477S1V/Ibmq2sJCJ3w3g==', 'LOCAL', 'ACTIVE', '2026-10-01 08:00:00', 0, NULL);

-- user_roles: 14 row(s)
INSERT INTO "user_roles" ("user_id", "role_id") VALUES
  (1, 1),
  (2, 1),
  (3, 2),
  (4, 2),
  (5, 3),
  (6, 3),
  (7, 4),
  (8, 4),
  (9, 5),
  (10, 5),
  (11, 6),
  (12, 6),
  (13, 7),
  (14, 7);

-- role_permissions: 23 row(s)
INSERT INTO "role_permissions" ("role_id", "permission_id") VALUES
  (1, 1),
  (1, 2),
  (1, 14),
  (2, 6),
  (2, 7),
  (2, 9),
  (2, 14),
  (3, 1),
  (3, 3),
  (3, 4),
  (3, 5),
  (3, 14),
  (4, 5),
  (4, 8),
  (5, 10),
  (5, 14),
  (6, 4),
  (6, 5),
  (6, 11),
  (6, 14),
  (7, 11),
  (7, 12),
  (7, 13);

-- auth_sessions: 10 row(s)
INSERT INTO "auth_sessions" ("id", "user_id", "refresh_token_hash", "expires_at", "revoked_at") VALUES
  (1, 1, 'demo-revoked-session-1-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (2, 2, 'demo-revoked-session-2-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (3, 3, 'demo-revoked-session-3-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (4, 4, 'demo-revoked-session-4-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (5, 5, 'demo-revoked-session-5-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (6, 6, 'demo-revoked-session-6-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (7, 7, 'demo-revoked-session-7-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (8, 8, 'demo-revoked-session-8-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (9, 9, 'demo-revoked-session-9-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00'),
  (10, 10, 'demo-revoked-session-10-NOT-A-REAL-TOKEN', '2026-11-01 00:00:00', '2026-10-25 18:00:00');

-- hospitals: 1 row(s)
INSERT INTO "hospitals" ("id", "hospital_code", "name", "address", "is_active") VALUES
  (1, 'HOS-DEMO-01', 'Bệnh viện MedFlow (giả lập)', 'Số 1 Đường Demo, Hà Nội (địa chỉ giả lập)', TRUE);

-- departments: 6 row(s)
INSERT INTO "departments" ("id", "hospital_id", "department_code", "name", "is_active") VALUES
  (1, 1, 'OPD', 'Khoa Khám bệnh', TRUE),
  (2, 1, 'GI', 'Khoa Tiêu hóa', TRUE),
  (3, 1, 'CARD', 'Khoa Tim mạch', TRUE),
  (4, 1, 'DIA', 'Khoa Cận lâm sàng', TRUE),
  (5, 1, 'PHA', 'Khoa Dược', TRUE),
  (6, 1, 'ADM', 'Phòng Hành chính', TRUE);

-- specialties: 5 row(s)
INSERT INTO "specialties" ("id", "specialty_code", "name", "is_active") VALUES
  (1, 'GI', 'Tiêu hóa', TRUE),
  (2, 'CARD', 'Tim mạch', TRUE),
  (3, 'LAB', 'Xét nghiệm', TRUE),
  (4, 'IMG', 'Chẩn đoán hình ảnh', TRUE),
  (5, 'GEN', 'Nội tổng quát', TRUE);

-- hospital_rooms: 10 row(s)
INSERT INTO "hospital_rooms" ("id", "hospital_id", "department_id", "room_code", "name", "room_type", "location", "capacity", "status") VALUES
  (1, 1, 2, 'RM-001', 'Khám Tiêu hóa 101', 'EXAMINATION', 'Tầng 1, Khu Demo', 2, 'ACTIVE'),
  (2, 1, 2, 'RM-002', 'Khám Tiêu hóa 102', 'EXAMINATION', 'Tầng 1, Khu Demo', 2, 'ACTIVE'),
  (3, 1, 3, 'RM-003', 'Khám Tim mạch 201', 'EXAMINATION', 'Tầng 1, Khu Demo', 2, 'ACTIVE'),
  (4, 1, 3, 'RM-004', 'Khám Tim mạch 202', 'EXAMINATION', 'Tầng 1, Khu Demo', 2, 'ACTIVE'),
  (5, 1, 4, 'RM-005', 'Phòng Cận lâm sàng 205', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE'),
  (6, 1, 4, 'RM-006', 'Phòng Cận lâm sàng 206', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE'),
  (7, 1, 4, 'RM-007', 'Phòng Cận lâm sàng 207', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE'),
  (8, 1, 4, 'RM-008', 'Phòng Cận lâm sàng 208', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE'),
  (9, 1, 4, 'RM-009', 'Phòng Cận lâm sàng 209', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE'),
  (10, 1, 4, 'RM-010', 'Phòng Cận lâm sàng 210', 'DIAGNOSTIC', 'Tầng 2, Khu Demo', 1, 'ACTIVE');

-- staff: 12 row(s)
INSERT INTO "staff" ("id", "user_id", "hospital_id", "department_id", "staff_code", "full_name", "status") VALUES
  (1, 3, 1, 2, 'STF-001', 'BS Nguyễn Minh Đức', 'ACTIVE'),
  (2, 4, 1, 3, 'STF-002', 'BS Trần Thu Hà', 'ACTIVE'),
  (3, 5, 1, 1, 'STF-003', 'Lễ tân Lê Lan', 'ACTIVE'),
  (4, 6, 1, 1, 'STF-004', 'Lễ tân Phạm Linh', 'ACTIVE'),
  (5, 7, 1, 4, 'STF-005', 'KTV Hoàng Anh', 'ACTIVE'),
  (6, 8, 1, 4, 'STF-006', 'KTV Võ Nam', 'ACTIVE'),
  (7, 9, 1, 5, 'STF-007', 'DS Nguyễn An', 'ACTIVE'),
  (8, 10, 1, 5, 'STF-008', 'DS Đặng Thảo', 'ACTIVE'),
  (9, 11, 1, 6, 'STF-009', 'QLBV Phan Bình', 'ACTIVE'),
  (10, 12, 1, 6, 'STF-010', 'QLBV Đỗ Hương', 'ACTIVE'),
  (11, 13, 1, 6, 'STF-011', 'QTHT Lưu Huy', 'ACTIVE'),
  (12, 14, 1, 6, 'STF-012', 'QTHT Trịnh Việt', 'ACTIVE');

-- doctors: 2 row(s)
INSERT INTO "doctors" ("id", "staff_id", "hospital_id", "doctor_code", "specialty_id", "professional_title", "is_active", "profile_image_url", "years_of_experience", "license_number", "professional_bio", "qualifications_summary") VALUES
  (1, 1, 1, 'DOC-001', 1, 'BS.CKI', TRUE, NULL, 9, 'DEMO-CCHN-001', 'Bác sĩ khám ngoại trú chuyên khoa Tiêu hóa (dữ liệu demo).', 'Bác sĩ chuyên khoa I'),
  (2, 2, 1, 'DOC-002', 2, 'ThS.BS', TRUE, NULL, 11, 'DEMO-CCHN-002', 'Bác sĩ khám ngoại trú chuyên khoa Tim mạch (dữ liệu demo).', 'Thạc sĩ y khoa');

-- patients: 10 row(s)
INSERT INTO "patients" ("id", "user_id", "patient_code", "full_name", "date_of_birth", "gender", "phone", "email", "identification_hash", "status", "address", "blood_type") VALUES
  (1, 1, 'PAT-0001', 'Nguyễn Văn An', '1980-02-02', 'MALE', '0900000001', 'patient01@medflow.test', 'd4dac994f8bef67d90e2466997300b8e6e35901a751d0a8df2c080fe47836236', 'ACTIVE', 'Địa chỉ demo số 1, Hà Nội', 'A+'),
  (2, 2, 'PAT-0002', 'Trần Thị Bình', '1981-03-03', 'FEMALE', '0900000002', 'patient02@medflow.test', '4accdcdf68ecdddffb1cfd4a7f3f793d9bdc064d959f1e6414fb87ad32748124', 'ACTIVE', 'Địa chỉ demo số 2, Hà Nội', 'B+'),
  (3, NULL, 'PAT-0003', 'Lê Minh Chi', '1982-04-04', 'FEMALE', '0900000003', 'patient03@medflow.test', 'a41f73e3f4696f0500890025e83f3a37d18a42d7e819a06c3d9745c7f24d42cf', 'ACTIVE', 'Địa chỉ demo số 3, Hà Nội', NULL),
  (4, NULL, 'PAT-0004', 'Phạm Quốc Dũng', '1983-05-05', 'MALE', '0900000004', 'patient04@medflow.test', 'c4843ab0a30cebb357db2c26dc7f918f17b19b3a19ba7abb4c8eb5ab46132d6b', 'ACTIVE', 'Địa chỉ demo số 4, Hà Nội', 'O-'),
  (5, NULL, 'PAT-0005', 'Đỗ Thị Hạnh', '1984-06-06', 'FEMALE', '0900000005', 'patient05@medflow.test', '9f8ed3bf1a237aaeb3b7901b29fdf096f808d49945de470d9205ae32fae7b82c', 'ACTIVE', 'Địa chỉ demo số 5, Hà Nội', NULL),
  (6, NULL, 'PAT-0006', 'Vũ Hoàng Long', '1985-07-07', 'MALE', '0900000006', 'patient06@medflow.test', '898e325723e1ae5313329ea96dace1c3412f025f30dff39096d958e0b292b9d8', 'ACTIVE', 'Địa chỉ demo số 6, Hà Nội', 'A+'),
  (7, NULL, 'PAT-0007', 'Ngô Thị Mai', '1986-08-08', 'FEMALE', '0900000007', 'patient07@medflow.test', '9f851c22802a61338ccdb4b13569cfde1a556c59ea849f1b1546b0ec2d7bc6c7', 'ACTIVE', 'Địa chỉ demo số 7, Hà Nội', NULL),
  (8, NULL, 'PAT-0008', 'Bùi Thanh Nam', '1987-09-09', 'MALE', '0900000008', 'patient08@medflow.test', '0fc4fe36ae6c52eec770a4fb1b19f59d76101928b6db2d3e5c44c1c0ad5743a6', 'ACTIVE', 'Địa chỉ demo số 8, Hà Nội', 'AB+'),
  (9, NULL, 'PAT-0009', 'Hoàng Ngọc Phương', '1988-10-10', 'FEMALE', '0900000009', 'patient09@medflow.test', '7ce8806f2b42e8375fc4d3898b2cc6a9233b612ad8e033f7dd553904ed91ffe8', 'ACTIVE', 'Địa chỉ demo số 9, Hà Nội', NULL),
  (10, NULL, 'PAT-0010', 'Đặng Gia Quân', '1989-11-11', 'MALE', '0900000010', 'patient10@medflow.test', 'aab424cd9d813044af011a046410a5ef5692fca2b2b1e7934e2f311deb422a30', 'ACTIVE', 'Địa chỉ demo số 10, Hà Nội', NULL);

-- patient_family_links: 2 row(s)
INSERT INTO "patient_family_links" ("owner_patient_id", "member_patient_id", "relationship", "verification_status", "verified_at", "valid_until", "revoked_at") VALUES
  (1, 3, 'CHILD', 'VERIFIED', '2026-10-01 09:00:00', '2027-10-01 09:00:00', NULL),
  (2, 4, 'SPOUSE', 'VERIFIED', '2026-10-01 09:00:00', '2027-10-01 09:00:00', NULL);

-- services: 10 row(s)
INSERT INTO "services" ("id", "hospital_id", "department_id", "specialty_id", "service_code", "name", "service_type", "description", "allows_online_booking", "requires_result_verification", "is_active") VALUES
  (1, 1, 2, 1, 'EX-GI-01', 'Khám Tiêu hóa lần đầu', 'EXAMINATION', 'Khám Tiêu hóa lần đầu (dịch vụ thử nghiệm)', TRUE, FALSE, TRUE),
  (2, 1, 3, 2, 'EX-CARD-01', 'Khám Tim mạch lần đầu', 'EXAMINATION', 'Khám Tim mạch lần đầu (dịch vụ thử nghiệm)', TRUE, FALSE, TRUE),
  (3, 1, 2, 1, 'EX-GI-02', 'Khám Tiêu hóa tái khám', 'EXAMINATION', 'Khám Tiêu hóa tái khám (dịch vụ thử nghiệm)', TRUE, FALSE, TRUE),
  (4, 1, 3, 2, 'EX-CARD-02', 'Khám Tim mạch tái khám', 'EXAMINATION', 'Khám Tim mạch tái khám (dịch vụ thử nghiệm)', TRUE, FALSE, TRUE),
  (5, 1, 4, 3, 'DIA-CBC', 'Công thức máu', 'DIAGNOSTIC', 'Công thức máu (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE),
  (6, 1, 4, 4, 'DIA-US', 'Siêu âm bụng', 'DIAGNOSTIC', 'Siêu âm bụng (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE),
  (7, 1, 4, 2, 'DIA-ECG', 'Điện tâm đồ', 'DIAGNOSTIC', 'Điện tâm đồ (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE),
  (8, 1, 4, 1, 'DIA-HP', 'Test H. pylori', 'DIAGNOSTIC', 'Test H. pylori (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE),
  (9, 1, 4, 3, 'DIA-GLU', 'Định lượng glucose', 'DIAGNOSTIC', 'Định lượng glucose (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE),
  (10, 1, 4, 4, 'DIA-XRAY', 'X-quang ngực', 'DIAGNOSTIC', 'X-quang ngực (dịch vụ thử nghiệm)', FALSE, TRUE, TRUE);

-- doctor_service_authorizations: 16 row(s)
INSERT INTO "doctor_service_authorizations" ("doctor_id", "service_id", "is_active") VALUES
  (1, 1, TRUE),
  (1, 3, TRUE),
  (1, 5, TRUE),
  (1, 6, TRUE),
  (1, 7, TRUE),
  (1, 8, TRUE),
  (1, 9, TRUE),
  (1, 10, TRUE),
  (2, 2, TRUE),
  (2, 4, TRUE),
  (2, 5, TRUE),
  (2, 6, TRUE),
  (2, 7, TRUE),
  (2, 8, TRUE),
  (2, 9, TRUE),
  (2, 10, TRUE);

-- service_prices: 10 row(s)
INSERT INTO "service_prices" ("id", "service_id", "price_amount", "effective_from", "effective_to") VALUES
  (1, 1, 200000, '2026-01-01 00:00:00', NULL),
  (2, 2, 220000, '2026-01-01 00:00:00', NULL),
  (3, 3, 170000, '2026-01-01 00:00:00', NULL),
  (4, 4, 180000, '2026-01-01 00:00:00', NULL),
  (5, 5, 90000, '2026-01-01 00:00:00', NULL),
  (6, 6, 180000, '2026-01-01 00:00:00', NULL),
  (7, 7, 120000, '2026-01-01 00:00:00', NULL),
  (8, 8, 250000, '2026-01-01 00:00:00', NULL),
  (9, 9, 70000, '2026-01-01 00:00:00', NULL),
  (10, 10, 160000, '2026-01-01 00:00:00', NULL);

-- examination_slots: 10 row(s)
INSERT INTO "examination_slots" ("id", "service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status") VALUES
  (1, 1, 1, 1, '2026-10-15 09:00:00', '2026-10-15 09:30:00', 3, 'CLOSED'),
  (2, 2, 2, 3, '2026-10-16 09:00:00', '2026-10-16 09:30:00', 3, 'CLOSED'),
  (3, 1, 1, 1, '2026-10-17 09:00:00', '2026-10-17 09:30:00', 3, 'CLOSED'),
  (4, 2, 2, 3, '2026-10-18 09:00:00', '2026-10-18 09:30:00', 3, 'CLOSED'),
  (5, 1, 1, 1, '2026-10-19 09:00:00', '2026-10-19 09:30:00', 3, 'CLOSED'),
  (6, 2, 2, 3, '2026-10-20 09:00:00', '2026-10-20 09:30:00', 3, 'CLOSED'),
  (7, 1, 1, 1, '2026-10-21 09:00:00', '2026-10-21 09:30:00', 3, 'CLOSED'),
  (8, 2, 2, 3, '2026-10-22 09:00:00', '2026-10-22 09:30:00', 3, 'CLOSED'),
  (9, 1, 1, 1, '2026-10-27 09:00:00', '2026-10-27 09:30:00', 3, 'OPEN'),
  (10, 2, 2, 3, '2026-10-28 09:00:00', '2026-10-28 09:30:00', 3, 'OPEN');

-- appointments: 10 row(s)
INSERT INTO "appointments" ("id", "appointment_code", "patient_id", "slot_id", "booking_channel", "idempotency_key", "reception_note", "status", "booked_by_user_id", "booked_at") VALUES
  (1, 'APT-202610-0001', 1, 1, 'ONLINE', 'booking-seed-0001', NULL, 'CHECKED_IN', 1, '2026-10-12 10:00:00'),
  (2, 'APT-202610-0002', 2, 2, 'ONLINE', 'booking-seed-0002', NULL, 'CHECKED_IN', 2, '2026-10-12 10:00:00'),
  (3, 'APT-202610-0003', 3, 3, 'COUNTER', 'booking-seed-0003', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (4, 'APT-202610-0004', 4, 4, 'COUNTER', 'booking-seed-0004', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (5, 'APT-202610-0005', 5, 5, 'COUNTER', 'booking-seed-0005', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (6, 'APT-202610-0006', 6, 6, 'COUNTER', 'booking-seed-0006', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (7, 'APT-202610-0007', 7, 7, 'COUNTER', 'booking-seed-0007', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (8, 'APT-202610-0008', 8, 8, 'COUNTER', 'booking-seed-0008', NULL, 'CHECKED_IN', 5, '2026-10-12 10:00:00'),
  (9, 'APT-202610-0009', 9, 9, 'COUNTER', 'booking-seed-0009', 'Lịch thử nghiệm', 'BOOKED', 5, '2026-10-12 10:00:00'),
  (10, 'APT-202610-0010', 10, 10, 'COUNTER', 'booking-seed-0010', 'Lịch thử nghiệm', 'BOOKED', 5, '2026-10-12 10:00:00');

-- visits: 10 row(s)
INSERT INTO "visits" ("id", "visit_code", "patient_id", "appointment_id", "assigned_doctor_id", "examination_room_id", "checked_in_by_staff_id", "reason_for_visit", "status", "checked_in_at", "completed_at", "completed_by_doctor_id") VALUES
  (1, 'VIS-202610-0001', 1, 1, 1, 1, 3, 'Đau thượng vị', 'COMPLETED', '2026-10-15 08:55:00', '2026-10-15 11:30:00', 1),
  (2, 'VIS-202610-0002', 2, 2, 2, 3, 4, 'Đau ngực không điển hình', 'COMPLETED', '2026-10-16 08:55:00', '2026-10-16 11:30:00', 2),
  (3, 'VIS-202610-0003', 3, 3, 1, 1, 3, 'Ợ nóng sau ăn', 'COMPLETED', '2026-10-17 08:55:00', '2026-10-17 11:30:00', 1),
  (4, 'VIS-202610-0004', 4, 4, 2, 3, 4, 'Theo dõi huyết áp', 'COMPLETED', '2026-10-18 08:55:00', '2026-10-18 11:30:00', 2),
  (5, 'VIS-202610-0005', 5, 5, 1, 1, 3, 'Đầy bụng', 'COMPLETED', '2026-10-19 08:55:00', '2026-10-19 11:30:00', 1),
  (6, 'VIS-202610-0006', 6, 6, 2, 3, 4, 'Đánh giá tim mạch định kỳ', 'COMPLETED', '2026-10-20 08:55:00', '2026-10-20 11:30:00', 2),
  (7, 'VIS-202610-0007', 7, 7, 1, 1, 3, 'Đau vùng bụng trên', 'COMPLETED', '2026-10-21 08:55:00', '2026-10-21 11:30:00', 1),
  (8, 'VIS-202610-0008', 8, 8, 2, 3, 4, 'Theo dõi tăng huyết áp', 'COMPLETED', '2026-10-22 08:55:00', '2026-10-22 11:30:00', 2),
  (9, 'VIS-202610-0009', 9, NULL, 1, 1, 3, 'Đau dạ dày từng đợt', 'COMPLETED', '2026-10-22 08:55:00', '2026-10-22 11:30:00', 1),
  (10, 'VIS-202610-0010', 10, NULL, 2, 3, 4, 'Khám sức khỏe tim mạch', 'COMPLETED', '2026-10-23 08:55:00', '2026-10-23 11:30:00', 2);

-- queue_service_points: 10 row(s)
INSERT INTO "queue_service_points" ("id", "hospital_id", "room_id", "doctor_id", "service_id", "point_type", "name", "is_active") VALUES
  (1, 1, 1, 1, 1, 'DOCTOR', 'Hàng đợi bác sĩ 1', TRUE),
  (2, 1, 3, 2, 2, 'DOCTOR', 'Hàng đợi bác sĩ 2', TRUE),
  (3, 1, 5, NULL, 5, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 5', TRUE),
  (4, 1, 6, NULL, 6, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 6', TRUE),
  (5, 1, 7, NULL, 7, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 7', TRUE),
  (6, 1, 8, NULL, 8, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 8', TRUE),
  (7, 1, 9, NULL, 9, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 9', TRUE),
  (8, 1, 10, NULL, 10, 'DIAGNOSTIC', 'Hàng đợi dịch vụ 10', TRUE),
  (9, 1, NULL, NULL, NULL, 'RECEPTION', 'Quầy tiếp đón 1', TRUE),
  (10, 1, NULL, NULL, NULL, 'RECEPTION', 'Quầy tiếp đón 2', TRUE);

-- queue_entries: 22 row(s)
INSERT INTO "queue_entries" ("id", "visit_id", "appointment_id", "service_point_id", "current_service_point_id", "queue_date", "queue_number", "queue_type", "status", "scheduled_at", "presence_status", "arrived_at", "arrival_class", "manual_priority", "state_version", "ready_at", "called_at", "started_at", "completed_at", "blocked_reason") VALUES
  (1, 1, 1, 1, NULL, '2026-10-15', 1, 'EXAMINATION', 'COMPLETED', '2026-10-15 09:00:00', 'PRESENT', '2026-10-15 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-15 09:00:00', '2026-10-15 09:02:00', '2026-10-15 09:05:00', '2026-10-15 09:30:00', NULL),
  (11, 1, NULL, 3, NULL, '2026-10-15', 1, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-15 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-15 09:38:00', '2026-10-15 09:40:00', '2026-10-15 09:45:00', '2026-10-15 10:20:00', NULL),
  (2, 2, 2, 2, NULL, '2026-10-16', 2, 'EXAMINATION', 'COMPLETED', '2026-10-16 09:00:00', 'PRESENT', '2026-10-16 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-16 09:00:00', '2026-10-16 09:02:00', '2026-10-16 09:05:00', '2026-10-16 09:30:00', NULL),
  (12, 2, NULL, 4, NULL, '2026-10-16', 2, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-16 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-16 09:38:00', '2026-10-16 09:40:00', '2026-10-16 09:45:00', '2026-10-16 10:20:00', NULL),
  (3, 3, 3, 1, NULL, '2026-10-17', 3, 'EXAMINATION', 'COMPLETED', '2026-10-17 09:00:00', 'PRESENT', '2026-10-17 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-17 09:00:00', '2026-10-17 09:02:00', '2026-10-17 09:05:00', '2026-10-17 09:30:00', NULL),
  (13, 3, NULL, 5, NULL, '2026-10-17', 3, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-17 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-17 09:38:00', '2026-10-17 09:40:00', '2026-10-17 09:45:00', '2026-10-17 10:20:00', NULL),
  (4, 4, 4, 2, NULL, '2026-10-18', 4, 'EXAMINATION', 'COMPLETED', '2026-10-18 09:00:00', 'PRESENT', '2026-10-18 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-18 09:00:00', '2026-10-18 09:02:00', '2026-10-18 09:05:00', '2026-10-18 09:30:00', NULL),
  (14, 4, NULL, 6, NULL, '2026-10-18', 4, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-18 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-18 09:38:00', '2026-10-18 09:40:00', '2026-10-18 09:45:00', '2026-10-18 10:20:00', NULL),
  (5, 5, 5, 1, NULL, '2026-10-19', 5, 'EXAMINATION', 'COMPLETED', '2026-10-19 09:00:00', 'PRESENT', '2026-10-19 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-19 09:00:00', '2026-10-19 09:02:00', '2026-10-19 09:05:00', '2026-10-19 09:30:00', NULL),
  (15, 5, NULL, 7, NULL, '2026-10-19', 5, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-19 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-19 09:38:00', '2026-10-19 09:40:00', '2026-10-19 09:45:00', '2026-10-19 10:20:00', NULL),
  (6, 6, 6, 2, NULL, '2026-10-20', 6, 'EXAMINATION', 'COMPLETED', '2026-10-20 09:00:00', 'PRESENT', '2026-10-20 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-20 09:00:00', '2026-10-20 09:02:00', '2026-10-20 09:05:00', '2026-10-20 09:30:00', NULL),
  (16, 6, NULL, 8, NULL, '2026-10-20', 6, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-20 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-20 09:38:00', '2026-10-20 09:40:00', '2026-10-20 09:45:00', '2026-10-20 10:20:00', NULL),
  (7, 7, 7, 1, NULL, '2026-10-21', 7, 'EXAMINATION', 'COMPLETED', '2026-10-21 09:00:00', 'PRESENT', '2026-10-21 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-21 09:00:00', '2026-10-21 09:02:00', '2026-10-21 09:05:00', '2026-10-21 09:30:00', NULL),
  (17, 7, NULL, 3, NULL, '2026-10-21', 7, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-21 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-21 09:38:00', '2026-10-21 09:40:00', '2026-10-21 09:45:00', '2026-10-21 10:20:00', NULL),
  (8, 8, 8, 2, NULL, '2026-10-22', 8, 'EXAMINATION', 'COMPLETED', '2026-10-22 09:00:00', 'PRESENT', '2026-10-22 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-22 09:00:00', '2026-10-22 09:02:00', '2026-10-22 09:05:00', '2026-10-22 09:30:00', NULL),
  (18, 8, NULL, 4, NULL, '2026-10-22', 8, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-22 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-22 09:38:00', '2026-10-22 09:40:00', '2026-10-22 09:45:00', '2026-10-22 10:20:00', NULL),
  (9, 9, NULL, 1, NULL, '2026-10-22', 9, 'EXAMINATION', 'COMPLETED', NULL, 'PRESENT', '2026-10-22 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-22 09:00:00', '2026-10-22 09:02:00', '2026-10-22 09:05:00', '2026-10-22 09:30:00', NULL),
  (19, 9, NULL, 5, NULL, '2026-10-22', 9, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-22 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-22 09:38:00', '2026-10-22 09:40:00', '2026-10-22 09:45:00', '2026-10-22 10:20:00', NULL),
  (10, 10, NULL, 2, NULL, '2026-10-23', 10, 'EXAMINATION', 'COMPLETED', NULL, 'PRESENT', '2026-10-23 08:55:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-23 09:00:00', '2026-10-23 09:02:00', '2026-10-23 09:05:00', '2026-10-23 09:30:00', NULL),
  (20, 10, NULL, 6, NULL, '2026-10-23', 10, 'DIAGNOSTIC', 'COMPLETED', NULL, 'PRESENT', '2026-10-23 09:35:00', 'ON_TIME_OR_GRACE', 'NORMAL', 2, '2026-10-23 09:38:00', '2026-10-23 09:40:00', '2026-10-23 09:45:00', '2026-10-23 10:20:00', NULL),
  (21, NULL, 9, 1, NULL, '2026-10-27', 1, 'EXAMINATION', 'RESERVED', '2026-10-27 09:00:00', 'NOT_ARRIVED', NULL, NULL, 'NORMAL', 1, NULL, NULL, NULL, NULL, NULL),
  (22, NULL, 10, 2, NULL, '2026-10-28', 2, 'EXAMINATION', 'RESERVED', '2026-10-28 09:00:00', 'NOT_ARRIVED', NULL, NULL, 'NORMAL', 1, NULL, NULL, NULL, NULL, NULL);

-- queue_fairness_states: 10 row(s)
INSERT INTO "queue_fairness_states" ("service_point_id", "queue_date", "consecutive_priority_started", "last_started_queue_entry_id", "policy_version") VALUES
  (1, '2026-10-26', 0, NULL, 1),
  (2, '2026-10-26', 0, NULL, 1),
  (3, '2026-10-26', 0, NULL, 1),
  (4, '2026-10-26', 0, NULL, 1),
  (5, '2026-10-26', 0, NULL, 1),
  (6, '2026-10-26', 0, NULL, 1),
  (7, '2026-10-26', 0, NULL, 1),
  (8, '2026-10-26', 0, NULL, 1),
  (9, '2026-10-26', 0, NULL, 1),
  (10, '2026-10-26', 0, NULL, 1);

-- icd10_codes: 10 row(s)
INSERT INTO "icd10_codes" ("id", "parent_id", "code", "name_vi", "catalog_version", "node_type", "is_selectable", "not_primary_dx") VALUES
  (1, NULL, 'K29.5', 'Viêm dạ dày mạn tính', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (2, NULL, 'I10', 'Tăng huyết áp vô căn (nguyên phát)', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (3, NULL, 'K21.9', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (4, NULL, 'R07.4', 'Đau ngực không xác định', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (5, NULL, 'R10.13', 'Đau thượng vị', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (6, NULL, 'E11.9', 'Đái tháo đường type 2 không có biến chứng', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (7, NULL, 'J30.4', 'Viêm mũi dị ứng không xác định', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (8, NULL, 'M54.5', 'Đau vùng thắt lưng', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (9, NULL, 'K76.0', 'Gan nhiễm mỡ', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE),
  (10, NULL, 'R00.2', 'Đánh trống ngực', 'DEMO-ICD10', 'DISEASE', TRUE, FALSE);

-- medical_records: 10 row(s)
INSERT INTO "medical_records" ("id", "visit_id", "created_by_doctor_id", "status", "finalized_at") VALUES
  (1, 1, 1, 'FINALIZED', '2026-10-15 11:25:00'),
  (2, 2, 2, 'FINALIZED', '2026-10-16 11:25:00'),
  (3, 3, 1, 'FINALIZED', '2026-10-17 11:25:00'),
  (4, 4, 2, 'FINALIZED', '2026-10-18 11:25:00'),
  (5, 5, 1, 'FINALIZED', '2026-10-19 11:25:00'),
  (6, 6, 2, 'FINALIZED', '2026-10-20 11:25:00'),
  (7, 7, 1, 'FINALIZED', '2026-10-21 11:25:00'),
  (8, 8, 2, 'FINALIZED', '2026-10-22 11:25:00'),
  (9, 9, 1, 'FINALIZED', '2026-10-22 11:25:00'),
  (10, 10, 2, 'FINALIZED', '2026-10-23 11:25:00');

-- medical_record_revisions: 10 row(s)
INSERT INTO "medical_record_revisions" ("id", "medical_record_id", "revision_no", "presenting_complaint", "symptom_history", "relevant_history_allergies", "vital_signs", "clinical_findings", "clinical_conclusion", "edited_by_doctor_id", "recorded_at", "is_confirmed", "amendment_reason") VALUES
  (1, 1, 1, 'Đau thượng vị', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":119,"bp_diastolic_mmhg":76,"pulse_bpm":73,"temperature_c":36.6,"spo2_percent":98,"height_cm":161,"weight_kg":56}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Viêm dạ dày mạn tính - kết quả minh họa.', 1, '2026-10-15 11:20:00', TRUE, NULL),
  (2, 2, 1, 'Đau ngực không điển hình', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":120,"bp_diastolic_mmhg":77,"pulse_bpm":74,"temperature_c":36.7,"spo2_percent":99,"height_cm":162,"weight_kg":58}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Tăng huyết áp vô căn (nguyên phát) - kết quả minh họa.', 2, '2026-10-16 11:20:00', TRUE, NULL),
  (3, 3, 1, 'Ợ nóng sau ăn', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Dị ứng penicillin (giả lập, cần xác minh)', '{"bp_systolic_mmhg":121,"bp_diastolic_mmhg":78,"pulse_bpm":75,"temperature_c":36.5,"spo2_percent":97,"height_cm":163,"weight_kg":60}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản - kết quả minh họa.', 1, '2026-10-17 11:20:00', TRUE, NULL),
  (4, 4, 1, 'Theo dõi huyết áp', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":122,"bp_diastolic_mmhg":79,"pulse_bpm":76,"temperature_c":36.6,"spo2_percent":98,"height_cm":164,"weight_kg":62}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Đau ngực không xác định - kết quả minh họa.', 2, '2026-10-18 11:20:00', TRUE, NULL),
  (5, 5, 1, 'Đầy bụng', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":123,"bp_diastolic_mmhg":80,"pulse_bpm":77,"temperature_c":36.7,"spo2_percent":99,"height_cm":165,"weight_kg":64}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Đau thượng vị - kết quả minh họa.', 1, '2026-10-19 11:20:00', TRUE, NULL),
  (6, 6, 1, 'Đánh giá tim mạch định kỳ', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":124,"bp_diastolic_mmhg":75,"pulse_bpm":78,"temperature_c":36.5,"spo2_percent":97,"height_cm":166,"weight_kg":66}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Tăng huyết áp vô căn (nguyên phát) - kết quả minh họa.', 2, '2026-10-20 11:20:00', TRUE, NULL),
  (7, 7, 1, 'Đau vùng bụng trên', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":125,"bp_diastolic_mmhg":76,"pulse_bpm":79,"temperature_c":36.6,"spo2_percent":98,"height_cm":167,"weight_kg":68}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Viêm dạ dày mạn tính - kết quả minh họa.', 1, '2026-10-21 11:20:00', TRUE, NULL),
  (8, 8, 1, 'Theo dõi tăng huyết áp', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":126,"bp_diastolic_mmhg":77,"pulse_bpm":80,"temperature_c":36.7,"spo2_percent":99,"height_cm":168,"weight_kg":70}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Đau ngực không xác định - kết quả minh họa.', 2, '2026-10-22 11:20:00', TRUE, NULL),
  (9, 9, 1, 'Đau dạ dày từng đợt', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":127,"bp_diastolic_mmhg":78,"pulse_bpm":81,"temperature_c":36.5,"spo2_percent":97,"height_cm":169,"weight_kg":72}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản - kết quả minh họa.', 1, '2026-10-22 11:20:00', TRUE, NULL),
  (10, 10, 1, 'Khám sức khỏe tim mạch', 'Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).', 'Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).', '{"bp_systolic_mmhg":128,"bp_diastolic_mmhg":79,"pulse_bpm":82,"temperature_c":36.6,"spo2_percent":98,"height_cm":170,"weight_kg":74}'::jsonb, 'Khám lâm sàng ổn định (nội dung mô phỏng).', 'Tăng huyết áp vô căn (nguyên phát) - kết quả minh họa.', 2, '2026-10-23 11:20:00', TRUE, NULL);

-- visit_diagnoses: 10 row(s)
INSERT INTO "visit_diagnoses" ("id", "visit_id", "icd10_code_id", "is_primary", "code_snapshot", "name_snapshot", "catalog_version", "clinical_diagnosis", "status", "confirmed_at", "recorded_by_doctor_id", "recorded_at") VALUES
  (1, 1, 1, TRUE, 'K29.5', 'Viêm dạ dày mạn tính', 'DEMO-ICD10', 'Viêm dạ dày mạn tính (demo)', 'CONFIRMED', '2026-10-15 11:20:00', 1, '2026-10-15 11:15:00'),
  (2, 2, 2, TRUE, 'I10', 'Tăng huyết áp vô căn (nguyên phát)', 'DEMO-ICD10', 'Tăng huyết áp vô căn (nguyên phát) (demo)', 'CONFIRMED', '2026-10-16 11:20:00', 2, '2026-10-16 11:15:00'),
  (3, 3, 3, TRUE, 'K21.9', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản', 'DEMO-ICD10', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản (demo)', 'CONFIRMED', '2026-10-17 11:20:00', 1, '2026-10-17 11:15:00'),
  (4, 4, 4, TRUE, 'R07.4', 'Đau ngực không xác định', 'DEMO-ICD10', 'Đau ngực không xác định (demo)', 'CONFIRMED', '2026-10-18 11:20:00', 2, '2026-10-18 11:15:00'),
  (5, 5, 5, TRUE, 'R10.13', 'Đau thượng vị', 'DEMO-ICD10', 'Đau thượng vị (demo)', 'CONFIRMED', '2026-10-19 11:20:00', 1, '2026-10-19 11:15:00'),
  (6, 6, 2, TRUE, 'I10', 'Tăng huyết áp vô căn (nguyên phát)', 'DEMO-ICD10', 'Tăng huyết áp vô căn (nguyên phát) (demo)', 'CONFIRMED', '2026-10-20 11:20:00', 2, '2026-10-20 11:15:00'),
  (7, 7, 1, TRUE, 'K29.5', 'Viêm dạ dày mạn tính', 'DEMO-ICD10', 'Viêm dạ dày mạn tính (demo)', 'CONFIRMED', '2026-10-21 11:20:00', 1, '2026-10-21 11:15:00'),
  (8, 8, 4, TRUE, 'R07.4', 'Đau ngực không xác định', 'DEMO-ICD10', 'Đau ngực không xác định (demo)', 'CONFIRMED', '2026-10-22 11:20:00', 2, '2026-10-22 11:15:00'),
  (9, 9, 3, TRUE, 'K21.9', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản', 'DEMO-ICD10', 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản (demo)', 'CONFIRMED', '2026-10-22 11:20:00', 1, '2026-10-22 11:15:00'),
  (10, 10, 2, TRUE, 'I10', 'Tăng huyết áp vô căn (nguyên phát)', 'DEMO-ICD10', 'Tăng huyết áp vô căn (nguyên phát) (demo)', 'CONFIRMED', '2026-10-23 11:20:00', 2, '2026-10-23 11:15:00');

-- clinical_addenda: 2 row(s)
INSERT INTO "clinical_addenda" ("id", "visit_id", "record_type", "target_record_id", "corrected_content", "reason", "author_doctor_id", "recorded_at") VALUES
  (3, 3, 'MEDICAL_RECORD', 3, '{"note":"Đính chính lỗi chính tả trong ghi chú demo.","original_record_preserved":true}'::jsonb, 'Đính chính mô tả không làm thay đổi kết luận.', 1, '2026-10-17 14:00:00'),
  (7, 7, 'MEDICAL_RECORD', 7, '{"note":"Đính chính lỗi chính tả trong ghi chú demo.","original_record_preserved":true}'::jsonb, 'Đính chính mô tả không làm thay đổi kết luận.', 1, '2026-10-21 14:00:00');

-- diagnostic_orders: 10 row(s)
INSERT INTO "diagnostic_orders" ("id", "order_code", "visit_id", "ordering_doctor_id", "service_id", "queue_entry_id", "instruction", "required_for_conclusion", "priority", "status", "confirmed_by_staff_id", "confirmed_at", "ordered_at") VALUES
  (1, 'DIA-202610-0001', 1, 1, 5, 11, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 5, '2026-10-15 09:35:00', '2026-10-15 09:30:00'),
  (2, 'DIA-202610-0002', 2, 2, 6, 12, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 6, '2026-10-16 09:35:00', '2026-10-16 09:30:00'),
  (3, 'DIA-202610-0003', 3, 1, 7, 13, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 5, '2026-10-17 09:35:00', '2026-10-17 09:30:00'),
  (4, 'DIA-202610-0004', 4, 2, 8, 14, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 6, '2026-10-18 09:35:00', '2026-10-18 09:30:00'),
  (5, 'DIA-202610-0005', 5, 1, 9, 15, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 5, '2026-10-19 09:35:00', '2026-10-19 09:30:00'),
  (6, 'DIA-202610-0006', 6, 2, 10, 16, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 6, '2026-10-20 09:35:00', '2026-10-20 09:30:00'),
  (7, 'DIA-202610-0007', 7, 1, 5, 17, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 5, '2026-10-21 09:35:00', '2026-10-21 09:30:00'),
  (8, 'DIA-202610-0008', 8, 2, 6, 18, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 6, '2026-10-22 09:35:00', '2026-10-22 09:30:00'),
  (9, 'DIA-202610-0009', 9, 1, 7, 19, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 5, '2026-10-22 09:35:00', '2026-10-22 09:30:00'),
  (10, 'DIA-202610-0010', 10, 2, 8, 20, 'Thực hiện theo quy trình an toàn của cơ sở (demo).', TRUE, 'NORMAL', 'COMPLETED', 6, '2026-10-23 09:35:00', '2026-10-23 09:30:00');

-- diagnostic_results: 10 row(s)
INSERT INTO "diagnostic_results" ("id", "diagnostic_order_id", "status", "finalized_at", "finalized_by_staff_id") VALUES
  (1, 1, 'FINALIZED', '2026-10-15 10:35:00', 6),
  (2, 2, 'FINALIZED', '2026-10-16 10:35:00', 5),
  (3, 3, 'FINALIZED', '2026-10-17 10:35:00', 6),
  (4, 4, 'FINALIZED', '2026-10-18 10:35:00', 5),
  (5, 5, 'FINALIZED', '2026-10-19 10:35:00', 6),
  (6, 6, 'FINALIZED', '2026-10-20 10:35:00', 5),
  (7, 7, 'FINALIZED', '2026-10-21 10:35:00', 6),
  (8, 8, 'FINALIZED', '2026-10-22 10:35:00', 5),
  (9, 9, 'FINALIZED', '2026-10-22 10:35:00', 6),
  (10, 10, 'FINALIZED', '2026-10-23 10:35:00', 5);

-- diagnostic_result_versions: 10 row(s)
INSERT INTO "diagnostic_result_versions" ("id", "diagnostic_result_id", "version_no", "result_text", "conclusion", "report_file_url", "entered_by_staff_id", "entered_at", "is_finalized", "verified_by_staff_id", "verified_at", "correction_reason") VALUES
  (1, 1, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 5, '2026-10-15 10:25:00', TRUE, 6, '2026-10-15 10:35:00', NULL),
  (2, 2, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 6, '2026-10-16 10:25:00', TRUE, 5, '2026-10-16 10:35:00', NULL),
  (3, 3, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 5, '2026-10-17 10:25:00', TRUE, 6, '2026-10-17 10:35:00', NULL),
  (4, 4, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 6, '2026-10-18 10:25:00', TRUE, 5, '2026-10-18 10:35:00', NULL),
  (5, 5, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 5, '2026-10-19 10:25:00', TRUE, 6, '2026-10-19 10:35:00', NULL),
  (6, 6, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 6, '2026-10-20 10:25:00', TRUE, 5, '2026-10-20 10:35:00', NULL),
  (7, 7, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 5, '2026-10-21 10:25:00', TRUE, 6, '2026-10-21 10:35:00', NULL),
  (8, 8, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 6, '2026-10-22 10:25:00', TRUE, 5, '2026-10-22 10:35:00', NULL),
  (9, 9, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 5, '2026-10-22 10:25:00', TRUE, 6, '2026-10-22 10:35:00', NULL),
  (10, 10, 1, 'Kết quả trong khoảng theo dõi (giả lập, không dùng chẩn đoán).', 'Đã hoàn tất đo/khảo sát (dữ liệu demo).', NULL, 6, '2026-10-23 10:25:00', TRUE, 5, '2026-10-23 10:35:00', NULL);

-- medicines: 10 row(s)
INSERT INTO "medicines" ("id", "hospital_id", "medicine_code", "name", "active_ingredient", "strength", "dosage_form", "dispensing_unit", "content_quantity", "content_unit", "is_active") VALUES
  (1, 1, 'MED-001', 'Omeprazole', 'Omeprazole', '20 mg', 'Viên nang', 'Viên', 1, 'viên', TRUE),
  (2, 1, 'MED-002', 'Simethicone', 'Simethicone', '80 mg', 'Viên nhai', 'Viên', 1, 'viên', TRUE),
  (3, 1, 'MED-003', 'Sucralfate', 'Sucralfate', '1 g', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (4, 1, 'MED-004', 'Famotidine', 'Famotidine', '20 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (5, 1, 'MED-005', 'Alginate', 'Sodium alginate', '500 mg', 'Gói uống', 'Gói', 1, 'gói', TRUE),
  (6, 1, 'MED-006', 'Amlodipine', 'Amlodipine', '5 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (7, 1, 'MED-007', 'Losartan', 'Losartan', '50 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (8, 1, 'MED-008', 'Bisoprolol', 'Bisoprolol', '2.5 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (9, 1, 'MED-009', 'Atorvastatin', 'Atorvastatin', '10 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE),
  (10, 1, 'MED-010', 'Hydrochlorothiazide', 'Hydrochlorothiazide', '12.5 mg', 'Viên nén', 'Viên', 1, 'viên', TRUE);

-- prescriptions: 10 row(s)
INSERT INTO "prescriptions" ("id", "prescription_code", "visit_id", "prescribing_doctor_id", "fulfillment_choice", "status", "issued_at", "created_at", "replaces_prescription_id") VALUES
  (1, 'RX-202610-0001', 1, 1, 'HOSPITAL', 'DISPENSED', '2026-10-15 11:15:00', '2026-10-15 11:05:00', NULL),
  (2, 'RX-202610-0002', 2, 2, 'HOSPITAL', 'DISPENSED', '2026-10-16 11:15:00', '2026-10-16 11:05:00', NULL),
  (3, 'RX-202610-0003', 3, 1, 'HOSPITAL', 'DISPENSED', '2026-10-17 11:15:00', '2026-10-17 11:05:00', NULL),
  (4, 'RX-202610-0004', 4, 2, 'HOSPITAL', 'DISPENSED', '2026-10-18 11:15:00', '2026-10-18 11:05:00', NULL),
  (5, 'RX-202610-0005', 5, 1, 'HOSPITAL', 'DISPENSED', '2026-10-19 11:15:00', '2026-10-19 11:05:00', NULL),
  (6, 'RX-202610-0006', 6, 2, 'HOSPITAL', 'DISPENSED', '2026-10-20 11:15:00', '2026-10-20 11:05:00', NULL),
  (7, 'RX-202610-0007', 7, 1, 'HOSPITAL', 'DISPENSED', '2026-10-21 11:15:00', '2026-10-21 11:05:00', NULL),
  (8, 'RX-202610-0008', 8, 2, 'HOSPITAL', 'DISPENSED', '2026-10-22 11:15:00', '2026-10-22 11:05:00', NULL),
  (9, 'RX-202610-0009', 9, 1, 'HOSPITAL', 'DISPENSED', '2026-10-22 11:15:00', '2026-10-22 11:05:00', NULL),
  (10, 'RX-202610-0010', 10, 2, 'HOSPITAL', 'DISPENSED', '2026-10-23 11:15:00', '2026-10-23 11:05:00', NULL);

-- prescription_items: 10 row(s)
INSERT INTO "prescription_items" ("id", "prescription_id", "medicine_id", "prescribed_quantity", "single_dose_quantity", "single_dose_unit", "dose_schedule_text", "frequency", "duration_days", "administration_route", "instructions") VALUES
  (1, 1, 1, 14, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 14, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (2, 2, 6, 30, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 30, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (3, 3, 2, 14, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 14, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (4, 4, 7, 30, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 30, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (5, 5, 3, 14, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 14, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (6, 6, 8, 30, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 30, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (7, 7, 4, 14, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 14, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (8, 8, 9, 30, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 30, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (9, 9, 5, 14, 1, 'Gói', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 14, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.'),
  (10, 10, 10, 30, 1, 'Viên', '1 đơn vị mỗi ngày (mô phỏng; không phải hướng dẫn điều trị).', '1 lần/ngày', 30, 'ORAL', 'Chỉ dành cho demo, KHÔNG sử dụng làm y lệnh thật.');

-- medicine_batches: 10 row(s)
INSERT INTO "medicine_batches" ("id", "medicine_id", "batch_number", "received_at", "expiry_date", "quantity_on_hand", "is_blocked") VALUES
  (1, 1, 'LOT-DEMO-001', '2026-10-01 08:00:00', '2028-12-31', 186, FALSE),
  (2, 2, 'LOT-DEMO-002', '2026-10-01 08:00:00', '2028-12-31', 186, FALSE),
  (3, 3, 'LOT-DEMO-003', '2026-10-01 08:00:00', '2028-12-31', 186, FALSE),
  (4, 4, 'LOT-DEMO-004', '2026-10-01 08:00:00', '2028-12-31', 186, FALSE),
  (5, 5, 'LOT-DEMO-005', '2026-10-01 08:00:00', '2028-12-31', 186, FALSE),
  (6, 6, 'LOT-DEMO-006', '2026-10-01 08:00:00', '2028-12-31', 170, FALSE),
  (7, 7, 'LOT-DEMO-007', '2026-10-01 08:00:00', '2028-12-31', 170, FALSE),
  (8, 8, 'LOT-DEMO-008', '2026-10-01 08:00:00', '2028-12-31', 170, FALSE),
  (9, 9, 'LOT-DEMO-009', '2026-10-01 08:00:00', '2028-12-31', 170, FALSE),
  (10, 10, 'LOT-DEMO-010', '2026-10-01 08:00:00', '2028-12-31', 170, FALSE);

-- medication_dispenses: 10 row(s)
INSERT INTO "medication_dispenses" ("id", "prescription_id", "prepared_by_staff_id", "dispensed_by_staff_id", "status", "prepared_at", "dispensed_at", "idempotency_key") VALUES
  (1, 1, 7, 8, 'COMPLETED', '2026-10-15 11:35:00', '2026-10-15 11:50:00', 'dispense-seed-0001'),
  (2, 2, 7, 8, 'COMPLETED', '2026-10-16 11:35:00', '2026-10-16 11:50:00', 'dispense-seed-0002'),
  (3, 3, 7, 8, 'COMPLETED', '2026-10-17 11:35:00', '2026-10-17 11:50:00', 'dispense-seed-0003'),
  (4, 4, 7, 8, 'COMPLETED', '2026-10-18 11:35:00', '2026-10-18 11:50:00', 'dispense-seed-0004'),
  (5, 5, 7, 8, 'COMPLETED', '2026-10-19 11:35:00', '2026-10-19 11:50:00', 'dispense-seed-0005'),
  (6, 6, 7, 8, 'COMPLETED', '2026-10-20 11:35:00', '2026-10-20 11:50:00', 'dispense-seed-0006'),
  (7, 7, 7, 8, 'COMPLETED', '2026-10-21 11:35:00', '2026-10-21 11:50:00', 'dispense-seed-0007'),
  (8, 8, 7, 8, 'COMPLETED', '2026-10-22 11:35:00', '2026-10-22 11:50:00', 'dispense-seed-0008'),
  (9, 9, 7, 8, 'COMPLETED', '2026-10-22 11:35:00', '2026-10-22 11:50:00', 'dispense-seed-0009'),
  (10, 10, 7, 8, 'COMPLETED', '2026-10-23 11:35:00', '2026-10-23 11:50:00', 'dispense-seed-0010');

-- dispense_items: 10 row(s)
INSERT INTO "dispense_items" ("id", "dispense_id", "prescription_item_id", "medicine_batch_id", "planned_quantity", "dispensed_quantity", "shortage_reason") VALUES
  (1, 1, 1, 1, 14, 14, NULL),
  (2, 2, 2, 6, 30, 30, NULL),
  (3, 3, 3, 2, 14, 14, NULL),
  (4, 4, 4, 7, 30, 30, NULL),
  (5, 5, 5, 3, 14, 14, NULL),
  (6, 6, 6, 8, 30, 30, NULL),
  (7, 7, 7, 4, 14, 14, NULL),
  (8, 8, 8, 9, 30, 30, NULL),
  (9, 9, 9, 5, 14, 14, NULL),
  (10, 10, 10, 10, 30, 30, NULL);

-- stock_transactions: 10 row(s)
INSERT INTO "stock_transactions" ("id", "medicine_batch_id", "dispense_item_id", "transaction_type", "quantity_delta", "performed_by_staff_id", "reason", "source_reference", "occurred_at") VALUES
  (1, 1, 1, 'DISPENSE', -14, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0001', '2026-10-15 11:50:00'),
  (2, 6, 2, 'DISPENSE', -30, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0002', '2026-10-16 11:50:00'),
  (3, 2, 3, 'DISPENSE', -14, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0003', '2026-10-17 11:50:00'),
  (4, 7, 4, 'DISPENSE', -30, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0004', '2026-10-18 11:50:00'),
  (5, 3, 5, 'DISPENSE', -14, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0005', '2026-10-19 11:50:00'),
  (6, 8, 6, 'DISPENSE', -30, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0006', '2026-10-20 11:50:00'),
  (7, 4, 7, 'DISPENSE', -14, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0007', '2026-10-21 11:50:00'),
  (8, 9, 8, 'DISPENSE', -30, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0008', '2026-10-22 11:50:00'),
  (9, 5, 9, 'DISPENSE', -14, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0009', '2026-10-22 11:50:00'),
  (10, 10, 10, 'DISPENSE', -30, 8, 'Xuất thuốc theo đơn demo', 'RX-202610-0010', '2026-10-23 11:50:00');

-- visit_charges: 20 row(s)
INSERT INTO "visit_charges" ("id", "visit_id", "service_id", "diagnostic_order_id", "unit_price", "payment_requirement") VALUES
  (1, 1, 1, NULL, 200000, 'REQUIRED'),
  (2, 1, 5, 1, 90000, 'REQUIRED'),
  (3, 2, 2, NULL, 220000, 'REQUIRED'),
  (4, 2, 6, 2, 180000, 'REQUIRED'),
  (5, 3, 1, NULL, 200000, 'REQUIRED'),
  (6, 3, 7, 3, 120000, 'REQUIRED'),
  (7, 4, 2, NULL, 220000, 'REQUIRED'),
  (8, 4, 8, 4, 250000, 'REQUIRED'),
  (9, 5, 1, NULL, 200000, 'REQUIRED'),
  (10, 5, 9, 5, 70000, 'REQUIRED'),
  (11, 6, 2, NULL, 220000, 'REQUIRED'),
  (12, 6, 10, 6, 160000, 'REQUIRED'),
  (13, 7, 1, NULL, 200000, 'REQUIRED'),
  (14, 7, 5, 7, 90000, 'REQUIRED'),
  (15, 8, 2, NULL, 220000, 'REQUIRED'),
  (16, 8, 6, 8, 180000, 'REQUIRED'),
  (17, 9, 1, NULL, 200000, 'REQUIRED'),
  (18, 9, 7, 9, 120000, 'REQUIRED'),
  (19, 10, 2, NULL, 220000, 'REQUIRED'),
  (20, 10, 8, 10, 250000, 'REQUIRED');

-- payments: 10 row(s)
INSERT INTO "payments" ("id", "payment_code", "visit_id", "amount", "external_reference", "status", "paid_at", "created_at") VALUES
  (1, 'PAY-202610-0001', 1, 290000, 'DEMO-SEPAY-0001', 'PAID', '2026-10-15 10:45:00', '2026-10-15 10:25:00'),
  (2, 'PAY-202610-0002', 2, 400000, 'DEMO-SEPAY-0002', 'PAID', '2026-10-16 10:45:00', '2026-10-16 10:25:00'),
  (3, 'PAY-202610-0003', 3, 320000, 'DEMO-SEPAY-0003', 'PAID', '2026-10-17 10:45:00', '2026-10-17 10:25:00'),
  (4, 'PAY-202610-0004', 4, 470000, 'DEMO-SEPAY-0004', 'PAID', '2026-10-18 10:45:00', '2026-10-18 10:25:00'),
  (5, 'PAY-202610-0005', 5, 270000, 'DEMO-SEPAY-0005', 'PAID', '2026-10-19 10:45:00', '2026-10-19 10:25:00'),
  (6, 'PAY-202610-0006', 6, 380000, 'DEMO-SEPAY-0006', 'PAID', '2026-10-20 10:45:00', '2026-10-20 10:25:00'),
  (7, 'PAY-202610-0007', 7, 290000, 'DEMO-SEPAY-0007', 'PAID', '2026-10-21 10:45:00', '2026-10-21 10:25:00'),
  (8, 'PAY-202610-0008', 8, 400000, 'DEMO-SEPAY-0008', 'PAID', '2026-10-22 10:45:00', '2026-10-22 10:25:00'),
  (9, 'PAY-202610-0009', 9, 320000, 'DEMO-SEPAY-0009', 'PAID', '2026-10-22 10:45:00', '2026-10-22 10:25:00'),
  (10, 'PAY-202610-0010', 10, 470000, 'DEMO-SEPAY-0010', 'PAID', '2026-10-23 10:45:00', '2026-10-23 10:25:00');

-- notifications: 10 row(s)
INSERT INTO "notifications" ("id", "patient_id", "appointment_id", "visit_id", "channel", "recipient", "subject", "body", "status", "retry_count", "idempotency_key", "scheduled_at", "sent_at", "last_error", "created_at") VALUES
  (1, 1, 1, 1, 'EMAIL', 'patient01@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0001', '2026-10-15 07:30:00', '2026-10-15 07:30:30', NULL, '2026-10-12 10:05:00'),
  (2, 2, 2, 2, 'EMAIL', 'patient02@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0002', '2026-10-16 07:30:00', '2026-10-16 07:30:30', NULL, '2026-10-12 10:05:00'),
  (3, 3, 3, 3, 'EMAIL', 'patient03@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0003', '2026-10-17 07:30:00', '2026-10-17 07:30:30', NULL, '2026-10-12 10:05:00'),
  (4, 4, 4, 4, 'EMAIL', 'patient04@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0004', '2026-10-18 07:30:00', '2026-10-18 07:30:30', NULL, '2026-10-12 10:05:00'),
  (5, 5, 5, 5, 'EMAIL', 'patient05@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0005', '2026-10-19 07:30:00', '2026-10-19 07:30:30', NULL, '2026-10-12 10:05:00'),
  (6, 6, 6, 6, 'EMAIL', 'patient06@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0006', '2026-10-20 07:30:00', '2026-10-20 07:30:30', NULL, '2026-10-12 10:05:00'),
  (7, 7, 7, 7, 'EMAIL', 'patient07@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0007', '2026-10-21 07:30:00', '2026-10-21 07:30:30', NULL, '2026-10-12 10:05:00'),
  (8, 8, 8, 8, 'EMAIL', 'patient08@medflow.test', 'Xác nhận lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'SENT', 0, 'notify-seed-0008', '2026-10-22 07:30:00', '2026-10-22 07:30:30', NULL, '2026-10-12 10:05:00'),
  (9, 9, 9, NULL, 'EMAIL', 'patient09@medflow.test', 'Nhắc lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'PENDING', 0, 'notify-seed-0009', '2026-10-27 07:30:00', NULL, NULL, '2026-10-12 10:05:00'),
  (10, 10, 10, NULL, 'EMAIL', 'patient10@medflow.test', 'Nhắc lịch khám (demo)', 'Nội dung thông báo thử nghiệm cho dữ liệu MedFlow AI.', 'PENDING', 0, 'notify-seed-0010', '2026-10-28 07:30:00', NULL, NULL, '2026-10-12 10:05:00');

-- hospital_policies: 10 row(s)
INSERT INTO "hospital_policies" ("id", "hospital_id", "queue_service_point_id", "policy_type", "policy_key", "policy_value", "policy_version", "effective_from", "effective_to") VALUES
  (1, 1, NULL, 'APPOINTMENT', 'reschedule_hours', '{"minimum_hours":12}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (2, 1, NULL, 'APPOINTMENT', 'cancel_hours', '{"minimum_hours":6}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (3, 1, NULL, 'QUEUE', 'walkin_priority_ratio', '{"priority":3,"normal":1}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (4, 1, NULL, 'QUEUE', 'grace_minutes', '{"minutes":15}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (5, 1, NULL, 'QUEUE', 'public_number_immutable', '{"enabled":true}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (6, 1, NULL, 'QUEUE', 'manual_priority_requires_verification', '{"enabled":true}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (7, 1, NULL, 'DIAGNOSTIC', 'final_result_required', '{"enabled":true}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (8, 1, NULL, 'APPOINTMENT', 'max_active_bookings', '{"count":3}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (9, 1, NULL, 'NOTIFICATION', 'reminder_hours', '{"hours_before":24}'::jsonb, 1, '2026-10-01 00:00:00', NULL),
  (10, 1, NULL, 'PHARMACY', 'fefo', '{"enabled":true}'::jsonb, 1, '2026-10-01 00:00:00', NULL);

-- system_integrations: 6 row(s)
INSERT INTO "system_integrations" ("id", "hospital_id", "integration_code", "integration_type", "base_url", "public_config", "is_enabled") VALUES
  (1, 1, 'SEPAY', 'PAYMENT', 'https://example.invalid/medflow/sepay', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE),
  (2, 1, 'VNPT_EKYC', 'IDENTITY', 'https://example.invalid/medflow/vnpt_ekyc', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE),
  (3, 1, 'EMAIL_MOCK', 'NOTIFICATION', 'https://example.invalid/medflow/email_mock', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE),
  (4, 1, 'SMS_MOCK', 'NOTIFICATION', 'https://example.invalid/medflow/sms_mock', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE),
  (5, 1, 'LIS_MOCK', 'DIAGNOSTIC', 'https://example.invalid/medflow/lis_mock', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE),
  (6, 1, 'RIS_MOCK', 'DIAGNOSTIC', 'https://example.invalid/medflow/ris_mock', '{"mode":"MOCK","sandbox":true}'::jsonb, FALSE);

-- audit_logs: 10 row(s)
INSERT INTO "audit_logs" ("id", "actor_user_id", "hospital_id", "action", "entity_type", "entity_id", "reason", "change_data", "occurred_at") VALUES
  (1, 3, 1, 'VIEW', 'VISIT', '1', 'Seed data demo', '{"demo":true,"visit_id":1}'::jsonb, '2026-10-25 14:00:00'),
  (2, 5, 1, 'UPDATE', 'VISIT', '2', 'Seed data demo', '{"demo":true,"visit_id":2}'::jsonb, '2026-10-25 14:00:00'),
  (3, 3, 1, 'VIEW', 'VISIT', '3', 'Seed data demo', '{"demo":true,"visit_id":3}'::jsonb, '2026-10-25 14:00:00'),
  (4, 5, 1, 'UPDATE', 'VISIT', '4', 'Seed data demo', '{"demo":true,"visit_id":4}'::jsonb, '2026-10-25 14:00:00'),
  (5, 3, 1, 'VIEW', 'VISIT', '5', 'Seed data demo', '{"demo":true,"visit_id":5}'::jsonb, '2026-10-25 14:00:00'),
  (6, 5, 1, 'UPDATE', 'VISIT', '6', 'Seed data demo', '{"demo":true,"visit_id":6}'::jsonb, '2026-10-25 14:00:00'),
  (7, 3, 1, 'VIEW', 'VISIT', '7', 'Seed data demo', '{"demo":true,"visit_id":7}'::jsonb, '2026-10-25 14:00:00'),
  (8, 5, 1, 'UPDATE', 'VISIT', '8', 'Seed data demo', '{"demo":true,"visit_id":8}'::jsonb, '2026-10-25 14:00:00'),
  (9, 3, 1, 'VIEW', 'VISIT', '9', 'Seed data demo', '{"demo":true,"visit_id":9}'::jsonb, '2026-10-25 14:00:00'),
  (10, 5, 1, 'UPDATE', 'VISIT', '10', 'Seed data demo', '{"demo":true,"visit_id":10}'::jsonb, '2026-10-25 14:00:00');

SELECT setval(pg_get_serial_sequence('"roles"', 'id'), (SELECT MAX("id") FROM "roles"), true);
SELECT setval(pg_get_serial_sequence('"permissions"', 'id'), (SELECT MAX("id") FROM "permissions"), true);
SELECT setval(pg_get_serial_sequence('"users"', 'id'), (SELECT MAX("id") FROM "users"), true);
SELECT setval(pg_get_serial_sequence('"auth_sessions"', 'id'), (SELECT MAX("id") FROM "auth_sessions"), true);
SELECT setval(pg_get_serial_sequence('"hospitals"', 'id'), (SELECT MAX("id") FROM "hospitals"), true);
SELECT setval(pg_get_serial_sequence('"departments"', 'id'), (SELECT MAX("id") FROM "departments"), true);
SELECT setval(pg_get_serial_sequence('"specialties"', 'id'), (SELECT MAX("id") FROM "specialties"), true);
SELECT setval(pg_get_serial_sequence('"hospital_rooms"', 'id'), (SELECT MAX("id") FROM "hospital_rooms"), true);
SELECT setval(pg_get_serial_sequence('"staff"', 'id'), (SELECT MAX("id") FROM "staff"), true);
SELECT setval(pg_get_serial_sequence('"doctors"', 'id'), (SELECT MAX("id") FROM "doctors"), true);
SELECT setval(pg_get_serial_sequence('"patients"', 'id'), (SELECT MAX("id") FROM "patients"), true);
SELECT setval(pg_get_serial_sequence('"services"', 'id'), (SELECT MAX("id") FROM "services"), true);
SELECT setval(pg_get_serial_sequence('"service_prices"', 'id'), (SELECT MAX("id") FROM "service_prices"), true);
SELECT setval(pg_get_serial_sequence('"examination_slots"', 'id'), (SELECT MAX("id") FROM "examination_slots"), true);
SELECT setval(pg_get_serial_sequence('"appointments"', 'id'), (SELECT MAX("id") FROM "appointments"), true);
SELECT setval(pg_get_serial_sequence('"visits"', 'id'), (SELECT MAX("id") FROM "visits"), true);
SELECT setval(pg_get_serial_sequence('"queue_service_points"', 'id'), (SELECT MAX("id") FROM "queue_service_points"), true);
SELECT setval(pg_get_serial_sequence('"queue_entries"', 'id'), (SELECT MAX("id") FROM "queue_entries"), true);
SELECT setval(pg_get_serial_sequence('"icd10_codes"', 'id'), (SELECT MAX("id") FROM "icd10_codes"), true);
SELECT setval(pg_get_serial_sequence('"medical_records"', 'id'), (SELECT MAX("id") FROM "medical_records"), true);
SELECT setval(pg_get_serial_sequence('"medical_record_revisions"', 'id'), (SELECT MAX("id") FROM "medical_record_revisions"), true);
SELECT setval(pg_get_serial_sequence('"visit_diagnoses"', 'id'), (SELECT MAX("id") FROM "visit_diagnoses"), true);
SELECT setval(pg_get_serial_sequence('"clinical_addenda"', 'id'), (SELECT MAX("id") FROM "clinical_addenda"), true);
SELECT setval(pg_get_serial_sequence('"diagnostic_orders"', 'id'), (SELECT MAX("id") FROM "diagnostic_orders"), true);
SELECT setval(pg_get_serial_sequence('"diagnostic_results"', 'id'), (SELECT MAX("id") FROM "diagnostic_results"), true);
SELECT setval(pg_get_serial_sequence('"diagnostic_result_versions"', 'id'), (SELECT MAX("id") FROM "diagnostic_result_versions"), true);
SELECT setval(pg_get_serial_sequence('"medicines"', 'id'), (SELECT MAX("id") FROM "medicines"), true);
SELECT setval(pg_get_serial_sequence('"prescriptions"', 'id'), (SELECT MAX("id") FROM "prescriptions"), true);
SELECT setval(pg_get_serial_sequence('"prescription_items"', 'id'), (SELECT MAX("id") FROM "prescription_items"), true);
SELECT setval(pg_get_serial_sequence('"medicine_batches"', 'id'), (SELECT MAX("id") FROM "medicine_batches"), true);
SELECT setval(pg_get_serial_sequence('"medication_dispenses"', 'id'), (SELECT MAX("id") FROM "medication_dispenses"), true);
SELECT setval(pg_get_serial_sequence('"dispense_items"', 'id'), (SELECT MAX("id") FROM "dispense_items"), true);
SELECT setval(pg_get_serial_sequence('"stock_transactions"', 'id'), (SELECT MAX("id") FROM "stock_transactions"), true);
SELECT setval(pg_get_serial_sequence('"visit_charges"', 'id'), (SELECT MAX("id") FROM "visit_charges"), true);
SELECT setval(pg_get_serial_sequence('"payments"', 'id'), (SELECT MAX("id") FROM "payments"), true);
SELECT setval(pg_get_serial_sequence('"notifications"', 'id'), (SELECT MAX("id") FROM "notifications"), true);
SELECT setval(pg_get_serial_sequence('"hospital_policies"', 'id'), (SELECT MAX("id") FROM "hospital_policies"), true);
SELECT setval(pg_get_serial_sequence('"system_integrations"', 'id'), (SELECT MAX("id") FROM "system_integrations"), true);
SELECT setval(pg_get_serial_sequence('"audit_logs"', 'id'), (SELECT MAX("id") FROM "audit_logs"), true);

COMMIT;

-- POST-IMPORT CHECKS (run separately in pgAdmin Query Tool):
-- SELECT r.code, COUNT(ur.user_id) AS user_count FROM roles r JOIN user_roles ur ON r.id=ur.role_id GROUP BY r.code ORDER BY r.code;
-- SELECT COUNT(*) FROM visits;        -- 10
-- SELECT COUNT(*) FROM queue_entries; -- 22 (including 2 future reservations)
-- SELECT COUNT(*) FROM diagnostic_orders; -- 10
-- SELECT COUNT(*) FROM prescriptions; -- 10
-- SELECT COUNT(*) FROM payments;      -- 10

