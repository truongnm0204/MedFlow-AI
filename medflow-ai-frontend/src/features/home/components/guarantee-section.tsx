/** Cam kết minh bạch viện phí Non-BHYT (anchor #services). */
export function GuaranteeSection() {
  return (
    <section id="services" className="py-12 bg-white border-t border-slate-200 scroll-mt-24">
      <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="p-6 sm:p-8 rounded-2xl bg-gradient-to-r from-emerald-50/50 via-white to-sky-50/50 border border-emerald-200 flex flex-col md:flex-row items-center justify-between gap-6">
          <div className="space-y-2">
            <div className="inline-flex items-center gap-2 text-emerald-800 text-xs font-bold">
              <span className="w-2.5 h-2.5 rounded-full bg-emerald-500" />
              CAM KẾT 100% MINH BẠCH VIỆN PHÍ (KHÔNG BHYT)
            </div>
            <h3 className="text-lg sm:text-xl font-extrabold text-[#0A2540]">
              Không Phụ Phí Ẩn • Xuất Hóa Đơn VAT Điện Tử Ngay Sau Khám
            </h3>
            <p className="text-xs text-slate-600 max-w-2xl leading-relaxed">
              MedFlowAI phục vụ khám tự nguyện 100% chất lượng cao. Chúng tôi cung cấp đầy đủ bảng
              kê chi tiết viện phí và hỗ trợ giấy tờ thanh toán bảo hiểm tư nhân (Bảo Việt, PTI,
              Manulife, Prudential, Liberty...).
            </p>
          </div>

          <a
            href="#services"
            className="px-5 py-2.5 bg-emerald-600 hover:bg-emerald-700 text-white text-xs font-bold rounded-xl whitespace-nowrap shadow-sm transition-colors"
          >
            Xem Bảng Giá Đầy Đủ
          </a>
        </div>
      </div>
    </section>
  );
}
