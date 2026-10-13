import { describe, expect, it } from 'vitest';

import { apiClient } from '../axios';

describe('apiClient', () => {
  it('is configured with /api/v1 base url', () => {
    expect(apiClient.defaults.baseURL).toBe('/api/v1');
  });
});
