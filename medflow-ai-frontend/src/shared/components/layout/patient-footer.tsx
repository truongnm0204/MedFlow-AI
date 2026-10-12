import { MedflowLogo } from './medflow-logo'

export function PatientFooter() {
  return (
    <footer className="w-full bg-white border-t border-slate-200 select-none text-slate-600">
      {/* Main Multi-Column Section */}
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-12 lg:py-16">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-5 gap-8 lg:gap-10">
          {/* Col 1: Brand & Credentials (lg:col-span-2) */}
          <div className="lg:col-span-2 space-y-4">
            <div className="flex flex-col gap-1.5">
              <MedflowLogo variant="full" className="h-12 w-auto self-start" />
              <p className="text-[11px] text-slate-400 font-medium">Bệnh Viện Đa Khoa Quốc Tế Thông Minh</p>
            </div>

            <p className="text-xs text-slate-600 leading-relaxed max-w-sm">
              Hệ thống khám chữa bệnh ngoại trú 100% tự nguyện chất lượng cao. Ứng dụng trí tuệ nhân tạo định tuyến luồng khám thông minh, loại bỏ hoàn toàn thời gian chờ đợi và minh bạch viện phí trước khi khám.
            </p>

            <div className="pt-2 flex flex-wrap gap-2 text-[11px] font-mono">
              <span className="px-2.5 py-1 rounded-md bg-slate-50 border border-slate-200 text-slate-700 font-medium">
                GPKD: 0892/BYT-GPHĐ
              </span>
              <span className="px-2.5 py-1 rounded-md bg-sky-50 border border-sky-200 text-sky-800 font-medium">
                HL7/FHIR v4.0.1
              </span>
              <span className="px-2.5 py-1 rounded-md bg-emerald-50 border border-emerald-200 text-emerald-800 font-medium">
                100% Non-BHYT
              </span>
            </div>
          </div>

          {/* Col 2: Key Specialties */}
          <div className="space-y-3">
            <h3 className="text-xs font-bold text-slate-900 uppercase tracking-wider font-mono">
              Chuyên Khoa Ngoại Trú
            </h3>
            <ul className="space-y-2 text-xs">
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Nội Tiêu Hóa & Nội Soi</a>
              </li>
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Tim Mạch & Huyết Áp</a>
              </li>
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Tai Mũi Họng Công Nghệ Cao</a>
              </li>
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Nhi Khoa & Tiêm Chủng</a>
              </li>
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Cơ Xương Khớp & Phục Hồi</a>
              </li>
              <li>
                <a href="#specialties" className="hover:text-sky-600 transition-colors">Khám Sức Khỏe Tổng Quát</a>
              </li>
            </ul>
          </div>

          {/* Col 3: Patient Care & EMR */}
          <div className="space-y-3">
            <h3 className="text-xs font-bold text-slate-900 uppercase tracking-wider font-mono">
              Dịch Vụ & Tra Cứu
            </h3>
            <ul className="space-y-2 text-xs">
              <li>
                <a href="#booking" className="hover:text-sky-600 transition-colors">Đặt Lịch Khám Thông Minh</a>
              </li>
              <li>
                <a href="#services" className="hover:text-sky-600 transition-colors">Bảng Giá Viện Phí Công Khai</a>
              </li>
              <li>
                <a href="#emr" className="hover:text-sky-600 transition-colors">Tra Cứu Hồ Sơ Bệnh Án EMR</a>
              </li>
              <li>
                <a href="#pacs" className="hover:text-sky-600 transition-colors">Xem Kết Quả Chụp CLS/PACS</a>
              </li>
              <li>
                <a href="#insurance" className="hover:text-sky-600 transition-colors">Bảo Hiểm Tư Nhân & Hóa Đơn VAT</a>
              </li>
              <li>
                <a href="#faq" className="hover:text-sky-600 transition-colors">Câu Hỏi Thường Gặp (FAQ)</a>
              </li>
            </ul>
          </div>

          {/* Col 4: Contact & Emergency */}
          <div className="space-y-3">
            <h3 className="text-xs font-bold text-slate-900 uppercase tracking-wider font-mono">
              Liên Hệ & Cấp Cứu 24/7
            </h3>
            <div className="space-y-2 text-xs">
              <div>
                <span className="text-[11px] text-slate-400 block">Tổng đài tiếp nhận đặt khám:</span>
                <a href="tel:19006868" className="font-bold text-sky-700 hover:text-sky-800 font-mono text-sm">
                  1900 6868 (Phím 1)
                </a>
              </div>
              <div>
                <span className="text-[11px] text-slate-400 block">Hotline Cấp cứu ngoại trú:</span>
                <a href="tel:02439998888" className="font-bold text-rose-600 hover:text-rose-700 font-mono text-sm">
                  024.3999.8888
                </a>
              </div>
              <div className="pt-1">
                <span className="text-[11px] text-slate-400 block">Giờ phục vụ:</span>
                <span className="font-medium text-slate-700">Thứ 2 - Chủ Nhật: 07:00 - 20:00</span>
              </div>
              <div className="pt-1">
                <span className="text-[11px] text-slate-400 block">Cơ sở chính:</span>
                <span className="text-slate-600">Số 88 Phố Y Dược, Q. Cầu Giấy, Hà Nội</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Sub-Footer Copyright & Legal Bar */}
      <div className="border-t border-slate-200 bg-slate-50/80 py-4 px-4 sm:px-6 lg:px-8">
        <div className="max-w-7xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-3 text-[11px] text-slate-500">
          <p>© 2026 MedFlowAI. Nền tảng Y tế Ngoại trú Tự nguyện Thông minh Chuẩn HL7/FHIR.</p>
          <div className="flex flex-wrap items-center gap-4">
            <a href="#privacy" className="hover:text-slate-800 transition-colors">Quy Định Bảo Mật Dữ Liệu Y Tế</a>
            <span>•</span>
            <a href="#terms" className="hover:text-slate-800 transition-colors">Điều Khoản Sử Dụng Dịch Vụ</a>
            <span>•</span>
            <a href="#quality" className="hover:text-slate-800 transition-colors">Cam Kết Chất Lượng ISO 15189</a>
          </div>
        </div>
      </div>
    </footer>
  )
}

