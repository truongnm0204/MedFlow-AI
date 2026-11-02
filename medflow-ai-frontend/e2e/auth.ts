import type { Page } from '@playwright/test';
import { loginPatient, type PatientAuthTokens } from './helpers.ts';

let cachedTokens: PatientAuthTokens | null = null;

export async function authenticatePatient(page: Page): Promise<void> {
  if (!cachedTokens) {
    cachedTokens = await loginPatient(page.request);
  }
  const tokens = cachedTokens;
  await page.addInitScript(({ accessToken, refreshToken, user }) => {
    localStorage.setItem('medflow.access_token', accessToken);
    localStorage.setItem('medflow.refresh_token', refreshToken);
    localStorage.setItem(
      'medflow.auth',
      JSON.stringify({
        state: {
          user,
          accessToken,
          refreshToken,
          isAuthenticated: true,
        },
        version: 0,
      }),
    );
  }, tokens);

  await page.goto('/');
}
