import { MOCK_SPECIALTIES } from '../data/home-catalog';

/** Danh mục chuyên khoa ngoại trú + bảng giá (anchor #specialties). */
export function SpecialtiesSection() {
  return (
    <section id="specialties" className="py-16 bg-canvas scroll-mt-24">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="text-center max-w-2xl mx-auto mb-12 space-y-2">
          <div className="inline-flex items-center gap-1.5 px-3 py-0.5 rounded-full bg-slate-100 text-slate-700 text-xs font-semibold">
            DANH MỤC DỊCH VỤ NGOẠI TRÚ
          </div>
          <h2 className="text-2xl sm:text-3xl font-extrabold text-navy tracking-tight">
            Chuyên Khoa Kỹ Thuật Cao & Bảng Giá Niêm Yết
          </h2>
          <p className="text-xs sm:text-sm text-slate-500">
            100% Khám tự nguyện theo yêu cầu, trang thiết bị đồng bộ thế hệ mới nhất.
          </p>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
          {MOCK_SPECIALTIES.map((spec) => (
            <div
              key={spec.id}
              className="bg-white rounded-2xl border border-slate-200 p-6 shadow-sm hover:shadow-md hover:border-sky-300 transition-all space-y-4 flex flex-col justify-between"
            >
              <div className="space-y-3">
                <div className="flex items-center justify-between">
                  <div className="w-12 h-12 rounded-xl bg-sky-50 border border-sky-100 flex items-center justify-center">
                    {spec.icon}
                  </div>
                  <span className="px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-700 text-[10px] font-bold border border-emerald-200">
                    {spec.tag}
                  </span>
                </div>

                <div>
                  <h3 className="text-base font-bold text-slate-900">{spec.name}</h3>
                  <p className="text-xs text-slate-500 mt-1 leading-relaxed">{spec.desc}</p>
                </div>
              </div>

              <div className="pt-4 border-t border-slate-100 flex items-center justify-between">
                <div>
                  <span className="text-[10px] text-slate-400 block font-mono">
                    Giá khám niêm yết:
                  </span>
                  <span className="text-sm font-bold text-sky-700 font-mono">{spec.price}</span>
                </div>
                <a
                  href="#booking"
                  className="px-3 py-1.5 rounded-lg bg-sky-50 text-sky-700 hover:bg-sky-600 hover:text-white text-xs font-bold border border-sky-200 transition-colors"
                >
                  Đặt khám
                </a>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
