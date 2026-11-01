#!/usr/bin/env node

const API_BASE = (process.env.API_BASE || 'http://localhost:5168/api/v1').replace(/\/+$/, '');
const PATIENT_EMAIL = process.env.PATIENT_EMAIL || 'patient01@medflow.test';
const PATIENT_PASSWORD = process.env.PATIENT_PASSWORD || 'Demo@12345';

function getTomorrowIsoVn() {
  const vnNowMs = Date.now() + 7 * 60 * 60 * 1000;
  const tomorrow = new Date(vnNowMs + 24 * 60 * 60 * 1000);
  return tomorrow.toISOString().slice(0, 10);
}

async function runStep(name, fn) {
  try {
    const detail = await fn();
    console.log(`[PASS] ${name}${detail ? ` — ${detail}` : ''}`);
    return true;
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[FAIL] ${name} — ${message}`);
    return false;
  }
}

async function main() {
  console.log(`Verifying FE-BE appointment integration against ${API_BASE}...`);

  let accessToken = '';

  const loginOk = await runStep('POST /auth/login', async () => {
    const res = await fetch(`${API_BASE}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: PATIENT_EMAIL,
        password: PATIENT_PASSWORD,
      }),
    });

    if (res.status !== 200) {
      const text = await res.text();
      throw new Error(`Expected 200, got ${res.status}: ${text}`);
    }

    const body = await res.json();
    if (!body || typeof body.accessToken !== 'string' || !body.accessToken) {
      throw new Error('Missing accessToken in login response');
    }

    accessToken = body.accessToken;
    return `Authenticated as ${PATIENT_EMAIL}`;
  });

  if (!loginOk) {
    process.exit(1);
  }

  const authHeaders = {
    Authorization: `Bearer ${accessToken}`,
    Accept: 'application/json',
  };

  const myAppointmentsOk = await runStep('GET /appointments/me', async () => {
    const res = await fetch(`${API_BASE}/appointments/me?tab=ALL&page=1&pageSize=10`, {
      headers: authHeaders,
    });

    if (res.status !== 200) {
      const text = await res.text();
      throw new Error(`Expected 200, got ${res.status}: ${text}`);
    }

    const body = await res.json();
    if (!body || !Array.isArray(body.items)) {
      throw new Error('Response body.items is not an array');
    }

    return `Received ${body.items.length} item(s), total=${body.total ?? body.items.length}`;
  });

  const tomorrowIso = getTomorrowIsoVn();
  const availableSlotsOk = await runStep('GET /appointments/available-slots', async () => {
    const qs = new URLSearchParams({
      doctorId: 'DOC-001',
      serviceId: 'EX-GI-01',
      dateIso: tomorrowIso,
    });
    const res = await fetch(`${API_BASE}/appointments/available-slots?${qs.toString()}`, {
      headers: authHeaders,
    });

    if (res.status !== 200) {
      const text = await res.text();
      throw new Error(`Expected 200, got ${res.status}: ${text}`);
    }

    const body = await res.json();
    if (!body || !Array.isArray(body.dates) || !Array.isArray(body.profiles)) {
      throw new Error('Response missing dates[] or profiles[]');
    }

    return `dateIso=${tomorrowIso}, dates=${body.dates.length}, profiles=${body.profiles.length}`;
  });

  if (!myAppointmentsOk || !availableSlotsOk) {
    process.exit(1);
  }

  console.log('All integration checks PASSED.');
}

main().catch((err) => {
  console.error('[FAIL] Unhandled error:', err);
  process.exit(1);
});
