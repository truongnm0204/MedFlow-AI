import { ArrowRight, Check, Sparkles } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';

import { aiTriageSchema } from '../schemas/home.schemas';

interface TriageSuggestion {
  specialtyLabel: string;
  searchKeyword: string;
}

function resolveSpecialtySuggestion(symptomText: string): TriageSuggestion {
  const lower = symptomText.toLowerCase();
  if (
    lower.includes('bụng') ||
    lower.includes('dạ dày') ||
    lower.includes('tiêu hóa') ||
    lower.includes('ợ')
  ) {
    return {
      specialtyLabel: 'Chuyên khoa Nội Tiêu Hóa',
      searchKeyword: 'Nội Tiêu Hóa',
    };
  }
  if (lower.includes('tim') || lower.includes('ngực') || lower.includes('huyết áp')) {
    return {
      specialtyLabel: 'Chuyên khoa Tim Mạch',
      searchKeyword: 'Tim Mạch',
    };
  }
  if (
    lower.includes('họng') ||
    lower.includes('tai') ||
    lower.includes('mũi') ||
    lower.includes('ho')
  ) {
    return {
      specialtyLabel: 'Chuyên khoa Tai Mũi Họng',
      searchKeyword: 'Tai Mũi Họng',
    };
  }
  return {
    specialtyLabel: 'Khám Nội Tổng Quát',
    searchKeyword: 'Nội Tổng Quát',
  };
}

/** Banner định hướng chuyên khoa theo triệu chứng kèm liên kết tra cứu lịch bác sĩ thực tế. */
export function AiTriageBanner() {
  const [symptomInput, setSymptomInput] = useState('');
  const [triageResult, setTriageResult] = useState<TriageSuggestion | null>(null);
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleTriageSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setValidationError(null);

    const parsed = aiTriageSchema.safeParse({ symptomInput });
    if (!parsed.success) {
      setTriageResult(null);
      setValidationError(
        parsed.error.issues[0]?.message ?? 'Vui lòng nhập triệu chứng để phân luồng chuyên khoa.',
      );
      return;
    }

    setTriageResult(resolveSpecialtySuggestion(parsed.data.symptomInput));
  };

  return (
    <section className="border-b border-border bg-white py-10">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mx-auto max-w-4xl rounded-2xl border border-sky-200 bg-gradient-to-r from-sky-50 via-white to-cyan-50 p-6 shadow-sm">
          <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
            <div className="space-y-1">
              <div className="inline-flex items-center gap-1.5 rounded-full bg-sky-100 px-2.5 py-0.5 text-[10px] font-bold text-sky-800">
                <Sparkles className="h-3 w-3 text-primary" aria-hidden="true" /> Định Hướng Chuyên
                Khoa MedFlow
              </div>
              <h3 className="text-base font-bold text-navy sm:text-lg">
                Bạn chưa biết nên khám chuyên khoa nào?
              </h3>
              <p className="text-xs text-sub">
                Nhập ngắn gọn triệu chứng để nhận gợi ý chuyên khoa phù hợp và tra cứu danh sách bác
                sĩ đang có lịch khám thực tế.
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
                className="flex-1 rounded-lg border border-border bg-white px-3 py-2 text-xs text-ink placeholder-slate-400 focus:ring-2 focus:ring-primary focus:outline-none"
              />
              <button
                type="submit"
                className="rounded-lg bg-primary px-3.5 py-2 text-xs font-bold whitespace-nowrap text-white transition-colors hover:bg-primary-dark"
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
            <div className="mt-4 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-sky-200 bg-white p-3 text-xs font-medium text-navy shadow-2xs">
              <div className="flex items-center gap-2">
                <Check className="h-4 w-4 shrink-0 text-emerald-600" aria-hidden="true" />
                <span>
                  Gợi ý phân luồng: <strong>{triageResult.specialtyLabel}</strong> • Tra cứu danh
                  sách bác sĩ &amp; khung giờ thực tế trên hệ thống
                </span>
              </div>
              <Link
                to={`/doctors?keyword=${encodeURIComponent(triageResult.searchKeyword)}`}
                className="inline-flex items-center gap-1 rounded-lg bg-sky-50 px-2.5 py-1 text-[11px] font-bold text-primary hover:bg-sky-100"
              >
                <span>Tra cứu bác sĩ &amp; lịch trống</span>
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
