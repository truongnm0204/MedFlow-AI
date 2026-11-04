export type HospitalConfigCategory =
  'DEPARTMENT_ROOM' | 'DOCTOR_SERVICE' | 'SLOT_SCHEDULE' | 'POLICY_REPORT';

export type HospitalConfigStatus = 'ACTIVE' | 'NEEDS_REVIEW' | 'MAINTENANCE';

export interface HospitalAdminStats {
  activeDepartments: number;
  totalDepartments: number;
  activeRooms: number;
  totalRooms: number;
  activeDoctors: number;
  onlineServicesCount: number;
  openSlotsToday: number;
  reviewSlotsCount: number;
}

export interface HospitalConfigWorklistItem {
  id: string;
  code: string;
  name: string;
  category: HospitalConfigCategory;
  departmentOrScope: string;
  metricLabel: string;
  updatedAt: string;
  status: HospitalConfigStatus;
  route: string;
}

export type HospitalAdminQuickActionIcon =
  'departments' | 'rooms' | 'doctors' | 'services' | 'slots' | 'reports';

export interface HospitalAdminQuickAction {
  id: string;
  label: string;
  description: string;
  ucCode: string;
  shortcut: string;
  route: string;
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
  icon: HospitalAdminQuickActionIcon;
}

export type HospitalPolicyAlertSeverity = 'WARNING' | 'INFO' | 'ACTIVE';

export interface HospitalPolicyAlertItem {
  id: string;
  code: string;
  title: string;
  detail: string;
  severity: HospitalPolicyAlertSeverity;
  route: string;
}

export interface HospitalAdminDashboardSummary {
  facilityName: string;
  facilityCode: string;
  shiftLabel: string;
  stats: HospitalAdminStats;
  worklist: HospitalConfigWorklistItem[];
  quickActions: HospitalAdminQuickAction[];
  policyAlerts: HospitalPolicyAlertItem[];
}
