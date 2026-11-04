import type {
  RoleItem,
  RoleMetrics,
  RolePermissionItem,
  RoleScope,
  RoleStatus,
} from '../types/role.types';

export const ROLE_SCOPE_LABELS: Record<RoleScope, string> = {
  SYSTEM: 'Quản trị hệ thống',
  CLINICAL: 'Lâm sàng & Cận lâm sàng',
  OPERATIONAL: 'Vận hành & Tiếp đón',
  PORTAL: 'Cổng bệnh nhân',
};

export const ROLE_STATUS_LABELS: Record<RoleStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  INACTIVE: 'Tạm ngừng',
};

export const SYSTEM_PERMISSIONS: Record<string, RolePermissionItem> = {
  PATIENT_PROFILE_READ: {
    code: 'PATIENT_PROFILE_READ',
    name: 'Xem hồ sơ bệnh nhân',
    module: 'Hồ sơ & Tiếp đón',
  },
  APPOINTMENT_BOOK: {
    code: 'APPOINTMENT_BOOK',
    name: 'Đặt lịch khám',
    module: 'Lịch hẹn',
  },
  PATIENT_CHECKIN: {
    code: 'PATIENT_CHECKIN',
    name: 'Tiếp nhận & Check-in',
    module: 'Tiếp đón',
  },
  VISIT_ASSIGN: {
    code: 'VISIT_ASSIGN',
    name: 'Phân buồng & Điều phối khám',
    module: 'Tiếp đón & Hàng đợi',
  },
  QUEUE_MANAGE: {
    code: 'QUEUE_MANAGE',
    name: 'Quản lý hàng đợi khám',
    module: 'Hàng đợi',
  },
  MEDICAL_RECORD_WRITE: {
    code: 'MEDICAL_RECORD_WRITE',
    name: 'Ghi nhận bệnh án lâm sàng',
    module: 'Khám lâm sàng',
  },
  DIAGNOSTIC_ORDER: {
    code: 'DIAGNOSTIC_ORDER',
    name: 'Chỉ định cận lâm sàng',
    module: 'Cận lâm sàng',
  },
  DIAGNOSTIC_RESULT_WRITE: {
    code: 'DIAGNOSTIC_RESULT_WRITE',
    name: 'Cập nhật kết quả CLS',
    module: 'Cận lâm sàng',
  },
  PRESCRIPTION_ISSUE: {
    code: 'PRESCRIPTION_ISSUE',
    name: 'Kê đơn thuốc điện tử',
    module: 'Dược & Đơn thuốc',
  },
  MEDICINE_DISPENSE: {
    code: 'MEDICINE_DISPENSE',
    name: 'Cấp phát thuốc nhà thuốc',
    module: 'Dược & Đơn thuốc',
  },
  HOSPITAL_CONFIG: {
    code: 'HOSPITAL_CONFIG',
    name: 'Cấu hình danh mục bệnh viện',
    module: 'Quản trị cơ sở',
  },
  USER_MANAGE: {
    code: 'USER_MANAGE',
    name: 'Quản lý tài khoản & vai trò',
    module: 'Quản trị hệ thống',
  },
  AUDIT_READ: {
    code: 'AUDIT_READ',
    name: 'Tra cứu nhật ký kiểm toán',
    module: 'An ninh & Kiểm toán',
  },
  PAYMENT_VIEW: {
    code: 'PAYMENT_VIEW',
    name: 'Tra cứu viện phí & thanh toán',
    module: 'Viện phí',
  },
};

const pickPermissions = (codes: string[]): RolePermissionItem[] =>
  codes.map((code) => SYSTEM_PERMISSIONS[code]).filter(Boolean);

export const ROLE_METRICS_MOCK: RoleMetrics = {
  totalRoles: 10,
  activeRoles: 9,
  totalAssignedAccounts: 142,
  totalPermissions: 14,
};

