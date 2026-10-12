import { JOURNEY_STEPS } from '../data/home-catalog'

/** Quy trình 4 bước khám không giấy tờ (anchor #about). */
export function JourneySection() {
  return (
    <section id="about" className="py-16 bg-[#F8FAFC] border-t border-slate-200 scroll-mt-24">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-2xl mx-auto mb-12 space-y-2">
          <div className="inline-flex items-center gap-1.5 px-3 py-0.5 rounded-full bg-emerald-50 text-emerald-800 text-xs font-semibold border border-emerald-200">
            QUY TRÌNH TINH GỌN
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-[#0A2540] tracking-tight">
            4 Bước Khám Ngoại Trú Không Giấy Tờ
          </h2>
          <p className="text-xs sm:text-sm text-slate-500">
            Tối ưu hóa toàn bộ trải nghiệm từ đặt lịch đến nhận kết quả chỉ trên điện thoại.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-4 gap-6">
          {JOURNEY_STEPS.map((item) => (
            <div key={item.step} className="bg-white rounded-2xl border border-slate-200 p-6 relative shadow-sm">
              <div className="w-8 h-8 rounded-lg bg-sky-50 text-sky-700 font-black text-sm flex items-center justify-center font-mono mb-3">
                {item.step}
              </div>
              <h4 className="text-sm font-bold text-slate-900 mb-1">{item.title}</h4>
              <p className="text-xs text-slate-500 leading-relaxed">{item.desc}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  )
}
