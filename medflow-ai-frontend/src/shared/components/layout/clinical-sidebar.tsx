import { useState } from 'react'
import { MedflowLogo } from './medflow-logo'

interface ClinicalSidebarProps {
  activeItem?: string
  onItemSelect?: (item: string) => void
}

export function ClinicalSidebar({ activeItem = 'queue', onItemSelect }: ClinicalSidebarProps) {
  const [isCollapsed, setIsCollapsed] = useState(false)

  const clinicalItems = [
    { id: 'queue', label: 'Hàng đợi khám', badge: '14 ca', hotkey: 'F1', icon: 'M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10' },
    { id: 'emr', label: 'Hồ sơ bệnh án EMR', icon: 'M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z' },
    { id: 'consult', label: 'Khám & Kê đơn', hotkey: 'F2', icon: 'M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z' },
    { id: 'diagnostics', label: 'Chỉ định CLS & PACS', badge: '3 chờ trả', badgeColor: 'amber', icon: 'M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2' },
    { id: 'prescription', label: 'Kê đơn thuốc điện tử', hotkey: 'F4', icon: 'M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2' },
  ]

  const toolItems = [
    { id: 'icd10', label: 'Từ điển ICD-10 chuẩn', icon: 'M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253' },
    { id: 'drugs', label: 'Tra cứu tương tác thuốc', icon: 'M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z' },
  ]

  return (
    <aside
      className={`bg-white border-r border-slate-200 flex flex-col justify-between transition-all duration-200 select-none ${
        isCollapsed ? 'w-[68px]' : 'w-[260px]'
      }`}
    >
      {/* Top Header & Role */}
      <div>
        {/* Brand Bar */}
        <div className="h-16 px-4 border-b border-slate-200 flex items-center justify-between">
          <div className="flex items-center gap-2.5 overflow-hidden">
            <MedflowLogo
              variant={isCollapsed ? 'icon' : 'full'}
              className={isCollapsed ? 'h-8 w-auto flex-shrink-0' : 'h-9 w-auto flex-shrink-0'}
              linkToHome={false}
            />
            {!isCollapsed && (
              <div className="text-[9px] font-mono text-slate-400 uppercase truncate">CS1 • Ngoại Trú</div>
            )}
          </div>
          <button
            type="button"
            onClick={() => setIsCollapsed(!isCollapsed)}
            className="p-1 rounded-md text-slate-400 hover:text-slate-700 hover:bg-slate-100 transition-colors"
            title={isCollapsed ? 'Mở rộng' : 'Thu gọn'}
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              {isCollapsed ? (
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 5l7 7-7 7M5 5l7 7-7 7" />
              ) : (
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M11 19l-7-7 7-7m8 14l-7-7 7-7" />
              )}
            </svg>
          </button>
        </div>

        {/* Role Identity Card */}
        {!isCollapsed && (
          <div className="m-3 p-3 rounded-xl bg-sky-50 border border-sky-100 flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-100 border border-sky-200 text-sky-700 flex items-center justify-center font-bold text-xs">
              LS
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-[9px] font-mono uppercase text-sky-700 font-bold tracking-wider">Doctor Workbench</div>
              <div className="text-xs font-bold text-slate-800 truncate">BS. CKII Lâm Sàng</div>
              <div className="text-[10px] text-slate-500">P.102 • Khám Tự Nguyện</div>
            </div>
            <span className="w-2 h-2 rounded-full bg-emerald-500 ring-2 ring-emerald-200" />
          </div>
        )}

        {/* Navigation Group 1 */}
        <div className="px-3 py-2 space-y-1">
          {!isCollapsed && (
            <div className="px-2 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
              Nghiệp Vụ Bàn Khám
            </div>
          )}
          {clinicalItems.map((item) => {
            const isActive = activeItem === item.id
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onItemSelect?.(item.id)}
                className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-semibold transition-all ${
                  isActive
                    ? 'bg-sky-600 text-white shadow-sm shadow-sky-600/30'
                    : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
                }`}
                title={item.label}
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <svg className={`w-4 h-4 flex-shrink-0 ${isActive ? 'text-white' : 'text-slate-400'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d={item.icon} />
                  </svg>
                  {!isCollapsed && <span className="truncate">{item.label}</span>}
                </div>
                {!isCollapsed && (
                  <div className="flex items-center gap-1.5 flex-shrink-0">
                    {item.badge && (
                      <span className={`px-1.5 py-0.5 rounded-full text-[10px] font-mono font-bold ${
                        isActive ? 'bg-white/20 text-white' : 'bg-sky-50 text-sky-700 border border-sky-200'
                      }`}>
                        {item.badge}
                      </span>
                    )}
                    {item.hotkey && (
                      <kbd className="text-[9px] font-mono text-slate-400 bg-slate-100 border border-slate-200 px-1 py-0.5 rounded">
                        {item.hotkey}
                      </kbd>
                    )}
                  </div>
                )}
              </button>
            )
          })}
        </div>

        {/* Navigation Group 2 */}
        <div className="px-3 py-2 space-y-1">
          {!isCollapsed && (
            <div className="px-2 text-[10px] font-bold text-slate-400 uppercase tracking-wider font-mono mb-1">
              Công Cụ Lâm Sàng
            </div>
          )}
          {toolItems.map((item) => {
            const isActive = activeItem === item.id
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onItemSelect?.(item.id)}
                className={`w-full flex items-center justify-between px-3 py-2 rounded-xl text-xs font-medium transition-colors ${
                  isActive
                    ? 'bg-sky-600 text-white shadow-sm'
                    : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
                }`}
                title={item.label}
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <svg className="w-4 h-4 flex-shrink-0 text-slate-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d={item.icon} />
                  </svg>
                  {!isCollapsed && <span className="truncate">{item.label}</span>}
                </div>
              </button>
            )
          })}
        </div>
      </div>

      {/* Footer Status */}
      <div className="p-3 bg-slate-50 border-t border-slate-200">
        {!isCollapsed ? (
          <div className="p-2 rounded-lg bg-white border border-slate-200 text-[10px] space-y-1 shadow-2xs">
            <div className="flex items-center justify-between text-slate-700 font-semibold">
              <span>HIS SYNC: ONLINE</span>
              <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
            </div>
            <div className="flex items-center justify-between font-mono text-slate-400">
              <span>v4.2.0-PROD</span>
              <span className="text-sky-600 font-semibold">Hotline: #101</span>
            </div>
          </div>
        ) : (
          <div className="flex justify-center" title="HIS Sync Online">
            <span className="w-2.5 h-2.5 rounded-full bg-emerald-500 ring-2 ring-emerald-200 animate-pulse" />
          </div>
        )}
      </div>
    </aside>
  )
}
