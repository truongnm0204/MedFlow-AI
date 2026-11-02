import { stopBackend } from './helpers.ts';

export default async function globalTeardown(): Promise<void> {
  stopBackend();
}
