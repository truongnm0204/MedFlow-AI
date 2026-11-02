import { Monitor, Volume2 } from 'lucide-react';

import type { AppointmentQueueStatus } from '../types/appointment-queue.types';

interface QueueCallBoardProps {
  data: AppointmentQueueStatus;
}

export function QueueCallBoard({ data }: QueueCallBoardProps) {
  const shortRoomTitle = data.roomName.split(' - ')[0] ?? data.roomName;

  return (
    <section
      aria-label="Bảng điện tử gọi số trực tiếp"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border pb-4">
        <div className="flex items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-primary">
            <Monitor className="h-5 w-5" aria-hidden="true" />
          </div>
          <div>
            <h2 className="text-sm font-bold text-ink uppercase">
              Bảng Điện Tử Gọi Số Trực Tiếp Tại Cửa {shortRoomTitle}
            </h2>
            <p className="text-xs text-sub">
              Đồng bộ tín hiệu màn hình LED phòng khám theo thời gian thực.
            </p>
          </div>
        </div>

        <span className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-3 py-1 text-xs font-bold text-emerald-700">
          <span className="h-2 w-2 rounded-full bg-emerald-500" aria-hidden="true" />
          <span>Realtime</span>
        </span>
      </div>

      <div className="mt-5 grid gap-3 sm:grid-cols-3">
        <div className="rounded-xl border border-border bg-slate-50 p-4 text-center">
          <p className="text-[11px] font-bold tracking-wider text-sub uppercase">ĐANG KHÁM</p>
          <p className="mt-2 font-mono text-2xl font-extrabold text-ink">
            STT: {data.servingQueueNumber ?? '---'}
          </p>
        </div>

        <div className="rounded-xl border border-sky-200 bg-sky-50 p-4 text-center text-primary">
          <p className="text-[11px] font-bold tracking-wider uppercase">CHUẨN BỊ</p>
          <p className="mt-2 font-mono text-2xl font-extrabold">
            STT: {data.nextQueueNumber ?? '---'}
          </p>
        </div>

        <div className="rounded-xl border-2 border-emerald-400 bg-emerald-50 p-4 text-center text-emerald-700">
          <p className="text-[11px] font-bold tracking-wider uppercase">LƯỢT KHÁM CỦA BẠN</p>
          <p className="mt-2 font-mono text-2xl font-extrabold">STT: {data.queueNumber}</p>
          <span className="mt-2 inline-block rounded-full bg-white px-2.5 py-0.5 text-[10px] font-bold text-emerald-800">
            {data.estimatedWaitLabel ?? 'Chưa có ước tính thời gian chờ'}
          </span>
        </div>
      </div>

      <p className="mt-4 flex items-start gap-2 text-xs leading-relaxed text-sub">
        <Volume2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
        <span>
          Thời gian chờ chỉ hiển thị là ước tính khi có đủ dữ liệu. Thứ tự gọi được điều phối theo
          ưu tiên lâm sàng, lượt quay lại, bệnh nhân đến trễ và khả năng lấp khoảng trống.
        </span>
      </p>
    </section>
  );
}
