import type {
  ActivitySummaryResponse,
  PlannedSessionResponse,
  TargetRaceResponse,
  TrainingPlanDetailResponse,
  WeeklyEvaluationDetailResponse,
} from '../api/generated'

export type CoachGrade = 'progress' | 'consolidate' | 'adjust' | 'stop' | 'incomplete'
export type CoachSessionAction = 'keep' | 'adjust' | 'omit' | 'hold'

export type CoachSessionProposal = {
  session: PlannedSessionResponse
  action: CoachSessionAction
  actionLabel: string
  reason: string
  proposedObjective: string | null
}

export type CoachReview = {
  methodology: string
  methodologyCode: string
  grade: CoachGrade
  gradeLabel: string
  summary: string
  recommendedDecision: string
  proposalTitle: string
  immediateInstruction: string
  reasons: string[]
  missingData: string[]
  primaryRace: TargetRaceResponse | null
  preparatoryRace: TargetRaceResponse | null
  daysToPrimaryRace: number | null
  daysToPreparatoryRace: number | null
  phase: string
  phaseFocus: string
  runnerProfile: {
    level: string
    dominantType: string
    consistency: string
    specificity: string
    loadManagement: string
    currentStrength: string
    currentLimiter: string
    confidence: string
  }
  week: {
    plannedDistanceM: number | null
    actualDistanceM: number
    actualDurationSeconds: number | null
    treadmillDistanceM: number | null
    outdoorDistanceM: number | null
    recentAverageDistanceM: number | null
    historyWeekCount: number
    plannedRunCount: number
    actualRunCount: number
    unplannedRunCount: number
    unplannedActivities: ActivitySummaryResponse[]
  }
  nextWeek: {
    start: string
    end: string
    plannedDistanceM: number | null
    sessions: CoachSessionProposal[]
  }
}

type CoachReviewInput = {
  detail: WeeklyEvaluationDetailResponse
  plan: TrainingPlanDetailResponse
  races: TargetRaceResponse[]
  activities: ActivitySummaryResponse[]
}

const completedStatuses = new Set(['completed_as_planned', 'completed_modified', 'valid_substitution'])

