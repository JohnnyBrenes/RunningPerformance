import type { ActivitySessionSignalsResponse } from '../api/generated'
import { toNumber } from './dashboard'
import type { PlannedComparison } from './plannedComparison'
import type { RecentComparisonView } from './recentComparison'

export type SignalLevel = 'red' | 'yellow' | 'green'

export type SessionSummary = {
  level: SignalLevel | null
  headline: string
  points: string[]
}

/**
 * Turns the session into a few short sentences for someone without training
 * background.
 *
 * Safety leads. `coach-method-v1` is explicit that an adverse safety signal is
 * never offset by good compliance, distance or pace, so when one is present the
 * summary opens with it and says outright that performance does not cancel it.
 */
export function buildSessionSummary(
  signals: ActivitySessionSignalsResponse | null,
  planned: PlannedComparison | null,
  recent: RecentComparisonView | null,
): SessionSummary | null {
  const level = signalLevel(signals)
  const safety = signals == null ? [] : safetyPoints(signals)
  const performance = performancePoints(planned, recent)

  if (level === 'red') {
    return {
      level,
      headline: 'Esta sesión dejó una señal de seguridad: conviene revisarla antes de seguir entrenando.',
      points: [
        ...safety,
        'Una señal así no se compensa con haber cumplido el plan ni con un buen ritmo.',
        ...performance,
      ],
    }
  }

  if (level === 'yellow') {
    return {
      level,
      headline: 'Esta sesión dejó una señal que conviene mirar antes de subir la carga.',
      points: [...safety, ...performance],
    }
  }

  if (performance.length === 0 && safety.length === 0) return null

  return {
    level,
    headline: performance[0] ?? 'Sesión registrada sin señales adversas.',
    points: [...performance.slice(1), ...safety],
  }
}

/** Same thresholds the weekly evaluation applies in migration 0150. */
export function signalLevel(
  signals: ActivitySessionSignalsResponse | null,
): SignalLevel | null {
  if (signals == null) return null

  if (signals.gaitChanged === true
    || signals.hasIllnessOrSymptom === true
    || signals.recoveryResponse === 'adverse') {
    return 'red'
  }

  const pain = toNumber(signals.pain)
  const fatigue = toNumber(signals.fatigue)
  const sleepQuality = toNumber(signals.sleepQuality)
  const perceivedRecovery = toNumber(signals.perceivedRecovery)
  if ((pain != null && pain > 0)
    || (fatigue != null && fatigue >= 7)
    || (sleepQuality != null && sleepQuality <= 2)
    || (perceivedRecovery != null && perceivedRecovery <= 4)
    || signals.recoveryResponse === 'incomplete') {
    return 'yellow'
  }

  return 'green'
}

function safetyPoints(signals: ActivitySessionSignalsResponse): string[] {
  const points: string[] = []
  if (signals.gaitChanged === true) points.push('Cambió tu forma de correr durante la sesión.')
  if (signals.hasIllnessOrSymptom === true) points.push('Registraste un síntoma o malestar.')
  if (signals.recoveryResponse === 'adverse') points.push('Te recuperaste peor de lo normal después.')
  if (signals.recoveryResponse === 'incomplete') points.push('La recuperación quedó a medias.')

  const pain = toNumber(signals.pain)
  if (pain != null && pain > 0) {
    const where = signals.painLocation == null ? '' : ` en ${signals.painLocation.toLowerCase()}`
    points.push(`Registraste dolor${where}: ${trim(pain)} de 10.`)
  }

  const fatigue = toNumber(signals.fatigue)
  if (fatigue != null && fatigue >= 7) points.push(`Terminaste con fatiga alta: ${trim(fatigue)} de 10.`)

  const sleepQuality = toNumber(signals.sleepQuality)
  if (sleepQuality != null && sleepQuality <= 2) points.push(`Dormiste mal: ${trim(sleepQuality)} de 5.`)

  const perceivedRecovery = toNumber(signals.perceivedRecovery)
  if (perceivedRecovery != null && perceivedRecovery <= 4) {
    points.push(`Te sentiste poco recuperado: ${trim(perceivedRecovery)} de 10.`)
  }

  return points
}

function performancePoints(
  planned: PlannedComparison | null,
  recent: RecentComparisonView | null,
): string[] {
  const points: string[] = []

  const paceTrend = recent?.rows.find((row) => row.metric === 'pace')?.trend
  if (paceTrend === 'faster') points.push('Fuiste más rápido que en tus sesiones parecidas recientes.')
  if (paceTrend === 'slower') points.push('Fuiste más lento que en tus sesiones parecidas recientes.')
  if (paceTrend === 'similar') points.push('Fuiste a un ritmo parecido al de tus sesiones recientes.')

  const volume = planned?.rows.find(
    (row) => row.fulfilmentPercent != null && (row.metric === 'distance' || row.metric === 'duration'),
  )
  if (volume?.fulfilmentPercent != null) {
    if (volume.fulfilmentPercent < 90) points.push('Hiciste menos de lo que pedía el plan para ese día.')
    else if (volume.fulfilmentPercent > 110) points.push('Hiciste más de lo que pedía el plan para ese día.')
    else points.push('Hiciste lo que pedía el plan para ese día.')
  }

  const rpeStatus = planned?.rows.find((row) => row.metric === 'rpe')?.rpeStatus
  if (rpeStatus === 'within') points.push('El esfuerzo que sentiste fue el que el plan esperaba.')
  if (rpeStatus === 'above') points.push('Te costó más de lo que el plan esperaba.')
  if (rpeStatus === 'below') points.push('Te costó menos de lo que el plan esperaba.')

  return points
}

function trim(value: number): string {
  return Number.isInteger(value) ? String(value) : value.toFixed(1)
}
