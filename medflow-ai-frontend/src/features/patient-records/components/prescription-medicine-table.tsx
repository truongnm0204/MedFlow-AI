import { Info, Pill } from 'lucide-react';

import type { PatientPrescriptionMedicine } from '../types/patient-records.types';

export interface PrescriptionMedicineTableProps {
  items: PatientPrescriptionMedicine[];
}

export function PrescriptionMedicineTable({ items }: PrescriptionMedicineTableProps) {
  if (items.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-white p-6 text-center text-xs text-sub">
        Đơn thuốc hiện không có danh mục thuốc hiển thị.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-2xl border border-border bg-white shadow-xs">
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-border text-left text-xs">
            <thead className="bg-canvas text-[11px] font-bold uppercase tracking-wide text-sub">
              <tr>
                <th scope="col" className="px-4 py-3">
                  STT
                </th>
                <th scope="col" className="px-4 py-3">
                  Tên thuốc & Hoạt chất
                </th>
                <th scope="col" className="px-4 py-3">
                  Số lượng
                </th>
                <th scope="col" className="px-4 py-3">
                  Liều dùng & Đường dùng
                </th>
                <th scope="col" className="px-4 py-3">
                  Hướng dẫn sử dụng
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border bg-white">
              {items.map((item, idx) => (
                <tr key={item.id} className="align-top transition-colors hover:bg-canvas/60">
                  <td className="px-4 py-3.5 font-mono font-semibold text-sub">
                    {String(idx + 1).padStart(2, '0')}
                  </td>
                  <td className="px-4 py-3.5">
                    <div className="flex items-start gap-2">
                      <Pill className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                      <div>
                        <p className="font-bold text-ink">
                          {item.medicineName}
                          {item.strength ? ` ${item.strength}` : ''}
                        </p>
                        <p className="mt-0.5 text-[11px] text-sub">
                          {[
                            item.medicineCode ? `Mã: ${item.medicineCode}` : null,
                            item.activeIngredient ? `Hoạt chất: ${item.activeIngredient}` : null,
                            item.dosageForm,
                          ]
                            .filter(Boolean)
                            .join(' • ')}
                        </p>
                      </div>
                    </div>
                  </td>
                  <td className="px-4 py-3.5 whitespace-nowrap">
                    <span className="rounded-lg bg-sky-50 px-2.5 py-1 font-mono text-xs font-bold text-primary">
                      {item.prescribedQuantity} {item.dispensingUnit ?? 'Đơn vị'}
                    </span>
                    {item.durationDays != null && (
                      <p className="mt-1 text-[11px] text-sub">Trong {item.durationDays} ngày</p>
                    )}
                  </td>
                  <td className="px-4 py-3.5 text-ink">
                    <p className="font-medium">
                      {item.doseScheduleText ??
                        (item.singleDoseQuantity != null
                          ? `${item.singleDoseQuantity} ${item.singleDoseUnit ?? item.dispensingUnit ?? ''}/lần`
                          : 'Theo chỉ định')}
                    </p>
                    <p className="mt-0.5 text-[11px] text-sub">
                      {[item.frequency, item.administrationRoute].filter(Boolean).join(' • ') ||
                        'Đường dùng tiêu chuẩn'}
                    </p>
                  </td>
                  <td className="px-4 py-3.5 text-ink">
                    {item.instructions ? (
                      <span className="font-medium text-navy">{item.instructions}</span>
                    ) : (
                      <span className="text-sub">Tuân thủ hướng dẫn của bác sĩ kê đơn</span>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="flex items-start gap-3 rounded-2xl border border-sky-200 bg-sky-50/70 p-4 text-xs text-ink">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <div className="space-y-1">
          <p className="font-bold text-navy">Lưu ý an toàn dùng thuốc</p>
          <p className="leading-relaxed text-sub">
            Uống thuốc đúng liều lượng và thời điểm được bác sĩ chỉ định. Không tự ý ngưng thuốc
            hoặc thay đổi liều; nếu xuất hiện dấu hiệu dị ứng bất thường, vui lòng liên hệ ngay cơ
            sở y tế gần nhất.
          </p>
        </div>
      </div>
    </div>
  );
}
