import { describe, expect, it } from 'vitest'

import {
  loginSchema,
  passwordStrength,
  registerSchema,
  verifyOtpSchema,
} from '../schemas/auth.schemas'

describe('loginSchema', () => {
  it('chấp nhận identifier + password hợp lệ', () => {
    const r = loginSchema.safeParse({ identifier: 'a@b.com', password: 'x' })
    expect(r.success).toBe(true)
  })

  it('từ chối identifier trống', () => {
    expect(loginSchema.safeParse({ identifier: '', password: 'x' }).success).toBe(false)
    expect(loginSchema.safeParse({ identifier: 'a@b.com', password: '' }).success).toBe(false)
  })
})

describe('registerSchema', () => {
  const base = {
    email: 'a@b.com',
    password: 'MedFlow@2026',
    confirmPassword: 'MedFlow@2026',
    fullName: 'Nguyễn Văn A',
    dateOfBirth: '1995-06-15',
    gender: 'MALE' as const,
    phone: '0912345678',
    acceptTerms: true,
  }

  it('chấp nhận payload hợp lệ', () => {
    expect(registerSchema.safeParse(base).success).toBe(true)
  })

  it('từ chối email sai format', () => {
    expect(registerSchema.safeParse({ ...base, email: 'not-email' }).success).toBe(false)
  })

  it('từ chối password thiếu hoa/thường/số', () => {
    expect(registerSchema.safeParse({ ...base, password: 'abcdefgh', confirmPassword: 'abcdefgh' }).success).toBe(false)
    expect(registerSchema.safeParse({ ...base, password: 'ABCDEFGH', confirmPassword: 'ABCDEFGH' }).success).toBe(false)
    expect(registerSchema.safeParse({ ...base, password: '12345678', confirmPassword: '12345678' }).success).toBe(false)
  })

  it('từ chối confirmPassword không khớp', () => {
    const r = registerSchema.safeParse({ ...base, confirmPassword: 'Khac@123' })
    expect(r.success).toBe(false)
  })

  it('từ chối phone sai format VN', () => {
    expect(registerSchema.safeParse({ ...base, phone: '0123456789' }).success).toBe(false)
    expect(registerSchema.safeParse({ ...base, phone: '091234567' }).success).toBe(false)
    expect(registerSchema.safeParse({ ...base, phone: '09123456789' }).success).toBe(false)
  })

  it('từ chối dateOfBirth trong tương lai', () => {
    expect(registerSchema.safeParse({ ...base, dateOfBirth: '2999-01-01' }).success).toBe(false)
  })

  it('từ chối thiếu acceptTerms', () => {
    expect(registerSchema.safeParse({ ...base, acceptTerms: false as never }).success).toBe(false)
  })
})

describe('verifyOtpSchema', () => {
  it('chấp nhận 6 số', () => {
    expect(verifyOtpSchema.safeParse({ otp: '123456' }).success).toBe(true)
  })

  it('từ chối khác 6 ký tự', () => {
    expect(verifyOtpSchema.safeParse({ otp: '12345' }).success).toBe(false)
    expect(verifyOtpSchema.safeParse({ otp: '1234567' }).success).toBe(false)
  })
})

describe('passwordStrength', () => {
  it('trả 0 cho chuỗi rỗng', () => {
    expect(passwordStrength('')).toBe(0)
  })

  it('tăng theo độ dài + đa dạng ký tự', () => {
    expect(passwordStrength('abc')).toBe(0)
    expect(passwordStrength('Abcdefgh')).toBe(2)      // ≥8 + hoa+thường
    expect(passwordStrength('Abcdefgh1')).toBe(2)     // thêm số chưa đủ điều kiện ký tự đặc biệt
    expect(passwordStrength('Abcdefgh1!')).toBe(3)    // số + ký tự đặc biệt
    expect(passwordStrength('Abcdefgh1234!')).toBe(4) // ≥12 + số + đặc biệt
    expect(passwordStrength('Abcdefghijkl1!')).toBe(4)
  })
})
