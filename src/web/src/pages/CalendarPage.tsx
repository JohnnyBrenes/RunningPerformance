import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import {
  ActivitiesService,
  PlansService,
  SessionsService,
  type ActivitySummaryResponse,
  type PlannedSessionResponse,
} from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'
import {
  calendarDates,
  calendarTitle,
  executionLabel,
  isExecuted,
  isWithinPeriod,
  localDateKey,
  moveCalendarCursor,
  parseLocalDate,
  sessionKind,
  type CalendarView,
} from '../lib/calendar'

const viewLabels: Record<CalendarView, string> = {
  month: 'Mes',
  week: 'Semana',
  day: 'Día',
}

type CompletionResult = {
  statuses: Record<string, string | null>
  unavailable: number
}

export function CalendarPage() {
  const today = localDateKey()
  const [view, setView] = useState<CalendarView>('month')
  const [cursor, setCursor] = useState(today)
  const plan = useQuery({
    queryKey: ['plan-current'],
    queryFn: () => PlansService.getCurrentTrainingPlan(),
    retry: false,
  })
  const dates = useMemo(() => calendarDates(view, cursor), [cursor, view])
  const activities = useQuery({
    queryKey: ['calendar-activities', dates[0], dates[dates.length - 1]],
    queryFn: () => ActivitiesService.getActivities({
      page: 1,
      pageSize: 100,
      from: dates[0],
      to: dates[dates.length - 1],
      sort: 'startedAt',
      direction: 'asc',
    }),
  })
  const visibleSessions = useMemo(() => {
    if (!plan.data) return []
    const visible = new Set(dates)
    return plan.data.sessions.filter((session) => visible.has(session.scheduledDate))
  }, [dates, plan.data])
  const sessionIds = useMemo(
    () => visibleSessions.map((session) => session.id).sort(),
    [visibleSessions],
  )
  const completions = useQuery({
    queryKey: ['calendar-completions', sessionIds],
    enabled: sessionIds.length > 0,
    queryFn: async (): Promise<CompletionResult> => {
      const results = await Promise.all(visibleSessions.map(async (session) => {
        try {
          const completion = await SessionsService.getSessionCompletion({ sessionId: session.id })
          return { id: session.id, status: completion.outcome?.executionStatus ?? null, available: true }
        } catch {
          return { id: session.id, status: null, available: false }
        }
      }))
      return {
        statuses: Object.fromEntries(results.map((result) => [result.id, result.status])),
        unavailable: results.filter((result) => !result.available).length,
      }
    },
  })

  if (plan.isPending) return <LoadingState label="Preparando calendario" />
  if (plan.isError) {
    return <EmptyState title="Todavía no hay un calendario">Publica una versión del plan para consultar sus sesiones por mes, semana o día.</EmptyState>
  }
  if (!plan.data) return <ErrorState message="El plan respondió sin el contrato esperado." />

  const sessionsByDate = new Map<string, PlannedSessionResponse[]>()
  for (const session of plan.data.sessions) {
    const sessions = sessionsByDate.get(session.scheduledDate) ?? []
    sessions.push(session)
    sessionsByDate.set(session.scheduledDate, sessions)
  }
  const activitiesByDate = new Map<string, ActivitySummaryResponse[]>()
  for (const activity of activities.data?.items ?? []) {
    const date = activity.startedAtLocal.slice(0, 10)
    const items = activitiesByDate.get(date) ?? []
    items.push(activity)
    activitiesByDate.set(date, items)
  }
  const month = parseLocalDate(cursor).getMonth()
  const statuses = completions.data?.statuses ?? {}

  return (
    <div className="page calendar-page">
      <header className="page-heading split-heading">
        <div>
          <p className="eyebrow">Programa de entrenamiento</p>
          <h1>Calendario</h1>
          <p>Consulta qué toca y abre el detalle de cada sesión.</p>
        </div>
        <Link className="button secondary" to="/plan">Gestionar plan completo</Link>
      </header>

      <section className="calendar-toolbar" aria-label="Controles del calendario">
        <div className="calendar-view-switch" aria-label="Vista del calendario">
          {(Object.keys(viewLabels) as CalendarView[]).map((option) => (
            <button
              className={view === option ? 'active' : ''}
              key={option}
              type="button"
              aria-pressed={view === option}
              onClick={() => setView(option)}
            >
              {viewLabels[option]}
            </button>
          ))}
        </div>
        <div className="calendar-navigation">
          <button type="button" aria-label="Periodo anterior" onClick={() => setCursor(moveCalendarCursor(view, cursor, -1))}>←</button>
          <button type="button" onClick={() => setCursor(today)}>Hoy</button>
          <button type="button" aria-label="Periodo siguiente" onClick={() => setCursor(moveCalendarCursor(view, cursor, 1))}>→</button>
        </div>
      </section>

      <div className="calendar-period-heading">
        <h2>{calendarTitle(view, cursor)}</h2>
        <span>Plan v{plan.data.version.versionNumber}</span>
      </div>

      <div className="calendar-legend" aria-label="Leyenda">
        <span><i className="executed" /> Realizada</span>
        <span><i className="pending" /> Pendiente o no realizada</span>
      </div>

      {completions.data && completions.data.unavailable > 0 && (
        <p className="calendar-warning" role="status">No se pudo confirmar el estado de {completions.data.unavailable} {completions.data.unavailable === 1 ? 'sesión' : 'sesiones'}; se muestran en gris.</p>
      )}
      {activities.isError && <p className="calendar-warning" role="status">No se pudieron cargar las actividades realizadas; el plan sigue disponible.</p>}

      {view === 'month' && (
        <MonthCalendar
          dates={dates}
          month={month}
          today={today}
          planStart={plan.data.version.periodStart}
          planEnd={plan.data.version.periodEnd}
          planVersionId={plan.data.version.id}
          sessionsByDate={sessionsByDate}
          activitiesByDate={activitiesByDate}
          statuses={statuses}
        />
      )}
      {view === 'week' && (
        <div className="calendar-week-grid">
          {dates.map((date) => (
            <CalendarDay
              key={date}
              date={date}
              today={today}
              inPlan={isWithinPeriod(date, plan.data.version.periodStart, plan.data.version.periodEnd)}
              planVersionId={plan.data.version.id}
              sessions={sessionsByDate.get(date) ?? []}
              activities={activitiesByDate.get(date) ?? []}
              statuses={statuses}
            />
          ))}
        </div>
      )}
      {view === 'day' && (
        <div className="calendar-day-view">
          <CalendarDay
            date={cursor}
            today={today}
            inPlan={isWithinPeriod(cursor, plan.data.version.periodStart, plan.data.version.periodEnd)}
            planVersionId={plan.data.version.id}
            sessions={sessionsByDate.get(cursor) ?? []}
            activities={activitiesByDate.get(cursor) ?? []}
            statuses={statuses}
            expanded
          />
        </div>
      )}
    </div>
  )
}

