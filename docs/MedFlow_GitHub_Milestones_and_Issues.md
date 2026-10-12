# MedFlow AI — GitHub Milestones, Labels & Issues

> **Tài liệu triển khai từ backlog:** 3 milestones, đúng 5 labels, 355 issues, mỗi issue giữ lại UC scope/prerequisite từ workbook.

## 1. Thiết lập GitHub repository

- Chỉ sử dụng các label `type:feature`, `type:bug`, `type:test`, `type:docs`, `type:change-requirement`.
- Milestone biểu diễn **Iteration**, không dùng milestone riêng cho FE/BE/testing.
- Issue phát triển FE và BE tách biệt theo từng function; testing tách theo module, kiểm thử tích hợp và E2E.
- GitHub Projects fields khuyến nghị: `Status` (Backlog / Ready / In Progress / In Review / QA / Done), `Priority` (P0–P3), `Owner`. Không tạo status/priority labels.
- Branch: `feature/<issue-id>-<slug>` hoặc `bugfix/<issue-id>-<slug>`; PR review và CI/test pass trước merge; nhánh `main` ổn định.

## 2. Chính xác 5 labels

| Label | Hex color | Mục đích |
|---|---|---|
| `type:feature` | `#1D76DB` | FE or BE development |
| `type:bug` | `#D73A4A` | Defect fixing |
| `type:test` | `#5319E7` | Testing task |
| `type:docs` | `#0075CA` | Project documentation |
| `type:change-requirement` | `#FBCA04` | Requirement change |

## 3. Ba milestones và Definition of Done

### M1 — Foundation + Appointment Booking

- **Window:** Week 6-7
- **Backlog:** 47 functions
- **Increment demo:** Patient có thể đăng ký/đăng nhập, quản lý profile, tìm Doctor/Service, xem slot, đặt Appointment; Hospital/System Admin có dữ liệu nền và RBAC cơ bản để vận hành.
- **Scope:** Authentication; Profile core; Appointment core; Department/Room/Doctor/Examination Service foundation; Examination Slots + Appointment Policy; Staff/Role bootstrap
- **Exit criteria/testing:** Demo: Register/Login → Search → Slot → Booking → Appointment Detail. Unit tests cho business logic; integration test FE↔BE + auth/RBAC; E2E booking pass; không còn blocker/P0.
- **Git tag:** `v0.1-iter1`

### M2 — Reception + Visit + Queue + Basic Consultation

- **Window:** Week 8-9
- **Backlog:** 38 functions
- **Increment demo:** Patient có thể reschedule/cancel, đến quầy theo Appointment hoặc Walk-in, được tạo Visit, route vào phòng/Doctor, tham gia queue và Doctor ghi nhận khám lâm sàng.
- **Scope:** Appointment refinement; Reception & Check-in; Visit & Routing; Queue handling; Basic Consultation; safe deactivation + queue policy + staff/role refinement
- **Exit criteria/testing:** Demo: Appointment/Walk-in → Check-in → Visit → Queue → Call Next → Clinical Exam. Test state transition/no duplicate Visit; integration queue/routing; regression toàn bộ Iter 1; không còn blocker/P0.
- **Git tag:** `v0.2-iter2`

### M3 — Clinical Completion + Admin + Release Validation

- **Window:** Week 10-13*
- **Backlog:** 45 functions
- **Increment demo:** Hoàn chỉnh Doctor → Diagnostic → Result → Return-to-Doctor → Diagnosis → Prescription/Dispensing/Payment → Patient Record; hoàn thiện admin, integrations, audit, AI optional và release hardening.
- **Scope:** Return queue; Diagnostic; Consultation completion; Prescription & Pharmacy; Payment; Patient Records; Diagnostic Service/Pricing; Operational Report; Integration/Audit; AI optional; Cross-module hardening
- **Exit criteria/testing:** Feature complete mục tiêu Week 11; Week 12-13 dành cho system/regression/UAT. Full E2E outpatient journey pass; permission/security/performance smoke pass; no blocker/P0/P1 trước release.
- **Git tag:** `v1.0-rc`

**Quy tắc Iterative & Incremental:** M2 kế thừa M1 + regression; M3 kế thừa M1/M2 + system/regression/UAT. M3 triển khai tính năng mục tiêu W10–11, kiểm chứng/release W12–13. Không được đóng một feature nếu FE/BE thiếu test và integration.

## 4. Tổng hợp issue

| Milestone | FE | BE | Testing module | E2E/Quality | Docs | Tổng |
|---|---:|---:|---:|---:|---:|---:|
| M1 | 47 | 47 | 0 | 0 | 0 | **117** |
| M2 | 38 | 38 | 0 | 0 | 0 | **108** |
| M3 | 45 | 45 | 0 | 0 | 0 | **130** |

## 5. Dùng GitHub CLI để tạo milestones, labels và issues

Các lệnh sau là **hướng dẫn**; tài liệu này không tự tạo thay đổi trên GitHub. Yêu cầu cài `gh`, đăng nhập `gh auth login`, có quyền ghi issues/milestones ở repo. Điền `OWNER/REPO` chính xác trước khi chạy.

```bash
export GH_REPO="OWNER/REPO"
gh auth status
gh repo view "$GH_REPO"
```

### Tạo labels (thực hiện một lần)

```bash
gh label create "type:feature" --repo "$GH_REPO" --color "1D76DB" --description "FE or BE development" --force
gh label create "type:bug" --repo "$GH_REPO" --color "D73A4A" --description "Defect fixing" --force
gh label create "type:test" --repo "$GH_REPO" --color "5319E7" --description "Testing task" --force
gh label create "type:docs" --repo "$GH_REPO" --color "0075CA" --description "Project documentation" --force
gh label create "type:change-requirement" --repo "$GH_REPO" --color "FBCA04" --description "Requirement change" --force
```

### Tạo milestones (GitHub REST API)

GitHub CLI không có `gh milestone create` mặc định; tạo milestone qua `gh api`:

```bash
gh api --method POST "repos/$GH_REPO/milestones" -f title='M1 - Iteration 1' -f description='Foundation & Appointment Booking | Weeks 6-7 | v0.1-iter1'
gh api --method POST "repos/$GH_REPO/milestones" -f title='M2 - Iteration 2' -f description='Reception, Visit, Queue & Basic Consultation | Weeks 8-9 | v0.2-iter2'
gh api --method POST "repos/$GH_REPO/milestones" -f title='M3 - Iteration 3' -f description='Clinical Completion & Release Validation | Weeks 10-13 | v1.0-rc'
```

> Chỉ định due date sau khi nhóm xác nhận ngày bắt đầu của Week 1; Report 2 ghi week tương đối, không cung cấp ngày cố định cho milestone.

### Tạo một issue thủ công

```bash
gh issue create --repo "$GH_REPO" --title '[M1][FE] Login' --label 'type:feature' --milestone 'M1 - Iteration 1' --body 'UC scope: UC02 Login'
```

### Tạo hàng loạt từ chính file Markdown

Lưu khối lệnh Python sau vào `create_medflow_issues.py` **cùng thư mục** với Markdown. Script đọc tất cả mục trong phần “Issue Catalogue”, tạo issue theo milestone tương ứng, kiểm tra issue title đã tồn tại để tránh tạo trùng. Mặc định **dry run**; thêm `--apply` mới thực sự ghi lên GitHub.

```python
import argparse, os, re, subprocess, pathlib
parser = argparse.ArgumentParser()
parser.add_argument("--apply", action="store_true")
args = parser.parse_args()
repo = os.environ.get("GH_REPO")
if not repo: raise SystemExit("Set GH_REPO=OWNER/REPO first")
text = pathlib.Path(__file__).with_name("MedFlow_GitHub_Milestones_and_Issues.md").read_text(encoding="utf-8")
parts = re.split(r"^## 6\. Issue Catalogue\s*$", text, flags=re.M)[1]
parts = re.split(r"^## 7\. Workflow.*$", parts, flags=re.M)[0]
blocks = re.split(r"(?=^### (M[123]-\d{3}) — )", parts, flags=re.M)
existing = set()
if args.apply:
    result = subprocess.run(["gh","issue","list","--repo",repo,"--state","all","--limit","1000","--json","title","--jq",".[].title"],capture_output=True,text=True,check=True)
    existing = set(result.stdout.splitlines())
created = 0
for n in range(1, len(blocks), 2):
    issue_id, block = blocks[n], blocks[n+1]
    first = block.splitlines()[0].strip()
    title = first.split(" — ", 1)[1]
    def field(key):
        m = re.search(r"^- \*\*"+re.escape(key)+r":\*\* (.*)$", block, flags=re.M)
        return m.group(1).strip() if m else ""
    label, milestone = field("Label"), field("Milestone")
    body = "Tracking ID: "+issue_id+"\n\n"+block.strip()
    if title in existing:
        print("SKIP", issue_id, title)
        continue
    cmd = ["gh","issue","create","--repo",repo,"--title",title,"--label",label,"--milestone",milestone,"--body",body]
    print("CREATE" if args.apply else "DRY-RUN", issue_id, title)
    if args.apply:
        subprocess.run(cmd,check=True)
        created += 1
print("Created:", created)
```

Chạy:

```bash
python create_medflow_issues.py               # xem trước
python create_medflow_issues.py --apply       # tạo issue thật, sau khi review
```

**Lưu ý:** Chạy lệnh tạo labels/milestones trước khi `--apply`. Đối với repository đã có milestones tên tương tự, tránh chạy lại lệnh POST tạo milestones. Các `type:bug` hoặc `type:change-requirement` phát sinh mới sẽ được tạo trong quá trình dự án, không giả lập defect hoặc change request trước.

## 6. Issue Catalogue

Danh sách dưới đây giữ nguyên **355 issue từ sheet `GITHUB ISSUES DETAILED`**. Field `Dependency` bao gồm UC scope, UC prerequisite và FE/BE/test dependency theo backlog; không suy đoán thêm những dependency ngoài source. Các issue `type:test` phải hoàn thành đúng phép kiểm thử nêu trong `Testing approach`.

### M1-001 — [M1][FE] Login
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** Login
- **Dependency:** UC scope: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Login. Cho phép người dùng đã đăng ký đăng nhập vào MedFlow và được điều hướng theo đúng vai trò/quyền hạn.
- **Initial status:** Ready

### M1-002 — [M1][BE] Login
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** Login
- **Dependency:** UC scope: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Login. Cho phép người dùng đã đăng ký đăng nhập vào MedFlow và được điều hướng theo đúng vai trò/quyền hạn.
- **Initial status:** Ready

### M1-003 — [M1][FE] Register Patient Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** Register Patient Account
- **Dependency:** UC scope: UC03 Register Patient Account; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Register Patient Account. Cho phép Patient tạo tài khoản MedFlow và hồ sơ bệnh nhân ban đầu.
- **Initial status:** Ready

### M1-004 — [M1][BE] Register Patient Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** Register Patient Account
- **Dependency:** UC scope: UC03 Register Patient Account; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Register Patient Account. Cho phép Patient tạo tài khoản MedFlow và hồ sơ bệnh nhân ban đầu.
- **Initial status:** Ready

### M1-005 — [M1][FE] Verify Patient Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** Verify Patient Account
- **Dependency:** UC scope: UC03 Register Patient Account; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Verify Patient Account. Hiển thị trạng thái xác minh tài khoản và hỗ trợ hoàn tất xác minh trước khi sử dụng chức năng yêu cầu tài khoản hợp lệ.
- **Initial status:** Ready

### M1-006 — [M1][BE] Verify Patient Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** Verify Patient Account
- **Dependency:** UC scope: UC03 Register Patient Account; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Verify Patient Account. Hiển thị trạng thái xác minh tài khoản và hỗ trợ hoàn tất xác minh trước khi sử dụng chức năng yêu cầu tài khoản hợp lệ.
- **Initial status:** Ready

### M1-007 — [M1][FE] Logout
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** Logout
- **Dependency:** UC scope: UC04 Logout; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Logout. Cho phép người dùng kết thúc phiên làm việc và quay về màn hình Login.
- **Initial status:** Ready

### M1-008 — [M1][BE] Logout
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** Logout
- **Dependency:** UC scope: UC04 Logout; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Logout. Cho phép người dùng kết thúc phiên làm việc và quay về màn hình Login.
- **Initial status:** Ready

### M1-009 — [M1][FE] View Patient Home
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Patient Home
- **Dependency:** UC scope: UC01 View Homepage; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Patient Home. Hiển thị màn hình khởi đầu sau đăng nhập cho Patient, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-010 — [M1][BE] View Patient Home
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Patient Home
- **Dependency:** UC scope: UC01 View Homepage; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Patient Home. Hiển thị màn hình khởi đầu sau đăng nhập cho Patient, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-011 — [M1][FE] View Reception Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Reception Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Reception Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Patient Service Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-012 — [M1][BE] View Reception Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Reception Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Reception Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Patient Service Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-013 — [M1][FE] View Doctor Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Doctor Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Doctor Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Doctor, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-014 — [M1][BE] View Doctor Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Doctor Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Doctor Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Doctor, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-015 — [M1][FE] View Diagnostic Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Diagnostic Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Diagnostic Services Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-016 — [M1][BE] View Diagnostic Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Diagnostic Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Diagnostic Services Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-017 — [M1][FE] View Pharmacy Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Pharmacy Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Pharmacy Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Pharmacy Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-018 — [M1][BE] View Pharmacy Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Pharmacy Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Pharmacy Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Pharmacy Staff, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-019 — [M1][FE] View Hospital Admin Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Hospital Admin Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Hospital Admin Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Hospital Admin, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-020 — [M1][BE] View Hospital Admin Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View Hospital Admin Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Hospital Admin Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho Hospital Admin, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-021 — [M1][FE] View System Admin Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Authentication & Role Landing
- **Function/trace:** View System Admin Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View System Admin Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho System Admin, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-022 — [M1][BE] View System Admin Dashboard
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Authentication & Role Landing
- **Function/trace:** View System Admin Dashboard
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View System Admin Dashboard. Hiển thị màn hình khởi đầu sau đăng nhập cho System Admin, chỉ cung cấp các lối vào chức năng mà vai trò được phép sử dụng.
- **Initial status:** Ready

### M1-023 — [M1][FE] View Personal Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** View Personal Profile
- **Dependency:** UC scope: UC05 Manage Personal Profile; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Personal Profile. Hiển thị thông tin cá nhân của Patient và các hành động quản lý hồ sơ được phép.
- **Initial status:** Ready

### M1-024 — [M1][BE] View Personal Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** View Personal Profile
- **Dependency:** UC scope: UC05 Manage Personal Profile; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Personal Profile. Hiển thị thông tin cá nhân của Patient và các hành động quản lý hồ sơ được phép.
- **Initial status:** Ready

### M1-025 — [M1][FE] Update Personal Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** Update Personal Profile
- **Dependency:** UC scope: UC05 Manage Personal Profile; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Personal Profile. Cho phép Patient cập nhật các thông tin cá nhân được phép thay đổi.
- **Initial status:** Ready

