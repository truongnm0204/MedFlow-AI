import type { ReactNode } from 'react'

import { AuthBrandingPanel } from './auth-branding-panel'

interface AuthLayoutProps {
  children: ReactNode
}

/** Split-screen shell: trái branding navy (ẩn mobile), phải nội dung trên nền canvas. */
export function AuthLayout({ children }: AuthLayoutProps) {
  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      <AuthBrandingPanel />
      <main className="flex items-center justify-center bg-canvas px-4 py-10 sm:px-8">
        {children}
      </main>
    </div>
  )
}
