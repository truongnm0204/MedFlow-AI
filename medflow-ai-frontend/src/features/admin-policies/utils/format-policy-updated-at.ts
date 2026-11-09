export function formatPolicyUpdatedAt(isoString: string | null): string {
  if (!isoString) {
    return 'Đang áp dụng giá trị mặc định hệ thống';
  }
  const date = new Date(isoString);
  if (Number.isNaN(date.getTime())) {
    return 'Đang áp dụng giá trị mặc định hệ thống';
  }
  return new Intl.DateTimeFormat('vi-VN', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
    timeZone: 'Asia/Ho_Chi_Minh',
  }).format(date);
}