### M1-026 — [M1][BE] Update Personal Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** Update Personal Profile
- **Dependency:** UC scope: UC05 Manage Personal Profile; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Personal Profile. Cho phép Patient cập nhật các thông tin cá nhân được phép thay đổi.
- **Initial status:** Ready

### M1-027 — [M1][FE] View Family Profile List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** View Family Profile List
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Family Profile List. Hiển thị các hồ sơ người thân đang được liên kết với tài khoản Patient.
- **Initial status:** Ready

### M1-028 — [M1][BE] View Family Profile List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** View Family Profile List
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Family Profile List. Hiển thị các hồ sơ người thân đang được liên kết với tài khoản Patient.
- **Initial status:** Ready

### M1-029 — [M1][FE] View Family Profile Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** View Family Profile Detail
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Family Profile Detail. Hiển thị chi tiết một hồ sơ người thân để sử dụng khi đặt lịch hoặc cập nhật thông tin.
- **Initial status:** Ready

### M1-030 — [M1][BE] View Family Profile Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** View Family Profile Detail
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Family Profile Detail. Hiển thị chi tiết một hồ sơ người thân để sử dụng khi đặt lịch hoặc cập nhật thông tin.
- **Initial status:** Ready

### M1-031 — [M1][FE] Create Family Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** Create Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Family Profile. Cho phép Patient tạo hồ sơ người thân mới mà họ được phép quản lý.
- **Initial status:** Ready

### M1-032 — [M1][BE] Create Family Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** Create Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Family Profile. Cho phép Patient tạo hồ sơ người thân mới mà họ được phép quản lý.
- **Initial status:** Ready

### M1-033 — [M1][FE] Update Family Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** Update Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Family Profile. Cho phép Patient cập nhật thông tin hồ sơ người thân đã liên kết.
- **Initial status:** Ready

### M1-034 — [M1][BE] Update Family Profile
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** Update Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Family Profile. Cho phép Patient cập nhật thông tin hồ sơ người thân đã liên kết.
- **Initial status:** Ready

### M2-001 — [M2][FE] Remove Family Profile
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Profile Management
- **Function/trace:** Remove Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Remove Family Profile. Cho phép Patient gỡ liên kết hồ sơ người thân sau khi xác nhận, khi không vi phạm ràng buộc nghiệp vụ.
- **Initial status:** Backlog

### M2-002 — [M2][BE] Remove Family Profile
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Profile Management
- **Function/trace:** Remove Family Profile
- **Dependency:** UC scope: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Remove Family Profile. Cho phép Patient gỡ liên kết hồ sơ người thân sau khi xác nhận, khi không vi phạm ràng buộc nghiệp vụ.
- **Initial status:** Backlog

### M1-035 — [M1][FE] Search Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Search Doctor
- **Dependency:** UC scope: UC09 Search Doctor; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Search Doctor. Cho phép Patient tìm kiếm/lọc Doctor có thể đặt lịch.
- **Initial status:** Ready

### M1-036 — [M1][BE] Search Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Search Doctor
- **Dependency:** UC scope: UC09 Search Doctor; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Search Doctor. Cho phép Patient tìm kiếm/lọc Doctor có thể đặt lịch.
- **Initial status:** Ready

### M1-037 — [M1][FE] View Doctor Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** View Doctor Detail
- **Dependency:** UC scope: UC09 Search Doctor; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Doctor Detail. Hiển thị thông tin Doctor cần thiết trước khi Patient chọn lịch khám.
- **Initial status:** Ready

### M1-038 — [M1][BE] View Doctor Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** View Doctor Detail
- **Dependency:** UC scope: UC09 Search Doctor; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Doctor Detail. Hiển thị thông tin Doctor cần thiết trước khi Patient chọn lịch khám.
- **Initial status:** Ready

### M1-039 — [M1][FE] Search Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Search Examination Service
- **Dependency:** UC scope: UC10 Search Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Search Examination Service. Cho phép Patient tìm kiếm/lọc các Examination Service đang hoạt động và cho phép đặt lịch.
- **Initial status:** Ready

### M1-040 — [M1][BE] Search Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Search Examination Service
- **Dependency:** UC scope: UC10 Search Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Search Examination Service. Cho phép Patient tìm kiếm/lọc các Examination Service đang hoạt động và cho phép đặt lịch.
- **Initial status:** Ready

### M1-041 — [M1][FE] View Examination Service Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** View Examination Service Detail
- **Dependency:** UC scope: UC10 Search Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Examination Service Detail. Hiển thị thông tin dịch vụ khám, khoa/phòng liên quan, thời lượng và giá tham khảo.
- **Initial status:** Ready

### M1-042 — [M1][BE] View Examination Service Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** View Examination Service Detail
- **Dependency:** UC scope: UC10 Search Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Examination Service Detail. Hiển thị thông tin dịch vụ khám, khoa/phòng liên quan, thời lượng và giá tham khảo.
- **Initial status:** Ready

### M1-043 — [M1][FE] View Available Appointment Slots
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** View Available Appointment Slots
- **Dependency:** UC scope: UC11 View Available Appointment Slots; Prerequisite UCs: UC09 Search Doctor; UC10 Search Examination Service; UC53 Manage Examination Slots; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Available Appointment Slots. Hiển thị các ngày/ca/slot còn khả dụng theo Doctor hoặc Examination Service đã chọn.
- **Initial status:** Ready

### M1-044 — [M1][BE] View Available Appointment Slots
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** View Available Appointment Slots
- **Dependency:** UC scope: UC11 View Available Appointment Slots; Prerequisite UCs: UC09 Search Doctor; UC10 Search Examination Service; UC53 Manage Examination Slots; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Available Appointment Slots. Hiển thị các ngày/ca/slot còn khả dụng theo Doctor hoặc Examination Service đã chọn.
- **Initial status:** Ready

### M1-045 — [M1][FE] Review Appointment Booking
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Review Appointment Booking
- **Dependency:** UC scope: UC12 Book Appointment; Prerequisite UCs: UC11 View Available Appointment Slots; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Review Appointment Booking. Cho phép Patient kiểm tra hồ sơ người khám, Doctor/dịch vụ, slot và chi phí dự kiến trước khi xác nhận.
- **Initial status:** Ready

### M1-046 — [M1][BE] Review Appointment Booking
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Review Appointment Booking
- **Dependency:** UC scope: UC12 Book Appointment; Prerequisite UCs: UC11 View Available Appointment Slots; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Review Appointment Booking. Cho phép Patient kiểm tra hồ sơ người khám, Doctor/dịch vụ, slot và chi phí dự kiến trước khi xác nhận.
- **Initial status:** Ready

### M1-047 — [M1][FE] Book Appointment
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Book Appointment
- **Dependency:** UC scope: UC12 Book Appointment; Prerequisite UCs: UC11 View Available Appointment Slots; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Book Appointment. Tạo Appointment hợp lệ, giữ chỗ theo capacity và hiển thị thông tin xác nhận sau khi đặt lịch thành công.
- **Initial status:** Ready

### M1-048 — [M1][BE] Book Appointment
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Book Appointment
- **Dependency:** UC scope: UC12 Book Appointment; Prerequisite UCs: UC11 View Available Appointment Slots; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Book Appointment. Tạo Appointment hợp lệ, giữ chỗ theo capacity và hiển thị thông tin xác nhận sau khi đặt lịch thành công.
- **Initial status:** Ready

### M1-049 — [M1][FE] View Appointment List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** View Appointment List
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Appointment List. Hiển thị danh sách lịch hẹn sắp tới và lịch sử lịch hẹn của Patient.
- **Initial status:** Ready

### M1-050 — [M1][BE] View Appointment List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** View Appointment List
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Appointment List. Hiển thị danh sách lịch hẹn sắp tới và lịch sử lịch hẹn của Patient.
- **Initial status:** Ready

### M1-051 — [M1][FE] View Appointment Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** View Appointment Detail
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Appointment Detail. Hiển thị chi tiết, trạng thái và các hành động còn hợp lệ của một Appointment.
- **Initial status:** Ready

### M1-052 — [M1][BE] View Appointment Detail
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** View Appointment Detail
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Appointment Detail. Hiển thị chi tiết, trạng thái và các hành động còn hợp lệ của một Appointment.
- **Initial status:** Ready

### M2-003 — [M2][FE] Reschedule Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Reschedule Appointment
- **Dependency:** UC scope: UC13 Reschedule Appointment; Prerequisite UCs: UC12 Book Appointment; UC11 View Available Appointment Slots; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Reschedule Appointment. Cho phép Patient chuyển Appointment sang slot khác còn khả dụng theo chính sách đặt lịch.
- **Initial status:** Backlog

### M2-004 — [M2][BE] Reschedule Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Reschedule Appointment
- **Dependency:** UC scope: UC13 Reschedule Appointment; Prerequisite UCs: UC12 Book Appointment; UC11 View Available Appointment Slots; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Reschedule Appointment. Cho phép Patient chuyển Appointment sang slot khác còn khả dụng theo chính sách đặt lịch.
- **Initial status:** Backlog

### M2-005 — [M2][FE] Cancel Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Cancel Appointment
- **Dependency:** UC scope: UC14 Cancel Appointment; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Cancel Appointment. Cho phép Patient hủy Appointment còn đủ điều kiện và yêu cầu xác nhận trước khi hủy.
- **Initial status:** Backlog

### M2-006 — [M2][BE] Cancel Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Cancel Appointment
- **Dependency:** UC scope: UC14 Cancel Appointment; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Cancel Appointment. Cho phép Patient hủy Appointment còn đủ điều kiện và yêu cầu xác nhận trước khi hủy.
- **Initial status:** Backlog

### M2-007 — [M2][FE] Track Appointment Status
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Track Appointment Status
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Track Appointment Status. Cho phép Patient theo dõi trạng thái hiện tại của Appointment và bước tiếp theo.
- **Initial status:** Backlog

### M2-008 — [M2][BE] Track Appointment Status
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Track Appointment Status
- **Dependency:** UC scope: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Track Appointment Status. Cho phép Patient theo dõi trạng thái hiện tại của Appointment và bước tiếp theo.
- **Initial status:** Backlog

### M2-009 — [M2][FE] Receive Appointment Reminder
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Appointment Management
- **Function/trace:** Receive Appointment Reminder
- **Dependency:** UC scope: UC17 Receive Notifications and Reminders; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Receive Appointment Reminder. Gửi/hiển thị nhắc lịch trước thời gian khám theo cấu hình hệ thống.
- **Initial status:** Backlog

### M2-010 — [M2][BE] Receive Appointment Reminder
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Appointment Management
- **Function/trace:** Receive Appointment Reminder
- **Dependency:** UC scope: UC17 Receive Notifications and Reminders; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Receive Appointment Reminder. Gửi/hiển thị nhắc lịch trước thời gian khám theo cấu hình hệ thống.
- **Initial status:** Backlog

### M2-011 — [M2][FE] Search Patient / Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** Search Patient / Appointment
- **Dependency:** UC scope: UC22 Search Patient; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Search Patient / Appointment. Cho phép Patient Service Staff tìm Patient hoặc Appointment khi tiếp nhận tại quầy.
- **Initial status:** Backlog

### M2-012 — [M2][BE] Search Patient / Appointment
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** Search Patient / Appointment
- **Dependency:** UC scope: UC22 Search Patient; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Search Patient / Appointment. Cho phép Patient Service Staff tìm Patient hoặc Appointment khi tiếp nhận tại quầy.
- **Initial status:** Backlog

### M2-013 — [M2][FE] View Patient Detail
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** View Patient Detail
- **Dependency:** UC scope: UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Patient Detail. Hiển thị thông tin Patient cần thiết cho tiếp nhận, đối chiếu và đăng ký Visit.
- **Initial status:** Backlog

### M2-014 — [M2][BE] View Patient Detail
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** View Patient Detail
- **Dependency:** UC scope: UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Patient Detail. Hiển thị thông tin Patient cần thiết cho tiếp nhận, đối chiếu và đăng ký Visit.
- **Initial status:** Backlog

### M2-015 — [M2][FE] Create Patient Profile at Reception
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** Create Patient Profile at Reception
- **Dependency:** UC scope: UC23 Manage Patient Profile at Reception; Prerequisite UCs: UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Patient Profile at Reception. Cho phép Patient Service Staff tạo hồ sơ Patient khi chưa tồn tại trong hệ thống.
- **Initial status:** Backlog

### M2-016 — [M2][BE] Create Patient Profile at Reception
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** Create Patient Profile at Reception
- **Dependency:** UC scope: UC23 Manage Patient Profile at Reception; Prerequisite UCs: UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Patient Profile at Reception. Cho phép Patient Service Staff tạo hồ sơ Patient khi chưa tồn tại trong hệ thống.
- **Initial status:** Backlog

### M2-017 — [M2][FE] Update Patient Profile at Reception
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** Update Patient Profile at Reception
- **Dependency:** UC scope: UC23 Manage Patient Profile at Reception; Prerequisite UCs: UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Patient Profile at Reception. Cho phép Patient Service Staff cập nhật thông tin Patient được phép chỉnh sửa tại quầy.
- **Initial status:** Backlog

### M2-018 — [M2][BE] Update Patient Profile at Reception
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** Update Patient Profile at Reception
- **Dependency:** UC scope: UC23 Manage Patient Profile at Reception; Prerequisite UCs: UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Patient Profile at Reception. Cho phép Patient Service Staff cập nhật thông tin Patient được phép chỉnh sửa tại quầy.
- **Initial status:** Backlog

### M2-019 — [M2][FE] Check-in Scheduled Patient
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** Check-in Scheduled Patient
- **Dependency:** UC scope: UC24 Check-in Patient with Appointment; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Check-in Scheduled Patient. Xác nhận Appointment hợp lệ, Patient có mặt và thực hiện check-in chính thức để tạo/kích hoạt Visit.
- **Initial status:** Backlog

### M2-020 — [M2][BE] Check-in Scheduled Patient
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** Check-in Scheduled Patient
- **Dependency:** UC scope: UC24 Check-in Patient with Appointment; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Check-in Scheduled Patient. Xác nhận Appointment hợp lệ, Patient có mặt và thực hiện check-in chính thức để tạo/kích hoạt Visit.
- **Initial status:** Backlog

### M2-021 — [M2][FE] View Check-in Result
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** View Check-in Result
- **Dependency:** UC scope: UC24 Check-in Patient with Appointment; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Check-in Result. Hiển thị Visit, Doctor/room/queue và hướng dẫn tiếp theo sau check-in thành công.
- **Initial status:** Backlog

### M2-022 — [M2][BE] View Check-in Result
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** View Check-in Result
- **Dependency:** UC scope: UC24 Check-in Patient with Appointment; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Check-in Result. Hiển thị Visit, Doctor/room/queue và hướng dẫn tiếp theo sau check-in thành công.
- **Initial status:** Backlog

### M2-023 — [M2][FE] Create Walk-in Visit
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Reception & Check-in
- **Function/trace:** Create Walk-in Visit
- **Dependency:** UC scope: UC25 Create Walk-in Visit; Prerequisite UCs: UC22 Search Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Walk-in Visit. Tạo Visit ngoại trú cho Patient không có Appointment và thu thập thông tin cần thiết để routing.
- **Initial status:** Backlog

