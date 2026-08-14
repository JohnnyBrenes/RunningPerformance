import { describe, expect, it } from 'vitest'
import { calculatePace, goalTimeInputFromSeconds, secondsFromClock } from './RacesPage'

describe('race goal pace', () => {
  it('requires an unambiguous hours:minutes:seconds goal time', () => {
    expect(secondsFromClock('01:45:00')).toBe(6_300)
    expect(secondsFromClock('1:45')).toBeNull()
    expect(secondsFromClock('01:60:00')).toBeNull()
  })

  it('calculates seconds per kilometer from total seconds and meters', () => {
    expect(calculatePace('01:45:00', 21_000)).toBe(300)
    expect(calculatePace('00:48:00', 10_000)).toBe(288)
  })

  it('opens existing goal times in the required format', () => {
    expect(goalTimeInputFromSeconds(2_880)).toBe('00:48:00')
    expect(goalTimeInputFromSeconds(6_300)).toBe('01:45:00')
    expect(goalTimeInputFromSeconds(null)).toBe('')
  })
})
