import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { GuestOnly, RequireAuth } from '@/app/router/guards'
import { useAuthStore } from '../store/auth.store'

function renderWithRouter(ui: React.ReactElement, initial = '/protected') {
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <Routes>
        <Route path="/protected" element={ui} />
        <Route path="/login" element={<div>LOGIN PAGE</div>} />
        <Route path="/" element={<div>HOME PAGE</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('RequireAuth', () => {
  it('chưa login → redirect /login?returnUrl=', () => {
    useAuthStore.setState({ isAuthenticated: false })
    renderWithRouter(<RequireAuth>SECRET</RequireAuth>)
    expect(screen.queryByText('SECRET')).toBeNull()
    expect(screen.getByText('LOGIN PAGE')).toBeDefined()
  })

  it('đã login → render children', () => {
    useAuthStore.setState({ isAuthenticated: true })
    renderWithRouter(<RequireAuth>SECRET</RequireAuth>)
    expect(screen.getByText('SECRET')).toBeDefined()
  })
})

describe('GuestOnly', () => {
  it('đã login → redirect /', () => {
    useAuthStore.setState({ isAuthenticated: true })
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>)
    expect(screen.queryByText('GUEST FORM')).toBeNull()
    expect(screen.getByText('HOME PAGE')).toBeDefined()
  })

  it('chưa login → render children', () => {
    useAuthStore.setState({ isAuthenticated: false })
    renderWithRouter(<GuestOnly>GUEST FORM</GuestOnly>)
    expect(screen.getByText('GUEST FORM')).toBeDefined()
  })
})