### M2-024 — [M2][BE] Create Walk-in Visit
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Reception & Check-in
- **Function/trace:** Create Walk-in Visit
- **Dependency:** UC scope: UC25 Create Walk-in Visit; Prerequisite UCs: UC22 Search Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Walk-in Visit. Tạo Visit ngoại trú cho Patient không có Appointment và thu thập thông tin cần thiết để routing.
- **Initial status:** Backlog

### M2-025 — [M2][FE] Assign Doctor to Visit
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Visit & Routing
- **Function/trace:** Assign Doctor to Visit
- **Dependency:** UC scope: UC26 Assign Doctor to Visit; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Assign Doctor to Visit. Cho phép staff chọn Doctor phù hợp cho Visit dựa trên dịch vụ, khả dụng và workload.
- **Initial status:** Backlog

### M2-026 — [M2][BE] Assign Doctor to Visit
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Visit & Routing
- **Function/trace:** Assign Doctor to Visit
- **Dependency:** UC scope: UC26 Assign Doctor to Visit; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Assign Doctor to Visit. Cho phép staff chọn Doctor phù hợp cho Visit dựa trên dịch vụ, khả dụng và workload.
- **Initial status:** Backlog

### M2-027 — [M2][FE] Assign Examination Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Visit & Routing
- **Function/trace:** Assign Examination Room
- **Dependency:** UC scope: UC27 Assign Examination Room to Visit; Prerequisite UCs: UC26 Assign Doctor to Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Assign Examination Room. Gán Examination Room tương thích và còn khả dụng cho Visit.
- **Initial status:** Backlog

### M2-028 — [M2][BE] Assign Examination Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Visit & Routing
- **Function/trace:** Assign Examination Room
- **Dependency:** UC scope: UC27 Assign Examination Room to Visit; Prerequisite UCs: UC26 Assign Doctor to Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Assign Examination Room. Gán Examination Room tương thích và còn khả dụng cho Visit.
- **Initial status:** Backlog

### M2-029 — [M2][FE] Transfer Patient to Compatible Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Visit & Routing
- **Function/trace:** Transfer Patient to Compatible Room
- **Dependency:** UC scope: UC27 Assign Examination Room to Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Transfer Patient to Compatible Room. Cho phép chuyển Patient sang phòng khác phù hợp khi vận hành yêu cầu và giữ lịch sử thay đổi.
- **Initial status:** Backlog

### M2-030 — [M2][BE] Transfer Patient to Compatible Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Visit & Routing
- **Function/trace:** Transfer Patient to Compatible Room
- **Dependency:** UC scope: UC27 Assign Examination Room to Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Transfer Patient to Compatible Room. Cho phép chuyển Patient sang phòng khác phù hợp khi vận hành yêu cầu và giữ lịch sử thay đổi.
- **Initial status:** Backlog

### M2-031 — [M2][FE] Print Examination Ticket
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Visit & Routing
- **Function/trace:** Print Examination Ticket
- **Dependency:** UC scope: UC29 Print Examination Ticket; Prerequisite UCs: UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Print Examination Ticket. Hiển thị/cho phép in thông tin Visit, phòng khám và số thứ tự cho Patient.
- **Initial status:** Backlog

### M2-032 — [M2][BE] Print Examination Ticket
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Visit & Routing
- **Function/trace:** Print Examination Ticket
- **Dependency:** UC scope: UC29 Print Examination Ticket; Prerequisite UCs: UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Print Examination Ticket. Hiển thị/cho phép in thông tin Visit, phòng khám và số thứ tự cho Patient.
- **Initial status:** Backlog

### M2-033 — [M2][FE] Track Patient Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Track Patient Queue
- **Dependency:** UC scope: UC16 Track Examination Queue; Prerequisite UCs: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Track Patient Queue. Cho phép Patient xem số thứ tự, service point, trạng thái chờ và tiến trình khám liên quan.
- **Initial status:** Backlog

### M2-034 — [M2][BE] Track Patient Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Track Patient Queue
- **Dependency:** UC scope: UC16 Track Examination Queue; Prerequisite UCs: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Track Patient Queue. Cho phép Patient xem số thứ tự, service point, trạng thái chờ và tiến trình khám liên quan.
- **Initial status:** Backlog

### M2-035 — [M2][FE] View Examination Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** View Examination Queue
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Examination Queue. Hiển thị Queue Entries theo service point/room cùng trạng thái và thứ tự phục vụ hiện tại.
- **Initial status:** Backlog

### M2-036 — [M2][BE] View Examination Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** View Examination Queue
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Examination Queue. Hiển thị Queue Entries theo service point/room cùng trạng thái và thứ tự phục vụ hiện tại.
- **Initial status:** Backlog

### M2-037 — [M2][FE] Update Queue Status
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Update Queue Status
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC24 Check-in Patient with Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Queue Status. Cho phép staff cập nhật trạng thái Queue Entry theo các chuyển tiếp trạng thái hợp lệ.
- **Initial status:** Backlog

### M2-038 — [M2][BE] Update Queue Status
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Update Queue Status
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC24 Check-in Patient with Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Queue Status. Cho phép staff cập nhật trạng thái Queue Entry theo các chuyển tiếp trạng thái hợp lệ.
- **Initial status:** Backlog

### M2-039 — [M2][FE] View Doctor Assigned Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** View Doctor Assigned Queue
- **Dependency:** UC scope: UC30 View Assigned Queue; Prerequisite UCs: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Doctor Assigned Queue. Hiển thị các Patient được gán hoặc đủ điều kiện để Doctor gọi khám.
- **Initial status:** Backlog

### M2-040 — [M2][BE] View Doctor Assigned Queue
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** View Doctor Assigned Queue
- **Dependency:** UC scope: UC30 View Assigned Queue; Prerequisite UCs: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Doctor Assigned Queue. Hiển thị các Patient được gán hoặc đủ điều kiện để Doctor gọi khám.
- **Initial status:** Backlog

### M2-041 — [M2][FE] Call Next Eligible Patient
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Call Next Eligible Patient
- **Dependency:** UC scope: UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC30 View Assigned Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Call Next Eligible Patient. Cho phép Doctor gọi Patient tiếp theo đủ điều kiện theo queue policy, không dựa đơn thuần vào queue number.
- **Initial status:** Backlog

### M2-042 — [M2][BE] Call Next Eligible Patient
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Call Next Eligible Patient
- **Dependency:** UC scope: UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC30 View Assigned Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Call Next Eligible Patient. Cho phép Doctor gọi Patient tiếp theo đủ điều kiện theo queue policy, không dựa đơn thuần vào queue number.
- **Initial status:** Backlog

### M2-043 — [M2][FE] Handle Early Arrival / Safe Backfill
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Handle Early Arrival / Safe Backfill
- **Dependency:** UC scope: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Handle Early Arrival / Safe Backfill. Bổ sung logic early-arrival và safe backfill để tận dụng thời gian trống nhưng không phá vỡ appointment protection.
- **Initial status:** Backlog

### M2-044 — [M2][BE] Handle Early Arrival / Safe Backfill
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Handle Early Arrival / Safe Backfill
- **Dependency:** UC scope: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Handle Early Arrival / Safe Backfill. Bổ sung logic early-arrival và safe backfill để tận dụng thời gian trống nhưng không phá vỡ appointment protection.
- **Initial status:** Backlog

### M2-045 — [M2][FE] Handle Late Arrival / Missed Turn
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Handle Late Arrival / Missed Turn
- **Dependency:** UC scope: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Handle Late Arrival / Missed Turn. Xử lý Patient đến muộn hoặc lỡ lượt theo policy và đưa về vị trí phục vụ hợp lý mà không renumber số đã cấp.
- **Initial status:** Backlog

### M2-046 — [M2][BE] Handle Late Arrival / Missed Turn
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Handle Late Arrival / Missed Turn
- **Dependency:** UC scope: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Handle Late Arrival / Missed Turn. Xử lý Patient đến muộn hoặc lỡ lượt theo policy và đưa về vị trí phục vụ hợp lý mà không renumber số đã cấp.
- **Initial status:** Backlog

### M3-001 — [M3][FE] Create Return-to-Doctor Queue Entry
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Create Return-to-Doctor Queue Entry
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC35 View Diagnostic Result; UC43 Update Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Return-to-Doctor Queue Entry. Tạo Queue Entry mới cho lượt quay lại Doctor khi diagnostic results đã sẵn sàng và Patient đã trở lại khu vực khám.
- **Initial status:** Backlog

### M3-002 — [M3][BE] Create Return-to-Doctor Queue Entry
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Create Return-to-Doctor Queue Entry
- **Dependency:** UC scope: UC28 Manage Examination Queue; Prerequisite UCs: UC35 View Diagnostic Result; UC43 Update Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Return-to-Doctor Queue Entry. Tạo Queue Entry mới cho lượt quay lại Doctor khi diagnostic results đã sẵn sàng và Patient đã trở lại khu vực khám.
- **Initial status:** Backlog

### M2-047 — [M2][FE] Apply Clinical Priority
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Apply Clinical Priority
- **Dependency:** UC scope: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Apply Clinical Priority. Cho phép áp dụng clinical priority khi được người có thẩm quyền xác nhận và lưu đầy đủ lý do/audit.
- **Initial status:** Backlog

### M2-048 — [M2][BE] Apply Clinical Priority
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Apply Clinical Priority
- **Dependency:** UC scope: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Apply Clinical Priority. Cho phép áp dụng clinical priority khi được người có thẩm quyền xác nhận và lưu đầy đủ lý do/audit.
- **Initial status:** Backlog

### M2-049 — [M2][FE] Cancel Queue Entry
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Queue Management
- **Function/trace:** Cancel Queue Entry
- **Dependency:** UC scope: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Cancel Queue Entry. Cho phép hủy Queue Entry khi đủ điều kiện và giữ lại lịch sử thay đổi.
- **Initial status:** Backlog

### M2-050 — [M2][BE] Cancel Queue Entry
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Queue Management
- **Function/trace:** Cancel Queue Entry
- **Dependency:** UC scope: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Cancel Queue Entry. Cho phép hủy Queue Entry khi đủ điều kiện và giữ lại lịch sử thay đổi.
- **Initial status:** Backlog

### M2-051 — [M2][FE] Open Consultation Workspace
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Open Consultation Workspace
- **Dependency:** UC scope: UC31 View Patient Examination; Prerequisite UCs: UC30 View Assigned Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Open Consultation Workspace. Mở Visit hiện tại để Doctor xem thông tin Patient, queue, clinical context và thao tác khám.
- **Initial status:** Backlog

### M2-052 — [M2][BE] Open Consultation Workspace
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Open Consultation Workspace
- **Dependency:** UC scope: UC31 View Patient Examination; Prerequisite UCs: UC30 View Assigned Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Open Consultation Workspace. Mở Visit hiện tại để Doctor xem thông tin Patient, queue, clinical context và thao tác khám.
- **Initial status:** Backlog

### M2-053 — [M2][FE] View Medical History
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** View Medical History
- **Dependency:** UC scope: UC32 View Medical History; Prerequisite UCs: UC31 View Patient Examination; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Medical History. Cho phép Doctor xem lịch sử khám cần thiết của Patient trong phạm vi được phép.
- **Initial status:** Backlog

### M2-054 — [M2][BE] View Medical History
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** View Medical History
- **Dependency:** UC scope: UC32 View Medical History; Prerequisite UCs: UC31 View Patient Examination; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Medical History. Cho phép Doctor xem lịch sử khám cần thiết của Patient trong phạm vi được phép.
- **Initial status:** Backlog

### M2-055 — [M2][FE] Record Clinical Examination
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Record Clinical Examination
- **Dependency:** UC scope: UC33 Record Clinical Examination; Prerequisite UCs: UC31 View Patient Examination; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Record Clinical Examination. Cho phép Doctor ghi triệu chứng, dấu hiệu, nhận xét và thông tin khám lâm sàng.
- **Initial status:** Backlog

### M2-056 — [M2][BE] Record Clinical Examination
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Record Clinical Examination
- **Dependency:** UC scope: UC33 Record Clinical Examination; Prerequisite UCs: UC31 View Patient Examination; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Record Clinical Examination. Cho phép Doctor ghi triệu chứng, dấu hiệu, nhận xét và thông tin khám lâm sàng.
- **Initial status:** Backlog

### M3-003 — [M3][FE] Record Diagnosis & Clinical Conclusion
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Record Diagnosis & Clinical Conclusion
- **Dependency:** UC scope: UC36 Record Diagnosis; Prerequisite UCs: UC33 Record Clinical Examination; UC35 View Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Record Diagnosis & Clinical Conclusion. Cho phép Doctor ghi chẩn đoán và kết luận lâm sàng cho Visit.
- **Initial status:** Backlog

### M3-004 — [M3][BE] Record Diagnosis & Clinical Conclusion
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Record Diagnosis & Clinical Conclusion
- **Dependency:** UC scope: UC36 Record Diagnosis; Prerequisite UCs: UC33 Record Clinical Examination; UC35 View Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Record Diagnosis & Clinical Conclusion. Cho phép Doctor ghi chẩn đoán và kết luận lâm sàng cho Visit.
- **Initial status:** Backlog

### M3-005 — [M3][FE] Review Diagnostic Results
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Review Diagnostic Results
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC34 Create Diagnostic Order; UC43 Update Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Review Diagnostic Results. Cho phép Doctor xem kết quả diagnostic đã sẵn sàng gắn với Visit hiện tại.
- **Initial status:** Backlog

### M3-006 — [M3][BE] Review Diagnostic Results
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Review Diagnostic Results
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC34 Create Diagnostic Order; UC43 Update Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Review Diagnostic Results. Cho phép Doctor xem kết quả diagnostic đã sẵn sàng gắn với Visit hiện tại.
- **Initial status:** Backlog

### M3-007 — [M3][FE] Create Additional Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Create Additional Diagnostic Order
- **Dependency:** UC scope: UC34 Create Diagnostic Order; Prerequisite UCs: UC33 Record Clinical Examination; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Additional Diagnostic Order. Cho phép Doctor tạo thêm Diagnostic Order khi cần sau khi xem kết quả/diễn biến khám.
- **Initial status:** Backlog

### M3-008 — [M3][BE] Create Additional Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Create Additional Diagnostic Order
- **Dependency:** UC scope: UC34 Create Diagnostic Order; Prerequisite UCs: UC33 Record Clinical Examination; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Additional Diagnostic Order. Cho phép Doctor tạo thêm Diagnostic Order khi cần sau khi xem kết quả/diễn biến khám.
- **Initial status:** Backlog

### M3-009 — [M3][FE] Complete Visit
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Consultation
- **Function/trace:** Complete Visit
- **Dependency:** UC scope: UC38 Complete Visit; Prerequisite UCs: UC36 Record Diagnosis; UC37 Create Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Complete Visit. Hoàn tất Visit khi các thông tin lâm sàng bắt buộc đã đầy đủ và không còn bước cần xử lý.
- **Initial status:** Backlog

