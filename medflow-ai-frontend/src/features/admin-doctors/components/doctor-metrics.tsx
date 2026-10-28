import { GraduationCap, UserCheck, Users, UserX, type LucideIcon } from 'lucide-react';

import type { Doctor } from '../types/doctor.types';

export function DoctorMetrics({ doctors }: { doctors: Doctor[] }) {
  const active = doctors.filter((doctor) => doctor.active).length;
  const advanced = doctors.filter((doctor) => /Thạc sĩ|PGS|khoa II/.test(doctor.degree)).length;
  const items: Array<{
    title: string;
    count: number;
    note: string;
    Icon: LucideIcon;
    tone: string;
  }> = [
    {
      title: 'TỔNG SỐ BÁC SĨ CƠ SỞ',
      count: doctors.length,
      note: 'Lâm sàng • Cận lâm sàng',
      Icon: Users,
      tone: 'bg-sky-50 text-primary',
    },
    {
      title: 'ĐANG NHẬN LỊCH KHÁM',
      count: active,
      note: 'Sẵn sàng tiếp nhận',
      Icon: UserCheck,
      tone: 'bg-emerald-50 text-emerald-600',
    },
    {
      title: 'TẠM NGỪNG NHẬN LỊCH',
      count: doctors.length - active,
      note: 'Hội chẩn / NCKH / Nghỉ phép',
      Icon: UserX,
      tone: 'bg-amber-50 text-amber-600',
    },
    {
      title: 'TRÌNH ĐỘ SAU ĐẠI HỌC',
      count: advanced,
      note: 'ThS, BSCKII, PGS, TS',
      Icon: GraduationCap,
      tone: 'bg-indigo-50 text-indigo-600',
    },
  ];
  return (
    <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4" aria-label="Tổng quan bác sĩ">
      {items.map((item) => (
        <div
          key={item.title}
          className="flex min-h-32 items-center justify-between rounded-lg border border-border bg-white p-5 shadow-sm"
        >
          <div>
            <p className="text-[11px] font-semibold tracking-wide text-sub">{item.title}</p>
            <p className="mt-2 text-3xl font-bold leading-none">
              {String(item.count).padStart(2, '0')}{' '}
              <span className="text-xs font-normal text-sub">Bác sĩ</span>
            </p>
            <p className="mt-3 text-[11px] font-medium text-sub">{item.note}</p>
          </div>
          <span className={`grid h-11 w-11 shrink-0 place-items-center rounded-md ${item.tone}`}>
            <item.Icon className="h-5 w-5" aria-hidden="true" />
          </span>
        </div>
      ))}
    </section>
  );
}
