import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { Link } from 'react-router'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Legend,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import {
  DashboardService,
  ProfileService,
  RacesService,
} from '../api/generated'
import { ErrorState, LoadingState } from '../components/States'
import { getCurrentTrainingPlanOrNull, readableApiError } from '../lib/api'
import {
  buildDailyDistanceChartRows,
  formatPace,
} from '../lib/dashboard'
import { localDateKey, sessionKind } from '../lib/calendar'
import {
  buildRemainingWeekAgenda,
  sessionAgendaDetail,
  sessionAgendaTitle,
  type RemainingWeekDay,
} from '../lib/weekAgenda'

const windowOptions = [4, 8, 12] as const

function daysUntil(date: string): number {
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  return Math.ceil((new Date(`${date}T00:00:00`).getTime() - today.getTime()) / 86_400_000)
}

function dateLabel(value: string): string {
  return new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(new Date(`${value}T00:00:00`))
}

function fullDateLabel(value: string): string {
  return new Intl.DateTimeFormat('es', { day: 'numeric', month: 'long', year: 'numeric' }).format(new Date(`${value}T00:00:00`))
}

function formatGoalTime(value: number | string | null | undefined): string {
  if (value == null) return 'Por definir'
  const seconds = Math.round(Number(value))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const remainingSeconds = seconds % 60
  return hours > 0
    ? `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`
    : `${minutes}:${String(remainingSeconds).padStart(2, '0')}`
}

export function DashboardPage() {
  const [windowWeeks, setWindowWeeks] = useState<(typeof windowOptions)[number]>(4)

  const profile = useQuery({ queryKey: ['profile'], queryFn: () => ProfileService.getProfile() })
  const races = useQuery({ queryKey: ['races'], queryFn: () => RacesService.getRaces() })
  const plan = useQuery({ queryKey: ['plan-current'], queryFn: getCurrentTrainingPlanOrNull })
  const dashboard = useQuery({
    queryKey: ['dashboard', windowWeeks],
    queryFn: () => DashboardService.getDashboard({ weeks: windowWeeks }),
  })
  const chartRows = useMemo(
    () => buildDailyDistanceChartRows(dashboard.data?.dailyDistances ?? []),
    [dashboard.data?.dailyDistances],
  )

  if (profile.isPending || races.isPending || plan.isPending || dashboard.isPending) {
    return <LoadingState label="Preparando tu semana" />
  }
  const firstError = profile.error ?? races.error ?? plan.error ?? dashboard.error
  if (firstError) {
    return <ErrorState message={readableApiError(firstError)} retry={() => void Promise.all([
      profile.refetch(), races.refetch(), plan.refetch(), dashboard.refetch(),
    ])} />
  }
  if (!profile.data || !races.data || !dashboard.data) {
    return <ErrorState message="El dashboard respondió sin el contrato esperado." />
  }

  const nextRace = [...races.data]
    .filter((race) => race.status === 'planned' && daysUntil(race.raceDate) >= 0)
    .sort((left, right) => left.raceDate.localeCompare(right.raceDate))[0]
  const current = dashboard.data
  const today = localDateKey()
  const weekAgenda = buildRemainingWeekAgenda(today, plan.data?.sessions ?? [])

  return (
    <div className="page dashboard-page">
      <header className="page-heading split-heading">
        <div>
          <p className="eyebrow">Tu semana</p>
          <h1>Hola, {profile.data.displayName.split(' ')[0]}</h1>
          <p>Entrenamiento, recuperación y tendencias construidas desde tu historial cargado.</p>
        </div>
        <div className="dashboard-heading-actions">
          <span className="date-chip">Datos al {dateLabel(current.asOf)}</span>
          <Link className="button secondary" to="/calendar">Abrir calendario</Link>
        </div>
      </header>

      <section className="dashboard-essentials-grid" aria-label="Resumen semanal y próxima carrera">
        <article className="feature-card week-agenda-card">
          <div className="card-top"><span className="section-label">Plan vigente</span><Link to="/plan">Abrir plan →</Link></div>
          <div className="week-agenda-heading"><h2>Lo pendiente de esta semana</h2><p>Una vista simple desde hoy hasta el domingo. Los días anteriores desaparecen automáticamente.</p></div>
          <div className="week-agenda-groups">
            <WeekAgendaGroup label="Hoy" days={weekAgenda.slice(0, 1)} planVersionId={plan.data?.version.id} />
            <WeekAgendaGroup label="Mañana" days={weekAgenda.slice(1, 2)} planVersionId={plan.data?.version.id} emptyMessage="La semana termina hoy." />
            <WeekAgendaGroup label="Pendiente esta semana" days={weekAgenda.slice(2)} planVersionId={plan.data?.version.id} emptyMessage="No quedan más días después de mañana." />
          </div>
          <p className="rpe-help">RPE: percepción del esfuerzo, de 1 (muy fácil) a 10 (máximo). El objetivo completo aparece al abrir cada entrenamiento.</p>
        </article>

        <article className="quiet-card next-race-card">
          <div className="card-top"><span className="section-label">Próxima carrera</span><Link to="/races">Ver carreras →</Link></div>
          {nextRace ? <>
            <p className="race-countdown">En {daysUntil(nextRace.raceDate)} días</p>
            <h2>{nextRace.name}</h2>
            <dl className="next-race-details">
              <div><dt>Fecha</dt><dd>{fullDateLabel(nextRace.raceDate)}</dd></div>
              <div><dt>Distancia</dt><dd>{(Number(nextRace.distanceM) / 1000).toLocaleString('es', { maximumFractionDigits: 2 })} km</dd></div>
              <div><dt>Tiempo objetivo</dt><dd>{formatGoalTime(nextRace.currentGoal?.goalTimeSeconds)}</dd></div>
              <div><dt>Lugar</dt><dd>{nextRace.location ?? 'Por definir'}</dd></div>
            </dl>
          </> : <div className="today-rest compact"><span aria-hidden="true">△</span><div><h2>Sin próxima carrera</h2><p>Define una meta para verla aquí.</p></div></div>}
        </article>
      </section>

      <section className="dashboard-section" aria-labelledby="trend-heading">
        <div className="section-heading-row">
          <div><p className="eyebrow">Historial reciente</p><h2 id="trend-heading">Distancia por día</h2><p>Cada entrenamiento aparece en su fecha real; caminadora y exterior permanecen separadas.</p></div>
          <div className="window-switch" aria-label="Ventana de tendencias">
            {windowOptions.map((option) => <button key={option} type="button" className={windowWeeks === option ? 'active' : ''} onClick={() => setWindowWeeks(option)}>{option} sem</button>)}
          </div>
        </div>

        <article className="feature-card trend-card">
          <p className="chart-guide"><strong>X:</strong> fecha · <strong>Y:</strong> kilómetros realizados ese día.</p>
          <div className="chart-frame" role="img" aria-label="Distancia diaria separada entre caminadora, exterior y otras modalidades">
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={chartRows} margin={{ top: 12, right: 12, left: 12, bottom: 24 }}>
                <CartesianGrid stroke="#49675f" strokeDasharray="3 3" />
                <XAxis
                  dataKey="date"
                  tick={{ fill: '#c5d6d0', fontSize: 12 }}
                  axisLine={{ stroke: '#769089' }}
                  label={{ value: 'Fecha', position: 'insideBottomRight', offset: -14, fill: '#e7f0ed' }}
                />
                <YAxis
                  unit=" km"
                  width={58}
                  tick={{ fill: '#c5d6d0', fontSize: 12 }}
                  axisLine={{ stroke: '#769089' }}
                  label={{ value: 'Kilómetros', angle: -90, position: 'insideLeft', fill: '#e7f0ed' }}
                />
                <Tooltip />
                <Legend />
                <Bar dataKey="treadmillKm" name="Caminadora" fill="#d2693c" />
                <Bar dataKey="outdoorKm" name="Exterior" fill="#f2c14e" />
                <Bar dataKey="otherKm" name="Otra/ND" fill="#7b7f76" />
              </BarChart>
            </ResponsiveContainer>
          </div>

          <div className="trend-actions"><Link to="/activities">Ver actividades históricas →</Link></div>
          <details className="trend-table-details">
            <summary>Ver distancias diarias y actividades</summary>
            <div className="table-scroll"><table className="accessible-table">
              <caption>Distancias por fecha; los días sin carrera conservan cero.</caption>
              <thead><tr><th>Fecha</th><th>Caminadora</th><th>Exterior</th><th>Otra/ND</th><th>Actividades</th></tr></thead>
              <tbody>{(current.dailyDistances ?? []).map((day) => {
                const modality = (name: string) => day.modalities.find((item) => item.modality === name)
                return <tr key={day.date}>
                  <th>{dateLabel(day.date)}</th>
                  <td>{modality('treadmill')?.distanceM == null ? '0.00 km' : `${(Number(modality('treadmill')?.distanceM) / 1000).toFixed(2)} km · ${formatPace(modality('treadmill')?.paceSecondsPerKm)}`}</td>
                  <td>{modality('outdoor')?.distanceM == null ? '0.00 km' : `${(Number(modality('outdoor')?.distanceM) / 1000).toFixed(2)} km · ${formatPace(modality('outdoor')?.paceSecondsPerKm)}`}</td>
                  <td>{modality('other')?.distanceM == null ? '0.00 km' : `${(Number(modality('other')?.distanceM) / 1000).toFixed(2)} km`}</td>
                  <td>{day.sources.length ? day.sources.map((source) => <Link key={source.activityId} to={source.href}>{source.label}</Link>) : '—'}</td>
                </tr>
              })}</tbody>
            </table></div>
          </details>
        </article>
      </section>

    </div>
  )
}

