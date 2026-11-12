import type { ConsultationDiagnosticResultItemDto } from '../types/review-diagnostic.types';

export function formatResultDateTime(value: string | null): string {
  if (!value) return '—';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toLocaleString('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function getResultStatusBadge(item: ConsultationDiagnosticResultItemDto): {
  label: string;
  tone: 'emerald' | 'amber' | 'rose' | 'slate';
} {
  if (item.resultStatus === 'CANCELLED') return { label: 'ĐÃ HỦY', tone: 'slate' };
  if (item.resultStatus === 'VERIFIED') return { label: 'ĐÃ KÝ SỐ DUYỆT', tone: 'emerald' };
  if (item.isResultReady) return { label: 'ĐÃ CÓ KẾT QUẢ', tone: 'emerald' };
  return { label: 'CHỜ KẾT QUẢ', tone: 'amber' };
}

export function isSafeReportUrl(url: string | null): url is string {
  if (!url) return false;
  return url.startsWith('https://') || url.startsWith('http://') || url.startsWith('/');
}

export function splitResultText(text: string | null): string[] {
  if (!text) return [];
  return text
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}