export function buildCoachReview({ detail, plan, races, activities }: CoachReviewInput): CoachReview {
  const nextWeekStart = addDays(detail.evaluation.weekEnd, 1)
  const nextWeekEnd = addDays(nextWeekStart, 6)
  const weekSessions = plan.sessions.filter((session) => inRange(session.scheduledDate, detail.evaluation.weekStart, detail.evaluation.weekEnd))
  const nextSessions = plan.sessions.filter((session) => inRange(session.scheduledDate, nextWeekStart, nextWeekEnd))
  const plannedRuns = weekSessions.filter(isRunningSession)
  const nextRuns = nextSessions.filter(isRunningSession)
  const allRunningActivities = activities.filter(isRunningActivity)
  const runningActivities = allRunningActivities.filter((activity) => inRange(activity.startedAtLocal.slice(0, 10), detail.evaluation.weekStart, detail.evaluation.weekEnd))
  const historicalWeeks = weeklyRunningTotals(allRunningActivities.filter((activity) => activity.startedAtLocal.slice(0, 10) < detail.evaluation.weekStart))
  const recentAverageDistanceM = historicalWeeks.length > 0 ? historicalWeeks.reduce((total, week) => total + week, 0) / historicalWeeks.length : null
  const evidencedActivityIds = new Set(detail.metrics.flatMap((metric) => metric.evidence.filter((item) => item.sourceType === 'activity').map((item) => item.sourceId)))
  const unplannedRuns = runningActivities.filter((activity) => !evidencedActivityIds.has(activity.id))
  const actualDistanceM = sumKnown(runningActivities.map((activity) => activity.distanceM)) ?? 0
  const actualDurationSeconds = sumKnown(runningActivities.map((activity) => activity.durationSeconds))
  const treadmillDistanceM = sumKnown(runningActivities.filter((activity) => activity.modality === 'treadmill').map((activity) => activity.distanceM))
  const outdoorDistanceM = sumKnown(runningActivities.filter((activity) => activity.modality === 'outdoor').map((activity) => activity.distanceM))
  const plannedDistanceM = sumKnown(plannedRuns.map((session) => session.distanceM))
  const nextPlannedDistanceM = sumKnown(nextRuns.map((session) => session.distanceM))
  const materialVolumeExcess = plannedDistanceM != null
    && actualDistanceM - plannedDistanceM >= Math.max(1_000, plannedDistanceM * .1)
  const requiredPlanned = detail.sessions.filter((session) => session.classification === 'planned')
  const completedRequired = requiredPlanned.filter((session) => session.executionStatus && completedStatuses.has(session.executionStatus)).length
  const missingOutcome = requiredPlanned.some((session) => !session.executionStatus)
  const missingRecovery = detail.evaluation.rationale.toLowerCase().includes('falta respuesta')
    || detail.metrics.some((metric) => metric.metricCode === 'P5' && metric.dimension === 'responses_24_to_48_hours' && metric.status === 'missing')
  const hasRedSignal = detail.evaluation.trafficLight === 'red'
  const hasYellowSignal = detail.evaluation.trafficLight === 'yellow'

  const primaryRace = [...races]
    .filter((race) => race.priority === 'A' && isFutureRace(race, nextWeekStart))
    .sort(byRaceDate)[0] ?? null
  const preparatoryRace = primaryRace ? [...races]
    .filter((race) => race.priority !== 'A' && isFutureRace(race, nextWeekStart) && race.raceDate < primaryRace.raceDate)
    .sort(byRaceDate)[0] ?? null : null
  const daysToPrimaryRace = primaryRace ? daysBetween(nextWeekStart, primaryRace.raceDate) : null
  const daysToPreparatoryRace = preparatoryRace ? daysBetween(nextWeekStart, preparatoryRace.raceDate) : null
  const { phase, phaseFocus } = trainingPhase(primaryRace, preparatoryRace, nextWeekStart)
  const runnerProfile = classifyRunner(allRunningActivities, materialVolumeExcess, unplannedRuns.length)

  let grade: CoachGrade
  if (hasRedSignal) grade = 'stop'
  else if (unplannedRuns.length > 0 || materialVolumeExcess) grade = 'adjust'
  else if (missingOutcome || missingRecovery) grade = 'incomplete'
  else if (hasYellowSignal || (requiredPlanned.length > 0 && completedRequired < requiredPlanned.length)) grade = 'consolidate'
  else grade = 'progress'

  const reasons: string[] = []
  if (plannedDistanceM != null) reasons.push(`Volumen de running: ${formatKm(actualDistanceM)} realizados frente a ${formatKm(plannedDistanceM)} planificados.`)
  else reasons.push(`Volumen de running realizado: ${formatKm(actualDistanceM)}; el plan no tiene una distancia semanal completa para comparar.`)
  if (recentAverageDistanceM != null) reasons.push(`Promedio de las ${historicalWeeks.length} semanas anteriores disponibles: ${formatKm(recentAverageDistanceM)}.`)
  if (unplannedRuns.length > 0) reasons.push(`${unplannedRuns.length} ${unplannedRuns.length === 1 ? 'carrera no estaba' : 'carreras no estaban'} en el plan y también cuenta para decidir la carga siguiente.`)
  if (requiredPlanned.length > 0) reasons.push(`${completedRequired} de ${requiredPlanned.length} sesiones planificadas tienen una ejecución registrada.`)
  if (primaryRace) reasons.push(`Carrera A: ${primaryRace.name}, ${formatKm(Number(primaryRace.distanceM))}${primaryRace.currentGoal?.goalTimeSeconds != null ? ` con meta vigente de ${formatDuration(Number(primaryRace.currentGoal.goalTimeSeconds))}` : ''}.`)
  if (preparatoryRace && primaryRace) reasons.push(`${preparatoryRace.name} funciona como preparación y benchmark; el objetivo del ciclo sigue siendo ${primaryRace.name}.`)
  if (hasRedSignal) reasons.push('Existe una señal de seguridad que prevalece sobre cumplimiento, ritmo o volumen.')

  const missingData: string[] = []
  if (missingOutcome) missingData.push('Confirmar el resultado de las sesiones obligatorias pendientes.')
  if (missingRecovery) missingData.push('Registrar la respuesta de 24–48 h de las sesiones clave.')
  if (detail.metrics.some((metric) => metric.metricCode === 'P4' && metric.dimension === 'total' && metric.status === 'missing')) missingData.push('Completar RPE para calcular la carga interna semanal.')

  const gradeLabel = gradeLabels[grade]
  const recommendedDecision = grade === 'progress' ? 'execute_plan'
    : grade === 'stop' ? 'stop_and_assess'
      : grade === 'consolidate' ? 'reduce' : 'adapt'
  const proposalTitle = proposalTitles[grade]
  const immediateInstruction = immediateInstructionFor(grade, missingRecovery, materialVolumeExcess, unplannedRuns.length)
  const proposals = nextSessions.map((session) => proposeSession(session, grade, missingRecovery || materialVolumeExcess || unplannedRuns.length > 0))
  const summary = summaryFor(grade, primaryRace, preparatoryRace)

  return {
    methodology: 'Periodización por bloques hacia la carrera A, con intensidad mayoritariamente fácil y autorregulación mediante RPE, síntomas y respuesta de 24–48 h.',
    methodologyCode: 'goal-block-rpe-v1',
    grade,
    gradeLabel,
    summary,
    recommendedDecision,
    proposalTitle,
    immediateInstruction,
    reasons,
    missingData,
    primaryRace,
    preparatoryRace,
    daysToPrimaryRace,
    daysToPreparatoryRace,
    phase,
    phaseFocus,
    runnerProfile,
    week: {
      plannedDistanceM,
      actualDistanceM,
      actualDurationSeconds,
      treadmillDistanceM,
      outdoorDistanceM,
      recentAverageDistanceM,
      historyWeekCount: historicalWeeks.length,
      plannedRunCount: plannedRuns.length,
      actualRunCount: runningActivities.length,
      unplannedRunCount: unplannedRuns.length,
      unplannedActivities: unplannedRuns,
    },
    nextWeek: {
      start: nextWeekStart,
      end: nextWeekEnd,
      plannedDistanceM: nextPlannedDistanceM,
      sessions: proposals,
    },
  }
}