### M3-010 — [M3][BE] Complete Visit
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Consultation
- **Function/trace:** Complete Visit
- **Dependency:** UC scope: UC38 Complete Visit; Prerequisite UCs: UC36 Record Diagnosis; UC37 Create Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Complete Visit. Hoàn tất Visit khi các thông tin lâm sàng bắt buộc đã đầy đủ và không còn bước cần xử lý.
- **Initial status:** Backlog

### M3-011 — [M3][FE] Create Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Create Diagnostic Order
- **Dependency:** UC scope: UC34 Create Diagnostic Order; Prerequisite UCs: UC33 Record Clinical Examination; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Diagnostic Order. Cho phép Doctor tạo Diagnostic Order cho laboratory/imaging service trong Visit đang hoạt động.
- **Initial status:** Backlog

### M3-012 — [M3][BE] Create Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Create Diagnostic Order
- **Dependency:** UC scope: UC34 Create Diagnostic Order; Prerequisite UCs: UC33 Record Clinical Examination; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Diagnostic Order. Cho phép Doctor tạo Diagnostic Order cho laboratory/imaging service trong Visit đang hoạt động.
- **Initial status:** Backlog

### M3-013 — [M3][FE] View Diagnostic Results for Doctor
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Results for Doctor
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Results for Doctor. Tổng hợp các Diagnostic Order/Result của Visit để Doctor theo dõi mức độ hoàn thành.
- **Initial status:** Backlog

### M3-014 — [M3][BE] View Diagnostic Results for Doctor
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Results for Doctor
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Results for Doctor. Tổng hợp các Diagnostic Order/Result của Visit để Doctor theo dõi mức độ hoàn thành.
- **Initial status:** Backlog

### M3-015 — [M3][FE] View Diagnostic Worklist
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Worklist
- **Dependency:** UC scope: UC39 Review and Confirm Diagnostic Order; Prerequisite UCs: UC34 Create Diagnostic Order; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Worklist. Hiển thị các Diagnostic Order được giao cho khu vực diagnostic và trạng thái xử lý.
- **Initial status:** Backlog

### M3-016 — [M3][BE] View Diagnostic Worklist
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Worklist
- **Dependency:** UC scope: UC39 Review and Confirm Diagnostic Order; Prerequisite UCs: UC34 Create Diagnostic Order; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Worklist. Hiển thị các Diagnostic Order được giao cho khu vực diagnostic và trạng thái xử lý.
- **Initial status:** Backlog

### M3-017 — [M3][FE] Review & Confirm Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Review & Confirm Diagnostic Order
- **Dependency:** UC scope: UC39 Review and Confirm Diagnostic Order; Prerequisite UCs: UC34 Create Diagnostic Order; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Review & Confirm Diagnostic Order. Cho phép Diagnostic Services Staff kiểm tra order và xác nhận đủ điều kiện xử lý.
- **Initial status:** Backlog

### M3-018 — [M3][BE] Review & Confirm Diagnostic Order
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Review & Confirm Diagnostic Order
- **Dependency:** UC scope: UC39 Review and Confirm Diagnostic Order; Prerequisite UCs: UC34 Create Diagnostic Order; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Review & Confirm Diagnostic Order. Cho phép Diagnostic Services Staff kiểm tra order và xác nhận đủ điều kiện xử lý.
- **Initial status:** Backlog

### M3-019 — [M3][FE] Receive Diagnostic Patient
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Receive Diagnostic Patient
- **Dependency:** UC scope: UC40 Receive Diagnostic Patient; Prerequisite UCs: UC39 Review and Confirm Diagnostic Order; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Receive Diagnostic Patient. Xác nhận Patient đã đến service point diagnostic và sẵn sàng vào queue.
- **Initial status:** Backlog

### M3-020 — [M3][BE] Receive Diagnostic Patient
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Receive Diagnostic Patient
- **Dependency:** UC scope: UC40 Receive Diagnostic Patient; Prerequisite UCs: UC39 Review and Confirm Diagnostic Order; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Receive Diagnostic Patient. Xác nhận Patient đã đến service point diagnostic và sẵn sàng vào queue.
- **Initial status:** Backlog

### M3-021 — [M3][FE] Manage Diagnostic Queue
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Manage Diagnostic Queue
- **Dependency:** UC scope: UC41 Manage Diagnostic Queue; Prerequisite UCs: UC40 Receive Diagnostic Patient; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Manage Diagnostic Queue. Hiển thị và quản lý Queue Entries cho diagnostic service point.
- **Initial status:** Backlog

### M3-022 — [M3][BE] Manage Diagnostic Queue
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Manage Diagnostic Queue
- **Dependency:** UC scope: UC41 Manage Diagnostic Queue; Prerequisite UCs: UC40 Receive Diagnostic Patient; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Manage Diagnostic Queue. Hiển thị và quản lý Queue Entries cho diagnostic service point.
- **Initial status:** Backlog

### M3-023 — [M3][FE] Update Diagnostic Service Status
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Update Diagnostic Service Status
- **Dependency:** UC scope: UC42 Update Service Execution Status; Prerequisite UCs: UC41 Manage Diagnostic Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Diagnostic Service Status. Cập nhật trạng thái thực hiện dịch vụ diagnostic từ chờ đến đang thực hiện và hoàn tất.
- **Initial status:** Backlog

### M3-024 — [M3][BE] Update Diagnostic Service Status
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Update Diagnostic Service Status
- **Dependency:** UC scope: UC42 Update Service Execution Status; Prerequisite UCs: UC41 Manage Diagnostic Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Diagnostic Service Status. Cập nhật trạng thái thực hiện dịch vụ diagnostic từ chờ đến đang thực hiện và hoàn tất.
- **Initial status:** Backlog

### M3-025 — [M3][FE] Enter Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Enter Diagnostic Result
- **Dependency:** UC scope: UC43 Update Diagnostic Result; Prerequisite UCs: UC42 Update Service Execution Status; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Enter Diagnostic Result. Cho phép Diagnostic Services Staff nhập kết quả và kết luận cho Diagnostic Order đã thực hiện.
- **Initial status:** Backlog

### M3-026 — [M3][BE] Enter Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Enter Diagnostic Result
- **Dependency:** UC scope: UC43 Update Diagnostic Result; Prerequisite UCs: UC42 Update Service Execution Status; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Enter Diagnostic Result. Cho phép Diagnostic Services Staff nhập kết quả và kết luận cho Diagnostic Order đã thực hiện.
- **Initial status:** Backlog

### M3-027 — [M3][FE] Update Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** Update Diagnostic Result
- **Dependency:** UC scope: UC43 Update Diagnostic Result; Prerequisite UCs: UC42 Update Service Execution Status; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Diagnostic Result. Cho phép cập nhật kết quả khi còn ở trạng thái cho phép chỉnh sửa và lưu lịch sử thay đổi cần thiết.
- **Initial status:** Backlog

### M3-028 — [M3][BE] Update Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** Update Diagnostic Result
- **Dependency:** UC scope: UC43 Update Diagnostic Result; Prerequisite UCs: UC42 Update Service Execution Status; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Diagnostic Result. Cho phép cập nhật kết quả khi còn ở trạng thái cho phép chỉnh sửa và lưu lịch sử thay đổi cần thiết.
- **Initial status:** Backlog

### M3-029 — [M3][FE] View Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Result
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Result. Hiển thị kết quả diagnostic hoàn chỉnh và tài liệu/viewer liên quan cho staff được phép.
- **Initial status:** Backlog

### M3-030 — [M3][BE] View Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Diagnostic Management
- **Function/trace:** View Diagnostic Result
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Result. Hiển thị kết quả diagnostic hoàn chỉnh và tài liệu/viewer liên quan cho staff được phép.
- **Initial status:** Backlog

### M3-031 — [M3][FE] Create Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Create Prescription
- **Dependency:** UC scope: UC37 Create Prescription; Prerequisite UCs: UC36 Record Diagnosis; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Prescription. Cho phép Doctor tạo Prescription gắn với Visit và hướng dẫn sử dụng thuốc.
- **Initial status:** Backlog

### M3-032 — [M3][BE] Create Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Create Prescription
- **Dependency:** UC scope: UC37 Create Prescription; Prerequisite UCs: UC36 Record Diagnosis; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Prescription. Cho phép Doctor tạo Prescription gắn với Visit và hướng dẫn sử dụng thuốc.
- **Initial status:** Backlog

### M3-033 — [M3][FE] Search Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Search Prescription
- **Dependency:** UC scope: UC44 Search Prescription; Prerequisite UCs: UC37 Create Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Search Prescription. Cho phép Pharmacy Staff tìm và mở Prescription hợp lệ cần chuẩn bị/dispense.
- **Initial status:** Backlog

### M3-034 — [M3][BE] Search Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Search Prescription
- **Dependency:** UC scope: UC44 Search Prescription; Prerequisite UCs: UC37 Create Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Search Prescription. Cho phép Pharmacy Staff tìm và mở Prescription hợp lệ cần chuẩn bị/dispense.
- **Initial status:** Backlog

### M3-035 — [M3][FE] Prepare Medicines
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Prepare Medicines
- **Dependency:** UC scope: UC45 Prepare Medicines According to Prescription; Prerequisite UCs: UC44 Search Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Prepare Medicines. Hiển thị thuốc kê, số lượng, tồn kho khả dụng và hỗ trợ chuẩn bị thuốc theo Prescription.
- **Initial status:** Backlog

### M3-036 — [M3][BE] Prepare Medicines
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Prepare Medicines
- **Dependency:** UC scope: UC45 Prepare Medicines According to Prescription; Prerequisite UCs: UC44 Search Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Prepare Medicines. Hiển thị thuốc kê, số lượng, tồn kho khả dụng và hỗ trợ chuẩn bị thuốc theo Prescription.
- **Initial status:** Backlog

### M3-037 — [M3][FE] Select Medicine Batch (FEFO)
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Select Medicine Batch (FEFO)
- **Dependency:** UC scope: UC45 Prepare Medicines According to Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Select Medicine Batch (FEFO). Chọn batch hợp lệ ưu tiên FEFO; không cho phép dùng batch hết hạn hoặc bị blocked.
- **Initial status:** Backlog

### M3-038 — [M3][BE] Select Medicine Batch (FEFO)
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Select Medicine Batch (FEFO)
- **Dependency:** UC scope: UC45 Prepare Medicines According to Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Select Medicine Batch (FEFO). Chọn batch hợp lệ ưu tiên FEFO; không cho phép dùng batch hết hạn hoặc bị blocked.
- **Initial status:** Backlog

### M3-039 — [M3][FE] Confirm Medicine Dispensing
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Confirm Medicine Dispensing
- **Dependency:** UC scope: UC46 Confirm Medicine Dispensing; Prerequisite UCs: UC45 Prepare Medicines According to Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Confirm Medicine Dispensing. Xác nhận lượng thuốc thực tế bàn giao, cập nhật tồn kho và đánh dấu Prescription/Dispense phù hợp.
- **Initial status:** Backlog

### M3-040 — [M3][BE] Confirm Medicine Dispensing
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** Confirm Medicine Dispensing
- **Dependency:** UC scope: UC46 Confirm Medicine Dispensing; Prerequisite UCs: UC45 Prepare Medicines According to Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Confirm Medicine Dispensing. Xác nhận lượng thuốc thực tế bàn giao, cập nhật tồn kho và đánh dấu Prescription/Dispense phù hợp.
- **Initial status:** Backlog

### M3-041 — [M3][FE] View Dispensing Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Prescription & Pharmacy
- **Function/trace:** View Dispensing Result
- **Dependency:** UC scope: UC46 Confirm Medicine Dispensing; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Dispensing Result. Hiển thị kết quả bàn giao thuốc và thông tin tham chiếu sau khi dispense thành công.
- **Initial status:** Backlog

### M3-042 — [M3][BE] View Dispensing Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Prescription & Pharmacy
- **Function/trace:** View Dispensing Result
- **Dependency:** UC scope: UC46 Confirm Medicine Dispensing; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Dispensing Result. Hiển thị kết quả bàn giao thuốc và thông tin tham chiếu sau khi dispense thành công.
- **Initial status:** Backlog

### M3-043 — [M3][FE] View Medical Record List
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Patient Records
- **Function/trace:** View Medical Record List
- **Dependency:** UC scope: UC19 View Medical Record; Prerequisite UCs: UC38 Complete Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Medical Record List. Hiển thị các Visit/Medical Record mà Patient được phép truy cập.
- **Initial status:** Backlog

### M3-044 — [M3][BE] View Medical Record List
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Patient Records
- **Function/trace:** View Medical Record List
- **Dependency:** UC scope: UC19 View Medical Record; Prerequisite UCs: UC38 Complete Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Medical Record List. Hiển thị các Visit/Medical Record mà Patient được phép truy cập.
- **Initial status:** Backlog

### M3-045 — [M3][FE] View Medical Record Detail
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Patient Records
- **Function/trace:** View Medical Record Detail
- **Dependency:** UC scope: UC19 View Medical Record; Prerequisite UCs: UC38 Complete Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Medical Record Detail. Hiển thị thông tin khám, chẩn đoán, kết luận và dữ liệu lâm sàng được phép sau Visit.
- **Initial status:** Backlog

### M3-046 — [M3][BE] View Medical Record Detail
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Patient Records
- **Function/trace:** View Medical Record Detail
- **Dependency:** UC scope: UC19 View Medical Record; Prerequisite UCs: UC38 Complete Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Medical Record Detail. Hiển thị thông tin khám, chẩn đoán, kết luận và dữ liệu lâm sàng được phép sau Visit.
- **Initial status:** Backlog

### M3-047 — [M3][FE] View Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Patient Records
- **Function/trace:** View Diagnostic Result
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Result. Cho phép Patient xem kết quả diagnostic và tài liệu/viewer link được phép công bố.
- **Initial status:** Backlog

### M3-048 — [M3][BE] View Diagnostic Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Patient Records
- **Function/trace:** View Diagnostic Result
- **Dependency:** UC scope: UC35 View Diagnostic Result; Prerequisite UCs: UC43 Update Diagnostic Result; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Result. Cho phép Patient xem kết quả diagnostic và tài liệu/viewer link được phép công bố.
- **Initial status:** Backlog

### M3-049 — [M3][FE] View Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Patient Records
- **Function/trace:** View Prescription
- **Dependency:** UC scope: UC21 View Prescription; Prerequisite UCs: UC37 Create Prescription; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Prescription. Cho phép Patient xem thuốc đã được kê và hướng dẫn sử dụng.
- **Initial status:** Backlog

### M3-050 — [M3][BE] View Prescription
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Patient Records
- **Function/trace:** View Prescription
- **Dependency:** UC scope: UC21 View Prescription; Prerequisite UCs: UC37 Create Prescription; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Prescription. Cho phép Patient xem thuốc đã được kê và hướng dẫn sử dụng.
- **Initial status:** Backlog