function MonthCalendar({
  dates,
  month,
  today,
  planStart,
  planEnd,
  planVersionId,
  sessionsByDate,
  activitiesByDate,
  statuses,
}: {
  dates: string[]
  month: number
  today: string
  planStart: string
  planEnd: string
  planVersionId: string
  sessionsByDate: Map<string, PlannedSessionResponse[]>
  activitiesByDate: Map<string, ActivitySummaryResponse[]>
  statuses: Record<string, string | null>
}) {
  return (
    <div className="calendar-month-scroll">
      <div className="calendar-month-grid">
        {['Lun', 'Mar', 'Mié', 'Jue', 'Vie', 'Sáb', 'Dom'].map((weekday) => <span className="calendar-weekday" key={weekday}>{weekday}</span>)}
        {dates.map((date) => {
          const sessions = sessionsByDate.get(date) ?? []
          const activities = activitiesByDate.get(date) ?? []
          const inPlan = isWithinPeriod(date, planStart, planEnd)
          const outsideMonth = parseLocalDate(date).getMonth() !== month
          return (
            <div className={`calendar-month-day${outsideMonth ? ' outside' : ''}${date === today ? ' today' : ''}`} key={date}>
              <span className="calendar-day-number">{parseLocalDate(date).getDate()}{date === today && <small>Hoy</small>}</span>
              <div className="calendar-month-sessions">
                {sessions.map((session) => (
                  <SessionLink
                    compact
                    key={session.id}
                    planVersionId={planVersionId}
                    session={session}
                    status={statuses[session.id]}
                  />
                ))}
                {activities.map((activity) => <ActivityLink activity={activity} compact key={activity.id} />)}
                {sessions.length === 0 && activities.length === 0 && inPlan && <span className="rest-label">Descanso</span>}
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function CalendarDay({
  date,
  today,
  inPlan,
  planVersionId,
  sessions,
  activities,
  statuses,
  expanded = false,
}: {
  date: string
  today: string
  inPlan: boolean
  planVersionId: string
  sessions: PlannedSessionResponse[]
  activities: ActivitySummaryResponse[]
  statuses: Record<string, string | null>
  expanded?: boolean
}) {
  const label = new Intl.DateTimeFormat('es', {
    weekday: 'long',
    day: 'numeric',
    month: expanded ? 'long' : 'short',
  }).format(parseLocalDate(date))

  return (
    <article className={`calendar-day-card${date === today ? ' today' : ''}${expanded ? ' expanded' : ''}`}>
      <header><span>{label}</span>{date === today && <strong>Hoy</strong>}</header>
      <div className="calendar-day-sessions">
        {sessions.map((session) => (
          <SessionLink
            key={session.id}
            planVersionId={planVersionId}
            session={session}
            status={statuses[session.id]}
            expanded={expanded}
          />
        ))}
        {activities.map((activity) => <ActivityLink activity={activity} key={activity.id} expanded={expanded} />)}
        {sessions.length === 0 && activities.length === 0 && (
          <div className="calendar-rest">
            <span aria-hidden="true">○</span>
            <div><strong>{inPlan ? 'Descanso' : 'Sin programación'}</strong><p>{inPlan ? 'No hay una sesión prevista para este día.' : 'Este día está fuera del periodo del plan publicado.'}</p></div>
          </div>
        )}
      </div>
    </article>
  )
}

function ActivityLink({
  activity,
  compact = false,
  expanded = false,
}: {
  activity: ActivitySummaryResponse
  compact?: boolean
  expanded?: boolean
}) {
  const distance = activity.distanceM == null ? null : `${(Number(activity.distanceM) / 1000).toFixed(2)} km`
  const duration = activity.durationSeconds == null ? null : `${Math.round(Number(activity.durationSeconds) / 60)} min`
  const metrics = [distance, duration].filter(Boolean).join(' · ')
  return (
    <Link className={`calendar-activity${compact ? ' compact' : ''}`} to={`/activities?activity=${encodeURIComponent(activity.id)}`}>
      {!compact && <span className="calendar-activity-icon" aria-hidden="true">✓</span>}
      <span className="calendar-session-copy">
        <strong>{activity.title ?? activity.activityType.replaceAll('_', ' ')}</strong>
        {!compact && <small>Realizado{activity.modality ? ` · ${activity.modality === 'treadmill' ? 'Caminadora' : activity.modality === 'outdoor' ? 'Exterior' : activity.modality}` : ''}</small>}
        {expanded && metrics && <span className="calendar-session-metrics">{metrics}</span>}
      </span>
    </Link>
  )
}

function SessionLink({
  session,
  planVersionId,
  status,
  compact = false,
  expanded = false,
}: {
  session: PlannedSessionResponse
  planVersionId: string
  status: string | null | undefined
  compact?: boolean
  expanded?: boolean
}) {
  const kind = sessionKind(session)
  const executed = isExecuted(status)
  const href = `/plan?version=${encodeURIComponent(planVersionId)}&session=${encodeURIComponent(session.id)}`
  return (
    <Link className={`calendar-session ${executed ? 'executed' : 'pending'} ${kind.className}${compact ? ' compact' : ''}`} to={href}>
      <span className="calendar-session-icon" aria-hidden="true">{kind.icon}</span>
      <span className="calendar-session-copy">
        <strong>{kind.label}</strong>
        {!compact && <small>{session.objective}</small>}
        {expanded && <span className="calendar-session-metrics">{session.durationSeconds == null ? 'Duración ND' : `${Math.round(Number(session.durationSeconds) / 60)} min`}{session.distanceM == null ? '' : ` · ${(Number(session.distanceM) / 1000).toFixed(1)} km`}</span>}
      </span>
      {!compact && <span className="calendar-session-status">{executionLabel(status)}</span>}
    </Link>
  )
}
