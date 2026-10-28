import { Sparkles } from 'lucide-react';
import { useState } from 'react';

import { aiTriageSchema } from '../schemas/home.schemas';

/** Banner trợ lý AI gợi ý chuyên khoa theo triệu chứng kèm kiểm tra hợp lệ đầu vào. */
export function AiTriageBanner() {
  const [symptomInput, setSymptomInput] = useState('');
  const [triageResult, setTriageResult] = useState<string | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleTriageSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const parsed = aiTriageSchema.safeParse({ symptomInput });
    if (!parsed.success) {
      setTriageResult(null);
      setValidationError(
        parsed.error.issues[0]?.message ?? 'Vui lòng nhập triệu chứng để AI phân luồng.',
      );
      return;
    }

    const lower = parsed.data.symptomInput.toLowerCase();
    if (
      lower.includes('bụng') ||
      lower.includes('dạ dày') ||
      lower.includes('tiêu hóa') ||
      lower.includes('ợ')
    ) {
      setTriageResult(
        'Gợi ý AI: Chuyên khoa Nội Tiêu Hóa • Bác sĩ đề xuất: TS. BS Nguyễn Văn Hùng (Còn lịch trống lúc 08:45 hôm nay)',
      );
    } else if (lower.includes('tim') || lower.includes('ngực') || lower.includes('huyết áp')) {
      setTriageResult(
        'Gợi ý AI: Chuyên khoa Tim Mạch • Bác sĩ đề xuất: BS. CKII Trần Mai Anh (Còn lịch trống lúc 09:15 hôm nay)',
      );
    } else if (
      lower.includes('họng') ||
      lower.includes('tai') ||
      lower.includes('mũi') ||
      lower.includes('ho')
    ) {
      setTriageResult(
        'Gợi ý AI: Chuyên khoa Tai Mũi Họng • Bác sĩ đề xuất: ThS. BS Lê Thu Hà (Còn lịch trống lúc 10:00 hôm nay)',
      );
    } else {
      setTriageResult(
        'Gợi ý AI: Khám Sức Khỏe Tổng Quát & Định Tuyến Lâm Sàng tại Bàn P.102 (Đang có sẵn khung giờ tiếp nhận)',
      );
    }
  };

  return (
    <section className="border-b border-slate-200 bg-white py-10">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-4xl rounded-2xl border border-sky-200 bg-gradient-to-r from-sky-50 via-white to-cyan-50 p-6 shadow-sm">
          <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
            <div className="space-y-1">
              <div className="inline-flex items-center gap-1.5 rounded-full bg-sky-100 px-2.5 py-0.5 text-[10px] font-bold text-sky-800">
                <Sparkles className="h-3 w-3 text-sky-600" aria-hidden="true" /> Trợ Lý Y Tế AI
                MedFlow
              </div>
              <h3 className="text-base font-bold text-navy sm:text-lg">
                Bạn chưa biết nên khám chuyên khoa nào?
              </h3>
              <p className="text-xs text-slate-600">
                Nhập ngắn gọn triệu chứng để AI phân luồng chính xác khoa khám và bác sĩ phù hợp
                trong 3 giây.
              </p>
            </div>

            <form
              onSubmit={handleTriageSubmit}
              noValidate
              className="flex max-w-md flex-1 items-center gap-2"
            >
              <input
                type="text"
                aria-label="Mô tả triệu chứng"
                value={symptomInput}
                onChange={(e) => {
                  setSymptomInput(e.target.value);
                  if (validationError) setValidationError(null);
                }}
                placeholder="VD: đau bụng trên rốn, ợ chua..."
                className="flex-1 rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-800 placeholder-slate-400 focus:ring-2 focus:ring-sky-500 focus:outline-none"
              />
              <button
                type="submit"
                className=" rounded-lg bg-sky-600 px-3.5 py-2 text-xs font-bold whitespace-nowrap text-white transition-colors hover:bg-sky-700"
              >
                Gợi ý ngay
              </button>
            </form>
          </div>

          {validationError && (
            <div
              role="alert"
              className="mt-3 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-xs font-medium text-rose-700"
            >
              {validationError}
            </div>
          )}

          {triageResult && (
            <div className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-sky-200 bg-white p-3 text-xs font-medium text-sky-900 shadow-2xs">
              <div className="flex items-center gap-2">
                <span className="font-bold text-emerald-600">✓</span>
                <span>{triageResult}</span>
              </div>
              <a
                href="#booking"
                className="rounded-lg bg-sky-50 px-2.5 py-1 text-[11px] font-bold text-sky-700 hover:bg-sky-100"
              >
                Đặt khám theo gợi ý →
              </a>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