export const ROLES_MOCK_DATA: RoleItem[] = [
  {
    id: 'ROL-01',
    code: 'SYSTEM_ADMIN',
    name: 'Quản trị hệ thống',
    description:
      'Toàn quyền cấu hình kỹ thuật, quản lý tài khoản nhân sự, vai trò RBAC và nhật ký kiểm toán.',
    scope: 'SYSTEM',
    isSystemRole: true,
    assignedUsersCount: 2,
    permissions: pickPermissions(['HOSPITAL_CONFIG', 'USER_MANAGE', 'AUDIT_READ']),
    status: 'ACTIVE',
    updatedAt: '15/03/2026',
    segregationNote: 'Tách biệt với quyền sửa dữ liệu lâm sàng trực tiếp (SoD).',
  },
  {
    id: 'ROL-02',
    code: 'HOSPITAL_ADMIN',
    name: 'Quản trị bệnh viện',
    description:
      'Quản lý danh mục khoa phòng, buồng khám, dịch vụ, slot khám và điều phối vận hành.',
    scope: 'SYSTEM',
    isSystemRole: true,
    assignedUsersCount: 4,
    permissions: pickPermissions([
      'VISIT_ASSIGN',
      'QUEUE_MANAGE',
      'HOSPITAL_CONFIG',
      'PAYMENT_VIEW',
    ]),
    status: 'ACTIVE',
    updatedAt: '14/03/2026',
    segregationNote: 'Không bao gồm quyền cấp phát hoặc thu hồi vai trò SYSTEM_ADMIN.',
  },
  {
    id: 'ROL-03',
    code: 'DOCTOR',
    name: 'Bác sĩ lâm sàng',
    description:
      'Thực hiện khám bệnh, ghi nhận bệnh án điện tử, chỉ định cận lâm sàng và kê đơn thuốc.',
    scope: 'CLINICAL',
    isSystemRole: true,
    assignedUsersCount: 38,
    permissions: pickPermissions([
      'PATIENT_PROFILE_READ',
      'MEDICAL_RECORD_WRITE',
      'DIAGNOSTIC_ORDER',
      'PRESCRIPTION_ISSUE',
      'PAYMENT_VIEW',
    ]),
    status: 'ACTIVE',
    updatedAt: '12/03/2026',
    segregationNote: 'Chỉ truy cập hồ sơ bệnh nhân thuộc lượt khám được phân công.',
  },
  {
    id: 'ROL-04',
    code: 'RECEPTION',
    name: 'Nhân viên tiếp đón',
    description:
      'Tiếp nhận bệnh nhân tại quầy, xác thực check-in, phân buồng khám và điều phối hàng đợi.',
    scope: 'OPERATIONAL',
    isSystemRole: true,
    assignedUsersCount: 14,
    permissions: pickPermissions([
      'PATIENT_PROFILE_READ',
      'PATIENT_CHECKIN',
      'VISIT_ASSIGN',
      'QUEUE_MANAGE',
      'PAYMENT_VIEW',
    ]),
    status: 'ACTIVE',
    updatedAt: '10/03/2026',
    segregationNote: 'Không có quyền xem hoặc chỉnh sửa chẩn đoán lâm sàng chi tiết.',
  },
  {
    id: 'ROL-05',
    code: 'DIAGNOSTIC_STAFF',
    name: 'Nhân viên cận lâm sàng',
    description: 'Tiếp nhận phiếu chỉ định xét nghiệm / chẩn đoán hình ảnh và trả kết quả CLS.',
    scope: 'CLINICAL',
    isSystemRole: true,
    assignedUsersCount: 18,
    permissions: pickPermissions(['QUEUE_MANAGE', 'DIAGNOSTIC_RESULT_WRITE']),
    status: 'ACTIVE',
    updatedAt: '10/03/2026',
    segregationNote: 'Giới hạn trong phạm vi phiếu chỉ định cận lâm sàng được giao.',
  },
  {
    id: 'ROL-06',
    code: 'PHARMACY_STAFF',
    name: 'Nhân viên nhà thuốc',
    description: 'Kiểm duyệt đơn thuốc điện tử, đối chiếu viện phí và thực hiện cấp phát thuốc.',
    scope: 'CLINICAL',
    isSystemRole: true,
    assignedUsersCount: 8,
    permissions: pickPermissions(['MEDICINE_DISPENSE', 'PAYMENT_VIEW']),
    status: 'ACTIVE',
    updatedAt: '09/03/2026',
    segregationNote:
      'Tách biệt giữa quyền kê đơn (DOCTOR) và quyền cấp phát thuốc (PHARMACY_STAFF).',
  },
  {
    id: 'ROL-07',
    code: 'PATIENT',
    name: 'Bệnh nhân',
    description: 'Tra cứu hồ sơ sức khỏe cá nhân, đặt lịch khám trực tuyến và theo dõi viện phí.',
    scope: 'PORTAL',
    isSystemRole: true,
    assignedUsersCount: 42,
    permissions: pickPermissions(['PATIENT_PROFILE_READ', 'APPOINTMENT_BOOK', 'PAYMENT_VIEW']),
    status: 'ACTIVE',
    updatedAt: '08/03/2026',
    segregationNote: 'Chỉ truy cập hồ sơ cá nhân hoặc hồ sơ người thân được ủy quyền.',
  },
  {
    id: 'ROL-08',
    code: 'HEAD_DOCTOR',
    name: 'Trưởng khoa lâm sàng',
    description:
      'Phụ trách chuyên môn khoa, điều phối buồng khám trong khoa và phê duyệt hội chẩn.',
    scope: 'CLINICAL',
    isSystemRole: false,
    assignedUsersCount: 6,
    permissions: pickPermissions([
      'PATIENT_PROFILE_READ',
      'VISIT_ASSIGN',
      'QUEUE_MANAGE',
      'MEDICAL_RECORD_WRITE',
      'DIAGNOSTIC_ORDER',
      'PRESCRIPTION_ISSUE',
    ]),
    status: 'ACTIVE',
    updatedAt: '06/03/2026',
    segregationNote: 'Giới hạn điều phối trong phạm vi chuyên khoa phụ trách.',
  },
  {
    id: 'ROL-09',
    code: 'CLINICAL_NURSE',
    name: 'Điều dưỡng phụ khám',
    description:
      'Hỗ trợ đo sinh hiệu, tiếp nhận bệnh nhân tại buồng khám và quản lý hàng đợi buồng.',
    scope: 'OPERATIONAL',
    isSystemRole: false,
    assignedUsersCount: 8,
    permissions: pickPermissions(['PATIENT_PROFILE_READ', 'PATIENT_CHECKIN', 'QUEUE_MANAGE']),
    status: 'ACTIVE',
    updatedAt: '05/03/2026',
    segregationNote: 'Không được phép ký số đơn thuốc hoặc kết luận bệnh án.',
  },
  {
    id: 'ROL-10',
    code: 'SECURITY_AUDITOR',
    name: 'Kiểm toán viên an ninh',
    description: 'Định kỳ rà soát nhật ký truy cập hồ sơ bệnh án và kiểm tra tuân thủ bảo mật.',
    scope: 'SYSTEM',
    isSystemRole: false,
    assignedUsersCount: 2,
    permissions: pickPermissions(['AUDIT_READ']),
    status: 'INACTIVE',
    updatedAt: '01/03/2026',
    segregationNote: 'Vai trò chỉ đọc (Read-only), kích hoạt theo đợt kiểm toán định kỳ.',
  },
];
