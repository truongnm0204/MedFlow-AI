import { describe, expect, it } from 'vitest';

import { isNavLinkActive, PATIENT_NAV_LINKS, PUBLIC_NAV_LINKS } from '../nav-links';

function activeLabels(pathname: string, links = PATIENT_NAV_LINKS): string[] {
  return links.filter((l) => isNavLinkActive(pathname, l)).map((l) => l.label);
}

describe('isNavLinkActive', () => {
  it.each([
    ['/appointment/doctors/doc-01/booking', ['Bác sĩ']],
    ['/doctors/doc-01', ['Bác sĩ']],
    ['/doctors', ['Bác sĩ']],
    ['/services/srv-th-01', ['Dịch vụ khám']],
    ['/appointment/services/srv-01/booking', ['Dịch vụ khám']],
    ['/booking', ['Đặt lịch khám']],
    ['/appointment/book', ['Đặt lịch khám']],
    ['/', ['Trang chủ']],
    ['/patient', ['Trang chủ']],
    ['/patient/home', ['Trang chủ']],
    ['/doctorsX', []],
    ['/appointments/apt-01', ['Lịch hẹn của tôi']],
    ['/emr/emr-01', ['Hồ sơ bệnh án']],
    ['/nowhere', []],
  ])('pathname %s → active [%s]', (pathname, expected) => {
    expect(activeLabels(pathname)).toEqual(expected);
  });

  it('hash links (Chuyên khoa / Giới thiệu) are never active', () => {
    const hashLinks = PUBLIC_NAV_LINKS.filter((l) => l.to.includes('#'));
    for (const link of hashLinks) {
      expect(isNavLinkActive('/', link)).toBe(false);
      expect(isNavLinkActive('/anything', link)).toBe(false);
    }
  });
});
