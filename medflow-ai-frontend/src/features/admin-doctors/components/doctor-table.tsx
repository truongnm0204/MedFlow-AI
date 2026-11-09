import { BadgeCheck, Calendar, MoreVertical, Power, SquarePen } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';

import type { Doctor } from '../types/doctor.types';

interface DoctorTableProps {
  doctors: Doctor[];
  selectedIds: string[];
  onToggleSelection: (id: string) => void;
  onToggleAll: () => void;
  onToggleStatus: (id: string) => void;
  onRequestDeactivate?: (doctor: Doctor) => void;
}
export function DoctorTable({
  doctors,
  selectedIds,
  onToggleSelection,
  onToggleAll,
  onToggleStatus,
  onRequestDeactivate,
}: DoctorTableProps) {
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(event.target as Node)) {
        setOpenMenuId(null);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

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
            <th className="px-2">TRẠNG THÁI HỒ SƠ</th>
            <th className="px-2">THAO TÁC</th>
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
                <BadgeCheck
                  className="ml-2 inline-block h-3.5 w-3.5 text-emerald-600"
                  aria-hidden="true"
                />
              </td>
              <td className="px-2">
                <span className="inline-flex border border-border bg-canvas px-2 py-1.5 font-mono text-[10px] text-sub">
                  <span className="mr-1 text-primary">•</span>
                  {doctor.room}
                </span>
              </td>
              <td className="px-2">
                <span
                  className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[10px] font-semibold ${
                    doctor.active
                      ? 'border-sky-200 bg-sky-50 text-sky-700'
                      : 'border-slate-200 bg-slate-100 text-slate-600'
                  }`}
                >
                  <span
                    className={`h-1.5 w-1.5 rounded-full ${
                      doctor.active ? 'bg-sky-500' : 'bg-slate-400'
                    }`}
                  />
                  {doctor.active ? 'Đang hoạt động' : 'Ngừng hoạt động'}
                </span>
              </td>
              <td className="px-2">
                <div
                  ref={openMenuId === doctor.id ? menuRef : undefined}
                  className="relative flex items-center gap-1.5"
                >
                  <Link
                    to={`/admin/doctors/${doctor.id}/edit`}
                    title={`Chỉnh sửa ${doctor.name}`}
                    aria-label={`Chỉnh sửa ${doctor.name}`}
                    className="rounded p-1 text-slate-500 transition-colors hover:bg-slate-100 hover:text-primary"
                  >
                    <SquarePen className="h-4 w-4" aria-hidden="true" />
                  </Link>
                  <button
                    type="button"
                    disabled
                    aria-label={`Lịch khám ${doctor.name}`}
                    title="Lịch khám bác sĩ (SCR-ADM-DOC-04)"
                    className="rounded p-1 text-slate-500 opacity-40 cursor-not-allowed"
                  >
                    <Calendar className="h-4 w-4" aria-hidden="true" />
                  </button>
                  <div className="relative">
                    <button
                      type="button"
                      aria-label={`Thao tác ${doctor.name}`}
                      title="Tùy chọn thao tác"
                      aria-expanded={openMenuId === doctor.id}
                      onClick={() =>
                        setOpenMenuId((current) => (current === doctor.id ? null : doctor.id))
                      }
                      className="rounded p-1 text-slate-500 transition-colors hover:bg-slate-100 hover:text-ink"
                    >
                      <MoreVertical className="h-4 w-4" aria-hidden="true" />
                    </button>
                    {openMenuId === doctor.id && (
                      <div className="absolute right-0 top-full z-30 mt-1 w-44 rounded-xl border border-border bg-white p-1.5 shadow-lg">
                        {doctor.active ? (
                          <button
                            type="button"
                            aria-label={`Ngừng hoạt động ${doctor.name}`}
                            onClick={() => {
                              setOpenMenuId(null);
                              if (onRequestDeactivate) {
                                onRequestDeactivate(doctor);
                              } else {
                                onToggleStatus(doctor.id);
                              }
                            }}
                            className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs font-semibold text-amber-700 transition-colors hover:bg-amber-50"
                          >
                            <Power className="h-3.5 w-3.5" aria-hidden="true" />
                            Ngừng hoạt động
                          </button>
                        ) : (
                          <button
                            type="button"
                            aria-label={`Kích hoạt lại ${doctor.name}`}
                            onClick={() => {
                              setOpenMenuId(null);
                              onToggleStatus(doctor.id);
                            }}
                            className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs font-semibold text-emerald-700 transition-colors hover:bg-emerald-50"
                          >
                            <Power className="h-3.5 w-3.5" aria-hidden="true" />
                            Kích hoạt lại
                          </button>
                        )}
                        <Link
                          to={`/admin/doctors/${doctor.id}/edit`}
                          onClick={() => setOpenMenuId(null)}
                          className="flex w-full items-center gap-2 rounded-lg px-2.5 py-2 text-left text-xs font-medium text-slate-700 transition-colors hover:bg-slate-50"
                        >
                          <SquarePen className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
                          Chỉnh sửa hồ sơ
                        </Link>
                      </div>
                    )}
                  </div>
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
