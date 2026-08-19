import { describe, expect, it } from 'vitest'
import type { ActivitySessionSignalsResponse } from '../api/generated'
import type { PlannedComparison } from './plannedComparison'
import type { RecentComparisonView } from './recentComparison'
import { buildSessionSummary, signalLevel } from './sessionSummary'

describe('session summary', () => {
  it('leads with the safety signal and refuses to offset it with performance', () => {
    const summary = buildSessionSummary(
      signals({ gaitChanged: true }),
      plannedWith({ fulfilmentPercent: 100, rpeStatus: 'within' }),
      recentWith('faster'),
    )

    expect(summary?.level).toBe('red')
    expect(summary?.headline).toContain('señal de seguridad')
    expect(summary?.points[0]).toBe('Cambió tu forma de correr durante la sesión.')
    expect(summary?.points).toContain(
      'Una señal así no se compensa con haber cumplido el plan ni con un buen ritmo.',
    )
    // The good news still appears, but never before the signal.
    expect(summary?.points.indexOf('Fuiste más rápido que en tus sesiones parecidas recientes.'))
      .toBeGreaterThan(0)
  })

  it('opens with performance when nothing adverse was recorded', () => {
    const summary = buildSessionSummary(
      signals({ pain: 0, fatigue: 3, recoveryResponse: 'normal' }),
      plannedWith({ fulfilmentPercent: 100, rpeStatus: 'within' }),
      recentWith('similar'),
    )

    expect(summary?.level).toBe('green')
    expect(summary?.headline).toBe('Fuiste a un ritmo parecido al de tus sesiones recientes.')
    expect(summary?.points).toContain('Hiciste lo que pedía el plan para ese día.')
  })

  it('reads pain as a signal worth looking at, not as a red one', () => {
    const summary = buildSessionSummary(signals({ pain: 2, painLocation: 'Rodilla derecha' }), null, null)

    expect(summary?.level).toBe('yellow')
    expect(summary?.points).toContain('Registraste dolor en rodilla derecha: 2 de 10.')
  })

  it('says nothing when there is nothing recorded to say', () => {
    expect(buildSessionSummary(null, null, null)).toBeNull()
  })
})

describe('signal level', () => {
  it('applies the same thresholds as the weekly evaluation', () => {
    expect(signalLevel(signals({ gaitChanged: true }))).toBe('red')
    expect(signalLevel(signals({ hasIllnessOrSymptom: true }))).toBe('red')
    expect(signalLevel(signals({ recoveryResponse: 'adverse' }))).toBe('red')
    expect(signalLevel(signals({ pain: 1 }))).toBe('yellow')
    expect(signalLevel(signals({ fatigue: 7 }))).toBe('yellow')
    expect(signalLevel(signals({ sleepQuality: 2 }))).toBe('yellow')
    expect(signalLevel(signals({ perceivedRecovery: 4 }))).toBe('yellow')
    expect(signalLevel(signals({ recoveryResponse: 'incomplete' }))).toBe('yellow')
    expect(signalLevel(signals({ pain: 0, fatigue: 6, sleepQuality: 3, perceivedRecovery: 5 }))).toBe('green')
    expect(signalLevel(null)).toBeNull()
  })
})

function signals(
  overrides: Partial<ActivitySessionSignalsResponse>,
): ActivitySessionSignalsResponse {
  return {
    pain: null,
    fatigue: null,
    sleepQuality: null,
    perceivedRecovery: null,
    gaitChanged: null,
    hasIllnessOrSymptom: null,
    recoveryResponse: null,
    painLocation: null,
    ...overrides,
  }
}

function plannedWith(
  overrides: { fulfilmentPercent: number; rpeStatus: 'below' | 'within' | 'above' },
): PlannedComparison {
  return {
    basis: 'logical_session',
    activityCount: 1,
    rows: [
      {
        metric: 'duration',
        label: 'Duración',
        actual: '45 min',
        planned: '45 min',
        fulfilmentPercent: overrides.fulfilmentPercent,
        rpeStatus: null,
        plannedIsDerived: false,
      },
      {
        metric: 'rpe',
        label: 'RPE',
        actual: '6.0',
        planned: '5.0–7.0',
        fulfilmentPercent: null,
        rpeStatus: overrides.rpeStatus,
        plannedIsDerived: false,
      },
    ],
  }
}

function recentWith(trend: 'faster' | 'similar' | 'slower'): RecentComparisonView {
  return {
    sampleSize: 4,
    windowDays: 90,
    distanceBand: '6.00–10.00 km',
    rows: [{ metric: 'pace', label: 'Ritmo', actual: '5:30 /km', median: '6:00 /km', trend }],
  }
}