const gradeLabels: Record<CoachGrade, string> = {
  progress: 'A · Lista para progresar',
  consolidate: 'B · Conviene consolidar',
  adjust: 'C · Ajustar antes de progresar',
  stop: 'D · Detener y valorar',
  incomplete: 'ND · Faltan datos para progresar',
}

const proposalTitles: Record<CoachGrade, string> = {
  progress: 'Continuar con el siguiente estímulo del bloque',
  consolidate: 'Consolidar la carga antes de aumentarla',
  adjust: 'Absorber la carga y después retomar la progresión',
  stop: 'Suspender el estímulo afectado',
  incomplete: 'Mantener la propuesta provisional hasta completar la recuperación',
}

function summaryFor(grade: CoachGrade, primary: TargetRaceResponse | null, preparatory: TargetRaceResponse | null) {
  const objective = primary ? ` hacia ${primary.name}` : ''
  if (grade === 'progress') return `La respuesta registrada permite avanzar un paso dentro del bloque${objective}, sin añadir trabajo fuera de la prescripción.`
  if (grade === 'consolidate') return `La semana aporta entrenamiento, pero conviene repetir o estabilizar el estímulo antes de progresar${objective}.`
  if (grade === 'adjust') return `La carga real se apartó de la prevista. El siguiente paso debe favorecer recuperación y continuidad${objective}, no compensar ni sumar más trabajo.`
  if (grade === 'stop') return 'La seguridad manda: no corresponde prescribir otro estímulo intenso hasta valorar la señal registrada.'
  const prep = preparatory ? `, incluida la preparación para ${preparatory.name}` : ''
  return `La semana todavía no tiene información suficiente para liberar la progresión${prep}.`
}

function immediateInstructionFor(grade: CoachGrade, missingRecovery: boolean, volumeExcess: boolean, unplannedRuns: number) {
  if (grade === 'stop') return 'No realizar calidad, pliometría ni fuerza intensa. Valorar la señal registrada antes de continuar.'
  if (grade === 'adjust') {
    if (missingRecovery) return 'No iniciar automáticamente la sesión de calidad. Preferir descanso o 20–30 minutos muy fáciles a RPE 2–3 hasta confirmar dolor, fatiga y recuperación.'
    if (volumeExcess || unplannedRuns > 0) return 'No añadir kilómetros para compensar. Mantener la siguiente salida fácil y retirar primero el trabajo opcional o explosivo.'
  }
  if (grade === 'incomplete') return 'Mantener solo trabajo fácil o descanso hasta completar los datos que faltan; después actualizar el análisis.'
  if (grade === 'consolidate') return 'Conservar los estímulos clave, reducir primero lo opcional y no aumentar simultáneamente volumen e intensidad.'
  return 'Ejecutar la semana propuesta tal como está, sin añadir carga extra.'
}

