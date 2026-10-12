import { useState } from 'react'

/** Banner trợ lý AI gợi ý chuyên khoa theo triệu chứng. */
export function AiTriageBanner() {
  const [symptomInput, setSymptomInput] = useState('')
  const [triageResult, setTriageResult] = useState<string | null>(null)

  const handleTriageSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!symptomInput.trim()) return

    const lower = symptomInput.toLowerCase()
    if (lower.includes('bụng') || lower.includes('dạ dày') || lower.includes('tiêu hóa') || lower.includes('ợ')) {
      setTriageResult('Gợi ý AI: Chuyên khoa Nội Tiêu Hóa • Bác sĩ đề xuất: TS. BS Nguyễn Văn Hùng (Còn lịch trống lúc 08:45 hôm nay)')
    } else if (lower.includes('tim') || lower.includes('ngực') || lower.includes('huyết áp')) {
      setTriageResult('Gợi ý AI: Chuyên khoa Tim Mạch • Bác sĩ đề xuất: BS. CKII Trần Mai Anh (Còn lịch trống lúc 09:15 hôm nay)')
    } else if (lower.includes('họng') || lower.includes('tai') || lower.includes('mũi') || lower.includes('ho')) {
      setTriageResult('Gợi ý AI: Chuyên khoa Tai Mũi Họng • Bác sĩ đề xuất: ThS. BS Lê Thu Hà (Còn lịch trống lúc 10:00 hôm nay)')
    } else {
      setTriageResult('Gợi ý AI: Khám Sức Khỏe Tổng Quát & Định Tuyến Lâm Sàng tại Bàn P.102 (Đang có sẵn khung giờ tiếp nhận)')
    }
  }

  return (
    <section className="py-10 bg-white border-b border-slate-200">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="max-w-4xl mx-auto bg-gradient-to-r from-sky-50 via-white to-cyan-50 rounded-2xl border border-sky-200 p-6 shadow-sm">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
            <div className="space-y-1">
              <div className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-sky-100 text-sky-800 text-[10px] font-bold">
                <span>⚡</span> Trợ Lý Y Tế AI MedFlow
              </div>
              <h3 className="text-base sm:text-lg font-bold text-[#0A2540]">
                Bạn chưa biết nên khám chuyên khoa nào?
              </h3>
              <p className="text-xs text-slate-600">
                Nhập ngắn gọn triệu chứng để AI phân luồng chính xác khoa khám và bác sĩ phù hợp trong 3 giây.
              </p>
            </div>

            <form onSubmit={handleTriageSubmit} className="flex-1 max-w-md flex items-center gap-2">
              <input
                type="text"
                value={symptomInput}
                onChange={(e) => setSymptomInput(e.target.value)}
                placeholder="VD: đau bụng trên rốn, ợ chua..."
                className="flex-1 text-xs bg-white border border-slate-300 rounded-lg px-3 py-2 text-slate-800 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-sky-500"
              />
              <button
                type="submit"
                className="px-3.5 py-2 bg-sky-600 hover:bg-sky-700 text-white text-xs font-bold rounded-lg transition-colors whitespace-nowrap"
              >
                Gợi ý ngay
              </button>
            </form>
          </div>

          {triageResult && (
            <div className="mt-4 p-3 rounded-xl bg-white border border-sky-200 text-xs text-sky-900 font-medium flex items-center gap-2 shadow-2xs">
              <span className="text-emerald-600 font-bold">✓</span>
              <span>{triageResult}</span>
            </div>
          )}
        </div>
      </div>
    </section>
  )
}
