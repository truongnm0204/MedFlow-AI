import { Bell, CheckCheck, Clock, Info } from 'lucide-react';
import { useState } from 'react';

import { useClickOutside } from '@/shared/hooks/use-click-outside';

import type { AppointmentReminder, PatientNotification } from '../types/appointment-reminder.types';
import { AppointmentReminderModal } from './appointment-reminder-modal';

interface NotificationBellPopoverProps {
  initialNotifications?: PatientNotification[];
}

export function NotificationBellPopover({
  initialNotifications = [],
}: NotificationBellPopoverProps = {}) {
  const [isOpen, setIsOpen] = useState(false);
  const [notifications, setNotifications] = useState<PatientNotification[]>(initialNotifications);
  const [selectedReminder, setSelectedReminder] = useState<AppointmentReminder | null>(null);

  const containerRef = useClickOutside<HTMLDivElement>(() => setIsOpen(false));

  const unreadCount = notifications.filter((n) => !n.isRead).length;

  const handleMarkAllAsRead = () => {
    setNotifications((prev) => prev.map((item) => ({ ...item, isRead: true })));
  };

  const handleNotificationClick = (item: PatientNotification) => {
    setNotifications((prev) => prev.map((n) => (n.id === item.id ? { ...n, isRead: true } : n)));
    if (item.appointmentReminder) {
      setSelectedReminder(item.appointmentReminder);
      setIsOpen(false);
    }
  };

  return (
    <>
      <div className="relative" ref={containerRef}>
        <button
          type="button"
          onClick={() => setIsOpen((prev) => !prev)}
          aria-label={
            unreadCount > 0 ? `Thông báo: có ${unreadCount} thông báo chưa đọc` : 'Thông báo'
          }
          aria-expanded={isOpen}
          aria-haspopup="true"
          className="relative rounded-xl p-2.5 text-sub transition-colors hover:bg-slate-100 hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-hidden"
        >
          <Bell className="h-5 w-5" aria-hidden="true" />
          {unreadCount > 0 && (
            <span
              data-testid="unread-badge"
              className="animate-in zoom-in-75 absolute top-1.5 right-1.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-rose-500 px-1 font-mono text-[10px] font-bold text-white shadow-xs"
            >
              {unreadCount}
            </span>
          )}
        </button>

        {isOpen && (
          <div
            role="menu"
            aria-label="Danh sách thông báo"
            className="animate-in fade-in zoom-in-95 absolute right-0 z-50 mt-2 w-80 overflow-hidden rounded-2xl border border-border bg-white shadow-xl duration-150 sm:w-96"
          >
            {/* Header */}
            <div className="flex items-center justify-between border-b border-border bg-canvas p-4">
              <div className="flex items-center gap-2">
                <span className="text-sm font-bold text-ink">Thông báo</span>
                {unreadCount > 0 && (
                  <span className="rounded-full bg-rose-100 px-2 py-0.5 font-mono text-xs font-bold text-rose-700">
                    {unreadCount} mới
                  </span>
                )}
              </div>
              {unreadCount > 0 && (
                <button
                  type="button"
                  onClick={handleMarkAllAsRead}
                  className="flex items-center gap-1 text-xs font-medium text-primary transition-colors hover:text-primary-dark"
                >
                  <CheckCheck className="h-3.5 w-3.5" aria-hidden="true" />
                  Đọc tất cả
                </button>
              )}
            </div>

            {/* List */}
            <div className="max-h-[360px] divide-y divide-slate-100 overflow-y-auto">
              {notifications.length === 0 ? (
                <div className="p-8 text-center text-xs text-sub">Bạn không có thông báo nào.</div>
              ) : (
                notifications.map((item) => {
                  const isReminder = item.category === 'APPOINTMENT_REMINDER';
                  return (
                    <div
                      key={item.id}
                      role="menuitem"
                      tabIndex={0}
                      onClick={() => handleNotificationClick(item)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          handleNotificationClick(item);
                        }
                      }}
                      className={`flex cursor-pointer items-start gap-3 p-3.5 transition-colors hover:bg-canvas ${
                        !item.isRead ? 'bg-sky-50/50' : 'bg-white'
                      }`}
                    >
                      <div
                        className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-xl border ${
                          isReminder
                            ? 'border-amber-200 bg-amber-100 text-amber-700'
                            : 'border-border bg-slate-100 text-sub'
                        }`}
                      >
                        {isReminder ? (
                          <Clock className="h-4 w-4" aria-hidden="true" />
                        ) : (
                          <Info className="h-4 w-4" aria-hidden="true" />
                        )}
                      </div>

                      <div className="min-w-0 flex-1">
                        <div className="flex items-center justify-between gap-2">
                          <p
                            className={`truncate text-xs ${
                              !item.isRead ? 'font-bold text-ink' : 'font-medium text-slate-700'
                            }`}
                          >
                            {item.title}
                          </p>
                          <span className="shrink-0 text-[10px] text-sub">{item.createdAt}</span>
                        </div>
                        <p className="mt-0.5 line-clamp-2 text-[11px] leading-relaxed text-sub">
                          {item.message}
                        </p>
                        {item.appointmentReminder && (
                          <span className="mt-1.5 inline-block text-[10px] font-semibold text-primary">
                            Xem chi tiết lịch hẹn →
                          </span>
                        )}
                      </div>

                      {!item.isRead && (
                        <span
                          className="mt-2 h-2 w-2 shrink-0 rounded-full bg-primary"
                          aria-label="Chưa đọc"
                        />
                      )}
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}
      </div>

      {selectedReminder && (
        <AppointmentReminderModal
          isOpen={!!selectedReminder}
          onClose={() => setSelectedReminder(null)}
          reminder={selectedReminder}
        />
      )}
    </>
  );
}
