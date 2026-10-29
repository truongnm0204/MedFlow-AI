import { ExternalLink } from 'lucide-react';
import { useState } from 'react';

import { Button } from '@/shared/ui/button';

export function ServiceCreateSidebar() {
  const [notice, setNotice] = useState<string | null>(null);

  const handleOpenPriceTable = () => {
    setNotice('Chuyển hướng liên kết đến Bảng giá theo cơ sở (UC52) trong giai đoạn tiếp theo.');
    setTimeout(() => setNotice(null), 4000);
  };

  const handleOpenBookingPolicy = () => {
    setNotice(
      'Chuyển hướng liên kết đến Cấu hình Chính sách đặt hẹn (UC54) trong giai đoạn tiếp theo.',
    );
    setTimeout(() => setNotice(null), 4000);
  };

  return (
    <aside className="space-y-6">
      {notice && (
        <div
          role="status"
          className="rounded-lg border border-sky-200 bg-sky-50 p-3 text-xs text-sky-800 shadow-xs"
        >
          {notice}
        </div>
      )}

      {/* CARD 1: FACILITY PRICING REFERENCE */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-sm">
        <div>
          <h2 className="text-sm font-bold text-ink">Giá dịch vụ theo cơ sở</h2>
          <p className="mt-1 text-[11px] text-slate-500">
            Giá được quản lý tập trung tại UC52; màn này chỉ tham chiếu trạng thái.
          </p>
          <div className="mt-2.5">
            <span className="inline-block rounded bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-600">
              Chỉ xem - UC52
            </span>
          </div>
        </div>

        <div className="mt-5 space-y-3 border-t border-slate-100 pt-4">
          <h3 className="text-xs font-semibold text-ink">Giá hiện hành</h3>

          <div className="rounded-lg border border-slate-200 bg-white p-3.5 shadow-xs">
            <div className="font-bold text-xs text-ink">Quản lý tại UC52</div>
            <div className="mt-1 text-[11px] text-slate-500 leading-relaxed">
              Chỉ xem tại đây. Thêm hoặc cập nhật đơn giá trong Bảng giá theo cơ sở (UC52).
            </div>
          </div>

          <div className="rounded-lg border border-slate-100 bg-slate-50/80 p-3.5 text-xs">
            <div className="font-bold text-slate-700 text-xs mb-2">Thông tin giá hiện hành</div>
            <ul className="space-y-1.5 text-[11px] text-slate-600">
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Cơ sở áp dụng · Hà Nội HN-01</span>
              </li>
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Ngày hiệu lực · tra cứu trong UC52</span>
              </li>
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Mức giá · xem trong UC52</span>
              </li>
            </ul>
          </div>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={handleOpenPriceTable}
            className="w-full justify-center gap-1.5 text-xs"
          >
            <span>Mở bảng giá theo cơ sở</span>
            <ExternalLink className="h-3.5 w-3.5 text-slate-400" />
          </Button>

          <p className="text-[10px] text-slate-400 text-center">
            Mức giá tra cứu theo cơ sở và ngày hiệu lực trong UC52.
          </p>
        </div>
      </section>

      {/* CARD 2: BOOKING POLICY REFERENCE */}
      <section className="rounded-xl border border-border bg-white p-6 shadow-sm">
        <div>
          <h2 className="text-sm font-bold text-ink">Chính sách đặt hẹn theo cơ sở</h2>
          <p className="mt-1 text-[11px] text-slate-500">
            Cấu hình quy tắc đặt lịch trong phạm vi cơ sở.
          </p>
          <div className="mt-2.5">
            <span className="inline-block rounded bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-600">
              Quản lý riêng - UC54
            </span>
          </div>
        </div>

        <div className="mt-5 space-y-3 border-t border-slate-100 pt-4">
          <div className="rounded-lg border border-slate-100 bg-slate-50/80 p-3.5 text-xs">
            <div className="font-bold text-slate-700 text-xs mb-2">Thông tin quản lý</div>
            <ul className="space-y-1.5 text-[11px] text-slate-600">
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Khoảng ngày được đặt trước</span>
              </li>
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Điều kiện đổi / hủy và thời gian có mặt</span>
              </li>
              <li className="flex items-center gap-1.5">
                <span className="h-1 w-1 rounded-full bg-slate-400" />
                <span>Không yêu cầu thanh toán trước</span>
              </li>
            </ul>
          </div>

          <Button
            type="button"
            variant="secondary"
            size="sm"
            onClick={handleOpenBookingPolicy}
            className="w-full justify-center gap-1.5 text-xs"
          >
            <span>Mở chính sách đặt lịch</span>
            <ExternalLink className="h-3.5 w-3.5 text-slate-400" />
          </Button>
        </div>
      </section>
    </aside>
  );
}
