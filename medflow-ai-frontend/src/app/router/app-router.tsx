import { Route, Routes } from 'react-router-dom'

import { HomePage } from '@/shared/components/layout/home-page'
import { NotFoundPage } from '@/shared/components/layout/not-found-page'

export function AppRouter() {
  return (
    <Routes>
      <Route path="/" element={<HomePage />} />
      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
