import { useState } from 'react';

/** Hero + thẻ đặt lịch nhanh (Quick-Booking). */
export function HeroSection() {
  const [selectedSpecialty, setSelectedSpecialty] = useState('gastro');
  const [selectedDoctor, setSelectedDoctor] = useState('all');
  const [selectedDate, setSelectedDate] = useState('today');

  return (
    <section className="relative bg-gradient-to-b from-white via-sky-50/30 to-transparent pt-10 pb-16 lg:pt-16 lg:pb-24 border-b border-slate-200/60 overflow-hidden">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-3xl mx-auto space-y-4">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-sky-50 border border-sky-200 text-sky-700 text-xs font-bold tracking-wide">
            <span className="w-2 h-2 rounded-full bg-sky-500 animate-pulse" />
            HỆ THỐNG Y TẾ NGOẠI TRÚ THÔNG MINH MEDFLOW AI
          </div>

          <h1 className="text-3xl sm:text-4xl lg:text-5xl font-extrabold text-[#0A2540] tracking-tight leading-tight">
            Khám Ngoại Trú Đẳng Cấp • Đặt Hẹn Tức Thì • Không Chờ Đợi
          </h1>

          <p className="text-sm sm:text-base text-slate-600 leading-relaxed max-w-2xl mx-auto">
            Mô hình khám chữa bệnh tự nguyện 100% chuẩn quốc tế. Định tuyến buồng khám thông minh
            bằng AI, không giấy tờ, bảng giá niêm yết minh bạch trước khi khám, kết quả cận lâm sàng
            và đơn thuốc điện tử trả ngay trên điện thoại.
          </p>
        </div>

        {/* SMART QUICK-BOOKING CARD */}
        <div
          id="booking"
          className="mt-10 max-w-4xl mx-auto bg-white rounded-2xl border border-slate-200 shadow-lg p-5 sm:p-6 space-y-4 scroll-mt-24"
        >
          <div className="flex items-center justify-between pb-3 border-b border-slate-100">
            <div className="flex items-center gap-2">
              <div className="w-6 h-6 rounded-md bg-sky-100 text-sky-700 flex items-center justify-center font-bold text-xs">
                ✓
              </div>
              <h2 className="text-sm font-bold text-slate-900">
                Đặt Lịch Khám Ngoại Trú Nhanh (100% Khám Tự Nguyện)
              </h2>
            </div>
            <span className="text-xs text-emerald-700 font-semibold bg-emerald-50 px-2 py-0.5 rounded border border-emerald-200">
              Không Cần Giấy Giới Thiệu
            </span>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
            {/* 1. Chọn Chuyên Khoa */}
            <div>
              <label
                htmlFor="specialty-select"
                className="text-[11px] font-bold text-slate-500 uppercase tracking-wider block mb-1"
              >
                Chuyên Khoa
              </label>
              <select
                id="specialty-select"
                value={selectedSpecialty}
                onChange={(e) => setSelectedSpecialty(e.target.value)}
                className="w-full text-xs font-medium text-slate-800 bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="gastro">Nội Tiêu Hóa (từ 350k)</option>
                <option value="cardio">Tim Mạch Can Thiệp (từ 400k)</option>
                <option value="ent">Tai Mũi Họng (từ 300k)</option>
                <option value="pediatrics">Nhi Khoa (từ 350k)</option>
                <option value="ortho">Cơ Xương Khớp (từ 350k)</option>
                <option value="eye">Mắt & Khúc Xạ (từ 300k)</option>
              </select>
            </div>

            {/* 2. Chọn Bác Sĩ */}
            <div>
              <label
                htmlFor="doctor-select"
                className="text-[11px] font-bold text-slate-500 uppercase tracking-wider block mb-1"
              >
                Bác Sĩ Trực Khám
              </label>
              <select
                id="doctor-select"
                value={selectedDoctor}
                onChange={(e) => setSelectedDoctor(e.target.value)}
                className="w-full text-xs font-medium text-slate-800 bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="all">Bất kỳ bác sĩ có lịch trống sớm nhất</option>
                <option value="doc1">TS. BS. Nguyễn Văn Hùng</option>
                <option value="doc2">BS. CKII Trần Mai Anh</option>
                <option value="doc3">ThS. BS. Lê Thu Hà</option>
                <option value="doc4">BS. CKI Phạm Quang Minh</option>
              </select>
            </div>

            {/* 3. Ngày & Khung Giờ */}
            <div>
              <label
                htmlFor="date-select"
                className="text-[11px] font-bold text-slate-500 uppercase tracking-wider block mb-1"
              >
                Ngày Khám
              </label>
              <select
                id="date-select"
                value={selectedDate}
                onChange={(e) => setSelectedDate(e.target.value)}
                className="w-full text-xs font-medium text-slate-800 bg-slate-50 border border-slate-200 rounded-lg p-2.5 focus:bg-white focus:outline-none focus:ring-2 focus:ring-sky-500"
              >
                <option value="today">Hôm nay (Còn 18 slot)</option>
                <option value="tomorrow">Ngày mai (Còn 35 slot)</option>
                <option value="weekend">Thứ Bảy / Chủ Nhật này</option>
              </select>
            </div>

            {/* 4. Action Button */}
            <div className="flex items-end">
              <button
                type="button"
                onClick={() =>
                  alert('Đã tìm thấy 8 khung giờ trống khả dụng cho chuyên khoa của bạn!')
                }
                className="w-full py-2.5 px-4 bg-sky-600 hover:bg-sky-700 text-white text-xs font-bold rounded-lg shadow-sm shadow-sky-600/30 transition-all flex items-center justify-center gap-1.5"
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth="2"
                    d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                  />
                </svg>
                Tìm Lịch Khám (AI)
              </button>
            </div>
          </div>

          {/* Trust Metrics Bar */}
          <div className="pt-4 border-t border-slate-100 grid grid-cols-2 md:grid-cols-4 gap-4 text-center">
            <div className="p-2">
              <div className="text-xl sm:text-2xl font-black text-sky-700 font-mono">0 Phút</div>
              <div className="text-[11px] text-slate-500 font-medium">
                Chờ tại quầy (Check-in QR)
              </div>
            </div>
            <div className="p-2">
              <div className="text-xl sm:text-2xl font-black text-emerald-700 font-mono">100%</div>
              <div className="text-[11px] text-slate-500 font-medium">Minh bạch giá (Non-BHYT)</div>
            </div>
            <div className="p-2">
              <div className="text-xl sm:text-2xl font-black text-slate-900 font-mono">50.000+</div>
              <div className="text-[11px] text-slate-500 font-medium">
                Lượt khám hài lòng (4.9★)
              </div>
            </div>
            <div className="p-2">
              <div className="text-xl sm:text-2xl font-black text-sky-700 font-mono">15 Phút</div>
              <div className="text-[11px] text-slate-500 font-medium">Trả kết quả CLS cơ bản</div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