export function ClinicalStatusBar() {
  return (
    <footer className="h-9 bg-white border-t border-slate-200 px-6 flex items-center justify-between text-[11px] text-slate-600 select-none z-30 flex-shrink-0">
      {/* Left: Engine Connectivity */}
      <div className="flex items-center gap-3">
        <div className="flex items-center gap-1.5 font-medium text-slate-700">
          <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
          <span>HIS Core: Kết nối ổn định <strong className="font-mono text-emerald-600 font-semibold">(18ms)</strong></span>
        </div>
        <span className="text-slate-300">•</span>
        <span className="text-slate-600">PACS/DICOM: <strong className="text-sky-600 font-semibold">Sẵn sàng</strong></span>
        <span className="text-slate-300 hidden md:inline">•</span>
        <span className="hidden md:inline text-slate-600">HL7 Engine: <span className="text-emerald-700 font-mono font-bold">ACTIVE</span></span>
      </div>

      {/* Center: Outpatient Tag */}
      <div className="hidden xl:flex items-center gap-2">
        <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-emerald-50 text-emerald-800 border border-emerald-200 uppercase tracking-wide">
          Chế độ 100% Non-BHYT
        </span>
        <span className="text-slate-500 text-[11px]">Bàn khám Ngoại trú P.102</span>
      </div>

      {/* Right: Keyboard Shortcuts */}
      <div className="flex items-center gap-3">
        <div className="hidden lg:flex items-center gap-2 text-slate-500 font-mono text-[10px]">
          <span><kbd className="px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-slate-700 font-bold">F1</kbd> Hàng đợi</span>
          <span><kbd className="px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-slate-700 font-bold">F2</kbd> Vào khám</span>
          <span><kbd className="px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-slate-700 font-bold">F4</kbd> Kê toa</span>
          <span><kbd className="px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-slate-700 font-bold">Ctrl+K</kbd> Tìm</span>
        </div>
      </div>
    </footer>
  )
}