### M3-051 — [M3][FE] View Charges & Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Payment
- **Function/trace:** View Charges & Payment
- **Dependency:** UC scope: UC18 Pay by QR; Prerequisite UCs: UC24 Check-in Patient with Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Charges & Payment. Hiển thị các khoản phí đủ điều kiện thanh toán, tổng tiền và trạng thái payment.
- **Initial status:** Backlog

### M3-052 — [M3][BE] View Charges & Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Payment
- **Function/trace:** View Charges & Payment
- **Dependency:** UC scope: UC18 Pay by QR; Prerequisite UCs: UC24 Check-in Patient with Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Charges & Payment. Hiển thị các khoản phí đủ điều kiện thanh toán, tổng tiền và trạng thái payment.
- **Initial status:** Backlog

### M3-053 — [M3][FE] Generate QR Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Payment
- **Function/trace:** Generate QR Payment
- **Dependency:** UC scope: UC18 Pay by QR; Prerequisite UCs: UC12 Book Appointment; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Generate QR Payment. Tạo/hiển thị QR payment request và hướng dẫn thanh toán cho Patient.
- **Initial status:** Backlog

### M3-054 — [M3][BE] Generate QR Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Payment
- **Function/trace:** Generate QR Payment
- **Dependency:** UC scope: UC18 Pay by QR; Prerequisite UCs: UC12 Book Appointment; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Generate QR Payment. Tạo/hiển thị QR payment request và hướng dẫn thanh toán cho Patient.
- **Initial status:** Backlog

### M3-055 — [M3][FE] Track Payment Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Payment
- **Function/trace:** Track Payment Result
- **Dependency:** UC scope: UC18 Pay by QR; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Track Payment Result. Hiển thị kết quả payment và xử lý các trạng thái pending, paid, failed, expired hoặc cancelled.
- **Initial status:** Backlog

### M3-056 — [M3][BE] Track Payment Result
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Payment
- **Function/trace:** Track Payment Result
- **Dependency:** UC scope: UC18 Pay by QR; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Track Payment Result. Hiển thị kết quả payment và xử lý các trạng thái pending, paid, failed, expired hoặc cancelled.
- **Initial status:** Backlog

### M1-053 — [M1][FE] View Department List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Department List
- **Dependency:** UC scope: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Department List. Hiển thị danh sách Department để phục vụ cấu hình và các lựa chọn vận hành.
- **Initial status:** Ready

### M1-054 — [M1][BE] View Department List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Department List
- **Dependency:** UC scope: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Department List. Hiển thị danh sách Department để phục vụ cấu hình và các lựa chọn vận hành.
- **Initial status:** Ready

### M1-055 — [M1][FE] Create Department
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Create Department
- **Dependency:** UC scope: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Department. Cho phép Hospital Admin tạo Department mới.
- **Initial status:** Ready

### M1-056 — [M1][BE] Create Department
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Create Department
- **Dependency:** UC scope: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Department. Cho phép Hospital Admin tạo Department mới.
- **Initial status:** Ready

### M1-057 — [M1][FE] Update Department
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Department
- **Dependency:** UC scope: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Department. Cho phép Hospital Admin cập nhật Department.
- **Initial status:** Ready

### M1-058 — [M1][BE] Update Department
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Department
- **Dependency:** UC scope: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Department. Cho phép Hospital Admin cập nhật Department.
- **Initial status:** Ready

### M2-057 — [M2][FE] Deactivate Department
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Department
- **Dependency:** UC scope: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Department. Cho phép vô hiệu hóa Department khi không vi phạm dependency đang hoạt động.
- **Initial status:** Backlog

### M2-058 — [M2][BE] Deactivate Department
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Department
- **Dependency:** UC scope: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Department. Cho phép vô hiệu hóa Department khi không vi phạm dependency đang hoạt động.
- **Initial status:** Backlog

### M1-059 — [M1][FE] View Examination Room List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Examination Room List
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Examination Room List. Hiển thị examination rooms và trạng thái khả dụng.
- **Initial status:** Ready

### M1-060 — [M1][BE] View Examination Room List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Examination Room List
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Examination Room List. Hiển thị examination rooms và trạng thái khả dụng.
- **Initial status:** Ready

### M1-061 — [M1][FE] Create Examination Room
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Create Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Examination Room. Cho phép Hospital Admin tạo examination room.
- **Initial status:** Ready

### M1-062 — [M1][BE] Create Examination Room
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Create Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Examination Room. Cho phép Hospital Admin tạo examination room.
- **Initial status:** Ready

### M1-063 — [M1][FE] Update Examination Room
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Examination Room. Cho phép Hospital Admin cập nhật room information/capacity/availability.
- **Initial status:** Ready

### M1-064 — [M1][BE] Update Examination Room
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Examination Room. Cho phép Hospital Admin cập nhật room information/capacity/availability.
- **Initial status:** Ready

### M2-059 — [M2][FE] Deactivate Examination Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Examination Room. Cho phép Hospital Admin vô hiệu hóa room khi không còn dependency đang hoạt động.
- **Initial status:** Backlog

### M2-060 — [M2][BE] Deactivate Examination Room
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Examination Room
- **Dependency:** UC scope: UC48 Manage Examination Room; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Examination Room. Cho phép Hospital Admin vô hiệu hóa room khi không còn dependency đang hoạt động.
- **Initial status:** Backlog

### M1-065 — [M1][FE] View Doctor List (Admin)
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Doctor List (Admin)
- **Dependency:** UC scope: UC49 Manage Doctor; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Doctor List (Admin). Hiển thị Doctors và trạng thái vận hành để quản trị.
- **Initial status:** Ready

### M1-066 — [M1][BE] View Doctor List (Admin)
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Doctor List (Admin)
- **Dependency:** UC scope: UC49 Manage Doctor; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Doctor List (Admin). Hiển thị Doctors và trạng thái vận hành để quản trị.
- **Initial status:** Ready

### M1-067 — [M1][FE] Create Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Create Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; Prerequisite UCs: UC47 Manage Department; UC57 Manage Staff Accounts; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Doctor. Cho phép Hospital Admin tạo Doctor profile dựa trên Staff hợp lệ.
- **Initial status:** Ready

### M1-068 — [M1][BE] Create Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Create Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; Prerequisite UCs: UC47 Manage Department; UC57 Manage Staff Accounts; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Doctor. Cho phép Hospital Admin tạo Doctor profile dựa trên Staff hợp lệ.
- **Initial status:** Ready

### M1-069 — [M1][FE] Update Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Doctor. Cho phép Hospital Admin cập nhật Doctor specialty/department/title và trạng thái.
- **Initial status:** Ready

### M1-070 — [M1][BE] Update Doctor
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Doctor. Cho phép Hospital Admin cập nhật Doctor specialty/department/title và trạng thái.
- **Initial status:** Ready

### M2-061 — [M2][FE] Deactivate Doctor
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Doctor. Cho phép vô hiệu hóa Doctor sau khi kiểm tra lịch/Visit phụ thuộc.
- **Initial status:** Backlog

### M2-062 — [M2][BE] Deactivate Doctor
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Doctor
- **Dependency:** UC scope: UC49 Manage Doctor; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Doctor. Cho phép vô hiệu hóa Doctor sau khi kiểm tra lịch/Visit phụ thuộc.
- **Initial status:** Backlog

### M1-071 — [M1][FE] View Examination Service List (Admin)
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Examination Service List (Admin)
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Examination Service List (Admin). Hiển thị examination services dùng cho booking và vận hành.
- **Initial status:** Ready

### M1-072 — [M1][BE] View Examination Service List (Admin)
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Examination Service List (Admin)
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Examination Service List (Admin). Hiển thị examination services dùng cho booking và vận hành.
- **Initial status:** Ready

### M1-073 — [M1][FE] Create Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Create Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Examination Service. Cho phép Hospital Admin tạo examination service.
- **Initial status:** Ready

### M1-074 — [M1][BE] Create Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Create Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Examination Service. Cho phép Hospital Admin tạo examination service.
- **Initial status:** Ready

### M1-075 — [M1][FE] Update Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Examination Service. Cho phép Hospital Admin cập nhật thông tin service và online-booking availability.
- **Initial status:** Ready

### M1-076 — [M1][BE] Update Examination Service
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Examination Service. Cho phép Hospital Admin cập nhật thông tin service và online-booking availability.
- **Initial status:** Ready

### M2-063 — [M2][FE] Deactivate Examination Service
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Examination Service. Cho phép vô hiệu hóa service sau khi xử lý dependency.
- **Initial status:** Backlog

### M2-064 — [M2][BE] Deactivate Examination Service
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Examination Service
- **Dependency:** UC scope: UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Examination Service. Cho phép vô hiệu hóa service sau khi xử lý dependency.
- **Initial status:** Backlog

### M3-057 — [M3][FE] View Diagnostic Service List
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Diagnostic Service List
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Diagnostic Service List. Hiển thị diagnostic services cho Hospital Admin.
- **Initial status:** Backlog

### M3-058 — [M3][BE] View Diagnostic Service List
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Diagnostic Service List
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Diagnostic Service List. Hiển thị diagnostic services cho Hospital Admin.
- **Initial status:** Backlog

### M3-059 — [M3][FE] Create Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Create Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Diagnostic Service. Cho phép Hospital Admin tạo diagnostic service.
- **Initial status:** Backlog

### M3-060 — [M3][BE] Create Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Create Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Diagnostic Service. Cho phép Hospital Admin tạo diagnostic service.
- **Initial status:** Backlog

### M3-061 — [M3][FE] Update Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Diagnostic Service. Cho phép Hospital Admin cập nhật diagnostic service.
- **Initial status:** Backlog

### M3-062 — [M3][BE] Update Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Diagnostic Service. Cho phép Hospital Admin cập nhật diagnostic service.
- **Initial status:** Backlog

### M3-063 — [M3][FE] Deactivate Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Diagnostic Service. Cho phép vô hiệu hóa diagnostic service khi không còn dependency đang hoạt động.
- **Initial status:** Backlog

### M3-064 — [M3][BE] Deactivate Diagnostic Service
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Diagnostic Service
- **Dependency:** UC scope: UC51 Manage Diagnostic Service; Prerequisite UCs: UC47 Manage Department; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Diagnostic Service. Cho phép vô hiệu hóa diagnostic service khi không còn dependency đang hoạt động.
- **Initial status:** Backlog

### M3-065 — [M3][FE] View Service Pricing
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** View Service Pricing
- **Dependency:** UC scope: UC52 Manage Service Pricing; Prerequisite UCs: UC50 Manage Examination Service; UC51 Manage Diagnostic Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Service Pricing. Hiển thị mức giá hiện hành của các service.
- **Initial status:** Backlog

### M3-066 — [M3][BE] View Service Pricing
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** View Service Pricing
- **Dependency:** UC scope: UC52 Manage Service Pricing; Prerequisite UCs: UC50 Manage Examination Service; UC51 Manage Diagnostic Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Service Pricing. Hiển thị mức giá hiện hành của các service.
- **Initial status:** Backlog

### M3-067 — [M3][FE] Update Service Price
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Hospital Configuration
- **Function/trace:** Update Service Price
- **Dependency:** UC scope: UC52 Manage Service Pricing; Prerequisite UCs: UC50 Manage Examination Service; UC51 Manage Diagnostic Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Service Price. Cho phép Hospital Admin cập nhật giá service theo chính sách và lưu lịch sử thay đổi.
- **Initial status:** Backlog

### M3-068 — [M3][BE] Update Service Price
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Hospital Configuration
- **Function/trace:** Update Service Price
- **Dependency:** UC scope: UC52 Manage Service Pricing; Prerequisite UCs: UC50 Manage Examination Service; UC51 Manage Diagnostic Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Service Price. Cho phép Hospital Admin cập nhật giá service theo chính sách và lưu lịch sử thay đổi.
- **Initial status:** Backlog

### M1-077 — [M1][FE] View Examination Slot List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** View Examination Slot List
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Examination Slot List. Hiển thị schedules/slots, capacity và trạng thái hoạt động.
- **Initial status:** Ready

### M1-078 — [M1][BE] View Examination Slot List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** View Examination Slot List
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Examination Slot List. Hiển thị schedules/slots, capacity và trạng thái hoạt động.
- **Initial status:** Ready

### M1-079 — [M1][FE] Create Examination Slot
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** Create Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Examination Slot. Cho phép Hospital Admin tạo doctor-based hoặc service-based schedule/slot.
- **Initial status:** Ready

### M1-080 — [M1][BE] Create Examination Slot
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** Create Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Examination Slot. Cho phép Hospital Admin tạo doctor-based hoặc service-based schedule/slot.
- **Initial status:** Ready

### M1-081 — [M1][FE] Update Examination Slot
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** Update Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Examination Slot. Cho phép cập nhật slot/capacity khi không làm sai các booking đã tồn tại.
- **Initial status:** Ready

### M1-082 — [M1][BE] Update Examination Slot
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** Update Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Examination Slot. Cho phép cập nhật slot/capacity khi không làm sai các booking đã tồn tại.
- **Initial status:** Ready

### M2-065 — [M2][FE] Close Examination Slot
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** Close Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Close Examination Slot. Đóng slot không còn khả dụng và xử lý cảnh báo với booking phụ thuộc.
- **Initial status:** Backlog

### M2-066 — [M2][BE] Close Examination Slot
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** Close Examination Slot
- **Dependency:** UC scope: UC53 Manage Examination Slots; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Close Examination Slot. Đóng slot không còn khả dụng và xử lý cảnh báo với booking phụ thuộc.
- **Initial status:** Backlog

### M1-083 — [M1][FE] Configure Appointment Policy
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** Configure Appointment Policy
- **Dependency:** UC scope: UC54 Configure Appointment Policy; Prerequisite UCs: UC53 Manage Examination Slots; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Configure Appointment Policy. Cấu hình các giới hạn đặt, đổi, hủy, no-show và time window liên quan Appointment.
- **Initial status:** Ready

### M1-084 — [M1][BE] Configure Appointment Policy
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** Configure Appointment Policy
- **Dependency:** UC scope: UC54 Configure Appointment Policy; Prerequisite UCs: UC53 Manage Examination Slots; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Configure Appointment Policy. Cấu hình các giới hạn đặt, đổi, hủy, no-show và time window liên quan Appointment.
- **Initial status:** Ready

### M2-067 — [M2][FE] Configure Queue Policy
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** Configure Queue Policy
- **Dependency:** UC scope: UC55 Configure Queue Policy; Prerequisite UCs: UC28 Manage Examination Queue; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Configure Queue Policy. Cấu hình các rule vận hành queue như appointment protection, late recovery, backfill và return queue.
- **Initial status:** Backlog

### M2-068 — [M2][BE] Configure Queue Policy
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** Configure Queue Policy
- **Dependency:** UC scope: UC55 Configure Queue Policy; Prerequisite UCs: UC28 Manage Examination Queue; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Configure Queue Policy. Cấu hình các rule vận hành queue như appointment protection, late recovery, backfill và return queue.
- **Initial status:** Backlog

