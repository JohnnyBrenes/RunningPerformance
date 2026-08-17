import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { ActivitiesService, EvaluationsService, PlansService, RacesService, type PlannedSessionResponse, type TrainingPlanDetailResponse, type WeeklyEvaluationDetailResponse, type WeeklyEvaluationSummaryResponse, type WeeklyMetricValueResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'
import { buildCoachReview, type CoachReview } from '../lib/coach'

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
      <div><p className="eyebrow">Coach semanal</p><h1>Revisión semanal</h1><p>El sistema califica la semana y propone el siguiente paso hacia tu carrera principal.</p></div>
      <form className="weekly-review-controls" onSubmit={(event) => {
        event.preventDefault()
        createReview.mutate()
      }}>
        <label>Semana que inicia<input type="date" required max={previousMonday()} value={weekStart} onChange={(event) => setWeekStart(event.target.value)} /></label>
        {weeklyChoices.length > 0 && <label>Semanas guardadas<select value={selectedWeek?.weekStart ?? ''} onChange={(event) => event.target.value && setWeekStart(event.target.value)}><option value="">Elegir una semana</option>{weeklyChoices.map((evaluation) => <option key={evaluation.id} value={evaluation.weekStart}>{formatPeriod(evaluation.weekStart, evaluation.weekEnd)}</option>)}</select></label>}
        <button className="button primary" disabled={createReview.isPending}>{createReview.isPending ? 'Analizando…' : selectedWeek ? 'Actualizar análisis' : 'Evaluar semana'}</button>
      </form>
    </header>
    {createReview.isError && <p className="form-alert" role="alert">{readableApiError(createReview.error)}</p>}
    {!evaluationId && !createReview.isPending && <EmptyState title="Esta semana todavía no tiene evaluación">Evalúala cuando sus actividades y sensaciones estén registradas.</EmptyState>}
    {detail.isPending && <LoadingState label="Abriendo el resumen semanal" />}
    {detail.isError && <ErrorState message={readableApiError(detail.error)} retry={() => void detail.refetch()} />}
    {detail.data && <EvaluationDetail key={detail.data.evaluation.id} detail={detail.data} onChanged={async (updated) => {
      setEvaluationId(updated.evaluation.id)
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['evaluations'] }), queryClient.invalidateQueries({ queryKey: ['evaluation', updated.evaluation.id] }), queryClient.invalidateQueries({ queryKey: ['plans'] })])
    }} />}
  </div>
}

