import { ArrowLeft, RefreshCw } from 'lucide-react';
import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { AssignRoomSelector } from '../components/assign-room-selector';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { useAssignRoomMutation, useAvailableRooms } from '../hooks/use-reception-api';

export function AssignRoomPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const visitId = searchParams.get('visitId')?.trim() ?? '';

  const roomsQuery = useAvailableRooms(visitId);
  const assignRoom = useAssignRoomMutation(visitId);
  const [selectedRoomId, setSelectedRoomId] = useState<string | null>(null);

  const rooms = roomsQuery.data?.rooms ?? [];
  const selectedRoom = rooms.find((room) => room.id === selectedRoomId) ?? null;

  const handleConfirm = () => {
    if (!selectedRoom || !visitId) return;
    assignRoom.mutate(selectedRoom.id);
  };

  return (
    <div className="flex min-h-screen bg-canvas font-sans text-ink antialiased">
      <ReceptionSidebar
        activeItem="dashboard"
        onItemSelect={(item) => {
          if (item === 'dashboard') navigate('/reception');
          if (item === 'patients') navigate('/reception/patients');
          if (item === 'identity-claims') navigate('/reception/identity-claims');
        }}
      />
      <div className="flex-1 flex flex-col min-w-0">
        <header className="h-16 bg-white border-b border-border px-6 flex items-center gap-3">
          <button
            type="button"
            onClick={() => navigate('/reception')}
            aria-label="Quay lại dashboard tiếp đón"
            className="p-2 rounded-xl text-sub hover:text-navy hover:bg-canvas transition-colors"
          >
            <ArrowLeft className="w-5 h-5" aria-hidden="true" />
          </button>
          <h1 className="text-sm font-bold text-navy">
            Phân luồng Ca khám &amp; Gán Buồng khám theo Tải lượng (SCR-VIS-01)
          </h1>
        </header>

        <main className="p-6 flex-1 max-w-4xl w-full mx-auto space-y-6">
          {!visitId ? (
            <Alert tone="warning">
              Thiếu mã lượt khám. Hãy điều phối bác sĩ trước khi gán buồng khám.
            </Alert>
          ) : null}

          {roomsQuery.isPending && visitId ? (
            <div className="flex items-center justify-center gap-2 py-16 text-xs text-sub">
              <Spinner className="h-4 w-4" />
              Đang tải buồng khám tương thích...
            </div>
          ) : null}

          {roomsQuery.isError ? (
            <div className="space-y-3">
              <Alert tone="error">{getErrorMessage(roomsQuery.error)}</Alert>
              <Button
                variant="secondary"
                size="sm"
                onClick={() => void roomsQuery.refetch()}
                loading={roomsQuery.isFetching}
              >
                <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                Thử lại
              </Button>
            </div>
          ) : null}

          {roomsQuery.isSuccess ? (
            <AssignRoomSelector
              serviceName={roomsQuery.data.departmentName ?? 'Buồng khám lâm sàng'}
              requirement="Chỉ hiển thị buồng khám tương thích và còn chỗ cho lượt khám này."
              rooms={rooms}
              selectedRoomId={selectedRoomId}
              onSelect={(id) => {
                setSelectedRoomId(id);
                assignRoom.reset();
              }}
            />
          ) : null}

          {roomsQuery.isSuccess && rooms.length === 0 ? (
            <Alert tone="info">Chưa có buồng khám tương thích còn chỗ.</Alert>
          ) : null}

          {assignRoom.isError ? (
            <Alert tone="error">{getErrorMessage(assignRoom.error)}</Alert>
          ) : null}

          {assignRoom.data ? (
            <Alert tone="success">
              Đã gán buồng khám {assignRoom.data.roomName}
              {assignRoom.data.queueNumber !== null
                ? ` — số thứ tự ${assignRoom.data.queueNumber}`
                : ''}
              .
            </Alert>
          ) : null}

          <div className="rounded-2xl border border-border bg-white p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="text-sm text-ink" aria-live="polite">
              {assignRoom.data
                ? `Đã gán buồng khám: ${assignRoom.data.roomName}`
                : selectedRoom
                  ? `Buồng đã chọn: ${selectedRoom.name} (${selectedRoom.location})`
                  : 'Chưa chọn buồng khám'}
            </div>
            <div className="flex items-center gap-2">
              {assignRoom.data ? (
                <Button variant="secondary" size="sm" onClick={() => navigate('/reception/queue')}>
                  Xem hàng đợi
                </Button>
              ) : null}
              <Button
                variant="primary"
                size="sm"
                disabled={!selectedRoom}
                loading={assignRoom.isPending}
                onClick={handleConfirm}
                aria-label="Xác nhận gán buồng khám"
              >
                Xác nhận gán buồng khám
              </Button>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
}
