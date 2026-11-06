import { useState } from 'react';
import { Bell, CheckCheck, Clock, Info } from 'lucide-react';

import { useClickOutside } from '@/shared/hooks/use-click-outside';

import { PATIENT_NOTIFICATIONS_MOCK } from '../data/appointment-reminder-mock';
import type { AppointmentReminder, PatientNotification } from '../types/appointment-reminder.types';
import { AppointmentReminderModal } from './appointment-reminder-modal';

export function NotificationBellPopover() {
  const [isOpen, setIsOpen] = useState(false);
  const [notifications, setNotifications] = useState<PatientNotification[]>(
    PATIENT_NOTIFICATIONS_MOCK,
  );
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
          className="relative p-2.5 rounded-xl text-sub hover:text-ink hover:bg-slate-100 transition-colors focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary"
        >
          <Bell className="w-5 h-5" aria-hidden="true" />
          {unreadCount > 0 && (
            <span
              data-testid="unread-badge"
              className="absolute top-1.5 right-1.5 min-w-[18px] h-[18px] px-1 rounded-full bg-rose-500 text-white font-mono text-[10px] font-bold flex items-center justify-center shadow-xs animate-in zoom-in-75"
            >
              {unreadCount}
            </span>
          )}
        </button>

        {isOpen && (
          <div
            role="menu"
            aria-label="Danh sách thông báo"
            className="absolute right-0 mt-2 w-80 sm:w-96 rounded-2xl bg-white border border-border shadow-xl z-50 overflow-hidden animate-in fade-in zoom-in-95 duration-150"
          >
            {/* Header */}
            <div className="p-4 border-b border-border bg-slate-50/70 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="text-sm font-bold text-ink">Thông báo</span>
                {unreadCount > 0 && (
                  <span className="px-2 py-0.5 rounded-full bg-rose-100 text-rose-700 text-xs font-bold font-mono">
                    {unreadCount} mới
                  </span>
                )}
              </div>
              {unreadCount > 0 && (
                <button
                  type="button"
                  onClick={handleMarkAllAsRead}
                  className="text-xs text-primary hover:text-primary-dark font-medium flex items-center gap-1 transition-colors"
                >
                  <CheckCheck className="w-3.5 h-3.5" aria-hidden="true" />
                  Đọc tất cả
                </button>
              )}
            </div>

            {/* List */}
            <div className="max-h-[360px] overflow-y-auto divide-y divide-slate-100">
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
                      className={`p-3.5 flex items-start gap-3 cursor-pointer transition-colors hover:bg-slate-50 ${
                        !item.isRead ? 'bg-sky-50/50' : 'bg-white'
                      }`}
                    >
                      <div
                        className={`w-8 h-8 rounded-xl flex items-center justify-center shrink-0 border mt-0.5 ${
                          isReminder
                            ? 'bg-amber-100 text-amber-700 border-amber-200'
                            : 'bg-slate-100 text-slate-600 border-slate-200'
                        }`}
                      >
                        {isReminder ? (
                          <Clock className="w-4 h-4" aria-hidden="true" />
                        ) : (
                          <Info className="w-4 h-4" aria-hidden="true" />
                        )}
                      </div>

                      <div className="flex-1 min-w-0">
                        <div className="flex items-center justify-between gap-2">
                          <p
                            className={`text-xs truncate ${
                              !item.isRead ? 'font-bold text-ink' : 'font-medium text-slate-700'
                            }`}
                          >
                            {item.title}
                          </p>
                          <span className="text-[10px] text-sub shrink-0">{item.createdAt}</span>
                        </div>
                        <p className="text-[11px] text-sub mt-0.5 line-clamp-2 leading-relaxed">
                          {item.message}
                        </p>
                        {item.appointmentReminder && (
                          <span className="inline-block mt-1.5 text-[10px] font-semibold text-primary">
                            Xem chi tiết lịch hẹn →
                          </span>
                        )}
                      </div>

                      {!item.isRead && (
                        <span
                          className="w-2 h-2 rounded-full bg-primary shrink-0 mt-2"
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
