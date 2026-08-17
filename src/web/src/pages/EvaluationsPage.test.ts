import { describe, expect, it } from 'vitest'
import type { WeeklyEvaluationSummaryResponse } from '../api/generated'
import { formatMetricValue, previousMonday, selectWeeklyEvaluations } from './EvaluationsPage'

describe('weekly metric presentation', () => {
  it('defaults a new evaluation to the most recently completed week', () => {
    expect(previousMonday(new Date('2026-08-17T12:00:00'))).toBe('2026-08-10')
  })

  it('shows one closure per week and keeps the meaningful record', () => {
    const summary = (id: string, weekStart: string, status: string, hasDecision: boolean): WeeklyEvaluationSummaryResponse => ({
      id, weekStart, weekEnd: '2026-08-16', formatVersion: 'v1', planVersionId: null,
      cutoffAt: '2026-08-17T00:00:00Z', status, trafficLight: 'green', rationale: '',
      createdAt: '2026-08-17T00:00:00Z', hasDecision,
    })
    const selected = selectWeeklyEvaluations([
      summary('latest-provisional', '2026-08-10', 'provisional', false),
      summary('with-decision', '2026-08-10', 'provisional', true),
      summary('final', '2026-08-10', 'final', false),
      summary('another-week', '2026-08-03', 'provisional', false),
    ])

    expect(selected.map((evaluation) => evaluation.id)).toEqual(['final', 'another-week'])
  })

  it('renders a stored missing value as ND without converting it to zero', () => {
    expect(formatMetricValue({ status: 'missing', numericValue: null, booleanValue: null, textValue: null, unit: 'm', dimension: 'actual_distance_m:outdoor' })).toBe('ND')
  })

  it('preserves explicit zero and false values', () => {
    expect(formatMetricValue({ status: 'available', numericValue: 0, booleanValue: null, textValue: null, unit: '0-10', dimension: 'pain' })).toBe('0/10')
    expect(formatMetricValue({ status: 'available', numericValue: null, booleanValue: false, textValue: null, unit: null, dimension: 'gait_changed' })).toBe('No')
  })

  it('formats modality-specific pace from time divided by distance', () => {
    expect(formatMetricValue({ status: 'available', numericValue: 360, booleanValue: null, textValue: null, unit: 's/km', dimension: 'pace_seconds_per_km:treadmill' })).toBe('6:00 min/km')
    expect(formatMetricValue({ status: 'available', numericValue: 359.6, booleanValue: null, textValue: null, unit: 's/km', dimension: 'pace_seconds_per_km:outdoor' })).toBe('6:00 min/km')
  })
})
