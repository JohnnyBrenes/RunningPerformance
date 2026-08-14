import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { EvaluationsService, type WeeklyEvaluationDetailResponse, type WeeklyEvaluationSessionResponse, type WeeklyMetricValueResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'

const metricTitles: Record<string, string> = {
  P1: 'Cumplimiento por tipo',
  P2: 'Volumen de running',
  P3: 'Tirada larga exterior',
  P4: 'Carga interna sRPE',
  P5: 'Seguridad y recuperación',
}

export function EvaluationsPage() {
  const queryClient = useQueryClient()
  const [evaluationId, setEvaluationId] = useState<string | null>(null)
  const [snapshotStatus, setSnapshotStatus] = useState('provisional')
  const evaluations = useQuery({ queryKey: ['evaluations'], queryFn: () => EvaluationsService.getWeeklyEvaluations() })

  useEffect(() => {
    if (!evaluationId && evaluations.data?.[0]) setEvaluationId(evaluations.data[0].id)
  }, [evaluationId, evaluations.data])

  const detail = useQuery({
    queryKey: ['evaluation', evaluationId],
    queryFn: () => EvaluationsService.getWeeklyEvaluation({ evaluationId: evaluationId! }),
    enabled: Boolean(evaluationId),
  })

  const createSnapshot = useMutation({
    mutationFn: () => EvaluationsService.createWeeklyEvaluationSnapshot({
      requestBody: { weekStart: currentMonday(), status: snapshotStatus },
    }),
    onSuccess: async (created) => {
      setEvaluationId(created.evaluation.id)
      await queryClient.invalidateQueries({ queryKey: ['evaluations'] })
    },
  })

  if (evaluations.isPending) return <LoadingState label="Cargando evaluaciones" />
  if (evaluations.isError) return <ErrorState message={readableApiError(evaluations.error)} retry={() => void evaluations.refetch()} />

  return (
    <div className="page evaluations-page">
      <header className="page-heading split-heading">
        <div><p className="eyebrow">Cierre semanal</p><h1>Evaluación P1–P5</h1><p>Snapshot explicable, evidencia navegable y decisión humana antes de ajustar el plan.</p></div>
        <form className="snapshot-actions" onSubmit={(event) => { event.preventDefault(); createSnapshot.mutate() }}>
          <label>Nuevo snapshot<select value={snapshotStatus} onChange={(event) => setSnapshotStatus(event.target.value)}><option value="provisional">Provisional</option><option value="final">Final</option></select></label>
          <button className="button primary" disabled={createSnapshot.isPending}>{createSnapshot.isPending ? 'Congelando…' : 'Crear'}</button>
        </form>
      </header>
      {createSnapshot.isError && <p className="form-alert" role="alert">{readableApiError(createSnapshot.error)}</p>}

      {evaluations.data.length === 0 ? <EmptyState title="Todavía no hay evaluaciones">Crea el primer snapshot de la semana activa.</EmptyState> : <>
        <div className="evaluation-tabs" role="list" aria-label="Snapshots disponibles">
          {evaluations.data.map((evaluation) => <button className={evaluation.id === evaluationId ? 'active' : ''} type="button" key={evaluation.id} onClick={() => setEvaluationId(evaluation.id)}><strong>{formatPeriod(evaluation.weekStart, evaluation.weekEnd)}</strong><small>{statusLabel(evaluation.status)} · {trafficLabel(evaluation.trafficLight)}</small></button>)}
        </div>
        {detail.isPending && <LoadingState label="Abriendo snapshot" />}
        {detail.isError && <ErrorState message={readableApiError(detail.error)} retry={() => void detail.refetch()} />}
        {detail.data && <EvaluationDetail detail={detail.data} onChanged={async (updated) => { setEvaluationId(updated.evaluation.id); await Promise.all([queryClient.invalidateQueries({ queryKey: ['evaluations'] }), queryClient.invalidateQueries({ queryKey: ['evaluation', updated.evaluation.id] }), queryClient.invalidateQueries({ queryKey: ['plans'] })]) }} />}
      </>}
    </div>
  )
}

function EvaluationDetail({ detail, onChanged }: { detail: WeeklyEvaluationDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const grouped = useMemo(() => Object.fromEntries(['P1', 'P2', 'P3', 'P4', 'P5'].map((code) => [code, detail.metrics.filter((metric) => metric.metricCode === code)])), [detail.metrics])
  return <>
    <section className={`traffic-card ${detail.evaluation.trafficLight}`} aria-labelledby="traffic-title">
      <div className="traffic-light" aria-hidden="true"><i /><i /><i /></div>
      <div><span className="section-label">Semáforo · peor señal prevalece</span><h2 id="traffic-title">{trafficLabel(detail.evaluation.trafficLight)}</h2><p>{detail.evaluation.rationale}</p><small>{statusLabel(detail.evaluation.status)} · corte {formatDateTime(detail.evaluation.cutoffAt)} · {detail.evaluation.formatVersion}</small></div>
    </section>

    <div className="metric-sections">
      {Object.entries(grouped).map(([code, metrics]) => <MetricSection code={code} metrics={metrics} key={code} />)}
    </div>

    <section className="evaluation-sources section-block" aria-labelledby="sources-title">
      <div className="section-heading"><div><span className="section-label">Fuentes congeladas</span><h2 id="sources-title">Sesiones de la semana</h2></div><span className="date-chip">{detail.sessions.length} fuentes</span></div>
      <div className="source-list">{detail.sessions.map((session) => <article key={session.id}><div><strong>{session.scheduledDate ? fullDate(session.scheduledDate) : 'Actividad sin plan'}</strong><span>{sessionTypeLabel(session.sessionType)}</span></div><span className={`source-status ${session.executionStatus ? '' : 'missing'}`}>{executionLabel(session.executionStatus)}</span>{session.plannedSessionId && detail.evaluation.planVersionId && <a href={`/plan?version=${detail.evaluation.planVersionId}&session=${session.plannedSessionId}`}>Abrir sesión</a>}</article>)}</div>
    </section>

    {detail.decision ? <DecisionRecord detail={detail} /> : <DecisionForm detail={detail} onChanged={onChanged} />}
  </>
}

function MetricSection({ code, metrics }: { code: string; metrics: WeeklyMetricValueResponse[] }) {
  return <section className="weekly-metric" aria-labelledby={`metric-${code}`}><header><span>{code}</span><div><h2 id={`metric-${code}`}>{metricTitles[code]}</h2><p>{metricDescription(code)}</p></div></header><div className="weekly-metric-grid">{metrics.map((metric) => <article className={metric.status === 'missing' ? 'missing' : ''} key={metric.id}><span>{dimensionLabel(metric.dimension)}</span><strong>{formatMetricValue(metric)}</strong><small>{metric.status === 'missing' ? 'Dato no disponible' : metric.formulaVersion}</small><details><summary>Evidencia ({metric.evidence.length})</summary><ul>{metric.evidence.map((evidence, index) => <li key={`${evidence.sourceType}-${evidence.sourceId}-${index}`}>{evidence.href ? <a href={evidence.href}>{evidence.label}</a> : evidence.label}<small>{sourceTypeLabel(evidence.sourceType)}</small></li>)}</ul></details></article>)}</div></section>
}

function DecisionForm({ detail, onChanged }: { detail: WeeklyEvaluationDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const [decision, setDecision] = useState('execute_plan')
  const [observation, setObservation] = useState('')
  const [evidence, setEvidence] = useState('')
  const [comparison, setComparison] = useState('')
  const [interpretation, setInterpretation] = useState('')
  const [recommendation, setRecommendation] = useState('')
  const adjustable = decision === 'adapt' || decision === 'reduce'
  const sessions = detail.sessions.filter((session): session is WeeklyEvaluationSessionResponse & { plannedSessionId: string } => Boolean(session.plannedSessionId))
  const [sourceSessionId, setSourceSessionId] = useState(sessions[0]?.plannedSessionId ?? '')
  const sourceSession = sessions.find((session) => session.plannedSessionId === sourceSessionId) ?? sessions[0]
  const [scheduledDate, setScheduledDate] = useState(sourceSession?.scheduledDate ?? '')
  const [objective, setObjective] = useState(sourceSession?.objective ?? '')
  const [rationale, setRationale] = useState('')
  const [reviewCriterion, setReviewCriterion] = useState('')

  const confirm = useMutation({
    mutationFn: () => EvaluationsService.confirmWeeklyDecision({
      evaluationId: detail.evaluation.id,
      requestBody: {
        decision, observation, evidence, historicalComparison: comparison, interpretation, recommendation,
        planAdjustment: adjustable ? {
          sourcePlanVersionId: detail.evaluation.planVersionId!, rationale, reviewCriterion,
          sessionChanges: [{ sourcePlannedSessionId: sourceSessionId, scheduledDate: scheduledDate || null, objective: objective || null }],
        } : null,
      },
    }),
    onSuccess: onChanged,
  })

  const changeSource = (id: string) => {
    setSourceSessionId(id)
    const session = sessions.find((candidate) => candidate.plannedSessionId === id)
    setScheduledDate(session?.scheduledDate ?? '')
    setObjective(session?.objective ?? '')
  }

  return <section className="decision-panel" aria-labelledby="decision-title"><div><span className="section-label">Confirmación humana</span><h2 id="decision-title">Documentar decisión</h2><p>La recomendación no modifica el plan. Adaptar o reducir crea un borrador nuevo; la publicación sigue siendo otro acto explícito.</p></div><form onSubmit={(event) => { event.preventDefault(); confirm.mutate() }}><label>Decisión<select value={decision} onChange={(event) => setDecision(event.target.value)}><option value="execute_plan">Ejecutar el plan</option><option value="adapt">Adaptar</option><option value="reduce">Reducir</option><option value="stop_and_assess">Detener y valorar</option></select></label><NarrativeField label="Observación" value={observation} setValue={setObservation} /><NarrativeField label="Evidencia" value={evidence} setValue={setEvidence} /><NarrativeField label="Comparación histórica" value={comparison} setValue={setComparison} /><NarrativeField label="Interpretación" value={interpretation} setValue={setInterpretation} /><NarrativeField label="Recomendación" value={recommendation} setValue={setRecommendation} />{adjustable && <fieldset className="adjustment-fields"><legend>Nueva versión del plan</legend><label>Sesión de origen<select required value={sourceSessionId} onChange={(event) => changeSource(event.target.value)}>{sessions.map((session) => <option key={session.plannedSessionId} value={session.plannedSessionId}>{session.scheduledDate} · {sessionTypeLabel(session.sessionType)}</option>)}</select></label><label>Nueva fecha<input type="date" required value={scheduledDate} onChange={(event) => setScheduledDate(event.target.value)} /></label><label>Nuevo objetivo<textarea rows={3} required value={objective} onChange={(event) => setObjective(event.target.value)} /></label><NarrativeField label="Motivo exacto" value={rationale} setValue={setRationale} /><NarrativeField label="Criterio de revisión" value={reviewCriterion} setValue={setReviewCriterion} /></fieldset>}{confirm.isError && <p className="form-alert" role="alert">{readableApiError(confirm.error)}</p>}<button className="button primary" disabled={confirm.isPending || (adjustable && !detail.evaluation.planVersionId)}>{confirm.isPending ? 'Confirmando…' : 'Confirmar decisión'}</button></form></section>
}

function NarrativeField({ label, value, setValue }: { label: string; value: string; setValue: (value: string) => void }) { return <label>{label}<textarea rows={3} required maxLength={4000} value={value} onChange={(event) => setValue(event.target.value)} /></label> }

function DecisionRecord({ detail }: { detail: WeeklyEvaluationDetailResponse }) {
  const decision = detail.decision!
  return <section className="decision-record" aria-labelledby="decision-record-title"><header><div><span className="section-label">Decisión confirmada · {formatDateTime(decision.confirmedAt)}</span><h2 id="decision-record-title">{decisionLabel(decision.decision)}</h2></div><span className="audit-chip">Auditable</span></header><dl><div><dt>Observación</dt><dd>{decision.observation}</dd></div><div><dt>Evidencia</dt><dd>{decision.evidence}</dd></div><div><dt>Comparación</dt><dd>{decision.historicalComparison}</dd></div><div><dt>Interpretación</dt><dd>{decision.interpretation}</dd></div><div><dt>Recomendación</dt><dd>{decision.recommendation}</dd></div></dl>{decision.adjustments.map((adjustment) => <article className="adjustment-record" key={adjustment.id}><span>Nueva versión sin publicar</span><h3>{adjustmentTypeLabel(adjustment.adjustmentType)}</h3><p>{adjustment.rationale}</p><small>Criterio: {adjustment.reviewCriterion}</small><a className="button secondary" href={`/plan?version=${adjustment.targetPlanVersionId}`}>Revisar borrador</a></article>)}</section>
}

export function formatMetricValue(metric: Pick<WeeklyMetricValueResponse, 'status' | 'numericValue' | 'booleanValue' | 'textValue' | 'unit' | 'dimension'>) {
  if (metric.status === 'missing' || metric.status === 'not_applicable') return 'ND'
  if (metric.booleanValue != null) return metric.booleanValue ? 'Sí' : 'No'
  if (metric.textValue != null) return metric.textValue
  if (metric.numericValue == null) return 'ND'
  const value = Number(metric.numericValue)
  if (metric.unit === 'percent') return `${round(value)} %`
  if (metric.unit === 'm' && metric.dimension.includes('distance')) return `${round(value / 1000)} km`
  if (metric.unit === 's' && metric.dimension.includes('duration')) return `${round(value / 60)} min`
  if (metric.unit === 's/km') {
    const roundedSeconds = Math.round(value)
    return `${Math.floor(roundedSeconds / 60)}:${String(roundedSeconds % 60).padStart(2, '0')} min/km`
  }
  return `${round(value)}${metric.unit ? ` ${metric.unit}` : ''}`
}

function currentMonday() { const date = new Date(); const day = date.getDay() || 7; date.setDate(date.getDate() - day + 1); return localDate(date) }
function localDate(date: Date) { return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function parseDate(value: string) { return new Date(`${value}T00:00:00`) }
function formatPeriod(start: string, end: string) { return `${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(start))} – ${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(end))}` }
function fullDate(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', month: 'short' }).format(parseDate(value)) }
function formatDateTime(value: string) { return new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function round(value: number) { return Number(value.toFixed(2)).toLocaleString('es') }
function statusLabel(value: string) { return value === 'final' ? 'Final' : 'Provisional' }
function trafficLabel(value: string) { return ({ green: 'Verde · ejecutar', yellow: 'Amarillo · adaptar o revisar', red: 'Rojo · detener y valorar' } as Record<string, string>)[value] ?? value }
function decisionLabel(value: string) { return ({ execute_plan: 'Ejecutar el plan', adapt: 'Adaptar', reduce: 'Reducir', stop_and_assess: 'Detener y valorar' } as Record<string, string>)[value] ?? value }
function executionLabel(value: string | null) { return value ? ({ completed_as_planned: 'Según plan', completed_modified: 'Modificada', valid_substitution: 'Sustitución válida', not_completed: 'No realizada', optional_not_completed: 'Opcional no realizada' } as Record<string, string>)[value] ?? value : 'ND' }
function sessionTypeLabel(value: string | null) { return value ? ({ strength_mobility_plyometrics: 'Fuerza, movilidad y pliometría', easy_run: 'Carrera fácil', long_run: 'Tirada larga', quality: 'Calidad' } as Record<string, string>)[value] ?? value.replaceAll('_', ' ') : 'Sin tipo' }
function adjustmentTypeLabel(value: string) { return ({ objective: 'Objetivo ajustado', reschedule: 'Sesión reprogramada', reschedule_and_objective: 'Fecha y objetivo ajustados' } as Record<string, string>)[value] ?? value }
function sourceTypeLabel(value: string) { return ({ planned_session: 'Sesión planificada', activity: 'Actividad realizada', session_checkin: 'Check-in', observation: 'Observación' } as Record<string, string>)[value] ?? value }
function dimensionLabel(value: string) { return value.replace(/^session:[0-9a-f-]+$/, 'Carga de sesión').replaceAll(':', ' · ').replaceAll('_', ' ') }
function metricDescription(code: string) { return ({ P1: 'Los cinco resultados permanecen separados.', P2: 'Tiempo/distancia; ritmos de cinta y exterior no se mezclan.', P3: 'Observación explícita y respuesta posterior.', P4: 'Minutos × RPE por sesión, modalidad y semana.', P5: 'Cada componente conserva su propia señal y sus ausencias.' } as Record<string, string>)[code] }