### M3-069 — [M3][FE] View Operational Reports
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Scheduling & Operations
- **Function/trace:** View Operational Reports
- **Dependency:** UC scope: UC56 View Operational Report; Prerequisite UCs: UC28 Manage Examination Queue; UC38 Complete Visit; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Operational Reports. Hiển thị patient load, throughput, capacity utilization và revenue information được phép.
- **Initial status:** Backlog

### M3-070 — [M3][BE] View Operational Reports
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Scheduling & Operations
- **Function/trace:** View Operational Reports
- **Dependency:** UC scope: UC56 View Operational Report; Prerequisite UCs: UC28 Manage Examination Queue; UC38 Complete Visit; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Operational Reports. Hiển thị patient load, throughput, capacity utilization và revenue information được phép.
- **Initial status:** Backlog

### M1-085 — [M1][FE] Enforce Role-based Access
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Enforce Role-based Access
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Enforce Role-based Access. Áp dụng quyền truy cập cơ bản theo role cho toàn bộ màn hình/chức năng ngay từ increment đầu.
- **Initial status:** Ready

### M1-086 — [M1][BE] Enforce Role-based Access
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Enforce Role-based Access
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Enforce Role-based Access. Áp dụng quyền truy cập cơ bản theo role cho toàn bộ màn hình/chức năng ngay từ increment đầu.
- **Initial status:** Ready

### M1-087 — [M1][FE] View Staff Account List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** View Staff Account List
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Staff Account List. Hiển thị Staff accounts và trạng thái tài khoản.
- **Initial status:** Ready

### M1-088 — [M1][BE] View Staff Account List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** View Staff Account List
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Staff Account List. Hiển thị Staff accounts và trạng thái tài khoản.
- **Initial status:** Ready

### M1-089 — [M1][FE] Create Staff Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Create Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Staff Account. Cho phép System Admin tạo Staff account.
- **Initial status:** Ready

### M1-090 — [M1][BE] Create Staff Account
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Create Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Staff Account. Cho phép System Admin tạo Staff account.
- **Initial status:** Ready

### M2-069 — [M2][FE] Update Staff Account
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Update Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Staff Account. Cho phép System Admin cập nhật account information/role/status.
- **Initial status:** Backlog

### M2-070 — [M2][BE] Update Staff Account
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Update Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Staff Account. Cho phép System Admin cập nhật account information/role/status.
- **Initial status:** Backlog

### M2-071 — [M2][FE] Deactivate Staff Account
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Deactivate Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Deactivate Staff Account. Cho phép vô hiệu hóa Staff account sau khi kiểm tra dependency vận hành.
- **Initial status:** Backlog

### M2-072 — [M2][BE] Deactivate Staff Account
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Deactivate Staff Account
- **Dependency:** UC scope: UC57 Manage Staff Accounts; Prerequisite UCs: UC58 Manage Roles and Permissions; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Deactivate Staff Account. Cho phép vô hiệu hóa Staff account sau khi kiểm tra dependency vận hành.
- **Initial status:** Backlog

### M1-091 — [M1][FE] View Role List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** View Role List
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Role List. Hiển thị các role được cấu hình trong hệ thống.
- **Initial status:** Ready

### M1-092 — [M1][BE] View Role List
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** View Role List
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Role List. Hiển thị các role được cấu hình trong hệ thống.
- **Initial status:** Ready

### M2-073 — [M2][FE] Create Role
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Create Role
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Create Role. Cho phép System Admin tạo role mới.
- **Initial status:** Backlog

### M2-074 — [M2][BE] Create Role
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Create Role
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Create Role. Cho phép System Admin tạo role mới.
- **Initial status:** Backlog

### M2-075 — [M2][FE] Update Role
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Update Role
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Role. Cho phép System Admin cập nhật role information.
- **Initial status:** Backlog

### M2-076 — [M2][BE] Update Role
- **Milestone:** M2 - Iteration 2
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Update Role
- **Dependency:** UC scope: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Role. Cho phép System Admin cập nhật role information.
- **Initial status:** Backlog

### M1-093 — [M1][FE] Assign Permissions
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Assign Permissions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Assign Permissions. Cho phép System Admin gán/revoke permissions cho role theo ma trận quyền.
- **Initial status:** Ready

### M1-094 — [M1][BE] Assign Permissions
- **Milestone:** M1 - Iteration 1
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Assign Permissions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Assign Permissions. Cho phép System Admin gán/revoke permissions cho role theo ma trận quyền.
- **Initial status:** Ready

### M3-071 — [M3][FE] View Integration Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** View Integration Configuration
- **Dependency:** UC scope: UC59 Manage Integration Configuration; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Integration Configuration. Hiển thị external integrations và trạng thái cấu hình.
- **Initial status:** Backlog

### M3-072 — [M3][BE] View Integration Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** View Integration Configuration
- **Dependency:** UC scope: UC59 Manage Integration Configuration; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Integration Configuration. Hiển thị external integrations và trạng thái cấu hình.
- **Initial status:** Backlog

### M3-073 — [M3][FE] Update Integration Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Update Integration Configuration
- **Dependency:** UC scope: UC59 Manage Integration Configuration; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Update Integration Configuration. Cho phép System Admin cập nhật cấu hình external service trong phạm vi được phép.
- **Initial status:** Backlog

### M3-074 — [M3][BE] Update Integration Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Update Integration Configuration
- **Dependency:** UC scope: UC59 Manage Integration Configuration; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Update Integration Configuration. Cho phép System Admin cập nhật cấu hình external service trong phạm vi được phép.
- **Initial status:** Backlog

### M3-075 — [M3][FE] View Audit Log
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** View Audit Log
- **Dependency:** UC scope: UC60 View Audit Log; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for View Audit Log. Hiển thị hoạt động người dùng/hệ thống phục vụ audit và điều tra.
- **Initial status:** Backlog

### M3-076 — [M3][BE] View Audit Log
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** View Audit Log
- **Dependency:** UC scope: UC60 View Audit Log; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for View Audit Log. Hiển thị hoạt động người dùng/hệ thống phục vụ audit và điều tra.
- **Initial status:** Backlog

### M3-077 — [M3][FE] Search & Filter Audit Log
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** System Administration
- **Function/trace:** Search & Filter Audit Log
- **Dependency:** UC scope: UC60 View Audit Log; Prerequisite UCs: UC02 Login; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Search & Filter Audit Log. Cho phép System Admin tìm/lọc audit events theo tiêu chí nghiệp vụ.
- **Initial status:** Backlog

### M3-078 — [M3][BE] Search & Filter Audit Log
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** System Administration
- **Function/trace:** Search & Filter Audit Log
- **Dependency:** UC scope: UC60 View Audit Log; Prerequisite UCs: UC02 Login; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Search & Filter Audit Log. Cho phép System Admin tìm/lọc audit events theo tiêu chí nghiệp vụ.
- **Initial status:** Backlog

### M3-079 — [M3][FE] Receive Specialty / Service Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** AI Recommendation
- **Function/trace:** Receive Specialty / Service Recommendation
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Receive Specialty / Service Recommendation. Gửi triệu chứng/context đã nhập và hiển thị specialty/service recommendation; không chẩn đoán bệnh.
- **Initial status:** Backlog

### M3-080 — [M3][BE] Receive Specialty / Service Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** AI Recommendation
- **Function/trace:** Receive Specialty / Service Recommendation
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Receive Specialty / Service Recommendation. Gửi triệu chứng/context đã nhập và hiển thị specialty/service recommendation; không chẩn đoán bệnh.
- **Initial status:** Backlog

### M3-081 — [M3][FE] Receive Appointment Schedule Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** AI Recommendation
- **Function/trace:** Receive Appointment Schedule Recommendation
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Receive Appointment Schedule Recommendation. Đề xuất appointment schedules dựa trên availability và preferences, Patient vẫn là người chọn slot cuối cùng.
- **Initial status:** Backlog

### M3-082 — [M3][BE] Receive Appointment Schedule Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** AI Recommendation
- **Function/trace:** Receive Appointment Schedule Recommendation
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Receive Appointment Schedule Recommendation. Đề xuất appointment schedules dựa trên availability và preferences, Patient vẫn là người chọn slot cuối cùng.
- **Initial status:** Backlog

### M3-083 — [M3][FE] Unified Loading / Empty / Error States
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Cross-module Hardening
- **Function/trace:** Unified Loading / Empty / Error States
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Unified Loading / Empty / Error States. Chuẩn hóa trạng thái loading/empty/recoverable error trên toàn hệ thống và đảm bảo không lộ dữ liệu khi lỗi quyền.
- **Initial status:** Backlog

### M3-084 — [M3][BE] Unified Loading / Empty / Error States
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Cross-module Hardening
- **Function/trace:** Unified Loading / Empty / Error States
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Unified Loading / Empty / Error States. Chuẩn hóa trạng thái loading/empty/recoverable error trên toàn hệ thống và đảm bảo không lộ dữ liệu khi lỗi quyền.
- **Initial status:** Backlog

### M3-085 — [M3][FE] Unsaved Change Protection
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Cross-module Hardening
- **Function/trace:** Unsaved Change Protection
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Unsaved Change Protection. Cảnh báo khi rời form có thay đổi chưa lưu để tránh mất dữ liệu.
- **Initial status:** Backlog

### M3-086 — [M3][BE] Unsaved Change Protection
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Cross-module Hardening
- **Function/trace:** Unsaved Change Protection
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Unsaved Change Protection. Cảnh báo khi rời form có thay đổi chưa lưu để tránh mất dữ liệu.
- **Initial status:** Backlog

### M3-087 — [M3][FE] Activity & Audit Completeness
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Cross-module Hardening
- **Function/trace:** Activity & Audit Completeness
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for Activity & Audit Completeness. Bổ sung audit/activity logging cho các thao tác quan trọng và kiểm tra tính truy vết xuyên module.
- **Initial status:** Backlog

### M3-088 — [M3][BE] Activity & Audit Completeness
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Cross-module Hardening
- **Function/trace:** Activity & Audit Completeness
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for Activity & Audit Completeness. Bổ sung audit/activity logging cho các thao tác quan trọng và kiểm tra tính truy vết xuyên module.
- **Initial status:** Backlog

### M3-089 — [M3][FE] End-to-End Regression & UAT Fixes
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** FE
- **Module:** Cross-module Hardening
- **Function/trace:** End-to-End Regression & UAT Fixes
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; FE ↔ corresponding BE API contract
- **Acceptance criteria / DoD:** UI shows correct state; form validates; unauthorized actions hidden; API success/error handled; responsive smoke.
- **Testing approach:** Component/UI testing + API contract mocked + accessibility/basic manual test
- **Scope:** UI, form, client validation, loading/error/empty states, API wiring for End-to-End Regression & UAT Fixes. Chạy regression cho booking → check-in → queue → consultation → diagnostic → pharmacy/payment → record và sửa lỗi UAT.
- **Initial status:** Backlog

### M3-090 — [M3][BE] End-to-End Regression & UAT Fixes
- **Milestone:** M3 - Iteration 3
- **Label:** `type:feature`
- **Workstream:** BE
- **Module:** Cross-module Hardening
- **Function/trace:** End-to-End Regression & UAT Fixes
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; BE ↔ approved SRS/business rules
- **Acceptance criteria / DoD:** API validates input and role/ownership; state transitions and data integrity correct; failure responses consistent.
- **Testing approach:** Unit tests + API integration tests + auth/negative-path tests
- **Scope:** API + business rules, persistence, authorization, validation and error handling for End-to-End Regression & UAT Fixes. Chạy regression cho booking → check-in → queue → consultation → diagnostic → pharmacy/payment → record và sửa lỗi UAT.
- **Initial status:** Backlog

