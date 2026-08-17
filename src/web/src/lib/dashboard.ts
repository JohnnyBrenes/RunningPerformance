import type { DashboardDailyDistanceResponse, DashboardTrendWeekResponse, QuotaResourceResponse } from '../api/generated'

export type TrendChartRow = {
  week: string
  treadmillKm: number | null
  outdoorKm: number | null
  otherKm: number | null
}

export type DailyDistanceChartRow = {
  date: string
  treadmillKm: number
  outdoorKm: number
  otherKm: number
}

export function toNumber(value: number | string | null | undefined): number | null {
  if (value == null) return null
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

export function formatNullable(
  value: number | string | boolean | null | undefined,
  suffix = '',
): string {
  if (value == null) return 'ND'
  if (typeof value === 'boolean') return value ? 'Sí' : 'No'
  return `${value}${suffix}`
}

export function formatPace(value: number | string | null | undefined): string {
  const seconds = toNumber(value)
  if (seconds == null) return 'ND'
  const rounded = Math.round(seconds)
  return `${Math.floor(rounded / 60)}:${String(rounded % 60).padStart(2, '0')} /km`
}

export function quotaPercent(resource: QuotaResourceResponse): number | null {
  const used = toNumber(resource.used)
  const block = toNumber(resource.blockAt)
  if (used == null || block == null || block <= 0) return null
  return Math.min(100, Math.max(0, (used / block) * 100))
}

export function buildTrendChartRows(trends: DashboardTrendWeekResponse[]): TrendChartRow[] {
  return trends.map((trend) => {
    const distance = (modality: string) => {
      const value = trend.modalities.find((item) => item.modality === modality)?.distanceM
      const meters = toNumber(value)
      return meters == null ? null : Math.round((meters / 1000) * 100) / 100
    }
    return {
      week: new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short', timeZone: 'UTC' })
        .format(new Date(`${trend.weekStart}T12:00:00Z`)),
      treadmillKm: distance('treadmill'),
      outdoorKm: distance('outdoor'),
      otherKm: distance('other'),
    }
  })
}

export function buildDailyDistanceChartRows(days: DashboardDailyDistanceResponse[]): DailyDistanceChartRow[] {
  return days.map((day) => {
    const distance = (modality: string) => {
      const value = day.modalities.find((item) => item.modality === modality)?.distanceM
      const meters = toNumber(value)
      return meters == null ? 0 : Math.round((meters / 1000) * 100) / 100
    }
    return {
      date: new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short', timeZone: 'UTC' })
        .format(new Date(`${day.date}T12:00:00Z`)),
      treadmillKm: distance('treadmill'),
      outdoorKm: distance('outdoor'),
      otherKm: distance('other'),
    }
  })
}

export function weightedRecentPace(
  trends: DashboardTrendWeekResponse[],
  weeks = 4,
): number | null {
  let distanceM = 0
  let durationSeconds = 0
  for (const trend of trends.slice(-weeks)) {
    for (const modality of trend.modalities) {
      const distance = toNumber(modality.distanceM)
      const duration = toNumber(modality.durationSeconds)
      if (distance != null && distance > 0 && duration != null && duration > 0) {
        distanceM += distance
        durationSeconds += duration
      }
    }
  }
  return distanceM > 0 ? durationSeconds / (distanceM / 1000) : null
}
