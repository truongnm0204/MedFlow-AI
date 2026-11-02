import { expect, test } from '@playwright/test';
import { authenticatePatient } from './auth.ts';
import { countBookedAppointmentsForPatient01 } from './helpers.ts';

interface CreateAppointmentResponseDto {
  appointmentId: string;
  appointmentCode: string;
  queueNumber: string;
  status: string;
}

test.describe.serial('Patient Booking Flow (Full-stack E2E)', () => {
  let initialAppointmentCount = 0;

  test.beforeAll(() => {
    initialAppointmentCount = countBookedAppointmentsForPatient01();
  });

  test.afterAll(() => {
    const finalAppointmentCount = countBookedAppointmentsForPatient01();
    expect(finalAppointmentCount - initialAppointmentCount).toBeGreaterThanOrEqual(3);
  });

  test('1. Đặt khám theo Bác sĩ (hồ sơ bản thân) → confirm thành công', async ({ page }) => {
    await authenticatePatient(page);
    await page.goto('/booking');

    const hospitalRadio = page.getByRole('radio').first();
    await expect(hospitalRadio).toBeVisible();
    await hospitalRadio.click();

    await page.getByRole('button', { name: /Khám theo Bác sĩ/ }).click();
    await expect(page).toHaveURL(/\/doctors\?hospitalId=1/);

    const bookNowButton = page.getByRole('button', { name: /Đặt khám ngay/ }).first();
    await expect(bookNowButton).toBeVisible();

    const availableSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await bookNowButton.click();
    await expect(page).toHaveURL(/\/appointment\/doctors\/\d+\/booking/);
    await availableSlotsResponsePromise;

    const dateListbox = page.getByRole('listbox', { name: 'Danh sách ngày khám trong tuần' });
    const availableDateOptions = dateListbox.locator('button[role="option"]:not([disabled])');
    await expect(availableDateOptions.first()).toBeVisible();

    const dateSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await availableDateOptions.first().click();
    await dateSlotsResponsePromise;
    await expect(availableDateOptions.first()).toHaveAttribute('aria-selected', 'true');

    const patientRadioGroup = page.getByRole('radiogroup', { name: 'Hồ sơ người thăm khám' });
    const selfProfileRadio = patientRadioGroup.getByRole('radio', { name: /Nguyễn Văn An/ });
    await expect(selfProfileRadio).toBeVisible();
    await selfProfileRadio.click();
    await expect(selfProfileRadio).toHaveAttribute('aria-checked', 'true');

    const slotSection = page.locator('section[aria-labelledby="section-slot-heading"]');
    const availableSlotButtons = slotSection.locator('button:not([disabled])');
    await expect(availableSlotButtons.first()).toBeVisible();
    await availableSlotButtons.first().click();
    await expect(availableSlotButtons.first()).toHaveAttribute('aria-pressed', 'true');

    await page
      .getByRole('textbox', { name: 'Lý do khám / Triệu chứng ban đầu' })
      .fill('Đau thượng vị và đầy hơi sau ăn kéo dài 3 ngày');

    const reviewPreviewPromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/review-preview') && res.status() === 200,
    );
    await page.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/ }).click();
    await expect(page).toHaveURL(/\/appointment\/review\?.*mode=doctor.*doctorId=\d+.*slotId=\d+/);
    await reviewPreviewPromise;

    const confirmButton = page.getByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/,
    });
    await expect(confirmButton).toBeEnabled();

    const createAppointmentPromise = page.waitForResponse(
      (res) =>
        res.url().includes('/api/v1/appointments') &&
        res.request().method() === 'POST' &&
        res.status() === 201,
    );
    await confirmButton.click();

    const createResponse = await createAppointmentPromise;
    const created = (await createResponse.json()) as CreateAppointmentResponseDto;

    expect(created.appointmentCode).toMatch(/^APT-\d{6}-\d{4}$/);
    expect(Number(created.queueNumber)).toBeGreaterThan(0);

    await expect(page).toHaveURL(new RegExp(`/appointment/confirmation/${created.appointmentId}$`));

    const heroSection = page.getByRole('region', { name: 'Thông báo đặt lịch thành công' });
    await expect(heroSection).toContainText(`#${created.appointmentCode}`);
    await expect(heroSection).toContainText(/#APT-\d{6}-\d{4}/);

    const queueSection = page.getByRole('region', { name: 'Số thứ tự giữ trước' });
    await expect(queueSection).toContainText(`STT: ${created.queueNumber}`);
    await expect(queueSection).toContainText(/STT:\s*\d+/);
    await expect(page.getByText('Lịch hẹn này đã bị hủy.')).toHaveCount(0);
  });

  test('2. Đặt khám theo Dịch vụ (hồ sơ bản thân) → confirm thành công', async ({ page }) => {
    await authenticatePatient(page);
    await page.goto('/booking');

    const hospitalRadio = page.getByRole('radio').first();
    await expect(hospitalRadio).toBeVisible();
    await hospitalRadio.click();

    await page.getByRole('button', { name: /Khám theo Dịch vụ/ }).click();
    await expect(page).toHaveURL(/\/services\?hospitalId=1/);

    const selectServiceButton = page.getByRole('button', { name: /Chọn dịch vụ/ }).first();
    await expect(selectServiceButton).toBeVisible();

    const serviceShiftsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/service-shifts') && res.status() === 200,
    );
    await selectServiceButton.click();
    await expect(page).toHaveURL(/\/appointment\/services\/\d+\/booking/);
    await serviceShiftsResponsePromise;

    const dateListbox = page.getByRole('listbox', { name: 'Danh sách ngày khám trong tuần' });
    const availableDateOptions = dateListbox.locator('button[role="option"]:not([disabled])');
    await expect(availableDateOptions.first()).toBeVisible();
    const availableDateCount = await availableDateOptions.count();
    const targetDateIndex = availableDateCount > 1 ? 1 : 0;
    const targetDateOption = availableDateOptions.nth(targetDateIndex);

    const dateShiftsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/service-shifts') && res.status() === 200,
    );
    await targetDateOption.click();
    await dateShiftsResponsePromise;
    await expect(targetDateOption).toHaveAttribute('aria-selected', 'true');

    const patientRadioGroup = page.getByRole('radiogroup', { name: 'Hồ sơ người thăm khám' });
    const selfProfileRadio = patientRadioGroup.getByRole('radio', { name: /Nguyễn Văn An/ });
    await expect(selfProfileRadio).toBeVisible();
    await selfProfileRadio.click();
    await expect(selfProfileRadio).toHaveAttribute('aria-checked', 'true');

    const shiftRadioGroup = page.getByRole('radiogroup', { name: 'Chọn ca khám dịch vụ' });
    const availableShiftRadios = shiftRadioGroup.locator('button[role="radio"]:not([disabled])');
    await expect(availableShiftRadios.first()).toBeVisible();
    await availableShiftRadios.first().click();
    await expect(availableShiftRadios.first()).toHaveAttribute('aria-checked', 'true');

    await page
      .getByRole('textbox', { name: 'Lý do khám / Triệu chứng ban đầu' })
      .fill('Khám kiểm tra tiêu hóa định kỳ theo dịch vụ');

    const reviewPreviewPromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/review-preview') && res.status() === 200,
    );
    await page.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/ }).click();
    await expect(page).toHaveURL(
      /\/appointment\/review\?.*mode=service.*shiftId=shift-(morning|afternoon)/,
    );
    await reviewPreviewPromise;

    const confirmButton = page.getByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/,
    });
    await expect(confirmButton).toBeEnabled();

    const createAppointmentPromise = page.waitForResponse(
      (res) =>
        res.url().includes('/api/v1/appointments') &&
        res.request().method() === 'POST' &&
        res.status() === 201,
    );
    await confirmButton.click();

    const createResponse = await createAppointmentPromise;
    const created = (await createResponse.json()) as CreateAppointmentResponseDto;

    expect(created.appointmentCode).toMatch(/^APT-\d{6}-\d{4}$/);
    expect(Number(created.queueNumber)).toBeGreaterThan(0);

    await expect(page).toHaveURL(new RegExp(`/appointment/confirmation/${created.appointmentId}$`));

    const heroSection = page.getByRole('region', { name: 'Thông báo đặt lịch thành công' });
    await expect(heroSection).toContainText(`#${created.appointmentCode}`);
    await expect(heroSection).toContainText(/#APT-\d{6}-\d{4}/);

    const queueSection = page.getByRole('region', { name: 'Số thứ tự giữ trước' });
    await expect(queueSection).toContainText(`STT: ${created.queueNumber}`);
    await expect(queueSection).toContainText(/STT:\s*\d+/);
    await expect(page.getByText('Lịch hẹn này đã bị hủy.')).toHaveCount(0);
  });

  test('3. Đặt khám cho hồ sơ người thân (Lê Minh Chi) → xem chi tiết & danh sách lịch hẹn', async ({
    page,
  }) => {
    await authenticatePatient(page);
    await page.goto('/booking');

    const hospitalRadio = page.getByRole('radio').first();
    await expect(hospitalRadio).toBeVisible();
    await hospitalRadio.click();

    await page.getByRole('button', { name: /Khám theo Bác sĩ/ }).click();
    await expect(page).toHaveURL(/\/doctors\?hospitalId=1/);

    const bookNowButton = page.getByRole('button', { name: /Đặt khám ngay/ }).first();
    await expect(bookNowButton).toBeVisible();

    const availableSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await bookNowButton.click();
    await expect(page).toHaveURL(/\/appointment\/doctors\/\d+\/booking/);
    await availableSlotsResponsePromise;

    const dateListbox = page.getByRole('listbox', { name: 'Danh sách ngày khám trong tuần' });
    const availableDateOptions = dateListbox.locator('button[role="option"]:not([disabled])');
    await expect(availableDateOptions.first()).toBeVisible();

    const dateSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await availableDateOptions.first().click();
    await dateSlotsResponsePromise;
    await expect(availableDateOptions.first()).toHaveAttribute('aria-selected', 'true');

    // Chọn hồ sơ người thân (Lê Minh Chi - Con)
    const patientRadioGroup = page.getByRole('radiogroup', { name: 'Hồ sơ người thăm khám' });
    const familyProfileRadio = patientRadioGroup.getByRole('radio', { name: /Lê Minh Chi/ });
    await expect(familyProfileRadio).toBeVisible();
    await familyProfileRadio.click();
    await expect(familyProfileRadio).toHaveAttribute('aria-checked', 'true');

    const slotSection = page.locator('section[aria-labelledby="section-slot-heading"]');
    const availableSlotButtons = slotSection.locator('button:not([disabled])');
    const slotCount = await availableSlotButtons.count();
    const targetSlotButton =
      slotCount > 1 ? availableSlotButtons.nth(1) : availableSlotButtons.first();
    await targetSlotButton.click();
    await expect(targetSlotButton).toHaveAttribute('aria-pressed', 'true');

    await page
      .getByRole('textbox', { name: 'Lý do khám / Triệu chứng ban đầu' })
      .fill('Đau bụng quanh rốn kèm chán ăn ở trẻ nhỏ');

    const reviewPreviewPromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/review-preview') && res.status() === 200,
    );
    await page.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/ }).click();
    await expect(page).toHaveURL(/\/appointment\/review\?.*mode=doctor.*patientId=3/);
    await reviewPreviewPromise;

    const confirmButton = page.getByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/,
    });
    await expect(confirmButton).toBeEnabled();

    const createAppointmentPromise = page.waitForResponse(
      (res) =>
        res.url().includes('/api/v1/appointments') &&
        res.request().method() === 'POST' &&
        res.status() === 201,
    );
    await confirmButton.click();

    const createResponse = await createAppointmentPromise;
    const created = (await createResponse.json()) as CreateAppointmentResponseDto;

    await expect(page).toHaveURL(new RegExp(`/appointment/confirmation/${created.appointmentId}$`));

    // Từ trang Confirmation chuyển sang Xem chi tiết lịch hẹn
    await page.getByRole('link', { name: /Xem chi tiết lịch hẹn/ }).click();
    await expect(page).toHaveURL(new RegExp(`/appointments/${created.appointmentId}$`));
    await expect(page.getByText(new RegExp(created.appointmentCode)).first()).toBeVisible();
    await expect(page.getByText(/Lê Minh Chi/).first()).toBeVisible();

    // Điều hướng về Danh sách lịch hẹn của tôi (/appointments)
    await page.goto('/appointments');
    await expect(page.getByText(new RegExp(created.appointmentCode)).first()).toBeVisible();
  });

  test('4. Đặt trùng khung giờ khám đã có lịch → backend trả 409 Conflict và khóa nút xác nhận', async ({
    page,
  }) => {
    await authenticatePatient(page);
    await page.goto('/booking');

    const hospitalRadio = page.getByRole('radio').first();
    await expect(hospitalRadio).toBeVisible();
    await hospitalRadio.click();

    await page.getByRole('button', { name: /Khám theo Bác sĩ/ }).click();
    await expect(page).toHaveURL(/\/doctors\?hospitalId=1/);

    const bookNowButton = page.getByRole('button', { name: /Đặt khám ngay/ }).first();
    await expect(bookNowButton).toBeVisible();

    const availableSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await bookNowButton.click();
    await expect(page).toHaveURL(/\/appointment\/doctors\/\d+\/booking/);
    await availableSlotsResponsePromise;

    const dateListbox = page.getByRole('listbox', { name: 'Danh sách ngày khám trong tuần' });
    const availableDateOptions = dateListbox.locator('button[role="option"]:not([disabled])');
    await expect(availableDateOptions.first()).toBeVisible();

    const dateSlotsResponsePromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/available-slots') && res.status() === 200,
    );
    await availableDateOptions.first().click();
    await dateSlotsResponsePromise;
    await expect(availableDateOptions.first()).toHaveAttribute('aria-selected', 'true');

    // Chọn lại chính hồ sơ Nguyễn Văn An và đúng khung giờ đầu tiên đã đặt ở Test 1
    const patientRadioGroup = page.getByRole('radiogroup', { name: 'Hồ sơ người thăm khám' });
    const selfProfileRadio = patientRadioGroup.getByRole('radio', { name: /Nguyễn Văn An/ });
    await selfProfileRadio.click();
    await expect(selfProfileRadio).toHaveAttribute('aria-checked', 'true');

    const slotSection = page.locator('section[aria-labelledby="section-slot-heading"]');
    const availableSlotButtons = slotSection.locator('button:not([disabled])');
    await availableSlotButtons.first().click();
    await expect(availableSlotButtons.first()).toHaveAttribute('aria-pressed', 'true');

    const conflictPreviewPromise = page.waitForResponse(
      (res) => res.url().includes('/api/v1/appointments/review-preview') && res.status() === 409,
    );
    await page.getByRole('button', { name: /Tiếp tục: Soát xét Đặt lịch/ }).click();
    await conflictPreviewPromise;

    const alertBanner = page.getByRole('alert');
    await expect(alertBanner).toBeVisible();
    await expect(alertBanner).toContainText(/Không thể hoàn tất xác nhận đặt khám/);
    await expect(alertBanner).toContainText(/trùng thời gian/);

    const confirmButton = page.getByRole('button', {
      name: /Xác nhận Đặt khám & Cấp Số Thứ Tự/,
    });
    await expect(confirmButton).toBeDisabled();
  });

  test('5. Chưa đăng nhập truy cập cổng bệnh nhân (/patient) → bị RequireAuth chuyển hướng về /login', async ({
    page,
  }) => {
    await page.goto('/patient');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fpatient/);
  });
});