function EvaluationDetail({ detail, onChanged }: { detail: WeeklyEvaluationDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const coach = useQuery({
    queryKey: ['coach-review', detail.evaluation.id, detail.evaluation.cutoffAt],
    queryFn: async () => {
      const currentPlan = await PlansService.getCurrentTrainingPlan()
      const plan = detail.evaluation.planVersionId && currentPlan.version.id !== detail.evaluation.planVersionId
        ? await PlansService.getTrainingPlanVersion({ planId: currentPlan.id, versionId: detail.evaluation.planVersionId })
        : currentPlan
      const [races, activities] = await Promise.all([
        RacesService.getRaces(),
        ActivitiesService.getActivities({ page: 1, pageSize: 100, from: offsetDate(detail.evaluation.weekStart, -84), to: detail.evaluation.weekEnd, sort: 'startedAt', direction: 'asc' }),
      ])
      return { review: buildCoachReview({ detail, plan, races, activities: activities.items }), plan }
    },
  })
  const grouped = useMemo(() => Object.fromEntries(['P1', 'P2', 'P3', 'P4', 'P5'].map((code) => [code, detail.metrics.filter((metric) => metric.metricCode === code)])), [detail.metrics])
  const completed = detail.sessions.filter((session) => session.executionStatus && completedStatuses.has(session.executionStatus)).length
  const runningDistance = findMetric(detail.metrics, 'P2', 'actual_distance_m:all')
  const runningDuration = findMetric(detail.metrics, 'P2', 'actual_duration_seconds:all')
  const totalLoad = findMetric(detail.metrics, 'P4', 'total')
  const pain = findMetric(detail.metrics, 'P5', 'pain')
  const fatigue = findMetric(detail.metrics, 'P5', 'fatigue')
  const perceivedRecovery = findMetric(detail.metrics, 'P5', 'perceived_recovery')

  return <div id="weekly-review" className="weekly-review">
    {coach.isPending && <LoadingState label="Preparando la evaluación del coach" />}
    {coach.isError && <ErrorState message={`No se pudo construir la propuesta: ${readableApiError(coach.error)}`} retry={() => void coach.refetch()} />}
    {coach.data && <CoachReviewPanel review={coach.data.review} evaluation={detail} />}
    <section className="section-block weekly-summary" aria-labelledby="summary-title"><div className="section-heading"><div><span className="section-label">1 · Semana terminada</span><h2 id="summary-title">Qué se evaluó</h2></div><span className="date-chip">{formatPeriod(detail.evaluation.weekStart, detail.evaluation.weekEnd)}</span></div><div className="weekly-summary-grid">
      <SummaryCard label="Entrenamientos" value={`${completed} de ${detail.sessions.length}`} detail={completed === detail.sessions.length && detail.sessions.length > 0 ? 'Todos registrados' : `${detail.sessions.length - completed} sin completar`} />
      <SummaryCard label="Running total" value={coach.data ? formatRaceDistance(coach.data.review.week.actualDistanceM) : primaryMetricValue(runningDistance)} detail={coach.data ? `${coach.data.review.week.actualRunCount} carreras; ${coach.data.review.week.unplannedRunCount} fuera del plan` : runningDuration ? primaryMetricValue(runningDuration) : 'Duración sin registrar'} />
      <SummaryCard label="Carga" value={primaryMetricValue(totalLoad)} detail="sRPE: minutos × esfuerzo percibido" />
      <SummaryCard label="Recuperación" value={recoverySummary(pain, fatigue, perceivedRecovery)} detail="Dolor, fatiga y recuperación percibida" />
    </div></section>
    <section className="evaluation-sources section-block" aria-labelledby="sessions-title"><div className="section-heading"><div><span className="section-label">Evidencia de la semana</span><h2 id="sessions-title">Entrenamientos</h2></div><span className="date-chip">{detail.sessions.length + (coach.data?.review.week.unplannedRunCount ?? 0)} registros</span></div>
      {detail.sessions.length === 0 && !coach.data?.review.week.unplannedRunCount ? <p className="muted-copy">No hay entrenamientos registrados para esta semana.</p> : <div className="source-list">{detail.sessions.map((session) => {
        const dates = sessionDateLabels(session.scheduledDate, session.actualStartedAtLocal)
        return <article key={session.id}><div><strong>{dates.primary}</strong>{dates.planned && <span>{dates.planned}</span>}<span>{sessionTypeLabel(session.sessionType)}</span></div><span className={`source-status ${session.executionStatus ? '' : 'missing'}`}>{executionLabel(session.executionStatus)}</span>{session.plannedSessionId && detail.evaluation.planVersionId && <Link to={`/plan?version=${detail.evaluation.planVersionId}&session=${session.plannedSessionId}#completion`}>Abrir entrenamiento</Link>}{!session.plannedSessionId && session.activityId && <Link to={`/activities/${session.activityId}`}>Abrir actividad</Link>}</article>
      })}{coach.data?.review.week.unplannedActivities.map((activity) => <article className="unplanned" key={activity.id}><div><strong>Realizada: {fullActualDate(activity.startedAtLocal)}</strong><span>{activity.title ?? activity.activityType}</span><span>{activity.distanceM == null ? 'Sin distancia' : formatRaceDistance(activity.distanceM)}</span></div><span className="source-status extra">Fuera del plan</span><Link to={`/activities/${activity.id}`}>Abrir actividad</Link></article>)}</div>}
    </section>
    <details className="technical-evaluation section-block"><summary>Ver desglose técnico P1–P5</summary><p>Estos son los cálculos internos del resumen. Los entrenamientos que los originan aparecen una sola vez en la lista anterior.</p><div className="metric-sections">{Object.entries(grouped).map(([code, metrics]) => code === 'P2' && coach.data ? <CoachVolumeSection review={coach.data.review} key={code} /> : <MetricSection code={code} metrics={metrics} key={code} />)}</div></details>
    {detail.decision ? <DecisionRecord detail={detail} /> : coach.data && <DecisionForm detail={detail} coach={coach.data.review} plan={coach.data.plan} onChanged={onChanged} />}
  </div>
}

function CoachReviewPanel({ review, evaluation }: { review: CoachReview; evaluation: WeeklyEvaluationDetailResponse }) {
  return <>
    <section className={`coach-verdict ${review.grade}`} aria-labelledby="coach-verdict-title">
      <header><div><span className="section-label">Evaluación del coach</span><h2 id="coach-verdict-title">{review.gradeLabel}</h2></div><span className={`safety-chip ${evaluation.evaluation.trafficLight}`}>Seguridad: {trafficLabel(evaluation.evaluation.trafficLight)}</span></header>
      <p>{review.summary}</p>
      <div className="goal-chain">
        <article><span>Objetivo principal</span><strong>{review.primaryRace?.name ?? 'Sin carrera A definida'}</strong><small>{review.primaryRace ? `${formatRaceDistance(review.primaryRace.distanceM)} · ${formatFullDate(review.primaryRace.raceDate)} · ${review.daysToPrimaryRace} días${review.primaryRace.currentGoal?.goalTimeSeconds != null ? ` · meta ${formatClock(Number(review.primaryRace.currentGoal.goalTimeSeconds))}` : ''}` : 'Define una carrera con prioridad A'}</small></article>
        <i aria-hidden="true">←</i>
        <article><span>Carrera preparatoria</span><strong>{review.preparatoryRace?.name ?? 'Sin carrera previa'}</strong><small>{review.preparatoryRace ? `${formatRaceDistance(review.preparatoryRace.distanceM)} · ${formatFullDate(review.preparatoryRace.raceDate)} · ${review.daysToPreparatoryRace} días` : 'No hay una carrera B antes del objetivo'}</small></article>
      </div>
    </section>
    <section className="runner-profile section-block" aria-labelledby="runner-profile-title"><div className="section-heading"><div><span className="section-label">Perfil de mediano plazo</span><h2 id="runner-profile-title">Perfil actual del corredor</h2></div><span className="profile-confidence">Provisional · confianza {review.runnerProfile.confidence.toLowerCase()}</span></div><p>No cambia por una sola sesión; se recalcula con el historial y al terminar cada bloque.</p><dl><div><dt>Nivel</dt><dd>{review.runnerProfile.level}</dd></div><div><dt>Perfil dominante</dt><dd>{review.runnerProfile.dominantType}</dd></div><div><dt>Consistencia</dt><dd>{review.runnerProfile.consistency}</dd></div><div><dt>Especificidad</dt><dd>{review.runnerProfile.specificity}</dd></div><div><dt>Gestión de carga</dt><dd>{review.runnerProfile.loadManagement}</dd></div><div><dt>Fortaleza actual</dt><dd>{review.runnerProfile.currentStrength}</dd></div><div><dt>Limitante actual</dt><dd>{review.runnerProfile.currentLimiter}</dd></div></dl></section>
    <section className="coach-method section-block" aria-labelledby="coach-method-title"><div className="section-heading"><div><span className="section-label">Método {review.methodologyCode}</span><h2 id="coach-method-title">Cómo se tomó la decisión</h2></div></div><p>{review.methodology}</p><div className="coach-inputs">
      {review.reasons.map((reason) => <span key={reason}>{reason}</span>)}
      <span>Fase actual: <strong>{review.phase}</strong>. {review.phaseFocus}</span>
      <span>La seguridad no se promedia: una señal roja prevalece sobre cualquier buen resultado.</span>
    </div>{review.missingData.length > 0 && <div className="coach-missing"><strong>Falta para cerrar con confianza</strong>{review.missingData.map((item) => <span key={item}>{item}</span>)}</div>}</section>
    <section className="coach-proposal section-block" aria-labelledby="coach-proposal-title"><div className="section-heading"><div><span className="section-label">2 · Siguiente paso evolutivo</span><h2 id="coach-proposal-title">{review.proposalTitle}</h2></div><span className="date-chip">{formatPeriod(review.nextWeek.start, review.nextWeek.end)}</span></div><div className="immediate-instruction"><span>Indicación inmediata</span><strong>{review.immediateInstruction}</strong></div><div className="coach-week-summary"><span>{review.phase}</span><strong>{review.nextWeek.plannedDistanceM == null ? 'Volumen sin definir' : `${formatRaceDistance(review.nextWeek.plannedDistanceM)} planificados`}</strong><small>{review.phaseFocus}</small></div>
      {review.nextWeek.sessions.length === 0 ? <p className="muted-copy">El plan no tiene sesiones para la siguiente semana.</p> : <div className="coach-session-list">{review.nextWeek.sessions.map((proposal) => <article className={proposal.action} key={proposal.session.id}><div><time>{fullDate(proposal.session.scheduledDate)}</time><strong>{sessionTypeLabel(proposal.session.sessionType)}</strong><small>{sessionPrescription(proposal.session)}</small></div><span>{proposal.actionLabel}</span><p>{proposal.reason}</p>{proposal.proposedObjective && <details><summary>Ver cambio propuesto</summary><p>{proposal.proposedObjective}</p></details>}</article>)}</div>}
    </section>
  </>
}

function SummaryCard({ label, value, detail }: { label: string; value: string; detail: string }) { return <article className="weekly-summary-card"><span>{label}</span><strong>{value}</strong><small>{detail}</small></article> }

function MetricSection({ code, metrics }: { code: string; metrics: WeeklyMetricValueResponse[] }) {
  return <section className="weekly-metric" aria-labelledby={`metric-${code}`}><header><span>{code}</span><div><h2 id={`metric-${code}`}>{metricTitles[code]}</h2><p>{metricDescription(code)}</p></div></header>{metrics.length === 0 ? <p className="muted-copy">Sin datos calculados.</p> : <div className="weekly-metric-grid">{metrics.map((metric) => <article className={metric.status === 'missing' ? 'missing' : ''} key={metric.id}><span>{dimensionLabel(metric.dimension)}</span><strong>{technicalMetricValue(metric)}</strong><small>{metric.status === 'missing' || metric.status === 'not_applicable' ? 'Sin dato registrado' : 'Calculado'}</small></article>)}</div>}</section>
}

function CoachVolumeSection({ review }: { review: CoachReview }) {
  const values: Array<[string, string | null]> = [
    ['Distancia planificada', review.week.plannedDistanceM == null ? null : formatRaceDistance(review.week.plannedDistanceM)],
    ['Distancia realizada · todas las carreras', formatRaceDistance(review.week.actualDistanceM)],
    ['Distancia realizada · cinta', review.week.treadmillDistanceM == null ? null : formatRaceDistance(review.week.treadmillDistanceM)],
    ['Distancia realizada · exterior', review.week.outdoorDistanceM == null ? null : formatRaceDistance(review.week.outdoorDistanceM)],
    ['Duración realizada', review.week.actualDurationSeconds == null ? null : `${round(review.week.actualDurationSeconds / 60)} min`],
  ]
  return <section className="weekly-metric" aria-labelledby="metric-P2"><header><span>P2</span><div><h2 id="metric-P2">Volumen de running</h2><p>Incluye actividades enlazadas y carreras realizadas fuera del plan.</p></div></header><div className="weekly-metric-grid">{values.map(([label, value]) => <article className={value == null ? 'missing' : ''} key={label}><span>{label}</span><strong>{value ?? 'Sin dato'}</strong><small>{value == null ? 'Sin dato registrado' : 'Calculado con todas las actividades'}</small></article>)}</div></section>
}

function DecisionForm({ detail, coach, plan, onChanged }: { detail: WeeklyEvaluationDetailResponse; coach: CoachReview; plan: TrainingPlanDetailResponse; onChanged: (detail: WeeklyEvaluationDetailResponse) => Promise<void> }) {
  const [decision, setDecision] = useState(coach.recommendedDecision)
  const [notes, setNotes] = useState('')
  const adjustable = decision === 'adapt' || decision === 'reduce'
  const recommendedChanges = coach.nextWeek.sessions.filter((proposal) => proposal.proposedObjective)
  const [selectedChangeIds, setSelectedChangeIds] = useState(() => new Set(recommendedChanges.map((proposal) => proposal.session.id)))
  const futureSessions = coach.nextWeek.sessions.map((proposal) => proposal.session)
  const [manualSessionId, setManualSessionId] = useState(futureSessions[0]?.id ?? '')
  const manualSession = futureSessions.find((session) => session.id === manualSessionId) ?? futureSessions[0]
  const [manualObjective, setManualObjective] = useState(manualSession?.objective ?? '')
  const selectedRecommendedChanges = recommendedChanges.filter((proposal) => selectedChangeIds.has(proposal.session.id))
  const useManualChange = adjustable && recommendedChanges.length === 0
  const sessionChanges = selectedRecommendedChanges.length > 0
    ? selectedRecommendedChanges.map((proposal) => ({ sourcePlannedSessionId: proposal.session.id, scheduledDate: null, objective: proposal.proposedObjective }))
    : useManualChange && manualSession ? [{ sourcePlannedSessionId: manualSession.id, scheduledDate: null, objective: manualObjective }] : []
  const confirm = useMutation({
    mutationFn: () => EvaluationsService.confirmWeeklyDecision({ evaluationId: detail.evaluation.id, requestBody: {
      decision,
      observation: notes.trim() || `Confirmo la propuesta del coach: ${coach.proposalTitle}.`,
      evidence: coach.reasons.join(' '),
      historicalComparison: coach.week.plannedDistanceM == null ? 'No existe una distancia planificada completa para comparar.' : `${formatRaceDistance(coach.week.actualDistanceM)} realizados frente a ${formatRaceDistance(coach.week.plannedDistanceM)} planificados.`,
      interpretation: `${coach.gradeLabel}. ${coach.summary}`,
      recommendation: `${coach.immediateInstruction} ${recommendationForDecision(decision)}`,
      planAdjustment: adjustable ? { sourcePlanVersionId: plan.version.id, rationale: coach.summary, reviewCriterion: coach.missingData[0] ?? 'Revisar recuperación, RPE y respuesta en el siguiente cierre semanal.', sessionChanges } : null,
    } }),
    onSuccess: onChanged,
  })
  const cannotAdjust = adjustable && (!detail.evaluation.planVersionId || sessionChanges.length === 0)
  const changeManualSession = (id: string) => { setManualSessionId(id); setManualObjective(futureSessions.find((session) => session.id === id)?.objective ?? '') }
  return <section className="decision-panel" aria-labelledby="decision-title"><div><span className="section-label">3 · Confirmación humana</span><h2 id="decision-title">Confirmar el siguiente paso</h2><p>El coach hace la propuesta; tú conservas la decisión final. Un cambio crea un borrador y nunca modifica automáticamente el plan publicado.</p></div><form onSubmit={(event) => { event.preventDefault(); confirm.mutate() }}><label>Decisión<select value={decision} onChange={(event) => setDecision(event.target.value)}><option value="execute_plan">Seguir la progresión propuesta</option><option value="adapt">Adaptar la siguiente semana</option><option value="reduce">Reducir y consolidar</option><option value="stop_and_assess">Detener y valorar</option></select></label><p className="coach-recommendation"><span>Recomendación del coach</span><strong>{decisionLabel(coach.recommendedDecision)}</strong></p><label>Nota personal <span className="optional-label">(opcional)</span><textarea rows={2} maxLength={4000} value={notes} onChange={(event) => setNotes(event.target.value)} /></label>{adjustable && <fieldset className="adjustment-fields"><legend>Cambios para el borrador</legend>{recommendedChanges.length > 0 ? <div className="recommended-change-list">{recommendedChanges.map((proposal) => <label key={proposal.session.id}><input type="checkbox" checked={selectedChangeIds.has(proposal.session.id)} onChange={(event) => setSelectedChangeIds((current) => { const next = new Set(current); if (event.target.checked) next.add(proposal.session.id); else next.delete(proposal.session.id); return next })} /><span><strong>{fullDate(proposal.session.scheduledDate)} · {sessionTypeLabel(proposal.session.sessionType)}</strong><small>{proposal.proposedObjective}</small></span></label>)}</div> : futureSessions.length > 0 ? <><label>Sesión a cambiar<select required value={manualSessionId} onChange={(event) => changeManualSession(event.target.value)}>{futureSessions.map((session) => <option key={session.id} value={session.id}>{session.scheduledDate} · {sessionTypeLabel(session.sessionType)}</option>)}</select></label><label>Nuevo objetivo<textarea rows={3} required value={manualObjective} onChange={(event) => setManualObjective(event.target.value)} /></label></> : <p className="form-alert" role="alert">El plan no tiene sesiones futuras que puedan modificarse.</p>}</fieldset>}{confirm.isError && <p className="form-alert" role="alert">{readableApiError(confirm.error)}</p>}<button className="button primary" disabled={confirm.isPending || cannotAdjust}>{confirm.isPending ? 'Guardando…' : adjustable ? 'Aceptar y crear borrador' : 'Confirmar siguiente paso'}</button></form></section>
}

function DecisionRecord({ detail }: { detail: WeeklyEvaluationDetailResponse }) {
  const decision = detail.decision!
  return <section className="decision-record" aria-labelledby="decision-record-title"><header><div><span className="section-label">Decisión guardada · {formatDateTime(decision.confirmedAt)}</span><h2 id="decision-record-title">{decisionLabel(decision.decision)}</h2></div></header><p className="decision-note">{decision.observation}</p><details className="decision-details"><summary>Ver registro completo</summary><dl><div><dt>Resumen usado</dt><dd>{decision.evidence}</dd></div><div><dt>Comparación</dt><dd>{decision.historicalComparison}</dd></div><div><dt>Interpretación</dt><dd>{decision.interpretation}</dd></div><div><dt>Recomendación</dt><dd>{decision.recommendation}</dd></div></dl></details>{decision.adjustments.map((adjustment) => <article className="adjustment-record" key={adjustment.id}><span>Nueva versión sin publicar</span><h3>{adjustmentTypeLabel(adjustment.adjustmentType)}</h3><p>{adjustment.rationale}</p><small>Se revisará en el siguiente cierre semanal.</small><Link className="button secondary" to={`/plan?version=${adjustment.targetPlanVersionId}`}>Revisar borrador</Link></article>)}</section>
}

export function selectWeeklyEvaluations(evaluations: WeeklyEvaluationSummaryResponse[]) {
  const byWeek = new Map<string, WeeklyEvaluationSummaryResponse[]>()
  for (const evaluation of evaluations) { const group = byWeek.get(evaluation.weekStart) ?? []; group.push(evaluation); byWeek.set(evaluation.weekStart, group) }
  return [...byWeek.values()].map((group) => [...group].sort((left, right) => right.createdAt.localeCompare(left.createdAt))[0])
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

export function sessionDateLabels(scheduledDate: string | null, actualStartedAtLocal: string | null) {
  if (actualStartedAtLocal) return {
    primary: `Realizada: ${fullActualDate(actualStartedAtLocal)}`,
    planned: scheduledDate ? `Planificada: ${fullDate(scheduledDate)}` : null,
  }
  return {
    primary: scheduledDate ? `Planificada: ${fullDate(scheduledDate)}` : 'Actividad sin sesión planificada',
    planned: null,
  }
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
function offsetDate(value: string, days: number) { const date = parseDate(value); date.setDate(date.getDate() + days); return localDate(date) }
function parseDate(value: string) { return new Date(`${value}T00:00:00`) }
function formatPeriod(start: string, end: string) { return `${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(start))} – ${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(end))}` }
function fullDate(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', month: 'short' }).format(parseDate(value)) }
function fullActualDate(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(value)) }
function formatDateTime(value: string) { return new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) }
function round(value: number) { return Number(value.toFixed(2)).toLocaleString('es') }
function trafficTitle(value: string) { return ({ green: 'Semana en orden', yellow: 'Revisar antes de continuar', red: 'Pausa y revisión' } as Record<string, string>)[value] ?? value }
function trafficLabel(value: string) { return ({ green: 'sin alertas', yellow: 'precaución', red: 'detener' } as Record<string, string>)[value] ?? value }
function decisionLabel(value: string) { return ({ execute_plan: 'Mantener el plan', adapt: 'Adaptar una sesión', reduce: 'Reducir carga', stop_and_assess: 'Pausar y revisar' } as Record<string, string>)[value] ?? value }
function executionLabel(value: string | null) { return value ? ({ completed_as_planned: 'Completado según plan', completed_modified: 'Completado con cambios', valid_substitution: 'Sustitución válida', not_completed: 'No realizado', optional_not_completed: 'Opcional no realizado' } as Record<string, string>)[value] ?? value : 'Sin registrar' }
function sessionTypeLabel(value: string | null) { return value ? ({ strength_mobility_plyometrics: 'Fuerza, movilidad y pliometría', easy_run: 'Carrera fácil', long_run: 'Tirada larga', quality: 'Calidad', cross_training: 'Entrenamiento cruzado' } as Record<string, string>)[value] ?? value.replaceAll('_', ' ') : 'Sin tipo' }
function adjustmentTypeLabel(value: string) { return ({ objective: 'Objetivo ajustado', reschedule: 'Sesión reprogramada', reschedule_and_objective: 'Fecha y objetivo ajustados' } as Record<string, string>)[value] ?? value }
function formatRaceDistance(value: number | string) { return `${Number((Number(value) / 1000).toFixed(2)).toLocaleString('es')} km` }
function formatClock(totalSeconds: number) { const hours = Math.floor(totalSeconds / 3600); const minutes = Math.floor(totalSeconds % 3600 / 60); const seconds = Math.round(totalSeconds % 60); return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}` }
function formatFullDate(value: string) { return new Intl.DateTimeFormat('es', { day: 'numeric', month: 'long', year: 'numeric' }).format(parseDate(value)) }
function sessionPrescription(session: PlannedSessionResponse) {
  const parts = [session.distanceM != null ? formatRaceDistance(session.distanceM) : null, session.durationSeconds != null ? `${round(Number(session.durationSeconds) / 60)} min` : null, session.targetRpeMin != null ? `RPE ${session.targetRpeMin}${session.targetRpeMax != null && session.targetRpeMax !== session.targetRpeMin ? `–${session.targetRpeMax}` : ''}` : null]
  return parts.filter(Boolean).join(' · ') || session.objective
}
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
