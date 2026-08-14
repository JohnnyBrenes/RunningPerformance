import { describe, expect, it } from 'vitest'
import {
  calendarDates,
  isExecuted,
  moveCalendarCursor,
  sessionKind,
  startOfWeek,
} from './calendar'

describe('calendar presentation rules', () => {
  it('builds Monday-based week and six-week month views', () => {
    expect(startOfWeek('2026-08-14')).toBe('2026-08-10')
    expect(calendarDates('week', '2026-08-14')).toEqual([
      '2026-08-10', '2026-08-11', '2026-08-12', '2026-08-13',
      '2026-08-14', '2026-08-15', '2026-08-16',
    ])
    const month = calendarDates('month', '2026-08-14')
    expect(month).toHaveLength(42)
    expect(month[0]).toBe('2026-07-27')
    expect(month[41]).toBe('2026-09-06')
  })

  it('moves by the active view without overflowing short months', () => {
    expect(moveCalendarCursor('day', '2026-08-14', 1)).toBe('2026-08-15')
    expect(moveCalendarCursor('week', '2026-08-14', -1)).toBe('2026-08-07')
    expect(moveCalendarCursor('month', '2026-01-31', 1)).toBe('2026-02-28')
  })

  it('maps planned sessions to user-facing activity kinds', () => {
    expect(sessionKind({ sessionType: 'strength_mobility_plyometrics', modality: 'mixed', terrain: null }).label).toBe('Gimnasio')
    expect(sessionKind({ sessionType: 'easy_run', modality: 'running', terrain: 'Caminadora al 2%' }).label).toBe('Caminadora')
    expect(sessionKind({ sessionType: 'long_run', modality: 'running', terrain: 'Ruta conocida' }).label).toBe('Correr exterior')
    expect(sessionKind({ sessionType: 'quality', modality: 'running', terrain: null }).label).toBe('Correr')
  })

  it('only marks confirmed completion states as executed', () => {
    expect(isExecuted('completed_as_planned')).toBe(true)
    expect(isExecuted('completed_modified')).toBe(true)
    expect(isExecuted('valid_substitution')).toBe(true)
    expect(isExecuted('not_completed')).toBe(false)
    expect(isExecuted(null)).toBe(false)
  })
})
