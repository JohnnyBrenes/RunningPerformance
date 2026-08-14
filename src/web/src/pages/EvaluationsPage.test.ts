import { describe, expect, it } from 'vitest'
import { formatMetricValue } from './EvaluationsPage'

describe('weekly metric presentation', () => {
  it('renders a stored missing value as ND without converting it to zero', () => {
    expect(formatMetricValue({ status: 'missing', numericValue: null, booleanValue: null, textValue: null, unit: 'm', dimension: 'actual_distance_m:outdoor' })).toBe('ND')
  })

  it('preserves explicit zero and false values', () => {
    expect(formatMetricValue({ status: 'available', numericValue: 0, booleanValue: null, textValue: null, unit: '0-10', dimension: 'pain' })).toBe('0 0-10')
    expect(formatMetricValue({ status: 'available', numericValue: null, booleanValue: false, textValue: null, unit: null, dimension: 'gait_changed' })).toBe('No')
  })

  it('formats modality-specific pace from time divided by distance', () => {
    expect(formatMetricValue({ status: 'available', numericValue: 360, booleanValue: null, textValue: null, unit: 's/km', dimension: 'pace_seconds_per_km:treadmill' })).toBe('6:00 min/km')
    expect(formatMetricValue({ status: 'available', numericValue: 359.6, booleanValue: null, textValue: null, unit: 's/km', dimension: 'pace_seconds_per_km:outdoor' })).toBe('6:00 min/km')
  })
})
