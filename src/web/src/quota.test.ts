import { describe, expect, it } from 'vitest'
import { quotaState } from './quota'

describe('free tier quota state', () => {
  it.each([
    [299, 'available'],
    [300, 'warning'],
    [399, 'warning'],
    [400, 'blocked'],
  ] as const)('maps %i MB to %s without a paid state', (used, expected) => {
    expect(quotaState(used, 300, 400)).toBe(expected)
  })
})
