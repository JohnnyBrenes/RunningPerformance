import type { PlannedSessionResponse } from '../api/generated'
import { addDays, parseLocalDate, sessionKind, startOfWeek } from './calendar'

export type RemainingWeekDay = {
  date: string
  weekday: string
  shortDate: string
  isToday: boolean
  sessions: PlannedSessionResponse[]
}

export function buildRemainingWeekAgenda(
  today: string,
  sessions: PlannedSessionResponse[],
): RemainingWeekDay[] {
  const sunday = addDays(startOfWeek(today), 6)
  const remainingDays = Math.max(0, Math.round(
    (parseLocalDate(sunday).getTime() - parseLocalDate(today).getTime()) / 86_400_000,
  )) + 1

  return Array.from({ length: remainingDays }, (_, offset) => {
    const date = addDays(today, offset)
    return {
      date,
      weekday: capitalize(new Intl.DateTimeFormat('es-MX', { weekday: 'long' }).format(parseLocalDate(date))),
      shortDate: new Intl.DateTimeFormat('es-MX', { day: 'numeric', month: 'short' }).format(parseLocalDate(date)),
      isToday: offset === 0,
      sessions: sessions.filter((session) => session.scheduledDate === date),
    }
  })
}

export function sessionAgendaTitle(session: PlannedSessionResponse): string {
  const type = session.sessionType.toLowerCase()
  if (type.includes('strength') || type.includes('gym') || session.modality === 'strength') return 'Fuerza'
  if (type.includes('cross')) return 'Entrenamiento cruzado'
  if (type.includes('mobility')) return 'Movilidad'

  const kind = sessionKind(session)
  const distance = session.distanceM == null ? '' : ` ${formatDistance(Number(session.distanceM))}`
  return `${kind.label}${distance}`
}

export function sessionAgendaDetail(session: PlannedSessionResponse): string {
  const searchable = [
    session.objective,
    session.mainSet,
    ...session.blocks.flatMap((block) => [
      block.instructions,
      ...block.exercises.flatMap((planned) => [
        planned.exercise.canonicalName,
        planned.exercise.movementPattern,
      ]),
    ]),
  ].filter(Boolean).join(' ').toLowerCase()

  if (isStrength(session)) {
    const focus = [
      [/sentadilla|peso muerto|zancada|pierna|gemelo|pantorrilla|gl[uú]te|rodilla|cadera/, 'Piernas'],
      [/pecho|press de banca|flexi[oó]n|push[ -]?up/, 'Pecho'],
      [/pliometr|salto|pogo/, 'Pliometría'],
      [/core|plancha|tronco|abdominal/, 'Core'],
      [/movilidad|mobility/, 'Movilidad'],
    ].filter(([pattern]) => (pattern as RegExp).test(searchable)).map(([, label]) => label as string)
    return focus.length > 0 ? focus.slice(0, 3).join(', ') : 'Fuerza, movilidad y pliometría'
  }

  if (/fartlek/.test(searchable)) return 'Fartlek'
  if (/serie|interval|repetici|\b\d+\s*x\s*\d+/.test(searchable)) return 'Series'
  if (/progresiv|increment/.test(searchable)) return 'Progresivo'
  if (/tempo|umbral/.test(searchable)) return 'Ritmo controlado'
  if (session.sessionType === 'long_run') return 'Tirada larga'
  if (session.sessionType === 'quality') return 'Trabajo de calidad'
  if (session.sessionType === 'easy_run') return 'Ritmo suave'
  return shorten(session.objective)
}

function isStrength(session: PlannedSessionResponse): boolean {
  const type = session.sessionType.toLowerCase()
  return type.includes('strength') || type.includes('gym') || session.modality === 'strength'
}

function formatDistance(distanceM: number): string {
  const kilometres = distanceM / 1000
  const value = Number.isInteger(kilometres) ? String(kilometres) : kilometres.toFixed(1).replace('.', ',')
  return `${value} km`
}

function shorten(value: string): string {
  const cleaned = value.trim().replace(/\s+/g, ' ')
  return cleaned.length > 72 ? `${cleaned.slice(0, 69).trimEnd()}…` : cleaned || 'Ver indicaciones del plan'
}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}
