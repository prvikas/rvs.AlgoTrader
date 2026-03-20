import { describe, it, expect, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useAppStore, useStrategyStore } from '../../stores/appStore';

describe('useAppStore', () => {
  beforeEach(() => {
    useAppStore.setState({
      token: null,
      killSwitchActive: false,
      activeBroker: 'Zerodha',
      sidebarOpen: true,
    });
  });

  it('sets and retrieves JWT token', () => {
    const { result } = renderHook(() => useAppStore());
    act(() => {
      result.current.setToken('test-jwt-token');
    });
    expect(result.current.token).toBe('test-jwt-token');
  });

  it('toggles kill switch state', () => {
    const { result } = renderHook(() => useAppStore());
    act(() => {
      result.current.setKillSwitchActive(true);
    });
    expect(result.current.killSwitchActive).toBe(true);

    act(() => {
      result.current.setKillSwitchActive(false);
    });
    expect(result.current.killSwitchActive).toBe(false);
  });

  it('updates active broker', () => {
    const { result } = renderHook(() => useAppStore());
    act(() => {
      result.current.setActiveBroker('Upstox');
    });
    expect(result.current.activeBroker).toBe('Upstox');
  });

  it('toggles sidebar', () => {
    const { result } = renderHook(() => useAppStore());
    const initial = result.current.sidebarOpen;
    act(() => {
      result.current.toggleSidebar();
    });
    expect(result.current.sidebarOpen).toBe(!initial);
  });
});

describe('useStrategyStore', () => {
  beforeEach(() => {
    useStrategyStore.setState({ instances: {} });
  });

  it('adds strategy instance', () => {
    const { result } = renderHook(() => useStrategyStore());
    const instance = {
      id: 'inst-001',
      strategyName: 'PriceActionBreakout',
      internalSymbol: 'RELIANCE',
      timeframe: '5m',
      status: 'RUNNING' as const,
      brokerName: 'Zerodha',
      allocatedCapital: 100000,
      autoResumeOnRestart: true,
    };

    act(() => {
      result.current.setInstance(instance.id, instance);
    });

    expect(result.current.instances['inst-001']).toBeDefined();
    expect(result.current.instances['inst-001'].status).toBe('RUNNING');
  });

  it('updates strategy status', () => {
    const { result } = renderHook(() => useStrategyStore());
    const instance = {
      id: 'inst-002',
      strategyName: 'PriceActionBreakout',
      internalSymbol: 'INFY',
      timeframe: '15m',
      status: 'RUNNING' as const,
      brokerName: 'Zerodha',
      allocatedCapital: 50000,
      autoResumeOnRestart: false,
    };

    act(() => {
      result.current.setInstance(instance.id, instance);
      result.current.updateStatus(instance.id, 'PAUSED');
    });

    expect(result.current.instances['inst-002'].status).toBe('PAUSED');
  });
});
