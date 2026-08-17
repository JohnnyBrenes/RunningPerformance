import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { EvaluationsService, type WeeklyEvaluationDetailResponse, type WeeklyEvaluationSessionResponse, type WeeklyEvaluationSummaryResponse, type WeeklyMetricValueResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'

const metricTitles: Record<string, string> = {
  P1: 'Cumplimiento por tipo', P2: 'Volumen de running', P3: 'Tirada larga exterior', P4: 'Carga interna sRPE', P5: 'Seguridad y recuperación',
}
const completedStatuses = new Set(['completed_as_planned', 'completed_modified', 'valid_substitution'])

export function EvaluationsPage() {
  const queryClient = useQueryClient()
  const [evaluationId, setEvaluationId] = useState<string | null>(null)
  const [weekStart, setWeekStart] = useState(previousMonday())
  const evaluations = useQuery({ queryKey: ['evaluations'], queryFn: () => EvaluationsService.getWeeklyEvaluations() })
  const weeklyChoices = useMemo(() => selectWeeklyEvaluations(evaluations.data ?? []), [evaluations.data])
  const selectedWeek = weeklyChoices.find((evaluation) => evaluation.weekStart === weekStart) ?? null

  useEffect(() => { setEvaluationId(selectedWeek?.id ?? null) }, [selectedWeek?.id, weekStart])

  const detail = useQuery({ queryKey: ['evaluation', evaluationId], queryFn: () => EvaluationsService.getWeeklyEvaluation({ evaluationId: evaluationId! }), enabled: Boolean(evaluationId) })
  const createReview = useMutation({
    mutationFn: () => EvaluationsService.createWeeklyEvaluationSnapshot({ requestBody: { weekStart, status: 'provisional' } }),
    onSuccess: async (created) => { setEvaluationId(created.evaluation.id); await queryClient.invalidateQueries({ queryKey: ['evaluations'] }) },
  })

  if (evaluations.isPending) return <LoadingState label="Cargando cierres semanales" />
  if (evaluations.isError) return <ErrorState message={readableApiError(evaluations.error)} retry={() => void evaluations.refetch()} />

  return <div className="page evaluations-page">
    <header className="page-heading weekly-review-heading">
      <div><p className="eyebrow">Cierre semanal</p><h1>Cierre de semana</h1><p>Elige una semana, revisa lo realizado y decide cómo continuar con el plan.</p></div>
      <form className="weekly-review-controls" onSubmit={(event) => {
        event.preventDefault()
        if (selectedWeek) {
          setEvaluationId(selectedWeek.id)
          document.querySelector('#weekly-review')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
        } else createReview.mutate()
      }}>
        <label>Semana que inicia<input type="date" required max={previousMonday()} value={weekStart} onChange={(event) => setWeekStart(event.target.value)} /></label>
        {weeklyChoices.length > 0 && <label>Semanas guardadas<select value={selectedWeek?.weekStart ?? ''} onChange={(event) => event.target.value && setWeekStart(event.target.value)}><option value="">Elegir una semana</option>{weeklyChoices.map((evaluation) => <option key={evaluation.id} value={evaluation.weekStart}>{formatPeriod(evaluation.weekStart, evaluation.weekEnd)}</option>)}</select></label>}
        <button className="button primary" disabled={createReview.isPending}>{createReview.isPending ? 'Creando resumen…' : selectedWeek ? 'Ver resumen' : 'Crear resumen'}</button>
      </form>
    </header>
    {createReview.isError && <p className="form-alert" role="alert">{readableApiError(createReview.error)}</p>}
    {!evaluationId && !createReview.isPending && <EmptyState title="Esta semana todavía no tiene cierre">Crea el resumen cuando hayas terminado de registrar sus entrenamientos.</EmptyState>}
    {detail.isPending && <LoadingState label="Abriendo el resumen semanal" />}
    {detail.isError && <ErrorState message={readableApiError(detail.error)} retry={() => void detail.refetch()} />}
    {detail.data && <EvaluationDetail key={detail.data.evaluation.id} detail={detail.data} onChanged={async (updated) => {
      setEvaluationId(updated.evaluation.id)
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['evaluations'] }), queryClient.invalidateQueries({ queryKey: ['evaluation', updated.evaluation.id] }), queryClient.invalidateQueries({ queryKey: ['plans'] })])
    }} />}
  </div>
}