### M1-095 — [M1][TEST][FE UI / Component] Authentication & Role Landing
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Authentication & Role Landing
- **Function/trace:** Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard
- **Dependency:** Covered UCs: UC02 Login; UC03 Register Patient Account; UC04 Logout; UC01 View Homepage; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-096 — [M1][TEST][BE Unit / API] Authentication & Role Landing
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Authentication & Role Landing
- **Function/trace:** Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard
- **Dependency:** Covered UCs: UC02 Login; UC03 Register Patient Account; UC04 Logout; UC01 View Homepage; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-097 — [M1][TEST][FE-BE Integration] Authentication & Role Landing
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Authentication & Role Landing
- **Function/trace:** Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard
- **Dependency:** Covered UCs: UC02 Login; UC03 Register Patient Account; UC04 Logout; UC01 View Homepage; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Login; Register Patient Account; Verify Patient Account; Logout; View Patient Home; View Reception Dashboard; View Doctor Dashboard; View Diagnostic Dashboard; View Pharmacy Dashboard; View Hospital Admin Dashboard; View System Admin Dashboard; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-098 — [M1][TEST][FE UI / Component] Profile Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Profile Management
- **Function/trace:** View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile
- **Dependency:** Covered UCs: UC05 Manage Personal Profile; UC06 Manage Family Profile; Prerequisite UCs: UC02 Login; UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-099 — [M1][TEST][BE Unit / API] Profile Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Profile Management
- **Function/trace:** View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile
- **Dependency:** Covered UCs: UC05 Manage Personal Profile; UC06 Manage Family Profile; Prerequisite UCs: UC02 Login; UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-100 — [M1][TEST][FE-BE Integration] Profile Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Profile Management
- **Function/trace:** View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile
- **Dependency:** Covered UCs: UC05 Manage Personal Profile; UC06 Manage Family Profile; Prerequisite UCs: UC02 Login; UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Personal Profile; Update Personal Profile; View Family Profile List; View Family Profile Detail; Create Family Profile; Update Family Profile; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-077 — [M2][TEST][FE UI / Component] Profile Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Profile Management
- **Function/trace:** Remove Family Profile
- **Dependency:** Covered UCs: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Remove Family Profile; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-078 — [M2][TEST][BE Unit / API] Profile Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Profile Management
- **Function/trace:** Remove Family Profile
- **Dependency:** Covered UCs: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Remove Family Profile; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-079 — [M2][TEST][FE-BE Integration] Profile Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Profile Management
- **Function/trace:** Remove Family Profile
- **Dependency:** Covered UCs: UC06 Manage Family Profile; Prerequisite UCs: UC05 Manage Personal Profile; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Remove Family Profile; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-101 — [M1][TEST][FE UI / Component] Appointment Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Appointment Management
- **Function/trace:** Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail
- **Dependency:** Covered UCs: UC09 Search Doctor; UC10 Search Examination Service; UC11 View Available Appointment Slots; UC12 Book Appointment; UC15 Track Appointment Status; Prerequisite UCs: UC09 Search Doctor; UC10 Search Examination Service; UC53 Manage Examination Slots; UC11 View Available Appointment Slots; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-102 — [M1][TEST][BE Unit / API] Appointment Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Appointment Management
- **Function/trace:** Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail
- **Dependency:** Covered UCs: UC09 Search Doctor; UC10 Search Examination Service; UC11 View Available Appointment Slots; UC12 Book Appointment; UC15 Track Appointment Status; Prerequisite UCs: UC09 Search Doctor; UC10 Search Examination Service; UC53 Manage Examination Slots; UC11 View Available Appointment Slots; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-103 — [M1][TEST][FE-BE Integration] Appointment Management
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Appointment Management
- **Function/trace:** Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail
- **Dependency:** Covered UCs: UC09 Search Doctor; UC10 Search Examination Service; UC11 View Available Appointment Slots; UC12 Book Appointment; UC15 Track Appointment Status; Prerequisite UCs: UC09 Search Doctor; UC10 Search Examination Service; UC53 Manage Examination Slots; UC11 View Available Appointment Slots; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Search Doctor; View Doctor Detail; Search Examination Service; View Examination Service Detail; View Available Appointment Slots; Review Appointment Booking; Book Appointment; View Appointment List; View Appointment Detail; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-080 — [M2][TEST][FE UI / Component] Appointment Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Appointment Management
- **Function/trace:** Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder
- **Dependency:** Covered UCs: UC13 Reschedule Appointment; UC14 Cancel Appointment; UC15 Track Appointment Status; UC17 Receive Notifications and Reminders; Prerequisite UCs: UC12 Book Appointment; UC11 View Available Appointment Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-081 — [M2][TEST][BE Unit / API] Appointment Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Appointment Management
- **Function/trace:** Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder
- **Dependency:** Covered UCs: UC13 Reschedule Appointment; UC14 Cancel Appointment; UC15 Track Appointment Status; UC17 Receive Notifications and Reminders; Prerequisite UCs: UC12 Book Appointment; UC11 View Available Appointment Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-082 — [M2][TEST][FE-BE Integration] Appointment Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Appointment Management
- **Function/trace:** Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder
- **Dependency:** Covered UCs: UC13 Reschedule Appointment; UC14 Cancel Appointment; UC15 Track Appointment Status; UC17 Receive Notifications and Reminders; Prerequisite UCs: UC12 Book Appointment; UC11 View Available Appointment Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Reschedule Appointment; Cancel Appointment; Track Appointment Status; Receive Appointment Reminder; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-083 — [M2][TEST][FE UI / Component] Reception & Check-in
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Reception & Check-in
- **Function/trace:** Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit
- **Dependency:** Covered UCs: UC22 Search Patient; UC23 Manage Patient Profile at Reception; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-084 — [M2][TEST][BE Unit / API] Reception & Check-in
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Reception & Check-in
- **Function/trace:** Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit
- **Dependency:** Covered UCs: UC22 Search Patient; UC23 Manage Patient Profile at Reception; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-085 — [M2][TEST][FE-BE Integration] Reception & Check-in
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Reception & Check-in
- **Function/trace:** Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit
- **Dependency:** Covered UCs: UC22 Search Patient; UC23 Manage Patient Profile at Reception; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; Prerequisite UCs: UC12 Book Appointment; UC22 Search Patient; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Search Patient / Appointment; View Patient Detail; Create Patient Profile at Reception; Update Patient Profile at Reception; Check-in Scheduled Patient; View Check-in Result; Create Walk-in Visit; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-086 — [M2][TEST][FE UI / Component] Visit & Routing
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Visit & Routing
- **Function/trace:** Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket
- **Dependency:** Covered UCs: UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; UC29 Print Examination Ticket; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-087 — [M2][TEST][BE Unit / API] Visit & Routing
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Visit & Routing
- **Function/trace:** Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket
- **Dependency:** Covered UCs: UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; UC29 Print Examination Ticket; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-088 — [M2][TEST][FE-BE Integration] Visit & Routing
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Visit & Routing
- **Function/trace:** Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket
- **Dependency:** Covered UCs: UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; UC29 Print Examination Ticket; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC26 Assign Doctor to Visit; UC27 Assign Examination Room to Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Assign Doctor to Visit; Assign Examination Room; Transfer Patient to Compatible Room; Print Examination Ticket; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-089 — [M2][TEST][FE UI / Component] Queue Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Queue Management
- **Function/trace:** Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry
- **Dependency:** Covered UCs: UC16 Track Examination Queue; UC28 Manage Examination Queue; UC30 View Assigned Queue; UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC30 View Assigned Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-090 — [M2][TEST][BE Unit / API] Queue Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Queue Management
- **Function/trace:** Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry
- **Dependency:** Covered UCs: UC16 Track Examination Queue; UC28 Manage Examination Queue; UC30 View Assigned Queue; UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC30 View Assigned Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-091 — [M2][TEST][FE-BE Integration] Queue Management
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Queue Management
- **Function/trace:** Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry
- **Dependency:** Covered UCs: UC16 Track Examination Queue; UC28 Manage Examination Queue; UC30 View Assigned Queue; UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC24 Check-in Patient with Appointment; UC25 Create Walk-in Visit; UC30 View Assigned Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Track Patient Queue; View Examination Queue; Update Queue Status; View Doctor Assigned Queue; Call Next Eligible Patient; Handle Early Arrival / Safe Backfill; Handle Late Arrival / Missed Turn; Apply Clinical Priority; Cancel Queue Entry; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-091 — [M3][TEST][FE UI / Component] Queue Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Queue Management
- **Function/trace:** Create Return-to-Doctor Queue Entry
- **Dependency:** Covered UCs: UC28 Manage Examination Queue; Prerequisite UCs: UC35 View Diagnostic Result; UC43 Update Diagnostic Result; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Create Return-to-Doctor Queue Entry; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-092 — [M3][TEST][BE Unit / API] Queue Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Queue Management
- **Function/trace:** Create Return-to-Doctor Queue Entry
- **Dependency:** Covered UCs: UC28 Manage Examination Queue; Prerequisite UCs: UC35 View Diagnostic Result; UC43 Update Diagnostic Result; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Create Return-to-Doctor Queue Entry; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-093 — [M3][TEST][FE-BE Integration] Queue Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Queue Management
- **Function/trace:** Create Return-to-Doctor Queue Entry
- **Dependency:** Covered UCs: UC28 Manage Examination Queue; Prerequisite UCs: UC35 View Diagnostic Result; UC43 Update Diagnostic Result; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Create Return-to-Doctor Queue Entry; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-092 — [M2][TEST][FE UI / Component] Consultation
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Consultation
- **Function/trace:** Open Consultation Workspace; View Medical History; Record Clinical Examination
- **Dependency:** Covered UCs: UC31 View Patient Examination; UC32 View Medical History; UC33 Record Clinical Examination; Prerequisite UCs: UC30 View Assigned Queue; UC31 View Patient Examination; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Open Consultation Workspace; View Medical History; Record Clinical Examination; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-093 — [M2][TEST][BE Unit / API] Consultation
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Consultation
- **Function/trace:** Open Consultation Workspace; View Medical History; Record Clinical Examination
- **Dependency:** Covered UCs: UC31 View Patient Examination; UC32 View Medical History; UC33 Record Clinical Examination; Prerequisite UCs: UC30 View Assigned Queue; UC31 View Patient Examination; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Open Consultation Workspace; View Medical History; Record Clinical Examination; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-094 — [M2][TEST][FE-BE Integration] Consultation
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Consultation
- **Function/trace:** Open Consultation Workspace; View Medical History; Record Clinical Examination
- **Dependency:** Covered UCs: UC31 View Patient Examination; UC32 View Medical History; UC33 Record Clinical Examination; Prerequisite UCs: UC30 View Assigned Queue; UC31 View Patient Examination; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Open Consultation Workspace; View Medical History; Record Clinical Examination; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-094 — [M3][TEST][FE UI / Component] Consultation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Consultation
- **Function/trace:** Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit
- **Dependency:** Covered UCs: UC36 Record Diagnosis; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC38 Complete Visit; Prerequisite UCs: UC33 Record Clinical Examination; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC43 Update Diagnostic Result; UC36 Record Diagnosis; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-095 — [M3][TEST][BE Unit / API] Consultation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Consultation
- **Function/trace:** Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit
- **Dependency:** Covered UCs: UC36 Record Diagnosis; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC38 Complete Visit; Prerequisite UCs: UC33 Record Clinical Examination; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC43 Update Diagnostic Result; UC36 Record Diagnosis; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-096 — [M3][TEST][FE-BE Integration] Consultation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Consultation
- **Function/trace:** Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit
- **Dependency:** Covered UCs: UC36 Record Diagnosis; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC38 Complete Visit; Prerequisite UCs: UC33 Record Clinical Examination; UC35 View Diagnostic Result; UC34 Create Diagnostic Order; UC43 Update Diagnostic Result; UC36 Record Diagnosis; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Record Diagnosis & Clinical Conclusion; Review Diagnostic Results; Create Additional Diagnostic Order; Complete Visit; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-097 — [M3][TEST][FE UI / Component] Diagnostic Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Diagnostic Management
- **Function/trace:** Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result
- **Dependency:** Covered UCs: UC34 Create Diagnostic Order; UC35 View Diagnostic Result; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; UC43 Update Diagnostic Result; Prerequisite UCs: UC33 Record Clinical Examination; UC43 Update Diagnostic Result; UC34 Create Diagnostic Order; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-098 — [M3][TEST][BE Unit / API] Diagnostic Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Diagnostic Management
- **Function/trace:** Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result
- **Dependency:** Covered UCs: UC34 Create Diagnostic Order; UC35 View Diagnostic Result; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; UC43 Update Diagnostic Result; Prerequisite UCs: UC33 Record Clinical Examination; UC43 Update Diagnostic Result; UC34 Create Diagnostic Order; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-099 — [M3][TEST][FE-BE Integration] Diagnostic Management
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Diagnostic Management
- **Function/trace:** Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result
- **Dependency:** Covered UCs: UC34 Create Diagnostic Order; UC35 View Diagnostic Result; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; UC43 Update Diagnostic Result; Prerequisite UCs: UC33 Record Clinical Examination; UC43 Update Diagnostic Result; UC34 Create Diagnostic Order; UC39 Review and Confirm Diagnostic Order; UC40 Receive Diagnostic Patient; UC41 Manage Diagnostic Queue; UC42 Update Service Execution Status; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Create Diagnostic Order; View Diagnostic Results for Doctor; View Diagnostic Worklist; Review & Confirm Diagnostic Order; Receive Diagnostic Patient; Manage Diagnostic Queue; Update Diagnostic Service Status; Enter Diagnostic Result; Update Diagnostic Result; View Diagnostic Result; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-100 — [M3][TEST][FE UI / Component] Prescription & Pharmacy
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Prescription & Pharmacy
- **Function/trace:** Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result
- **Dependency:** Covered UCs: UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; UC46 Confirm Medicine Dispensing; Prerequisite UCs: UC36 Record Diagnosis; UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-101 — [M3][TEST][BE Unit / API] Prescription & Pharmacy
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Prescription & Pharmacy
- **Function/trace:** Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result
- **Dependency:** Covered UCs: UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; UC46 Confirm Medicine Dispensing; Prerequisite UCs: UC36 Record Diagnosis; UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-102 — [M3][TEST][FE-BE Integration] Prescription & Pharmacy
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Prescription & Pharmacy
- **Function/trace:** Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result
- **Dependency:** Covered UCs: UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; UC46 Confirm Medicine Dispensing; Prerequisite UCs: UC36 Record Diagnosis; UC37 Create Prescription; UC44 Search Prescription; UC45 Prepare Medicines According to Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Create Prescription; Search Prescription; Prepare Medicines; Select Medicine Batch (FEFO); Confirm Medicine Dispensing; View Dispensing Result; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-103 — [M3][TEST][FE UI / Component] Patient Records
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Patient Records
- **Function/trace:** View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription
- **Dependency:** Covered UCs: UC19 View Medical Record; UC35 View Diagnostic Result; UC21 View Prescription; Prerequisite UCs: UC38 Complete Visit; UC43 Update Diagnostic Result; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-104 — [M3][TEST][BE Unit / API] Patient Records
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Patient Records
- **Function/trace:** View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription
- **Dependency:** Covered UCs: UC19 View Medical Record; UC35 View Diagnostic Result; UC21 View Prescription; Prerequisite UCs: UC38 Complete Visit; UC43 Update Diagnostic Result; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-105 — [M3][TEST][FE-BE Integration] Patient Records
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Patient Records
- **Function/trace:** View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription
- **Dependency:** Covered UCs: UC19 View Medical Record; UC35 View Diagnostic Result; UC21 View Prescription; Prerequisite UCs: UC38 Complete Visit; UC43 Update Diagnostic Result; UC37 Create Prescription; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Medical Record List; View Medical Record Detail; View Diagnostic Result; View Prescription; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-106 — [M3][TEST][FE UI / Component] Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Payment
- **Function/trace:** View Charges & Payment; Generate QR Payment; Track Payment Result
- **Dependency:** Covered UCs: UC18 Pay by QR; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Charges & Payment; Generate QR Payment; Track Payment Result; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-107 — [M3][TEST][BE Unit / API] Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Payment
- **Function/trace:** View Charges & Payment; Generate QR Payment; Track Payment Result
- **Dependency:** Covered UCs: UC18 Pay by QR; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Charges & Payment; Generate QR Payment; Track Payment Result; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-108 — [M3][TEST][FE-BE Integration] Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Payment
- **Function/trace:** View Charges & Payment; Generate QR Payment; Track Payment Result
- **Dependency:** Covered UCs: UC18 Pay by QR; Prerequisite UCs: UC24 Check-in Patient with Appointment; UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Charges & Payment; Generate QR Payment; Track Payment Result; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-104 — [M1][TEST][FE UI / Component] Hospital Configuration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Hospital Configuration
- **Function/trace:** View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-105 — [M1][TEST][BE Unit / API] Hospital Configuration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Hospital Configuration
- **Function/trace:** View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-106 — [M1][TEST][FE-BE Integration] Hospital Configuration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Hospital Configuration
- **Function/trace:** View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Department List; Create Department; Update Department; View Examination Room List; Create Examination Room; Update Examination Room; View Doctor List (Admin); Create Doctor; Update Doctor; View Examination Service List (Admin); Create Examination Service; Update Examination Service; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-095 — [M2][TEST][FE UI / Component] Hospital Configuration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-096 — [M2][TEST][BE Unit / API] Hospital Configuration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-097 — [M2][TEST][FE-BE Integration] Hospital Configuration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Hospital Configuration
- **Function/trace:** Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service
- **Dependency:** Covered UCs: UC47 Manage Department; UC48 Manage Examination Room; UC49 Manage Doctor; UC50 Manage Examination Service; Prerequisite UCs: UC47 Manage Department; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Deactivate Department; Deactivate Examination Room; Deactivate Doctor; Deactivate Examination Service; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-109 — [M3][TEST][FE UI / Component] Hospital Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Hospital Configuration
- **Function/trace:** View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price
- **Dependency:** Covered UCs: UC51 Manage Diagnostic Service; UC52 Manage Service Pricing; Prerequisite UCs: UC47 Manage Department; UC50 Manage Examination Service; UC51 Manage Diagnostic Service; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-110 — [M3][TEST][BE Unit / API] Hospital Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Hospital Configuration
- **Function/trace:** View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price
- **Dependency:** Covered UCs: UC51 Manage Diagnostic Service; UC52 Manage Service Pricing; Prerequisite UCs: UC47 Manage Department; UC50 Manage Examination Service; UC51 Manage Diagnostic Service; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-111 — [M3][TEST][FE-BE Integration] Hospital Configuration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Hospital Configuration
- **Function/trace:** View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price
- **Dependency:** Covered UCs: UC51 Manage Diagnostic Service; UC52 Manage Service Pricing; Prerequisite UCs: UC47 Manage Department; UC50 Manage Examination Service; UC51 Manage Diagnostic Service; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Diagnostic Service List; Create Diagnostic Service; Update Diagnostic Service; Deactivate Diagnostic Service; View Service Pricing; Update Service Price; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-107 — [M1][TEST][FE UI / Component] Scheduling & Operations
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Scheduling & Operations
- **Function/trace:** View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC54 Configure Appointment Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC53 Manage Examination Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-108 — [M1][TEST][BE Unit / API] Scheduling & Operations
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Scheduling & Operations
- **Function/trace:** View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC54 Configure Appointment Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC53 Manage Examination Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-109 — [M1][TEST][FE-BE Integration] Scheduling & Operations
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Scheduling & Operations
- **Function/trace:** View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC54 Configure Appointment Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC53 Manage Examination Slots; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Examination Slot List; Create Examination Slot; Update Examination Slot; Configure Appointment Policy; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-098 — [M2][TEST][FE UI / Component] Scheduling & Operations
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Scheduling & Operations
- **Function/trace:** Close Examination Slot; Configure Queue Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC55 Configure Queue Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC28 Manage Examination Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Close Examination Slot; Configure Queue Policy; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-099 — [M2][TEST][BE Unit / API] Scheduling & Operations
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Scheduling & Operations
- **Function/trace:** Close Examination Slot; Configure Queue Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC55 Configure Queue Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC28 Manage Examination Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Close Examination Slot; Configure Queue Policy; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-100 — [M2][TEST][FE-BE Integration] Scheduling & Operations
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Scheduling & Operations
- **Function/trace:** Close Examination Slot; Configure Queue Policy
- **Dependency:** Covered UCs: UC53 Manage Examination Slots; UC55 Configure Queue Policy; Prerequisite UCs: UC49 Manage Doctor; UC50 Manage Examination Service; UC28 Manage Examination Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Close Examination Slot; Configure Queue Policy; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-112 — [M3][TEST][FE UI / Component] Scheduling & Operations
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Scheduling & Operations
- **Function/trace:** View Operational Reports
- **Dependency:** Covered UCs: UC56 View Operational Report; Prerequisite UCs: UC28 Manage Examination Queue; UC38 Complete Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Operational Reports; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-113 — [M3][TEST][BE Unit / API] Scheduling & Operations
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Scheduling & Operations
- **Function/trace:** View Operational Reports
- **Dependency:** Covered UCs: UC56 View Operational Report; Prerequisite UCs: UC28 Manage Examination Queue; UC38 Complete Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Operational Reports; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-114 — [M3][TEST][FE-BE Integration] Scheduling & Operations
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Scheduling & Operations
- **Function/trace:** View Operational Reports
- **Dependency:** Covered UCs: UC56 View Operational Report; Prerequisite UCs: UC28 Manage Examination Queue; UC38 Complete Visit; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Operational Reports; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-110 — [M1][TEST][FE UI / Component] System Administration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** System Administration
- **Function/trace:** Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions
- **Dependency:** Covered UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Prerequisite UCs: UC02 Login; UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-111 — [M1][TEST][BE Unit / API] System Administration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** System Administration
- **Function/trace:** Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions
- **Dependency:** Covered UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Prerequisite UCs: UC02 Login; UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-112 — [M1][TEST][FE-BE Integration] System Administration
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** System Administration
- **Function/trace:** Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions
- **Dependency:** Covered UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Prerequisite UCs: UC02 Login; UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Enforce Role-based Access; View Staff Account List; Create Staff Account; View Role List; Assign Permissions; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-101 — [M2][TEST][FE UI / Component] System Administration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** System Administration
- **Function/trace:** Update Staff Account; Deactivate Staff Account; Create Role; Update Role
- **Dependency:** Covered UCs: UC57 Manage Staff Accounts; UC58 Manage Roles and Permissions; Prerequisite UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Update Staff Account; Deactivate Staff Account; Create Role; Update Role; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-102 — [M2][TEST][BE Unit / API] System Administration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** System Administration
- **Function/trace:** Update Staff Account; Deactivate Staff Account; Create Role; Update Role
- **Dependency:** Covered UCs: UC57 Manage Staff Accounts; UC58 Manage Roles and Permissions; Prerequisite UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Update Staff Account; Deactivate Staff Account; Create Role; Update Role; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M2-103 — [M2][TEST][FE-BE Integration] System Administration
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** System Administration
- **Function/trace:** Update Staff Account; Deactivate Staff Account; Create Role; Update Role
- **Dependency:** Covered UCs: UC57 Manage Staff Accounts; UC58 Manage Roles and Permissions; Prerequisite UCs: UC58 Manage Roles and Permissions; UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Update Staff Account; Deactivate Staff Account; Create Role; Update Role; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-115 — [M3][TEST][FE UI / Component] System Administration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** System Administration
- **Function/trace:** View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log
- **Dependency:** Covered UCs: UC59 Manage Integration Configuration; UC60 View Audit Log; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-116 — [M3][TEST][BE Unit / API] System Administration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** System Administration
- **Function/trace:** View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log
- **Dependency:** Covered UCs: UC59 Manage Integration Configuration; UC60 View Audit Log; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-117 — [M3][TEST][FE-BE Integration] System Administration
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** System Administration
- **Function/trace:** View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log
- **Dependency:** Covered UCs: UC59 Manage Integration Configuration; UC60 View Audit Log; Prerequisite UCs: UC02 Login; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: View Integration Configuration; Update Integration Configuration; View Audit Log; Search & Filter Audit Log; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-118 — [M3][TEST][FE UI / Component] AI Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** AI Recommendation
- **Function/trace:** Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-119 — [M3][TEST][BE Unit / API] AI Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** AI Recommendation
- **Function/trace:** Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-120 — [M3][TEST][FE-BE Integration] AI Recommendation
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** AI Recommendation
- **Function/trace:** Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Receive Specialty / Service Recommendation; Receive Appointment Schedule Recommendation; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-121 — [M3][TEST][FE UI / Component] Cross-module Hardening
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE UI / Component
- **Module:** Cross-module Hardening
- **Function/trace:** Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** UI test suite passes for: Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes; screenshots/evidence attached; issues created for failures.
- **Testing approach:** UI validation, view states, navigation, accessible interactions, error and loading paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-122 — [M3][TEST][BE Unit / API] Cross-module Hardening
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** BE Unit / API
- **Module:** Cross-module Hardening
- **Function/trace:** Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** API/unit suite passes for: Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes; coverage and test evidence attached; bugs linked.
- **Testing approach:** Business rules, input boundary, role permission, invalid states, database consistency
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M3-123 — [M3][TEST][FE-BE Integration] Cross-module Hardening
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** FE-BE Integration
- **Module:** Cross-module Hardening
- **Function/trace:** Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Integration flow verified for: Unified Loading / Empty / Error States; Unsaved Change Protection; Activity & Audit Completeness; End-to-End Regression & UAT Fixes; at least one negative scenario per API; run in CI/staging.
- **Testing approach:** Real FE↔BE contract and database integration, permissions, errors and retry/failure paths
- **Scope:** Test execution evidence, failure tracking and retest
- **Initial status:** Backlog

