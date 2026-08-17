import { describe, expect, it } from 'vitest'
import { buildDailyDistanceChartRows, buildTrendChartRows, formatNullable, formatPace, quotaPercent, weightedRecentPace } from './dashboard'

describe('dashboard presentation rules', () => {
  it('keeps missing data distinct from explicit zero and false', () => {
    expect(formatNullable(null)).toBe('ND')
    expect(formatNullable(0)).toBe('0')
    expect(formatNullable(false)).toBe('No')
  })

  it('formats weighted pace without inventing a missing value', () => {
    expect(formatPace(359.6)).toBe('6:00 /km')
    expect(formatPace(null)).toBe('ND')
  })

  it('does not invent quota progress when provider usage is missing', () => {
    expect(quotaPercent({
      name: 'egress', used: null, unit: 'GB', warningAt: 4, blockAt: 5,
      state: 'not_available', code: 'nd', billingEnabled: false, source: 'manual', measuredAt: null,
    })).toBeNull()
  })

  it('creates separate chart series for treadmill and outdoor', () => {
    const rows = buildTrendChartRows([{
      weekStart: '2026-08-10', weekEnd: '2026-08-16', evaluationId: null,
      trafficLight: null, srpeTotal: null, sources: [], evaluationHref: null,
      modalities: [
        { modality: 'treadmill', activityCount: 1, distanceM: 5000, durationSeconds: 1800, paceSecondsPerKm: 360 },
        { modality: 'outdoor', activityCount: 1, distanceM: 3000, durationSeconds: 1200, paceSecondsPerKm: 400 },
      ],
    }])
    expect(rows[0]).toMatchObject({ week: '10 ago', treadmillKm: 5, outdoorKm: 3, otherKm: null })
  })

  it('plots each activity on its actual date and keeps rest days at zero', () => {
    const rows = buildDailyDistanceChartRows([
      { date: '2026-08-15', modalities: [], sources: [] },
      {
        date: '2026-08-16', sources: [], modalities: [
          { modality: 'outdoor', activityCount: 1, distanceM: 14115.6, durationSeconds: 5564.225, paceSecondsPerKm: 394.19 },
        ],
      },
    ])
    expect(rows).toEqual([
      { date: '15 ago', treadmillKm: 0, outdoorKm: 0, otherKm: 0 },
      { date: '16 ago', treadmillKm: 0, outdoorKm: 14.12, otherKm: 0 },
    ])
  })

  it('calculates recent pace from total time and distance', () => {
    const trend = {
      weekStart: '2026-08-10', weekEnd: '2026-08-16', evaluationId: null,
      trafficLight: null, srpeTotal: null, sources: [], evaluationHref: null,
      modalities: [
        { modality: 'treadmill', activityCount: 1, distanceM: 5000, durationSeconds: 2100, paceSecondsPerKm: 420 },
        { modality: 'outdoor', activityCount: 1, distanceM: 5000, durationSeconds: 1800, paceSecondsPerKm: 360 },
      ],
    }
    expect(weightedRecentPace([trend])).toBe(390)
    expect(weightedRecentPace([{ ...trend, modalities: [] }])).toBeNull()
  })
})
