import { describe, expect, it } from 'vitest'
import type { ActivityPlannedContextResponse, ActivitySummaryResponse } from '../api/generated'
import { buildPlannedComparison, rpeStatus } from './plannedComparison'

describe('planned versus completed comparison', () => {
  it('compares the whole logical session, not the single activity', () => {
    const comparison = buildPlannedComparison(
      activity({ durationSeconds: 600 }),
      context({ logicalActivityCount: 2, logicalDurationSeconds: 1500, plannedDurationSeconds: 2700 }),
    )

    expect(comparison.basis).toBe('logical_session')
    expect(comparison.activityCount).toBe(2)
    const duration = row(comparison, 'duration')
    expect(duration.actual).toBe('25 min')
    expect(duration.planned).toBe('45 min')
    expect(duration.fulfilmentPercent).toBe(56)
  })

  it('falls back to the activity when no confirmed link feeds the logical session', () => {
    const comparison = buildPlannedComparison(
      activity({ distanceM: 6500, durationSeconds: 2400 }),
      context({ logicalActivityCount: 0, plannedDistanceM: 8000 }),
    )

    expect(comparison.basis).toBe('activity')
    expect(row(comparison, 'distance').actual).toBe('6.50 km')
    expect(row(comparison, 'distance').fulfilmentPercent).toBe(81)
  })

  it('derives the planned pace only when the plan states distance and duration', () => {
    const withBoth = buildPlannedComparison(
      activity(),
      context({ logicalDistanceM: 8000, logicalDurationSeconds: 2400, plannedDistanceM: 8000, plannedDurationSeconds: 2700 }),
    )
    const durationOnly = buildPlannedComparison(
      activity(),
      context({ logicalDurationSeconds: 1500, plannedDurationSeconds: 2700 }),
    )

    expect(row(withBoth, 'pace').actual).toBe('5:00 /km')
    expect(row(withBoth, 'pace').planned).toBe('5:38 /km')
    expect(row(withBoth, 'pace').plannedIsDerived).toBe(true)
    expect(durationOnly.rows.some((item) => item.metric === 'pace')).toBe(false)
  })

  it('drops a row only when neither side has a value', () => {
    const strength = buildPlannedComparison(
      activity({ distanceM: null }),
      context({ logicalDurationSeconds: 1500, plannedDurationSeconds: 2700, sessionRpe: 5, targetRpeMin: 5, targetRpeMax: 7 }),
    )

    expect(strength.rows.map((item) => item.metric)).toEqual(['duration', 'rpe'])
  })

  it('keeps a row whose value is missing on one side only, as ND', () => {
    const comparison = buildPlannedComparison(
      activity({ distanceM: null }),
      context({ plannedDistanceM: 8000, targetRpeMin: 5, targetRpeMax: 7 }),
    )

    expect(row(comparison, 'distance').actual).toBe('ND')
    expect(row(comparison, 'distance').planned).toBe('8.00 km')
    expect(row(comparison, 'distance').fulfilmentPercent).toBeNull()
    expect(row(comparison, 'rpe').actual).toBe('ND')
    expect(row(comparison, 'rpe').planned).toBe('5.0–7.0')
    expect(row(comparison, 'rpe').rpeStatus).toBeNull()
  })

  it('reads the RPE target as a range', () => {
    const comparison = buildPlannedComparison(
      activity(),
      context({ sessionRpe: 5, targetRpeMin: 5, targetRpeMax: 7 }),
    )

    expect(row(comparison, 'rpe').actual).toBe('5.0')
    expect(row(comparison, 'rpe').planned).toBe('5.0–7.0')
    expect(row(comparison, 'rpe').rpeStatus).toBe('within')
  })
})

describe('rpe status', () => {
  it('places the effort against the planned range', () => {
    expect(rpeStatus(4, 5, 7)).toBe('below')
    expect(rpeStatus(6, 5, 7)).toBe('within')
    expect(rpeStatus(8, 5, 7)).toBe('above')
    expect(rpeStatus(8, 5, null)).toBe('within')
    expect(rpeStatus(null, 5, 7)).toBeNull()
    expect(rpeStatus(6, null, null)).toBeNull()
  })
})

function row(comparison: ReturnType<typeof buildPlannedComparison>, metric: string) {
  const found = comparison.rows.find((item) => item.metric === metric)
  if (!found) throw new Error(`Missing ${metric} row`)
  return found
}

function activity(overrides: Partial<ActivitySummaryResponse> = {}): ActivitySummaryResponse {
  return {
    id: 'activity',
    provisionalActivityKey: null,
    garminActivityId: null,
    activityType: 'running',
    activityCategory: 'running',
    modality: 'outdoor',
    startedAtLocal: '2026-08-18T06:00:00',
    title: 'Sesión',
    distanceM: null,
    durationSeconds: null,
    averagePaceSecondsPerKm: null,
    averageHeartRateBpm: null,
    maxHeartRateBpm: null,
    validationStatus: 'valid',
    ...overrides,
  }
}

function context(overrides: Partial<ActivityPlannedContextResponse>): ActivityPlannedContextResponse {
  return {
    plannedSessionId: 'session',
    linkId: 'link',
    linkStatus: 'confirmed',
    linkMethod: 'manual',
    linkConfidence: null,
    scheduledDate: '2026-08-18',
    sessionType: 'easy_run',
    modality: 'outdoor',
    obligation: 'planned',
    objective: 'Rodaje suave',
    plannedDistanceM: null,
    plannedDurationSeconds: null,
    targetRpeMin: null,
    targetRpeMax: null,
    terrain: null,
    planVersionStatus: 'published',
    executionStatus: null,
    logicalActivityCount: 1,
    logicalDistanceM: null,
    logicalDurationSeconds: null,
    sessionRpe: null,
    srpeLoad: null,
    ...overrides,
  }
}
