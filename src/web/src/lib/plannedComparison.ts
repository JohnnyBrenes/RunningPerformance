import type { ActivityPlannedContextResponse, ActivitySummaryResponse } from '../api/generated'
import { formatPace, toNumber } from './dashboard'

export type ComparisonBasis = 'logical_session' | 'activity'

export type RpeStatus = 'below' | 'within' | 'above'

export type PlannedComparisonRow = {
  metric: 'distance' | 'duration' | 'pace' | 'rpe'
  label: string
  actual: string
  planned: string
  fulfilmentPercent: number | null
  rpeStatus: RpeStatus | null
  plannedIsDerived: boolean
}

export type PlannedComparison = {
  basis: ComparisonBasis
  activityCount: number
  rows: PlannedComparisonRow[]
}

/**
 * Compares what was done against what the plan asked for that day.
 *
 * Several activities can form one logical session, so the comparison uses the
 * logical totals whenever the link is confirmed; comparing a single activity
 * against the planned total would understate completion.
 */
export function buildPlannedComparison(
  activity: ActivitySummaryResponse,
  context: ActivityPlannedContextResponse,
): PlannedComparison {
  const activityCount = toNumber(context.logicalActivityCount) ?? 0
  const basis: ComparisonBasis = activityCount >= 1 ? 'logical_session' : 'activity'

  const actualDistanceM = basis === 'logical_session'
    ? toNumber(context.logicalDistanceM)
    : toNumber(activity.distanceM)
  const actualDurationSeconds = basis === 'logical_session'
    ? toNumber(context.logicalDurationSeconds)
    : toNumber(activity.durationSeconds)
  const plannedDistanceM = toNumber(context.plannedDistanceM)
  const plannedDurationSeconds = toNumber(context.plannedDurationSeconds)

  const rows: PlannedComparisonRow[] = [
    {
      metric: 'distance',
      label: 'Distancia',
      actual: formatKilometres(actualDistanceM),
      planned: formatKilometres(plannedDistanceM),
      fulfilmentPercent: fulfilment(actualDistanceM, plannedDistanceM),
      rpeStatus: null,
      plannedIsDerived: false,
    },
    {
      metric: 'duration',
      label: 'Duración',
      actual: formatMinutes(actualDurationSeconds),
      planned: formatMinutes(plannedDurationSeconds),
      fulfilmentPercent: fulfilment(actualDurationSeconds, plannedDurationSeconds),
      rpeStatus: null,
      plannedIsDerived: false,
    },
    paceRow(actualDistanceM, actualDurationSeconds, plannedDistanceM, plannedDurationSeconds),
    rpeRow(context),
  ]

  // A row with no value on either side says nothing; one missing side still does.
  return { basis, activityCount, rows: rows.filter((row) => row.actual !== 'ND' || row.planned !== 'ND') }
}

/**
 * The plan states distance and duration but never a pace, so any planned pace
 * is arithmetic from those two and is flagged as derived. `coach-method-v1`
 * warns that a target pace is not a prescription for every session.
 */
function paceRow(
  actualDistanceM: number | null,
  actualDurationSeconds: number | null,
  plannedDistanceM: number | null,
  plannedDurationSeconds: number | null,
): PlannedComparisonRow {
  const plannedPace = pace(plannedDistanceM, plannedDurationSeconds)
  return {
    metric: 'pace',
    label: 'Ritmo',
    actual: formatPace(pace(actualDistanceM, actualDurationSeconds)),
    planned: formatPace(plannedPace),
    fulfilmentPercent: null,
    rpeStatus: null,
    plannedIsDerived: plannedPace != null,
  }
}

function rpeRow(context: ActivityPlannedContextResponse): PlannedComparisonRow {
  const actual = toNumber(context.sessionRpe)
  const minimum = toNumber(context.targetRpeMin)
  const maximum = toNumber(context.targetRpeMax)
  return {
    metric: 'rpe',
    label: 'RPE',
    actual: actual == null ? 'ND' : formatOneDecimal(actual),
    planned: formatRpeTarget(minimum, maximum),
    fulfilmentPercent: null,
    rpeStatus: rpeStatus(actual, minimum, maximum),
    plannedIsDerived: false,
  }
}

export function rpeStatus(
  actual: number | null,
  minimum: number | null,
  maximum: number | null,
): RpeStatus | null {
  if (actual == null || (minimum == null && maximum == null)) return null
  if (minimum != null && actual < minimum) return 'below'
  if (maximum != null && actual > maximum) return 'above'
  return 'within'
}

function pace(distanceM: number | null, durationSeconds: number | null): number | null {
  if (distanceM == null || distanceM <= 0 || durationSeconds == null || durationSeconds <= 0) return null
  return durationSeconds / (distanceM / 1000)
}

function fulfilment(actual: number | null, planned: number | null): number | null {
  if (actual == null || planned == null || planned <= 0) return null
  return Math.round((actual / planned) * 100)
}

function formatKilometres(value: number | null): string {
  return value == null ? 'ND' : `${(value / 1000).toFixed(2)} km`
}

function formatMinutes(value: number | null): string {
  return value == null ? 'ND' : `${Math.round(value / 60)} min`
}

function formatOneDecimal(value: number): string {
  return value.toFixed(1)
}

function formatRpeTarget(minimum: number | null, maximum: number | null): string {
  if (minimum == null && maximum == null) return 'ND'
  if (minimum != null && maximum != null) {
    return minimum === maximum
      ? formatOneDecimal(minimum)
      : `${formatOneDecimal(minimum)}–${formatOneDecimal(maximum)}`
  }
  return formatOneDecimal((minimum ?? maximum) as number)
}
