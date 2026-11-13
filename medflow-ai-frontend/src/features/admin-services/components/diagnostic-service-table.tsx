import { Pencil, Power } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type { DiagnosticServiceItem } from '../types/diagnostic-service.types';

interface DiagnosticServiceTableProps {
  services: DiagnosticServiceItem[];
  canManage?: boolean;
  onEdit?: (service: DiagnosticServiceItem) => void;
  onDeactivate?: (service: DiagnosticServiceItem) => void;
}

export function DiagnosticServiceTable({
  services,
  canManage = false,
  onEdit,
  onDeactivate,
}: DiagnosticServiceTableProps) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[960px] text-xs">
        <caption className="sr-only">Danh mục dịch vụ cận lâm sàng</caption>
        <thead>
          <tr className="border-b border-border bg-canvas text-left text-[11px] font-semibold tracking-wide text-sub uppercase">
            <th scope="col" className="px-4 py-3">
              Mã dịch vụ
            </th>
            <th scope="col" className="px-4 py-3">
              Tên dịch vụ
            </th>
            <th scope="col" className="px-4 py-3">
              Khoa phòng
            </th>
            <th scope="col" className="px-4 py-3">
              Chuyên khoa
            </th>
            <th scope="col" className="px-4 py-3 text-right">
              Đơn giá
            </th>
            <th scope="col" className="px-4 py-3">
              Xác nhận KQ
            </th>
            <th scope="col" className="px-4 py-3">
              Điểm phục vụ
            </th>
            <th scope="col" className="px-4 py-3">
              Trạng thái
            </th>
            {canManage && (
              <th scope="col" className="px-4 py-3">
                Thao tác
              </th>
            )}
          </tr>
        </thead>
        <tbody className="divide-y divide-border bg-white">
          {services.map((service) => {
            const pointLabels = service.servicePoints
              .map((sp) => sp.roomCode ?? sp.name)
              .join(', ');

            return (
              <tr key={service.id} className="transition-colors hover:bg-canvas/60">
                <td className="px-4 py-3.5 align-top font-mono font-semibold text-navy">
                  {service.serviceCode}
                </td>
                <td className="max-w-xs px-4 py-3.5 align-top">
                  <p className="font-semibold text-ink">{service.name}</p>
                  {service.description && (
                    <p
                      className="mt-0.5 line-clamp-1 text-[11px] text-sub"
                      title={service.description}
                    >
                      {service.description}
                    </p>
                  )}
                </td>
                <td className="px-4 py-3.5 align-top text-ink">{service.departmentName ?? '—'}</td>
                <td className="px-4 py-3.5 align-top text-sub">{service.specialtyName ?? '—'}</td>
                <td className="px-4 py-3.5 text-right align-top font-medium text-ink">
                  {service.priceFormatted !== null ? (
                    <span>{service.priceFormatted}</span>
                  ) : (
                    <Badge tone="amber">Chưa có giá</Badge>
                  )}
                </td>
                <td className="px-4 py-3.5 align-top">
                  {service.requiresResultVerification ? (
                    <Badge tone="sky">Cần xác nhận</Badge>
                  ) : (
                    <Badge tone="slate">Không</Badge>
                  )}
                </td>
                <td className="px-4 py-3.5 align-top">
                  {service.servicePointCount === 0 ? (
                    <span className="text-sub">Chưa gán điểm phục vụ</span>
                  ) : (
                    <div>
                      <p className="font-medium text-ink">
                        {service.activeServicePointCount}/{service.servicePointCount} hoạt động
                      </p>
                      {pointLabels && (
                        <p className="mt-0.5 font-mono text-[11px] text-sub">{pointLabels}</p>
                      )}
                    </div>
                  )}
                </td>
                <td className="px-4 py-3.5 align-top">
                  {service.status === 'ACTIVE' ? (
                    <Badge tone="emerald">Đang hoạt động</Badge>
                  ) : (
                    <Badge tone="slate">Ngừng hoạt động</Badge>
                  )}
                </td>
                {canManage && (
                  <td className="px-4 py-3.5 align-top">
                    {service.status !== 'INACTIVE' && (
                      <div className="flex gap-1">
                        <button
                          type="button"
                          className="rounded-lg p-1.5 text-navy hover:bg-canvas"
                          aria-label={`Sửa dịch vụ ${service.serviceCode}`}
                          onClick={() => onEdit?.(service)}
                        >
                          <Pencil aria-hidden="true" className="h-4 w-4" />
                        </button>
                        <button
                          type="button"
                          className="rounded-lg p-1.5 text-rose-700 hover:bg-canvas"
                          aria-label={`Ngừng hoạt động dịch vụ ${service.serviceCode}`}
                          onClick={() => onDeactivate?.(service)}
                        >
                          <Power aria-hidden="true" className="h-4 w-4" />
                        </button>
                      </div>
                    )}
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
