import { ensureTestDatabase, startBackend } from './helpers.ts';

export default async function globalSetup(): Promise<void> {
  const cfg = await ensureTestDatabase();
  await startBackend(cfg);
}
