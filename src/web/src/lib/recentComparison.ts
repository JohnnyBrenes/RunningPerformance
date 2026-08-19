import type { ActivityRecentComparisonResponse, ActivitySummaryResponse } from '../api/generated'
import { formatPace, toNumber } from './dashboard'

export type PaceTrend = 'faster' | 'similar' | 'slower'

export type RecentComparisonRow = {
  metric: 'pace' | 'heartRate'
  label: string
  actual: string
  median: string
  trend: PaceTrend | null
}

export type RecentComparisonView = {
  sampleSize: number
  windowDays: number
  distanceBand: string | null
  rows: RecentComparisonRow[]
}

/** Below this relative gap two paces read as the same effort rather than a change. */
export const similarPaceTolerance = 0.03

/**
 * Places this session next to comparable ones that preceded it.
 *
 * Pace carries a verdict; heart rate does not. `coach-method-v1` holds that
 * heart rate does not prescribe intensity while it remains unvalidated, so it
 * is reported as an observation and never as better or worse.
 */
export function buildRecentComparison(
  activity: ActivitySummaryResponse,
  comparison: ActivityRecentComparisonResponse,
): RecentComparisonView | null {
  const rows: RecentComparisonRow[] = []

  const actualPace = sessionPace(activity)
  const medianPace = toNumber(comparison.medianPaceSecondsPerKm)
  if (actualPace != null && medianPace != null) {
    rows.push({
      metric: 'pace',
      label: 'Ritmo',
      actual: formatPace(actualPace),
      median: formatPace(medianPace),
      trend: paceTrend(actualPace, medianPace),
    })
  }

  const actualHeartRate = toNumber(activity.averageHeartRateBpm)
  const medianHeartRate = toNumber(comparison.medianHeartRateBpm)
  if (actualHeartRate != null && medianHeartRate != null) {
    rows.push({
      metric: 'heartRate',
      label: 'FC media',
      actual: `${Math.round(actualHeartRate)} lpm`,
      median: `${Math.round(medianHeartRate)} lpm`,
      trend: null,
    })
  }

  if (rows.length === 0) return null

  return {
    sampleSize: toNumber(comparison.sampleSize) ?? 0,
    windowDays: toNumber(comparison.windowDays) ?? 0,
    distanceBand: distanceBand(comparison),
    rows,
  }
}

/** A lower pace is a faster one, so the comparison reads inverted. */
export function paceTrend(actualSecondsPerKm: number, medianSecondsPerKm: number): PaceTrend {
  if (medianSecondsPerKm <= 0) return 'similar'
  const gap = (actualSecondsPerKm - medianSecondsPerKm) / medianSecondsPerKm
  if (gap < -similarPaceTolerance) return 'faster'
  if (gap > similarPaceTolerance) return 'slower'
  return 'similar'
}

/** Mirrors the coalesce in the endpoint so both sides read the same pace. */
function sessionPace(activity: ActivitySummaryResponse): number | null {
  const stored = toNumber(activity.averagePaceSecondsPerKm)
  if (stored != null) return stored
  const distanceM = toNumber(activity.distanceM)
  const durationSeconds = toNumber(activity.durationSeconds)
  if (distanceM == null || distanceM <= 0 || durationSeconds == null || durationSeconds <= 0) return null
  return durationSeconds / (distanceM / 1000)
}

function distanceBand(comparison: ActivityRecentComparisonResponse): string | null {
  const minimum = toNumber(comparison.minDistanceM)
  const maximum = toNumber(comparison.maxDistanceM)
  if (minimum == null || maximum == null) return null
  return `${(minimum / 1000).toFixed(2)}–${(maximum / 1000).toFixed(2)} km`
}
