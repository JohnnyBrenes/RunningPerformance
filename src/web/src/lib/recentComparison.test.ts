import { describe, expect, it } from 'vitest'
import type { ActivityRecentComparisonResponse, ActivitySummaryResponse } from '../api/generated'
import { buildRecentComparison, paceTrend } from './recentComparison'

describe('recent history comparison', () => {
  it('reads a lower pace as faster than the recent median', () => {
    const view = buildRecentComparison(
      activity({ averagePaceSecondsPerKm: 330 }),
      comparison({ medianPaceSecondsPerKm: 360 }),
    )

    expect(row(view, 'pace').actual).toBe('5:30 /km')
    expect(row(view, 'pace').median).toBe('6:00 /km')
    expect(row(view, 'pace').trend).toBe('faster')
    expect(view?.distanceBand).toBe('6.00–10.00 km')
  })

  it('never gives heart rate a verdict', () => {
    const view = buildRecentComparison(
      activity({ averageHeartRateBpm: 158 }),
      comparison({ medianHeartRateBpm: 145 }),
    )

    const heartRate = row(view, 'heartRate')
    expect(heartRate.actual).toBe('158 lpm')
    expect(heartRate.median).toBe('145 lpm')
    expect(heartRate.trend).toBeNull()
  })

  it('derives the pace when the stored average is missing', () => {
    const view = buildRecentComparison(
      activity({ averagePaceSecondsPerKm: null, distanceM: 8000, durationSeconds: 2400 }),
      comparison({ medianPaceSecondsPerKm: 360 }),
    )

    expect(row(view, 'pace').actual).toBe('5:00 /km')
    expect(row(view, 'pace').trend).toBe('faster')
  })

  it('returns nothing when no metric can be placed against the median', () => {
    expect(buildRecentComparison(
      activity({ averagePaceSecondsPerKm: null, distanceM: null, averageHeartRateBpm: null }),
      comparison({}),
    )).toBeNull()
  })
})

describe('pace trend', () => {
  it('treats gaps under the tolerance as the same effort', () => {
    expect(paceTrend(360, 360)).toBe('similar')
    expect(paceTrend(350, 360)).toBe('similar')
    expect(paceTrend(370, 360)).toBe('similar')
    expect(paceTrend(340, 360)).toBe('faster')
    expect(paceTrend(380, 360)).toBe('slower')
  })
})

function row(view: ReturnType<typeof buildRecentComparison>, metric: string) {
  const found = view?.rows.find((item) => item.metric === metric)
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
    startedAtLocal: '2026-03-31T06:00:00',
    title: 'Sesión',
    distanceM: 8000,
    durationSeconds: 2880,
    averagePaceSecondsPerKm: null,
    averageHeartRateBpm: null,
    maxHeartRateBpm: null,
    validationStatus: 'valid',
    ...overrides,
  }
}

function comparison(
  overrides: Partial<ActivityRecentComparisonResponse>,
): ActivityRecentComparisonResponse {
  return {
    windowDays: 90,
    sampleSize: 3,
    minDistanceM: 6000,
    maxDistanceM: 10000,
    medianPaceSecondsPerKm: null,
    medianHeartRateBpm: null,
    ...overrides,
  }
}
