import type { LucideIcon } from 'lucide-react';
import { CalendarCheck, Check, FileText, QrCode, ShieldCheck } from 'lucide-react';
import { useState } from 'react';

import { useQuickSlotSearch } from '../hooks/use-patient-home';
import { quickBookingSchema } from '../schemas/home.schemas';
import type { QuickSlotItem } from '../types/home.types';
import { QuickSlotResults } from './quick-slot-results';

interface ServicePillar {
  title: string;
  subtitle: string;
  icon: LucideIcon;
  tone: string;
}

const SERVICE_PILLARS: ServicePillar[] = [
  {
    title: 'Check-in QR',
    subtitle: 'Tiếp nhận tự động tại quầy & Kiosk',
    icon: QrCode,
    tone: 'text-primary',
  },
  {
    title: 'Non-BHYT',
    subtitle: 'Công khai biểu phí trước khi khám',
    icon: ShieldCheck,
    tone: 'text-emerald-700',
  },
  {
    title: 'Hồ sơ EMR',
    subtitle: 'Lưu trữ xuyên suốt lịch sử khám',
    icon: FileText,
    tone: 'text-navy',
  },
  {
    title: 'Đặt hẹn số',
    subtitle: 'Chủ động chọn khung giờ phù hợp',
    icon: CalendarCheck,
    tone: 'text-primary',
  },
];

