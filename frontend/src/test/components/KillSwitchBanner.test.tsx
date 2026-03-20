import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { KillSwitchBanner } from '../../components/Dashboard/KillSwitchBanner';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

// Mock the API module
vi.mock('../../api/client', () => ({
  api: {
    killSwitch: {
      getStatus: vi.fn().mockResolvedValue({ isActive: false }),
      activate: vi.fn().mockResolvedValue({ success: true }),
      deactivate: vi.fn().mockResolvedValue({ success: true }),
    },
  },
}));

const createWrapper = () => {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
};

describe('KillSwitchBanner', () => {
  it('renders nothing when kill switch is inactive', async () => {
    const { container } = render(<KillSwitchBanner />, { wrapper: createWrapper() });
    await waitFor(() => {
      // Banner should not show when inactive
      expect(screen.queryByText(/kill switch/i)).toBeNull();
    });
  });

  it('renders red banner when kill switch is active', async () => {
    const { api } = await import('../../api/client');
    (api.killSwitch.getStatus as any).mockResolvedValue({ isActive: true, activatedBy: 'admin', reason: 'Test' });

    render(<KillSwitchBanner />, { wrapper: createWrapper() });

    await waitFor(() => {
      const banner = screen.queryByRole('alert') || screen.queryByText(/kill switch/i);
      // Banner element or kill switch text should appear when active
      expect(banner).toBeTruthy();
    });
  });
});
