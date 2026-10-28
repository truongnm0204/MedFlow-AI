import { Activity, Clock, HeartPulse, ShieldCheck } from 'lucide-react';

import { MedflowLogo } from '@/shared/components/layout/medflow-logo';

const METRICS = [
  { icon: Clock, value: '4.2 phút', label: 'Đặt lịch trung bình' },
  { icon: Activity, value: '24/24', label: 'Hỗ trợ trực tuyến' },
  { icon: ShieldCheck, value: '99.8%', label: 'Bảo mật dữ liệu' },
];

/** Cột trái navy `#0a2540` — branding + metrics (Figma `1:2`). Ẩn trên mobile. */
export function AuthBrandingPanel() {
  return (
    <aside className="relative hidden flex-col justify-between overflow-hidden bg-navy p-12 lg:flex">
      {/* Ambient blur */}
      <div className="pointer-events-none absolute -left-24 top-16 h-80 w-80 rounded-full bg-sky-500/20 blur-3xl" />
      <div className="pointer-events-none absolute -bottom-24 right-0 h-96 w-96 rounded-full bg-sky-400/10 blur-3xl" />

      <div className="relative">
        <MedflowLogo variant="full" tone="white" className="h-12 w-auto" linkToHome={false} />
      </div>

      <div className="relative flex flex-col gap-8">
        <div>
          <p className="mb-3 inline-flex items-center gap-2 rounded-full border border-white/20 bg-white/10 px-3 py-1 text-[11px] font-medium uppercase tracking-wider text-sky-200">
            <HeartPulse className="h-3.5 w-3.5" aria-hidden="true" />
            Nền tảng y tế thông minh
          </p>
          <h1 className="text-4xl font-extrabold leading-tight text-white xl:text-5xl">
            Khám Ngoại trú
            <br />
            <span className="bg-gradient-to-r from-sky-300 to-cyan-200 bg-clip-text text-transparent">
              MedFlow AI
            </span>
          </h1>
          <p className="mt-4 max-w-md text-sm leading-relaxed text-slate-300">
            Đặt lịch khám, quản lý hồ sơ bệnh án điện tử và nhận kết quả ngoại trú an toàn — tất cả
            trong một cổng dịch vụ duy nhất.
          </p>
        </div>

        <div className="rounded-2xl border border-white/20 bg-white/10 p-5 backdrop-blur">
          <p className="mb-4 flex items-center gap-2 text-xs font-medium text-emerald-300">
            <span className="h-2 w-2 animate-pulse rounded-full bg-emerald-400" />
            Hệ thống đang hoạt động ổn định
          </p>
          <div className="grid grid-cols-3 gap-3">
            {METRICS.map(({ icon: Icon, value, label }) => (
              <div key={label} className="flex flex-col gap-1">
                <Icon className="h-4 w-4 text-sky-300" aria-hidden="true" />
                <span className="text-lg font-bold text-white">{value}</span>
                <span className="text-[10px] leading-tight text-slate-400">{label}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      <p className="relative text-[11px] "></p>
    </aside>
  );
}
