import { useState } from 'react';

import { useQuickSlotSearch } from '../hooks/use-patient-home';
import { quickBookingSchema } from '../schemas/home.schemas';
import type { QuickSlotItem } from '../types/home.types';
import { QuickSlotResults } from './quick-slot-results';

const TRUST_METRICS = [
  { value: '0 Phút', label: 'Chờ tại quầy (Check-in QR)', color: 'text-sky-700' },
  { value: '100%', label: 'Minh bạch giá (Non-BHYT)', color: 'text-emerald-700' },
  { value: '50.000+', label: 'Lượt khám hài lòng (4.9★)', color: 'text-slate-900' },
  { value: '15 Phút', label: 'Trả kết quả CLS cơ bản', color: 'text-sky-700' },
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
    <section className="relative overflow-hidden border-b border-slate-200/60 bg-gradient-to-b from-white via-sky-50/30 to-transparent pt-10 pb-16 lg:pt-14 lg:pb-20">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-3xl space-y-3 text-center">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold text-sky-700">
            CỔNG ĐẶT LỊCH & ĐIỀU PHỐI KHÁM NGOẠI TRÚ THÔNG MINH
          </span>
          <h1 className="text-3xl leading-tight font-extrabold tracking-tight text-[#0A2540] sm:text-4xl lg:text-5xl">
            Đặt Lịch Khám Chuyên Khoa Nhanh Chóng, Không Chờ Đợi
          </h1>
          <p className="text-xs leading-relaxed text-slate-600 sm:text-sm">
            Kết nối trực tiếp đội ngũ Tiến sĩ, Bác sĩ CKII đầu ngành. Minh bạch 100% chi phí, định
            tuyến phòng khám tự động và theo dõi hồ sơ bệnh án điện tử xuyên suốt.
          </p>
        </div>

        <form
          id="booking"
          onSubmit={handleQuickSearch}
          noValidate
          className="mx-auto mt-8 max-w-4xl scroll-mt-24 space-y-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-lg sm:p-6"
        >
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3">
            <div className="flex items-center gap-2">
              <div className="flex h-6 w-6 items-center justify-center rounded-md bg-sky-100 text-xs font-bold text-sky-700">
                ✓
              </div>
              <h2 className="text-sm font-bold text-slate-900">
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
                className="mb-1 block text-[11px] font-bold tracking-wider text-slate-500 uppercase"
              >
                Chuyên Khoa
              </label>
              <select
                id="specialty-select"
                value={selectedSpecialty}
                onChange={(e) => setSelectedSpecialty(e.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-slate-50 p-2.5 text-xs font-medium text-slate-800 focus:bg-white focus:ring-2 focus:ring-sky-500 focus:outline-none"
              >
                <option value="">-- Chọn chuyên khoa --</option>
                <option value="gastro">Nội Tiêu Hóa (từ 350k)</option>
                <option value="cardio">Tim Mạch Can Thiệp (từ 400k)</option>
                <option value="ent">Tai Mũi Họng (từ 300k)</option>
                <option value="pediatrics">Nhi Khoa (từ 350k)</option>
                <option value="ortho">Cơ Xương Khớp (từ 350k)</option>
                <option value="eye">Mắt & Khúc Xạ (từ 300k)</option>
              </select>
            </div>

            <div>
              <label
                htmlFor="doctor-select"
                className="mb-1 block text-[11px] font-bold tracking-wider text-slate-500 uppercase"
              >
                Bác Sĩ Trực Khám
              </label>
              <select
                id="doctor-select"
                value={selectedDoctor}
                onChange={(e) => setSelectedDoctor(e.target.value)}
                className="w-full rounded-lg border border-slate-200 bg-slate-50 p-2.5 text-xs font-medium text-slate-800 focus:bg-white focus:ring-2 focus:ring-sky-500 focus:outline-none"
              >
                <option value="all">Bất kỳ bác sĩ có lịch trống sớm nhất</option>
                <option value="doc1">TS. BS. Nguyễn Văn Hùng</option>
                <option value="doc2">BS. CKII Trần Mai Anh</option>
                <option value="doc3">ThS. BS. Lê Thu Hà</option>
                <option value="doc4">BS. CKI Phạm Quang Minh</option>
              </select>
            </div>

            <div>
              <label
                htmlFor="date-select"
                className="mb-1 block text-[11px] font-bold tracking-wider text-slate-500 uppercase"
              >
                Ngày Khám
              </label>
              <select
                id="date-select"
                value={selectedDate}
                onChange={(e) =>
                  setSelectedDate(e.target.value as 'today' | 'tomorrow' | 'weekend')
                }
                className="w-full rounded-lg border border-slate-200 bg-slate-50 p-2.5 text-xs font-medium text-slate-800 focus:bg-white focus:ring-2 focus:ring-sky-500 focus:outline-none"
              >
                <option value="today">Hôm nay (Còn 18 slot)</option>
                <option value="tomorrow">Ngày mai (Còn 35 slot)</option>
                <option value="weekend">Thứ Bảy / Chủ Nhật này</option>
              </select>
            </div>

            <div className="flex items-end">
              <button
                type="submit"
                disabled={slotSearch.isPending}
                className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-sky-600 px-4 py-2.5 text-xs font-bold text-white shadow-sm shadow-sky-600/30 transition-all hover:bg-sky-700 disabled:opacity-60"
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
            className="w-full rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-xs text-slate-800 placeholder-slate-400 focus:bg-white focus:ring-2 focus:ring-sky-500 focus:outline-none"
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
            {TRUST_METRICS.map((item) => (
              <div key={item.label} className="p-2">
                <div className={`font-mono text-xl font-black sm:text-2xl ${item.color}`}>
                  {item.value}
                </div>
                <div className="text-[11px] font-medium text-slate-500">{item.label}</div>
              </div>
            ))}
          </div>
        </form>
      </div>
    </section>
  );
}
