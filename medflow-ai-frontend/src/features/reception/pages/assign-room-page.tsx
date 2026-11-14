import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';

import { Button } from '@/shared/ui/button';

import { AssignRoomSelector } from '../components/assign-room-selector';
import { ReceptionSidebar } from '../components/reception-sidebar';
import { ASSIGN_ROOM_SERVICE, AVAILABLE_ROOMS_MOCK } from '../data/assign-room-mock';
import type { AssignRoomPayload } from '../types/assign-room.types';

export function AssignRoomPage() {
  const navigate = useNavigate();
  const [selectedRoomId, setSelectedRoomId] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState<AssignRoomPayload | null>(null);

  const selectedRoom = AVAILABLE_ROOMS_MOCK.find((r) => r.id === selectedRoomId) ?? null;

  const handleConfirm = () => {
    if (!selectedRoom) return;
    setConfirmed({ roomId: selectedRoom.id, serviceName: ASSIGN_ROOM_SERVICE.serviceName });
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
            Phân luồng Ca khám & Gán Buồng khám theo Tải lượng (SCR-VIS-01)
          </h1>
        </header>

        <main className="p-6 flex-1 max-w-4xl w-full mx-auto space-y-6">
          <AssignRoomSelector
            serviceName={ASSIGN_ROOM_SERVICE.serviceName}
            requirement={ASSIGN_ROOM_SERVICE.requirement}
            rooms={AVAILABLE_ROOMS_MOCK}
            selectedRoomId={selectedRoomId}
            onSelect={(id) => {
              setSelectedRoomId(id);
              setConfirmed(null);
            }}
          />

          <div className="rounded-2xl border border-border bg-white p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="text-sm text-ink" aria-live="polite">
              {confirmed && selectedRoom
                ? `Đã gán buồng khám: ${selectedRoom.name}`
                : selectedRoom
                  ? `Buồng đã chọn: ${selectedRoom.name} (${selectedRoom.location})`
                  : 'Chưa chọn buồng khám'}
            </div>
            <Button
              variant="primary"
              size="sm"
              disabled={!selectedRoom}
              onClick={handleConfirm}
              aria-label="Xác nhận gán buồng khám"
            >
              Xác nhận gán buồng khám
            </Button>
          </div>
        </main>
      </div>
    </div>
  );
}
