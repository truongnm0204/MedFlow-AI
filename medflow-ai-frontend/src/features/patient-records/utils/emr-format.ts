export function formatEmrDateVn(value: string | null | undefined): string {
  if (!value) return 'Chưa cập nhật';
  const trimmed = value.trim();
  if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
    const [year, month, day] = trimmed.split('-');
    return `${day}/${month}/${year}`;
  }
  const parsed = new Date(trimmed);
  if (Number.isNaN(parsed.getTime())) return trimmed;
  return new Intl.DateTimeFormat('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(parsed);
}

export function formatEmrDateTimeVn(value: string | null | undefined): string {
  if (!value) return 'Chưa cập nhật';
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return formatEmrDateVn(value);
  return new Intl.DateTimeFormat('vi-VN', {
    hour: '2-digit',
    minute: '2-digit',
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
  }).format(parsed);
}

export function getGenderLabelVi(gender: string | null | undefined): string {
  const key = gender?.trim().toUpperCase();
  if (key === 'MALE' || key === 'NAM') return 'Nam';
  if (key === 'FEMALE' || key === 'NỮ' || key === 'NU') return 'Nữ';
  if (key === 'OTHER') return 'Khác';
  return 'Chưa cập nhật';
}

export interface StatusBadgeMeta {
  label: string;
  className: string;
}

export function getPrescriptionStatusMeta(status: string | null | undefined): StatusBadgeMeta {
  const key = status?.trim().toUpperCase();
  switch (key) {
    case 'DISPENSED':
      return {
        label: 'Đã cấp phát',
        className: 'border-emerald-200 bg-emerald-50 text-emerald-700',
      };
    case 'READY':
      return {
        label: 'Sẵn sàng nhận thuốc',
        className: 'border-sky-200 bg-sky-50 text-sky-700',
      };
    case 'PREPARING':
      return {
        label: 'Đang chuẩn bị thuốc',
        className: 'border-amber-200 bg-amber-50 text-amber-700',
      };
    case 'ISSUED':
    default:
      return {
        label: 'Đã kê đơn',
        className: 'border-indigo-200 bg-indigo-50 text-indigo-700',
      };
  }
}
