import { useAuthStore } from '@/features/auth/store/auth.store'
import { useLogout, useMe } from '@/features/auth/hooks/use-auth'

import { PatientNavbar } from './patient-navbar'
import { PublicNavbar } from './public-navbar'

/** Header tự chuyển Public/Patient navbar theo trạng thái đăng nhập. */
export function AppHeader() {
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated)
  const user = useAuthStore((s) => s.user)
  const logout = useLogout()

  // Làm tươi profile khi đã đăng nhập (silent refresh cũng cập nhật user).
  useMe()

  if (!isAuthenticated) return <PublicNavbar />
  return <PatientNavbar user={user} onLogout={() => logout.mutate()} />
}
