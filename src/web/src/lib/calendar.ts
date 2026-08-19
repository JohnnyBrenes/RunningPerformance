import type { PlannedSessionResponse } from '../api/generated'

export type CalendarView = 'month' | 'week' | 'day'

export type SessionKind = {
  label: string
  icon: string
  className: 'gym' | 'treadmill' | 'outdoor' | 'running'
}

const completedStatuses = new Set([
  'completed_as_planned',
  'completed_modified',
  'valid_substitution',
])

export function localDateKey(date = new Date()): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

export function parseLocalDate(value: string): Date {
  return new Date(`${value}T00:00:00`)
}

export function addDays(value: string, days: number): string {
  const date = parseLocalDate(value)
  date.setDate(date.getDate() + days)
  return localDateKey(date)
}

export function addMonths(value: string, months: number): string {
  const date = parseLocalDate(value)
  const originalDay = date.getDate()
  date.setDate(1)
  date.setMonth(date.getMonth() + months)
  const lastDay = new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate()
  date.setDate(Math.min(originalDay, lastDay))
  return localDateKey(date)
}

export function startOfWeek(value: string): string {
  const date = parseLocalDate(value)
  const mondayOffset = (date.getDay() + 6) % 7
  return addDays(value, -mondayOffset)
}

export function calendarDates(view: CalendarView, cursor: string): string[] {
  if (view === 'day') return [cursor]
  if (view === 'week') {
    const first = startOfWeek(cursor)
    return Array.from({ length: 7 }, (_, index) => addDays(first, index))
  }

  const month = parseLocalDate(cursor)
  const firstOfMonth = localDateKey(new Date(month.getFullYear(), month.getMonth(), 1))
  const first = startOfWeek(firstOfMonth)
  return Array.from({ length: 42 }, (_, index) => addDays(first, index))
}

export function moveCalendarCursor(view: CalendarView, cursor: string, direction: -1 | 1): string {
  if (view === 'month') return addMonths(cursor, direction)
  return addDays(cursor, direction * (view === 'week' ? 7 : 1))
}

export function calendarTitle(view: CalendarView, cursor: string): string {
  const date = parseLocalDate(cursor)
  if (view === 'month') {
    return new Intl.DateTimeFormat('es', { month: 'long', year: 'numeric' }).format(date)
  }
  if (view === 'day') {
    return new Intl.DateTimeFormat('es', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    }).format(date)
  }

  const first = startOfWeek(cursor)
  const last = addDays(first, 6)
  const format = new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' })
  return `${format.format(parseLocalDate(first))} – ${format.format(parseLocalDate(last))}`
}

export function sessionKind(
  session: Pick<PlannedSessionResponse, 'sessionType' | 'modality' | 'terrain'>,
): SessionKind {
  const sessionType = session.sessionType.toLowerCase()
  const modality = session.modality?.toLowerCase() ?? ''
  const terrain = session.terrain?.toLowerCase() ?? ''

  if (sessionType.includes('strength') || sessionType.includes('gym') || modality === 'strength') {
    return { label: 'Fuerza', icon: '◆', className: 'gym' }
  }
  if (modality === 'treadmill' || /cinta|caminadora|treadmill/.test(terrain)) {
    return { label: 'Caminadora', icon: '▱', className: 'treadmill' }
  }
  if (modality === 'outdoor' || /exterior|ruta|calle|pista|trail/.test(terrain)) {
    return { label: 'Correr exterior', icon: '↗', className: 'outdoor' }
  }
  return { label: 'Correr', icon: '◇', className: 'running' }
}

export function isExecuted(status: string | null | undefined): boolean {
  return status != null && completedStatuses.has(status)
}

export function executionLabel(status: string | null | undefined): string {
  if (isExecuted(status)) return 'Realizada'
  if (status === 'not_completed') return 'No realizada'
  if (status === 'optional_not_completed') return 'Opcional no realizada'
  return 'Pendiente'
}

export function isWithinPeriod(date: string, start: string, end: string): boolean {
  return date >= start && date <= end
}