/** Hero + thẻ đặt lịch nhanh (Quick-Booking) có kiểm tra hợp lệ và tra cứu slot trực tiếp. */
export function HeroSection() {
  const [selectedSpecialty, setSelectedSpecialty] = useState('gastro');
  const [selectedDoctor, setSelectedDoctor] = useState('all');
  const [selectedDate, setSelectedDate] = useState<'today' | 'tomorrow' | 'weekend'>('today');
  const [symptomNote, setSymptomNote] = useState('');
  const [validationError, setValidationError] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<QuickSlotItem | null>(null);
  const slotSearch = useQuickSlotSearch();

  const handleQuickSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);
    setSelectedSlot(null);

    const parsed = quickBookingSchema.safeParse({
      specialtyId: selectedSpecialty,
      doctorId: selectedDoctor,
      dateOption: selectedDate,
      symptomNote: symptomNote.trim() || undefined,
    });
    if (!parsed.success) {
      slotSearch.reset();
      setValidationError(parsed.error.issues[0]?.message ?? 'Thông tin đặt khám chưa hợp lệ.');
      return;
    }
    slotSearch.mutate(parsed.data);
  };

  return (
    <section className="relative overflow-hidden border-b border-border/60 bg-gradient-to-b from-white via-sky-50/30 to-transparent pt-10 pb-16 lg:pt-14 lg:pb-20">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-3xl space-y-3 text-center">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold text-primary">
            CỔNG ĐẶT LỊCH & ĐIỀU PHỐI KHÁM NGOẠI TRÚ THÔNG MINH
          </span>
          <h1 className="text-3xl leading-tight font-extrabold tracking-tight text-navy sm:text-4xl lg:text-5xl">
            Đặt Lịch Khám Chuyên Khoa Nhanh Chóng, Không Chờ Đợi
          </h1>
          <p className="text-xs leading-relaxed text-sub sm:text-sm">
            Kết nối trực tiếp đội ngũ Tiến sĩ, Bác sĩ CKII đầu ngành. Minh bạch chi phí theo biểu
            phí cơ sở, định tuyến phòng khám tự động và theo dõi hồ sơ bệnh án điện tử xuyên suốt.
          </p>
        </div>

        <form
          id="booking"
          onSubmit={handleQuickSearch}
          noValidate
          className="mx-auto mt-8 max-w-4xl scroll-mt-24 space-y-4 rounded-2xl border border-border bg-white p-5 shadow-lg sm:p-6"
        >
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3">
            <div className="flex items-center gap-2">
              <div className="flex h-6 w-6 items-center justify-center rounded-md bg-sky-100 text-primary">
                <Check className="h-3.5 w-3.5" aria-hidden="true" />
              </div>
              <h2 className="text-sm font-bold text-ink">
                Đặt Lịch Khám Ngoại Trú Nhanh (100% Khám Tự Nguyện)
              </h2>
            </div>
            <span className="rounded border border-emerald-200 bg-emerald-50 px-2 py-0.5 text-xs font-semibold text-emerald-700">
              Không Cần Giấy Giới Thiệu
            </span>
          </div>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <div>
              <label
                htmlFor="specialty-select"
                className="mb-1 block text-[11px] font-bold tracking-wider text-sub uppercase"
              >
                Chuyên Khoa
              </label>
              <select
                id="specialty-select"
                value={selectedSpecialty}
                onChange={(e) => setSelectedSpecialty(e.target.value)}
                className="w-full rounded-lg border border-border bg-canvas p-2.5 text-xs font-medium text-ink focus:bg-white focus:ring-2 focus:ring-primary focus:outline-none"
              >
                <option value="">-- Chọn chuyên khoa --</option>
                <option value="gastro">Nội Tiêu Hóa</option>
                <option value="cardio">Tim Mạch Can Thiệp</option>
                <option value="ent">Tai Mũi Họng</option>
                <option value="pediatrics">Nhi Khoa</option>
                <option value="ortho">Cơ Xương Khớp</option>
                <option value="eye">Mắt & Khúc Xạ</option>
              </select>
            </div>

            <div>
              <label
                htmlFor="doctor-select"
                className="mb-1 block text-[11px] font-bold tracking-wider text-sub uppercase"
              >
                Bác Sĩ Trực Khám
              </label>
              <select
                id="doctor-select"
                value={selectedDoctor}
                onChange={(e) => setSelectedDoctor(e.target.value)}
                className="w-full rounded-lg border border-border bg-canvas p-2.5 text-xs font-medium text-ink focus:bg-white focus:ring-2 focus:ring-primary focus:outline-none"
              >
                <option value="all">Bất kỳ bác sĩ có lịch trống sớm nhất</option>
                <option value="specialist">Theo lịch phân công chuyên khoa</option>
              </select>
            </div>

            <div>
              <label
                htmlFor="date-select"
                className="mb-1 block text-[11px] font-bold tracking-wider text-sub uppercase"
              >
                Ngày Khám
              </label>
              <select
                id="date-select"
                value={selectedDate}
                onChange={(e) =>
                  setSelectedDate(e.target.value as 'today' | 'tomorrow' | 'weekend')
                }
                className="w-full rounded-lg border border-border bg-canvas p-2.5 text-xs font-medium text-ink focus:bg-white focus:ring-2 focus:ring-primary focus:outline-none"
              >
                <option value="today">Hôm nay</option>
                <option value="tomorrow">Ngày mai</option>
                <option value="weekend">Thứ Bảy / Chủ Nhật này</option>
              </select>
            </div>

            <div className="flex items-end">
              <button
                type="submit"
                disabled={slotSearch.isPending}
                className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-primary px-4 py-2.5 text-xs font-bold text-white shadow-sm shadow-sky-600/30 transition-all hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none disabled:opacity-60"
              >
                {slotSearch.isPending ? 'Đang tìm slot...' : 'Tìm Lịch Khám (AI)'}
              </button>
            </div>
          </div>

          <input
            id="symptom-note-input"
            type="text"
            aria-label="Ghi chú triệu chứng"
            value={symptomNote}
            onChange={(e) => setSymptomNote(e.target.value)}
            placeholder="Ghi chú triệu chứng hoặc nhu cầu khám (tùy chọn, tối đa 200 ký tự)..."
            className="w-full rounded-lg border border-border bg-canvas px-3 py-2 text-xs text-ink placeholder-slate-400 focus:bg-white focus:ring-2 focus:ring-primary focus:outline-none"
          />

          <QuickSlotResults
            validationError={validationError}
            isError={slotSearch.isError}
            error={slotSearch.error}
            isSuccess={slotSearch.isSuccess}
            slots={slotSearch.data}
            selectedSlot={selectedSlot}
            onSelectSlot={setSelectedSlot}
          />

          <div className="grid grid-cols-2 gap-4 border-t border-slate-100 pt-4 text-center md:grid-cols-4">
            {SERVICE_PILLARS.map((item) => {
              const Icon = item.icon;
              return (
                <div key={item.title} className="p-2">
                  <div
                    className={`inline-flex items-center justify-center gap-1.5 text-sm font-extrabold sm:text-base ${item.tone}`}
                  >
                    <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span>{item.title}</span>
                  </div>
                  <div className="mt-0.5 text-[11px] font-medium text-sub">{item.subtitle}</div>
                </div>
              );
            })}
          </div>
        </form>
      </div>
    </section>
  );
}
