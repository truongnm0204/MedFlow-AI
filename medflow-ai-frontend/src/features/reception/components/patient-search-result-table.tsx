import { Calendar, Clock, Eye, Phone, UserCheck } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';

import type { PatientSearchResultItem } from '../types/patient-search.types';

interface PatientSearchResultTableProps {
  patients: PatientSearchResultItem[];
  onSelectPatient?: (patient: PatientSearchResultItem) => void;
  onCheckInPatient?: (patient: PatientSearchResultItem) => void;
}

const GENDER_LABEL: Record<string, string> = {
  MALE: 'Nam',
  FEMALE: 'Nữ',
  OTHER: 'Khác',
};

const APPOINTMENT_STATUS_CONFIG = {
  BOOKED: { label: 'Đã đặt', tone: 'sky' as const },
  CHECKED_IN: { label: 'Đã check-in', tone: 'emerald' as const },
  CANCELLED: { label: 'Đã hủy', tone: 'rose' as const },
  IN_CONSULTATION: { label: 'Đang khám', tone: 'amber' as const },
};

const CURRENT_YEAR = new Date().getFullYear();

export function PatientSearchResultTable({
  patients,
  onSelectPatient,
  onCheckInPatient,
}: PatientSearchResultTableProps) {
  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xs">
      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs border-collapse">
          <thead>
            <tr className="border-b border-slate-200 bg-slate-50/80 text-[11px] font-bold uppercase tracking-wider text-slate-500">
              <th scope="col" className="px-5 py-3.5">Mã Bệnh Nhân</th>
              <th scope="col" className="px-5 py-3.5">Họ Và Tên</th>
              <th scope="col" className="px-5 py-3.5">CCCD / SĐT</th>
              <th scope="col" className="px-5 py-3.5">BHYT</th>
              <th scope="col" className="px-5 py-3.5">Lịch Hẹn Hôm Nay</th>
              <th scope="col" className="px-5 py-3.5 text-right">Thao Tác</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {patients.map((patient) => {
              const birthYear = new Date(patient.dateOfBirth).getFullYear();
              const age = CURRENT_YEAR - birthYear;
              const hasAppt = !!patient.todayAppointment;
              const apptStatus = hasAppt ? patient.todayAppointment!.status : null;
              const statusCfg = apptStatus ? APPOINTMENT_STATUS_CONFIG[apptStatus] : null;

              return (
                <tr
                  key={patient.id}
                  className="hover:bg-slate-50/70 transition-colors group"
                >
                  {/* Mã BN */}
                  <td className="px-5 py-4 font-mono font-bold text-sky-700 whitespace-nowrap">
                    {patient.patientCode}
                  </td>

                  {/* Họ tên */}
                  <td className="px-5 py-4">
                    <div className="font-bold text-slate-900 text-sm">
                      {patient.fullName}
                    </div>
                    <div className="text-[11px] text-slate-500 mt-0.5">
                      {GENDER_LABEL[patient.gender] || 'Khác'} • {age} tuổi ({birthYear})
                    </div>
                  </td>

                  {/* CCCD / SĐT */}
                  <td className="px-5 py-4">
                    <div className="font-mono text-slate-800 font-medium">
                      {patient.identityCard}
                    </div>
                    <div className="text-[11px] text-slate-500 flex items-center gap-1 mt-0.5">
                      <Phone className="w-3 h-3 text-slate-400" aria-hidden="true" />
                      {patient.phoneNumber}
                    </div>
                  </td>

                  {/* BHYT */}
                  <td className="px-5 py-4">
                    {patient.healthInsuranceNumber ? (
                      <span className="font-mono text-slate-700 text-xs bg-slate-100 px-2 py-0.5 rounded-md">
                        {patient.healthInsuranceNumber}
                      </span>
                    ) : (
                      <span className="text-slate-400 italic text-[11px]">Chưa đăng ký</span>
                    )}
                  </td>

                  {/* Lịch hẹn hôm nay */}
                  <td className="px-5 py-4">
                    {hasAppt && patient.todayAppointment ? (
                      <div className="space-y-1">
                        <div className="flex items-center gap-2">
                          <span className="font-mono font-bold text-slate-900 text-xs flex items-center gap-1">
                            <Clock className="w-3.5 h-3.5 text-primary" aria-hidden="true" />
                            {patient.todayAppointment.time}
                          </span>
                          {statusCfg && (
                            <Badge tone={statusCfg.tone}>{statusCfg.label}</Badge>
                          )}
                        </div>
                        <div className="text-[11px] text-slate-500">
                          {patient.todayAppointment.department} • {patient.todayAppointment.doctorName} ({patient.todayAppointment.roomName})
                        </div>
                      </div>
                    ) : (
                      <span className="text-slate-400 text-[11px] flex items-center gap-1">
                        <Calendar className="w-3.5 h-3.5 text-slate-300" aria-hidden="true" />
                        Không có lịch hẹn
                      </span>
                    )}
                  </td>

                  {/* Thao tác */}
                  <td className="px-5 py-4 text-right">
                    <div className="flex items-center justify-end gap-2">
                      {hasAppt && apptStatus === 'BOOKED' ? (
                        <Button
                          variant="primary"
                          size="sm"
                          onClick={() => onCheckInPatient?.(patient)}
                          className="gap-1.5"
                        >
                          <UserCheck className="w-3.5 h-3.5" aria-hidden="true" />
                          Tiếp nhận
                        </Button>
                      ) : (
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() => onSelectPatient?.(patient)}
                          className="gap-1.5"
                        >
                          <Eye className="w-3.5 h-3.5" aria-hidden="true" />
                          Xem hồ sơ
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
