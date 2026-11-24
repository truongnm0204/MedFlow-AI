export const HOSPITAL_TIME_ZONE = 'Asia/Ho_Chi_Minh';

interface HospitalDateParts {
  year: string;
  month: string;
  day: string;
}

function extractHospitalDateParts(now: Date = new Date()): HospitalDateParts {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: HOSPITAL_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  }).formatToParts(now);

  const year = parts.find((part) => part.type === 'year')?.value ?? '1970';
  const month = parts.find((part) => part.type === 'month')?.value ?? '01';
  const day = parts.find((part) => part.type === 'day')?.value ?? '01';

  return { year, month, day };
}

/** Trả về ngày hiện tại theo định dạng ISO `YYYY-MM-DD` tại múi giờ bệnh viện (`Asia/Ho_Chi_Minh`). */
export function getHospitalTodayIso(now: Date = new Date()): string {
  const { year, month, day } = extractHospitalDateParts(now);
  return `${year}-${month}-${day}`;
}

/** Trả về ngày hiện tại theo định dạng `DD/MM/YYYY` tại múi giờ bệnh viện (`Asia/Ho_Chi_Minh`). */
export function formatHospitalTodayVn(now: Date = new Date()): string {
  const { year, month, day } = extractHospitalDateParts(now);
  return `${day}/${month}/${year}`;
}
