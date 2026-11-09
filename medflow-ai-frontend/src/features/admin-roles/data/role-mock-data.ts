import type {
  CreateRoleRequest,
  PermissionGroup,
  RoleItem,
  RoleMetrics,
  RolePermissionItem,
  RoleScope,
  RolesPermissionsCatalogDto,
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

export const SYSTEM_PERMISSION_LIST: RolePermissionItem[] = Object.values(SYSTEM_PERMISSIONS);

export const PERMISSION_GROUPS: PermissionGroup[] = SYSTEM_PERMISSION_LIST.reduce<
  PermissionGroup[]
>((groups, perm) => {
  const existing = groups.find((group) => group.module === perm.module);
  if (existing) {
    existing.permissions.push(perm);
  } else {
    groups.push({
      module: perm.module,
      permissions: [perm],
    });
  }
  return groups;
}, []);

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

function cloneRoleItem(role: RoleItem): RoleItem {
  return {
    ...role,
    permissions: role.permissions.map((perm) => ({ ...perm })),
  };
}

let currentMockRoles: RoleItem[] = ROLES_MOCK_DATA.map(cloneRoleItem);

export function getDefaultRolePermissionCodes(roleCode: string): string[] {
  const found = ROLES_MOCK_DATA.find((role) => role.code === roleCode || role.id === roleCode);
  return found ? found.permissions.map((perm) => perm.code) : [];
}

export function getMockRolesCatalog(): RolesPermissionsCatalogDto {
  const roles = currentMockRoles.map(cloneRoleItem);
  const permissions = SYSTEM_PERMISSION_LIST.map((perm) => ({ ...perm }));
  const metrics: RoleMetrics = {
    totalRoles: roles.length,
    activeRoles: roles.filter((role) => role.status === 'ACTIVE').length,
    totalAssignedAccounts: roles.reduce((sum, role) => sum + role.assignedUsersCount, 0),
    totalPermissions: permissions.length,
  };
  return {
    roles,
    permissions,
    metrics,
  };
}

export function updateMockRolePermissions(roleId: string, permissionCodes: string[]): RoleItem {
  const index = currentMockRoles.findIndex((role) => role.id === roleId || role.code === roleId);
  if (index === -1) {
    throw new Error('Không tìm thấy vai trò cần phân quyền.');
  }

  const codeSet = new Set(permissionCodes);
  const nextPermissions = SYSTEM_PERMISSION_LIST.filter((perm) => codeSet.has(perm.code)).map(
    (perm) => ({ ...perm }),
  );

  const updatedRole: RoleItem = {
    ...currentMockRoles[index],
    permissions: nextPermissions,
    updatedAt: 'Hôm nay',
  };

  currentMockRoles[index] = updatedRole;
  return cloneRoleItem(updatedRole);
}

export function createMockRole(payload: CreateRoleRequest): RoleItem {
  const code = payload.code.trim().toUpperCase();
  const name = payload.name.trim();

  if (currentMockRoles.some((role) => role.code.toUpperCase() === code)) {
    throw new Error(`Mã vai trò '${code}' đã tồn tại trong hệ thống.`);
  }

  if (currentMockRoles.some((role) => role.name.trim().toLowerCase() === name.toLowerCase())) {
    throw new Error(`Tên vai trò '${name}' đã tồn tại trong hệ thống.`);
  }

  const codeSet = new Set((payload.permissionCodes ?? []).map((item) => item.trim().toUpperCase()));
  const nextPermissions = SYSTEM_PERMISSION_LIST.filter((perm) => codeSet.has(perm.code)).map(
    (perm) => ({ ...perm }),
  );

  const nextNumericId = currentMockRoles.length + 1;
  const createdRole: RoleItem = {
    roleId: nextNumericId,
    id: `ROL-${String(nextNumericId).padStart(2, '0')}`,
    code,
    name,
    description: payload.description?.trim() || `Vai trò cấu hình mở rộng: ${name}.`,
    scope: payload.scope || 'OPERATIONAL',
    isSystemRole: false,
    assignedUsersCount: 0,
    activeUsersCount: 0,
    permissionCount: nextPermissions.length,
    permissions: nextPermissions,
    status: payload.status || 'ACTIVE',
    updatedAt: 'Hôm nay',
    segregationNote:
      payload.segregationNote?.trim() ||
      'Tuân thủ nguyên tắc phân quyền tối thiểu (Least Privilege).',
  };

  currentMockRoles = [...currentMockRoles, createdRole];
  return cloneRoleItem(createdRole);
}

export function resetMockRolesCatalog(): void {
  currentMockRoles = ROLES_MOCK_DATA.map(cloneRoleItem);
}
