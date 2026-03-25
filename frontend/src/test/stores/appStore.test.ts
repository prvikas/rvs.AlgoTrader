import { describe, it, expect, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useAppStore, useStrategyStore } from '../../stores/appStore';
import { StrategyInstance } from '../../api/client';

describe('useAppStore', () => {
  beforeEach(() => {
    useAppStore.setState({
      jwtToken: null,
      killSwitchActive: false,
      activeBroker: 'Zerodha',
      sidebarCollapsed: false,
    });
  });

  it('sets and retrieves JWT token', () => {
    const { result } = renderHook(() => useAppStore());
    act(() => {
      result.current.setJwtToken('test-jwt-token');
    });
    expect(result.current.jwtToken).toBe('test-jwt-token');
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
    const initial = result.current.sidebarCollapsed;
    act(() => {
      result.current.toggleSidebar();
    });
    expect(result.current.sidebarCollapsed).toBe(!initial);
  });
});

describe('useStrategyStore', () => {
  beforeEach(() => {
    useStrategyStore.setState({ instances: new Map() });
  });

  const makeInstance = (id: string): StrategyInstance => ({
    id,
    name: `Instance ${id}`,
    strategyType: 'PriceActionBreakout',
    internalSymbol: 'NSE:RELIANCE',
    timeframe: '5m',
    mode: 'Forward',
    brokerName: 'Zerodha',
    status: 'Running',
    allocatedCapital: 100000,
    createdAt: new Date().toISOString(),
  });

  it('adds strategy instance', () => {
    const { result } = renderHook(() => useStrategyStore());
    const instance = makeInstance('inst-001');

    act(() => {
      result.current.setInstance(instance);
    });

    expect(result.current.instances.get('inst-001')).toBeDefined();
    expect(result.current.instances.get('inst-001')?.status).toBe('Running');
  });

  it('updates strategy status', () => {
    const { result } = renderHook(() => useStrategyStore());
    const instance = makeInstance('inst-002');

    act(() => {
      result.current.setInstance(instance);
      result.current.updateStatus('inst-002', 'Paused');
    });

    expect(result.current.instances.get('inst-002')?.status).toBe('Paused');
  });
});
