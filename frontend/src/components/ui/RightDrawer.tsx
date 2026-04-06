import React from 'react'
import { C, NAV_HEIGHT } from '../../styles/tokens'

interface RightDrawerProps {
  isOpen: boolean
  title: string
  onClose: () => void
  children: React.ReactNode
  width?: number
}

export function RightDrawer({ isOpen, title, onClose, children, width = 520 }: RightDrawerProps) {
  if (!isOpen) return null

  return (
    <>
      {/* Backdrop */}
      <div
        onClick={onClose}
        style={{
          position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.5)',
          zIndex: 99,
        }}
      />
      {/* Drawer panel */}
      <div style={{
        position: 'fixed', right: 0, top: NAV_HEIGHT, bottom: 0,
        width, background: C.surface, borderLeft: `1px solid ${C.border}`,
        zIndex: 100, display: 'flex', flexDirection: 'column',
        overflow: 'hidden',
      }}>
        {/* Header */}
        <div style={{
          display: 'flex', alignItems: 'center', justifyContent: 'space-between',
          padding: '12px 16px', borderBottom: `1px solid ${C.border3}`,
          flexShrink: 0,
        }}>
          <span style={{ fontWeight: 700, fontSize: 14, color: C.text }}>{title}</span>
          <button
            onClick={onClose}
            style={{
              background: 'none', border: 'none', color: C.textMuted,
              cursor: 'pointer', fontSize: 18, lineHeight: 1, padding: '2px 6px',
            }}
          >
            ×
          </button>
        </div>
        {/* Body */}
        <div style={{ flex: 1, overflowY: 'auto', padding: '12px 16px' }}>
          {children}
        </div>
      </div>
    </>
  )
}