function proposeSession(session: PlannedSessionResponse, grade: CoachGrade, protectRecovery: boolean): CoachSessionProposal {
  if (grade === 'stop' && (isRunningSession(session) || session.sessionType === 'strength_mobility_plyometrics')) return {
    session, action: 'hold', actionLabel: 'En pausa', reason: 'La señal de seguridad debe valorarse antes de continuar.', proposedObjective: 'Sesión en pausa hasta valorar la señal de seguridad registrada.',
  }
  if ((grade === 'adjust' || grade === 'incomplete') && protectRecovery && session.sessionType === 'quality') return {
    session,
    action: 'adjust',
    actionLabel: 'Adaptar',
    reason: 'La calidad no se libera hasta confirmar que la carga anterior fue recuperada.',
    proposedObjective: 'Sustituir provisionalmente la calidad por descanso o 20–30 minutos de carrera muy fácil a RPE 2–3. Revaluar tras registrar dolor, fatiga y recuperación de 24–48 h.',
  }
  if ((grade === 'adjust' || grade === 'consolidate' || grade === 'incomplete') && session.obligation === 'optional') return {
    session,
    action: 'omit',
    actionLabel: 'Omitir primero',
    reason: 'El trabajo opcional no debe competir con la recuperación ni con los estímulos clave.',
    proposedObjective: 'Omitir esta sesión opcional durante la semana de consolidación. No recuperarla en otra fecha.',
  }
  if ((grade === 'adjust' || grade === 'consolidate') && session.sessionType === 'long_run') return {
    session,
    action: 'keep',
    actionLabel: 'Mantener sin progresar',
    reason: 'La tirada larga se conserva solo si la recuperación es normal; no se añade un final sostenido.',
    proposedObjective: null,
  }
  return { session, action: 'keep', actionLabel: 'Mantener', reason: 'Conserva el propósito del bloque actual.', proposedObjective: null }
}

function trainingPhase(primary: TargetRaceResponse | null, preparatory: TargetRaceResponse | null, weekStart: string) {
  if (primary && inRange(primary.raceDate, weekStart, addDays(weekStart, 6))) return { phase: 'Carrera principal', phaseFocus: `Llegar recuperado y ejecutar ${primary.name}.` }
  if (preparatory && inRange(preparatory.raceDate, weekStart, addDays(weekStart, 6))) return { phase: 'Carrera preparatoria', phaseFocus: `Usar ${preparatory.name} como estímulo específico y benchmark sin comprometer la carrera A.` }
  const daysToPrep = preparatory ? daysBetween(weekStart, preparatory.raceDate) : null
  if (daysToPrep != null && daysToPrep <= 21) return { phase: 'Específica para carrera preparatoria', phaseFocus: `Afinar resistencia y velocidad controlada para ${preparatory!.name}, manteniendo como prioridad la carrera A.` }
  const daysToPrimary = primary ? daysBetween(weekStart, primary.raceDate) : null
  if (daysToPrimary != null && daysToPrimary <= 14) return { phase: 'Puesta a punto', phaseFocus: `Reducir fatiga sin perder los estímulos específicos para ${primary!.name}.` }
  if (daysToPrimary != null && daysToPrimary <= 56) return { phase: 'Específica de medio maratón', phaseFocus: 'Desarrollar tirada larga recuperable y bloques sostenidos específicos, con la mayor parte del volumen fácil.' }
  return { phase: 'Construcción', phaseFocus: 'Construir continuidad, fuerza y resistencia antes del bloque más específico.' }
}

function isRunningSession(session: PlannedSessionResponse) {
  return ['quality', 'easy_run', 'long_run'].includes(session.sessionType)
    || session.modality === 'running'
    || session.sessionType.includes('run')
}

function isRunningActivity(activity: ActivitySummaryResponse) {
  return activity.activityCategory === 'running'
    || activity.activityType === 'running'
    || activity.activityType.toLowerCase().includes('run')
    || activity.activityType.toLowerCase().includes('treadmill')
}

function isFutureRace(race: TargetRaceResponse, reference: string) {
  return race.raceDate >= reference && !['cancelled', 'completed'].includes(race.status)
}

function sumKnown(values: Array<number | string | null>) {
  const known = values.filter((value): value is number | string => value != null).map(Number)
  return known.length > 0 ? known.reduce((total, value) => total + value, 0) : null
}