### M1-113 — [M1][TEST] E2E Booking
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Register/Login → role authorization → search doctor/service → available slot → booking → appointment detail; slot capacity/no duplicate booking
- **Dependency:** Covered UCs: UC15 Track Appointment Status; Prerequisite UCs: UC12 Book Appointment; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Cross-role smoke booking; 2 concurrent reservation attempts must not overbook
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M1-114 — [M1][TEST] Security & RBAC
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Patient/Staff/Admin routes; session expiration; cross-account patient data access
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Unauthorized requests denied; no patient data leakage
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M1-115 — [M1][TEST] Regression & Release
- **Milestone:** M1 - Iteration 1
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Re-run all M1 features with seeded demo data, browser smoke, CI
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** M1 release tag after FE, BE, integration, E2E pass
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M1-116 — [M1][DOCS] Test cases + report
- **Milestone:** M1 - Iteration 1
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Create/update Unit, Integration and E2E cases for Iteration 1; record runs, test report, known issues.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Create/update Unit, Integration and E2E cases for Iteration 1; record runs, test report, known issues.
- **Initial status:** Backlog

### M1-117 — [M1][DOCS] Iteration release notes
- **Milestone:** M1 - Iteration 1
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M1.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M1.
- **Initial status:** Backlog

### M2-104 — [M2][TEST] E2E Reception to Consultation
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** M1 appointment → reschedule/cancel where allowed → check-in OR walk-in → Visit → room/doctor → queue → call next → clinical exam
- **Dependency:** Covered UCs: UC31 Call Next Eligible Patient; Prerequisite UCs: UC28 Manage Examination Queue; UC30 View Assigned Queue; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** No duplicate visit; immutable queue number; real-time status coherent
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M2-105 — [M2][TEST] Queue State & Concurrency
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Early/backfill, missed turn, priority, cancellation, cross-room routing
- **Dependency:** Covered UCs: Cross-module / release scenario; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Transitions idempotent; no double-call; eligible patient ordering holds
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M2-106 — [M2][TEST] Regression Iteration 1 + 2
- **Milestone:** M2 - Iteration 2
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Repeat full M1 booking and M2 reception/consultation with integration environment
- **Dependency:** Covered UCs: UC59 Manage Integration Configuration; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** No regressions from M1; demonstrate two full M2 admission routes
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M2-107 — [M2][DOCS] Test cases + report
- **Milestone:** M2 - Iteration 2
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Create/update Unit, Integration and E2E cases for Iteration 2; record runs, test report, known issues.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Create/update Unit, Integration and E2E cases for Iteration 2; record runs, test report, known issues.
- **Initial status:** Backlog

### M2-108 — [M2][DOCS] Iteration release notes
- **Milestone:** M2 - Iteration 2
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M2.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M2.
- **Initial status:** Backlog

### M3-124 — [M3][TEST] E2E Diagnostic Closed Loop
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Doctor exam → diagnostic order → diagnostic queue → result → return-doctor queue → diagnosis → visit completion
- **Dependency:** Covered UCs: UC34 Create Diagnostic Order; Prerequisite UCs: UC33 Record Clinical Examination; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Return queue only when prerequisites ready; no lost results
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M3-125 — [M3][TEST] E2E Pharmacy & Payment
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Doctor prescription → pharmacy search → FEFO batch prepare → dispense → stock transaction; QR pending/success/fail
- **Dependency:** Covered UCs: UC37 Create Prescription; Prerequisite UCs: UC36 Record Diagnosis; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Blocked/expired batches not dispensed, no duplicate payment reference
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M3-126 — [M3][TEST] E2E Patient Records & AI
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Visit complete → patient sees own record/result/prescription; AI recommendation optional fallback
- **Dependency:** Covered UCs: UC37 Create Prescription; Prerequisite UCs: UC36 Record Diagnosis; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** Role/ownership safe; AI failure does not prevent manual booking
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M3-127 — [M3][TEST] Security & Performance
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Cross-role patient clinical access; concurrency on queue/slot; endpoint performance smoke
- **Dependency:** Covered UCs: UC58 Manage Roles and Permissions; Prerequisite UCs: UC57 Manage Staff Accounts; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** No critical authorization issue; no P0/P1 before release
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M3-128 — [M3][TEST] System Regression + UAT
- **Milestone:** M3 - Iteration 3
- **Label:** `type:test`
- **Workstream:** E2E / Quality
- **Module:** Cross-module
- **Function/trace:** Full booking → check-in/walk-in → queue → doctor → diagnostic → return → prescription → payment → records; UAT evidence
- **Dependency:** Covered UCs: UC37 Create Prescription; Prerequisite UCs: UC36 Record Diagnosis; Depends on relevant FE + BE issues and passing earlier-increment regression.
- **Acceptance criteria / DoD:** End-to-end happy + negative paths approved; defects triaged and retested
- **Testing approach:** E2E automation/manual reproducible test script + evidence
- **Scope:** Increment verification; each milestone ends with demonstrable, tested software
- **Initial status:** Backlog

### M3-129 — [M3][DOCS] Test cases + report
- **Milestone:** M3 - Iteration 3
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Create/update Unit, Integration and E2E cases for Iteration 3; record runs, test report, known issues.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Create/update Unit, Integration and E2E cases for Iteration 3; record runs, test report, known issues.
- **Initial status:** Backlog

### M3-130 — [M3][DOCS] Iteration release notes
- **Milestone:** M3 - Iteration 3
- **Label:** `type:docs`
- **Workstream:** Documentation
- **Module:** Quality
- **Function/trace:** All delivered module functions
- **Dependency:** UC scope: Cross-cutting task: no direct UC mapping; Review associated requirement/test artifacts
- **Acceptance criteria / DoD:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M3.
- **Testing approach:** Links to test evidence and PRs
- **Scope:** Document increment delta vs previous iteration, environment setup, demo script, limitations for M3.
- **Initial status:** Backlog

## 7. Workflow / kiểm soát chất lượng

1. **Ready:** issue gắn đúng milestone + label, UC/dependency, scope, acceptance criteria; FE/BE thống nhất API contract.
2. **In Progress:** tạo feature branch, implement và unit tests, cập nhật issue/PR link.
3. **In Review:** PR được review, build/lint/test tự động xanh, bảo mật dữ liệu bệnh nhân.
4. **QA:** UI/API/integration/E2E tương ứng pass; bug mới tách issue `type:bug` có steps, expected/actual, severity và UC liên quan.
5. **Done:** merged, demo được increment, report/test evidence có thể truy vết và không còn P0/blocker.
6. **Requirement change:** tạo `type:change-requirement` mô tả SRS/UC trước-sau, impact FE/BE/Tests, effort, approval rồi mới chỉnh backlog.

## 8. Testing theo milestone

- **M1:** Unit/Component tests cho Auth/Profile/Booking/Slot/RBAC, integration FE↔BE, E2E Register/Login → Doctor/Service → Slot → Booking.
- **M2:** Unit/API/state transition cho Check-in/Walk-in/Visit/Queue/Doctor; integration và E2E Arrival → Check-in/Walk-in → Queue → Clinical Examination; regression M1.
- **M3:** Diagnostic order/result/return-doctor, prescribing/dispensing/payment/records, cross-role permissions, E2E toàn hành trình, smoke security/performance, regression M1+M2, UAT/system test.

## 9. Tài liệu gốc

- `MedFlow_3_Iterations_Dependencies_by_UseCase.xlsx` — source of truth của 355 issue và dependency.
- `MedFlow_Concept_and_UseCases.md` — phạm vi và use case catalogue.
- `Report-2_Project_Management_Plan.docx` — lịch 3 iterations và verification/validation.
- `Report-3_Software_Requirement_Specification.docx` — use cases/business rules.
