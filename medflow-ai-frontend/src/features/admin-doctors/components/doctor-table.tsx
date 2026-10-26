import { Link } from 'react-router-dom';

import type { Doctor } from '../types/doctor.types';

interface DoctorTableProps {
  doctors: Doctor[];
  selectedIds: string[];
  onToggleSelection: (id: string) => void;
  onToggleAll: () => void;
  onToggleStatus: (id: string) => void;
}

export function DoctorTable({
  doctors,
  selectedIds,
  onToggleSelection,
  onToggleAll,
  onToggleStatus,
}: DoctorTableProps) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[1120px] table-fixed text-left text-xs">
        <colgroup>
          <col className="w-9" />
          <col className="w-[7%]" />
          <col className="w-[18%]" />
          <col className="w-[10%]" />
          <col className="w-[12%]" />
          <col className="w-[10%]" />
          <col className="w-[11%]" />
          <col className="w-[14%]" />
          <col className="w-[11%]" />
          <col className="w-[9%]" />
        </colgroup>
        <thead className="border-b border-border bg-slate-50 text-[10px] font-bold uppercase tracking-wide text-sub">
          <tr>
            <th className="px-4 py-4">
              <input
                aria-label="Chọn tất cả bác sĩ"
                type="checkbox"
                checked={
                  doctors.length > 0 && doctors.every((doctor) => selectedIds.includes(doctor.id))
                }
                onChange={onToggleAll}
              />
            </th>
            <th className="px-2">Mã BS</th>
            <th className="px-2">Họ và tên Bác sĩ</th>
            <th className="px-2">Học hàm / Học vị</th>
            <th className="px-2">Chuyên khoa chính</th>
            <th className="px-2">Khoa công tác</th>
            <th className="px-2">Số CCHN (BYT)</th>
            <th className="px-2">Buồng khám mặc định</th>
            <th className="px-2">Trạng thái</th>
            <th className="px-2">Thao tác</th>
          </tr>
        </thead>
        <tbody>
          {doctors.map((doctor) => (
            <tr key={doctor.id} className="h-[76px] border-b border-slate-100 hover:bg-sky-50/40">
              <td className="px-4">
                <input
                  aria-label={`Chọn ${doctor.name}`}
                  type="checkbox"
                  checked={selectedIds.includes(doctor.id)}
                  onChange={() => onToggleSelection(doctor.id)}
                />
              </td>
              <td className="px-2">
                <Link
                  to={`/admin/doctors/${doctor.id}/edit`}
                  className="font-mono font-bold text-primary hover:underline"
                >
                  {doctor.id}
                </Link>
              </td>
              <td className="px-2">
                <div className="flex items-center gap-2.5">
                  <img src={doctor.portrait} alt="" className="h-8 w-8 rounded-full object-cover" />
                  <div className="min-w-0">
                    <Link
                      to={`/admin/doctors/${doctor.id}/edit`}
                      className="block truncate font-bold text-ink hover:text-primary"
                    >
                      {doctor.name}
                    </Link>
                    <span className="block truncate text-[10px] text-sub">{doctor.email}</span>
                  </div>
                </div>
              </td>
              <td className="px-2">
                <span className="inline-block max-w-full rounded border border-slate-200 bg-slate-50 px-1.5 py-1 text-[10px] font-medium text-sub">
                  {doctor.degree}
                </span>
              </td>
              <td className="px-2 font-semibold text-ink">{doctor.specialty}</td>
              <td className="px-2 text-sub">{doctor.department}</td>
              <td className="px-2">
                <span className="font-mono text-[10px] font-semibold">{doctor.license}</span>
                <img src="/assets/admin-doctors/list-21.svg" alt="" className="ml-2 inline-block" />
              </td>
              <td className="px-2">
                <span className="inline-flex border border-border bg-canvas px-2 py-1.5 font-mono text-[10px] text-sub">
                  <span className="mr-1 text-primary">•</span>
                  {doctor.room}
                </span>
              </td>
              <td className="px-2">
                <span
                  className={`inline-flex items-center gap-1 rounded-full border px-2 py-1 text-[10px] font-semibold ${doctor.active ? 'border-emerald-200 bg-emerald-50 text-emerald-700' : 'border-amber-200 bg-amber-50 text-amber-700'}`}
                >
                  <span>●</span>
                  {doctor.active ? 'Đang nhận lịch' : 'Tạm ngừng nhận lịch'}
                </span>
              </td>
              <td className="px-2">
                <div className="flex items-center gap-2">
                  <Link
                    to={`/admin/doctors/${doctor.id}/edit`}
                    title={`Chỉnh sửa ${doctor.name}`}
                    aria-label={`Chỉnh sửa ${doctor.name}`}
                  >
                    <img src="/assets/admin-doctors/list-22.svg" alt="" />
                  </Link>
                  <button
                    type="button"
                    disabled
                    title="Lịch khám sẽ có ở màn hình tiếp theo"
                    className="opacity-40"
                  >
                    <img src="/assets/admin-doctors/list-23.svg" alt="" />
                  </button>
                  <button
                    type="button"
                    aria-label={`${doctor.active ? 'Tạm ngừng' : 'Kích hoạt'} ${doctor.name}`}
                    title={doctor.active ? 'Tạm ngừng nhận lịch' : 'Kích hoạt nhận lịch'}
                    onClick={() => onToggleStatus(doctor.id)}
                  >
                    <img src="/assets/admin-doctors/list-24.svg" alt="" />
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {doctors.length === 0 && (
        <p className="py-16 text-center text-sm text-sub">Không tìm thấy bác sĩ phù hợp.</p>
      )}
    </div>
  );
}