function EvaluationDetail({ detail, onChanged }: { detail: WeeklyEvaluationDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const grouped = useMemo(() => Object.fromEntries(['P1', 'P2', 'P3', 'P4', 'P5'].map((code) => [code, detail.metrics.filter((metric) => metric.metricCode === code)])), [detail.metrics])
  const completed = detail.sessions.filter((session) => session.executionStatus && completedStatuses.has(session.executionStatus)).length
  const runningDistance = findMetric(detail.metrics, 'P2', 'actual_distance_m:all')
  const runningDuration = findMetric(detail.metrics, 'P2', 'actual_duration_seconds:all')
  const totalLoad = findMetric(detail.metrics, 'P4', 'total')
  const pain = findMetric(detail.metrics, 'P5', 'pain')
  const fatigue = findMetric(detail.metrics, 'P5', 'fatigue')
  const perceivedRecovery = findMetric(detail.metrics, 'P5', 'perceived_recovery')

  return <div id="weekly-review" className="weekly-review">
    <section className={`traffic-card ${detail.evaluation.trafficLight}`} aria-labelledby="traffic-title"><div className="traffic-light" aria-hidden="true"><i /><i /><i /></div><div><span className="section-label">Resultado general</span><h2 id="traffic-title">{trafficTitle(detail.evaluation.trafficLight)}</h2><p>{plainRationale(detail.evaluation.rationale)}</p><small>Calculado con los datos registrados hasta {formatDateTime(detail.evaluation.cutoffAt)}.</small></div></section>
    <section className="section-block weekly-summary" aria-labelledby="summary-title"><div className="section-heading"><div><span className="section-label">Lo importante</span><h2 id="summary-title">Resumen de la semana</h2></div><span className="date-chip">{formatPeriod(detail.evaluation.weekStart, detail.evaluation.weekEnd)}</span></div><div className="weekly-summary-grid">
      <SummaryCard label="Entrenamientos" value={`${completed} de ${detail.sessions.length}`} detail={completed === detail.sessions.length && detail.sessions.length > 0 ? 'Todos registrados' : `${detail.sessions.length - completed} sin completar`} />
      <SummaryCard label="Running" value={primaryMetricValue(runningDistance)} detail={runningDuration ? primaryMetricValue(runningDuration) : 'Duración sin registrar'} />
      <SummaryCard label="Carga" value={primaryMetricValue(totalLoad)} detail="sRPE: minutos × esfuerzo percibido" />
      <SummaryCard label="Recuperación" value={recoverySummary(pain, fatigue, perceivedRecovery)} detail="Dolor, fatiga y recuperación percibida" />
    </div></section>
    <section className="evaluation-sources section-block" aria-labelledby="sessions-title"><div className="section-heading"><div><span className="section-label">Una vez por entrenamiento</span><h2 id="sessions-title">Entrenamientos</h2></div><span className="date-chip">{detail.sessions.length} registrados</span></div>
      {detail.sessions.length === 0 ? <p className="muted-copy">No hay entrenamientos registrados para esta semana.</p> : <div className="source-list">{detail.sessions.map((session) => <article key={session.id}><div><strong>{session.scheduledDate ? fullDate(session.scheduledDate) : 'Actividad sin sesión planificada'}</strong><span>{sessionTypeLabel(session.sessionType)}</span></div><span className={`source-status ${session.executionStatus ? '' : 'missing'}`}>{executionLabel(session.executionStatus)}</span>{session.plannedSessionId && detail.evaluation.planVersionId && <Link to={`/plan?version=${detail.evaluation.planVersionId}&session=${session.plannedSessionId}#completion`}>Abrir entrenamiento</Link>}{!session.plannedSessionId && session.activityId && <Link to={`/activities/${session.activityId}`}>Abrir actividad</Link>}</article>)}</div>}
    </section>
    <details className="technical-evaluation section-block"><summary>Ver desglose técnico P1–P5</summary><p>Estos son los cálculos internos del resumen. Los entrenamientos que los originan aparecen una sola vez en la lista anterior.</p><div className="metric-sections">{Object.entries(grouped).map(([code, metrics]) => <MetricSection code={code} metrics={metrics} key={code} />)}</div></details>
    {detail.decision ? <DecisionRecord detail={detail} /> : <DecisionForm detail={detail} onChanged={onChanged} />}
  </div>
}

function SummaryCard({ label, value, detail }: { label: string; value: string; detail: string }) { return <article className="weekly-summary-card"><span>{label}</span><strong>{value}</strong><small>{detail}</small></article> }

function MetricSection({ code, metrics }: { code: string; metrics: WeeklyMetricValueResponse[] }) {
  return <section className="weekly-metric" aria-labelledby={`metric-${code}`}><header><span>{code}</span><div><h2 id={`metric-${code}`}>{metricTitles[code]}</h2><p>{metricDescription(code)}</p></div></header>{metrics.length === 0 ? <p className="muted-copy">Sin datos calculados.</p> : <div className="weekly-metric-grid">{metrics.map((metric) => <article className={metric.status === 'missing' ? 'missing' : ''} key={metric.id}><span>{dimensionLabel(metric.dimension)}</span><strong>{technicalMetricValue(metric)}</strong><small>{metric.status === 'missing' || metric.status === 'not_applicable' ? 'Sin dato registrado' : 'Calculado'}</small></article>)}</div>}</section>
}

function DecisionForm({ detail, onChanged }: { detail: WeeklyEvaluationDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const [decision, setDecision] = useState('execute_plan')
  const [notes, setNotes] = useState('')
  const adjustable = decision === 'adapt' || decision === 'reduce'
  const sessions = detail.sessions.filter((session): session is WeeklyEvaluationSessionResponse & { plannedSessionId: string } => Boolean(session.plannedSessionId))
  const [sourceSessionId, setSourceSessionId] = useState(sessions[0]?.plannedSessionId ?? '')
  const sourceSession = sessions.find((session) => session.plannedSessionId === sourceSessionId) ?? sessions[0]
  const [scheduledDate, setScheduledDate] = useState(sourceSession?.scheduledDate ?? '')
  const [objective, setObjective] = useState(sourceSession?.objective ?? '')
  const [rationale, setRationale] = useState('')
  const confirm = useMutation({
    mutationFn: () => EvaluationsService.confirmWeeklyDecision({ evaluationId: detail.evaluation.id, requestBody: {
      decision, observation: notes.trim(), evidence: `Resumen automático de ${detail.sessions.length} entrenamientos y sus registros asociados.`, historicalComparison: 'No se añadió una comparación histórica en este cierre.', interpretation: `${trafficTitle(detail.evaluation.trafficLight)}: ${plainRationale(detail.evaluation.rationale)}`, recommendation: recommendationForDecision(decision),
      planAdjustment: adjustable ? { sourcePlanVersionId: detail.evaluation.planVersionId!, rationale, reviewCriterion: 'Revisar el cambio en el siguiente cierre semanal.', sessionChanges: [{ sourcePlannedSessionId: sourceSessionId, scheduledDate: scheduledDate || null, objective: objective || null }] } : null,
    } }),
    onSuccess: onChanged,
  })
  const changeSource = (id: string) => { setSourceSessionId(id); const session = sessions.find((candidate) => candidate.plannedSessionId === id); setScheduledDate(session?.scheduledDate ?? ''); setObjective(session?.objective ?? '') }
  const cannotAdjust = adjustable && (!detail.evaluation.planVersionId || sessions.length === 0)
  return <section className="decision-panel" aria-labelledby="decision-title"><div><span className="section-label">Siguiente paso</span><h2 id="decision-title">Decisión para la próxima semana</h2><p>Guarda qué harás y una nota breve. Si cambias una sesión, se creará un borrador del plan para que lo revises antes de publicarlo.</p></div><form onSubmit={(event) => { event.preventDefault(); confirm.mutate() }}><label>Qué hacer<select value={decision} onChange={(event) => setDecision(event.target.value)}><option value="execute_plan">Mantener el plan</option><option value="adapt">Adaptar una sesión</option><option value="reduce">Reducir carga</option><option value="stop_and_assess">Pausar y revisar</option></select></label><NarrativeField label="Notas de la semana" value={notes} setValue={setNotes} />{adjustable && <fieldset className="adjustment-fields"><legend>Cambio en el plan</legend>{sessions.length > 0 ? <><label>Sesión a cambiar<select required value={sourceSessionId} onChange={(event) => changeSource(event.target.value)}>{sessions.map((session) => <option key={session.plannedSessionId} value={session.plannedSessionId}>{session.scheduledDate} · {sessionTypeLabel(session.sessionType)}</option>)}</select></label><label>Nueva fecha<input type="date" required value={scheduledDate} onChange={(event) => setScheduledDate(event.target.value)} /></label><label>Nuevo objetivo<textarea rows={3} required value={objective} onChange={(event) => setObjective(event.target.value)} /></label><NarrativeField label="Motivo del cambio" value={rationale} setValue={setRationale} /></> : <p className="form-alert" role="alert">Esta semana no tiene sesiones del plan que puedan modificarse.</p>}</fieldset>}{confirm.isError && <p className="form-alert" role="alert">{readableApiError(confirm.error)}</p>}<button className="button primary" disabled={confirm.isPending || cannotAdjust}>{confirm.isPending ? 'Guardando…' : 'Guardar decisión'}</button></form></section>
}

function NarrativeField({ label, value, setValue }: { label: string; value: string; setValue: (value: string) => void }) { return <label>{label}<textarea rows={3} required maxLength={4000} value={value} onChange={(event) => setValue(event.target.value)} /></label> }

function DecisionRecord({ detail }: { detail: WeeklyEvaluationDetailResponse }) {
  const decision = detail.decision!
  return <section className="decision-record" aria-labelledby="decision-record-title"><header><div><span className="section-label">Decisión guardada · {formatDateTime(decision.confirmedAt)}</span><h2 id="decision-record-title">{decisionLabel(decision.decision)}</h2></div></header><p className="decision-note">{decision.observation}</p><details className="decision-details"><summary>Ver registro completo</summary><dl><div><dt>Resumen usado</dt><dd>{decision.evidence}</dd></div><div><dt>Comparación</dt><dd>{decision.historicalComparison}</dd></div><div><dt>Interpretación</dt><dd>{decision.interpretation}</dd></div><div><dt>Recomendación</dt><dd>{decision.recommendation}</dd></div></dl></details>{decision.adjustments.map((adjustment) => <article className="adjustment-record" key={adjustment.id}><span>Nueva versión sin publicar</span><h3>{adjustmentTypeLabel(adjustment.adjustmentType)}</h3><p>{adjustment.rationale}</p><small>Se revisará en el siguiente cierre semanal.</small><Link className="button secondary" to={`/plan?version=${adjustment.targetPlanVersionId}`}>Revisar borrador</Link></article>)}</section>
}

export function selectWeeklyEvaluations(evaluations: WeeklyEvaluationSummaryResponse[]) {
  const byWeek = new Map<string, WeeklyEvaluationSummaryResponse[]>()
  for (const evaluation of evaluations) { const group = byWeek.get(evaluation.weekStart) ?? []; group.push(evaluation); byWeek.set(evaluation.weekStart, group) }
  return [...byWeek.values()].map((group) => group.find((evaluation) => evaluation.status === 'final') ?? group.find((evaluation) => evaluation.hasDecision) ?? group[0])
}

export function formatMetricValue(metric: Pick<WeeklyMetricValueResponse, 'status' | 'numericValue' | 'booleanValue' | 'textValue' | 'unit' | 'dimension'>) {
  if (metric.status === 'missing' || metric.status === 'not_applicable') return 'ND'
  if (metric.booleanValue != null) return metric.booleanValue ? 'Sí' : 'No'
  if (metric.textValue != null) return metric.textValue
  if (metric.numericValue == null) return 'ND'
  const value = Number(metric.numericValue)
  if (metric.unit === 'percent') return `${round(value)} %`
  if (metric.unit === '0-10') return `${round(value)}/10`
  if (metric.unit?.toUpperCase() === 'AU') return `${round(value)} UA`
  if (metric.unit === 'm' && metric.dimension.includes('distance')) return `${round(value / 1000)} km`
  if (metric.unit === 's' && metric.dimension.includes('duration')) return `${round(value / 60)} min`
  if (metric.unit === 's/km') {
    const roundedSeconds = Math.round(value)
    return `${Math.floor(roundedSeconds / 60)}:${String(roundedSeconds % 60).padStart(2, '0')} min/km`
  }
  return `${round(value)}${metric.unit ? ` ${metric.unit}` : ''}`
}

export function previousMonday(reference = new Date()) {
  const date = new Date(reference)
  const day = date.getDay() || 7
  date.setDate(date.getDate() - day + 1 - 7)
  return localDate(date)
}

function findMetric(metrics: WeeklyMetricValueResponse[], code: string, dimension: string) { return metrics.find((metric) => metric.metricCode === code && metric.dimension === dimension) }
function primaryMetricValue(metric?: WeeklyMetricValueResponse) { return !metric || formatMetricValue(metric) === 'ND' ? 'Sin registrar' : formatMetricValue(metric) }
function technicalMetricValue(metric: WeeklyMetricValueResponse) { const value = formatMetricValue(metric); return value === 'ND' ? 'Sin dato' : value }
function recoverySummary(pain?: WeeklyMetricValueResponse, fatigue?: WeeklyMetricValueResponse, recovery?: WeeklyMetricValueResponse) {
  const available = [pain, fatigue, recovery].filter((metric) => metric && formatMetricValue(metric) !== 'ND')
  if (available.length === 0) return 'Sin registrar'
  if (pain?.booleanValue === true) return 'Dolor reportado'
  if (fatigue?.booleanValue === true) return 'Fatiga reportada'
  if (recovery) return `Recuperación ${formatMetricValue(recovery)}`
  return 'Sin alertas registradas'
}
function recommendationForDecision(decision: string) { return ({ execute_plan: 'Mantener el plan actual.', adapt: 'Aplicar el cambio indicado y revisarlo en el siguiente cierre semanal.', reduce: 'Reducir la carga según el cambio indicado y revisar la respuesta.', stop_and_assess: 'Pausar la progresión y valorar las señales registradas antes de continuar.' } as Record<string, string>)[decision] ?? 'Revisar el plan antes de continuar.' }
function plainRationale(value: string) {
  const rationale = value.replace(/^(Verde|Amarillo|Rojo)\s*:\s*/i, '').trim()
  return rationale ? `${rationale[0].toUpperCase()}${rationale.slice(1)}` : 'No hay una explicación registrada para este resultado.'
}
function localDate(date: Date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function parseDate(value: string) { return new Date(`${value}T00:00:00`) }
function formatPeriod(start: string, end: string) { return `${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(start))} – ${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(end))}` }
function fullDate(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', month: 'short' }).format(parseDate(value)) }
function formatDateTime(value: string) { return new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function round(value: number) { return Number(value.toFixed(2)).toLocaleString('es') }
function trafficTitle(value: string) { return ({ green: 'Semana en orden', yellow: 'Revisar antes de continuar', red: 'Pausa y revisión' } as Record<string, string>)[value] ?? value }
function decisionLabel(value: string) { return ({ execute_plan: 'Mantener el plan', adapt: 'Adaptar una sesión', reduce: 'Reducir carga', stop_and_assess: 'Pausar y revisar' } as Record<string, string>)[value] ?? value }
function executionLabel(value: string | null) { return value ? ({ completed_as_planned: 'Completado según plan', completed_modified: 'Completado con cambios', valid_substitution: 'Sustitución válida', not_completed: 'No realizado', optional_not_completed: 'Opcional no realizado' } as Record<string, string>)[value] ?? value : 'Sin registrar' }
function sessionTypeLabel(value: string | null) { return value ? ({ strength_mobility_plyometrics: 'Fuerza, movilidad y pliometría', easy_run: 'Carrera fácil', long_run: 'Tirada larga', quality: 'Calidad', cross_training: 'Entrenamiento cruzado' } as Record<string, string>)[value] ?? value.replaceAll('_', ' ') : 'Sin tipo' }
function adjustmentTypeLabel(value: string) { return ({ objective: 'Objetivo ajustado', reschedule: 'Sesión reprogramada', reschedule_and_objective: 'Fecha y objetivo ajustados' } as Record<string, string>)[value] ?? value }
function dimensionLabel(value: string) {
  const labels: Record<string, string> = {
    'actual_distance_m:all': 'Distancia realizada · total', 'actual_distance_m:outdoor': 'Distancia realizada · exterior', 'actual_distance_m:treadmill': 'Distancia realizada · cinta',
    'actual_duration_seconds:all': 'Duración realizada · total', 'actual_duration_seconds:outdoor': 'Duración realizada · exterior', 'actual_duration_seconds:treadmill': 'Duración realizada · cinta',
    'pace_seconds_per_km:all': 'Ritmo · total', 'pace_seconds_per_km:outdoor': 'Ritmo · exterior', 'pace_seconds_per_km:treadmill': 'Ritmo · cinta',
    planned_distance_m: 'Distancia planificada', planned_duration_seconds: 'Duración planificada', distance_m: 'Distancia', duration_seconds: 'Duración', elevation_gain_m: 'Desnivel positivo',
    outdoor_long_run_observation: 'Observación de tirada larga exterior', session_rpe: 'RPE de la sesión', running: 'Running', strength_mobility_plyometrics: 'Fuerza, movilidad y pliometría', cycling_or_other: 'Ciclismo u otra modalidad', total: 'Total semanal',
    fatigue: 'Fatiga', gait_changed: 'Cambio en la marcha', has_illness_or_symptom: 'Enfermedad o síntoma', pain: 'Dolor', perceived_recovery: 'Recuperación percibida', responses_24_to_48_hours: 'Respuesta 24–48 horas después', sleep_quality: 'Calidad del sueño',
  }
  return labels[value] ?? value.replace(/^session:[0-9a-f-]+$/, 'Carga de sesión').replaceAll(':', ' · ').replaceAll('_', ' ')
}
function metricDescription(code: string) { return ({ P1: 'Resultado de cada tipo de entrenamiento.', P2: 'Distancia, tiempo y ritmo de running.', P3: 'Resultado de la tirada larga exterior.', P4: 'Minutos × RPE por sesión y semana. RPE significa percepción del esfuerzo en una escala de 1 a 10.', P5: 'Dolor, fatiga, sueño y recuperación.' } as Record<string, string>)[code] }