function weeklyRunningTotals(activities: ActivitySummaryResponse[]) {
  const weeks = new Map<string, number>()
  for (const activity of activities) {
    const date = dateAtNoon(activity.startedAtLocal.slice(0, 10))
    const day = date.getDay() || 7
    date.setDate(date.getDate() - day + 1)
    const key = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
    weeks.set(key, (weeks.get(key) ?? 0) + Number(activity.distanceM ?? 0))
  }
  return [...weeks.entries()].sort(([left], [right]) => left.localeCompare(right)).slice(-4).map(([, distance]) => distance)
}

function classifyRunner(activities: ActivitySummaryResponse[], materialVolumeExcess: boolean, unplannedRunCount: number) {
  const totals = weeklyRunningTotalsAll(activities)
  const activeWeeks = totals.length
  const distances = activities.map((activity) => Number(activity.distanceM ?? 0))
  const maxDistance = distances.length > 0 ? Math.max(...distances) : 0
  const longRuns = distances.filter((distance) => distance >= 12_000).length
  const totalDistance = distances.reduce((total, distance) => total + distance, 0)
  const treadmillDistance = activities.filter((activity) => activity.modality === 'treadmill').reduce((total, activity) => total + Number(activity.distanceM ?? 0), 0)
  const outdoorDistance = activities.filter((activity) => activity.modality === 'outdoor').reduce((total, activity) => total + Number(activity.distanceM ?? 0), 0)
  const treadmillShare = totalDistance > 0 ? treadmillDistance / totalDistance : 0
  const outdoorShare = totalDistance > 0 ? outdoorDistance / totalDistance : 0
  const level = activeWeeks >= 10 && maxDistance >= 15_000 ? 'Intermedio recreativo'
    : activeWeeks >= 8 ? 'Recreativo consistente' : 'Base en desarrollo'
  const dominantType = longRuns >= 2 || maxDistance >= 18_000 ? 'Orientado a resistencia' : 'Equilibrado en desarrollo'
  const specificity = treadmillShare >= .6 ? 'Mixto, con predominio de cinta'
    : outdoorShare >= .6 ? 'Predominio exterior' : 'Mixto cinta/exterior'
  const loadManagement = materialVolumeExcess || unplannedRunCount > 0
    ? 'En observación: esta semana excedió la prescripción'
    : 'Estable respecto al plan disponible'
  const currentStrength = longRuns >= 2 ? 'Continuidad y resistencia en tiradas largas' : 'Continuidad de entrenamiento'
  const currentLimiter = treadmillShare > .5 && (materialVolumeExcess || unplannedRunCount > 0)
    ? 'Especificidad exterior y control de carga'
    : treadmillShare > .5 ? 'Mayor especificidad exterior' : materialVolumeExcess || unplannedRunCount > 0 ? 'Control de carga' : 'Consolidar el siguiente bloque específico'
  return {
    level,
    dominantType,
    consistency: `${Math.min(activeWeeks, 12)} de 12 semanas con running disponible`,
    specificity,
    loadManagement,
    currentStrength,
    currentLimiter,
    confidence: activeWeeks >= 8 ? 'Media-alta' : 'Media',
  }
}

function weeklyRunningTotalsAll(activities: ActivitySummaryResponse[]) {
  const weeks = new Set<string>()
  for (const activity of activities) {
    const date = dateAtNoon(activity.startedAtLocal.slice(0, 10))
    const day = date.getDay() || 7
    date.setDate(date.getDate() - day + 1)
    weeks.add(`${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`)
  }
  return [...weeks]
}

function byRaceDate(left: TargetRaceResponse, right: TargetRaceResponse) { return left.raceDate.localeCompare(right.raceDate) }
function inRange(value: string, start: string, end: string) { return value >= start && value <= end }
function dateAtNoon(value: string) { return new Date(`${value}T12:00:00`) }
function daysBetween(start: string, end: string) { return Math.round((dateAtNoon(end).getTime() - dateAtNoon(start).getTime()) / 86_400_000) }
function addDays(value: string, days: number) { const date = dateAtNoon(value); date.setDate(date.getDate() + days); return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}` }
function formatKm(value: number) { return `${Number((value / 1000).toFixed(2)).toLocaleString('es')} km` }
function formatDuration(totalSeconds: number) { const hours = Math.floor(totalSeconds / 3600); const minutes = Math.floor(totalSeconds % 3600 / 60); const seconds = Math.round(totalSeconds % 60); return `${hours}:${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}` }