function WeekAgendaGroup({
  label,
  days,
  planVersionId,
  emptyMessage = 'No hay días disponibles.',
}: {
  label: string
  days: RemainingWeekDay[]
  planVersionId: string | undefined
  emptyMessage?: string
}) {
  return <section className="week-agenda-group" aria-label={label}>
    <h3>{label}</h3>
    {days.length === 0 ? <p className="week-agenda-empty">{emptyMessage}</p> : days.map((day) => (
      <article className="week-agenda-day" key={day.date}>
        <time dateTime={day.date}><strong>{day.weekday}</strong><span>{day.shortDate}</span></time>
        <div className="week-agenda-items">
          {day.sessions.length === 0 ? <div className="week-agenda-rest"><span aria-hidden="true">○</span><div><strong>Descanso</strong><small>Sin entrenamiento programado</small></div></div> : day.sessions.map((session) => {
            const kind = sessionKind(session)
            const content = <><span className="week-agenda-icon" aria-hidden="true">{kind.icon}</span><span className="week-agenda-copy"><strong>{sessionAgendaTitle(session)}</strong><small>{sessionAgendaDetail(session)}{session.obligation === 'optional' ? ' · Opcional' : ''}</small></span><span className="week-agenda-arrow" aria-hidden="true">→</span></>
            return planVersionId
              ? <Link className={`week-agenda-session ${kind.className}`} to={`/plan?version=${planVersionId}&session=${session.id}`} key={session.id}>{content}</Link>
              : <div className={`week-agenda-session ${kind.className}`} key={session.id}>{content}</div>
          })}
        </div>
      </article>
    ))}
  </section>
}
