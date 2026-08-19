import { describe, expect, it } from 'vitest'
import type { PlannedExerciseResponse } from '../api/generated'
import { plannedDosage, plannedRest } from './plannedDosage'

function exercise(overrides: Partial<PlannedExerciseResponse>): PlannedExerciseResponse {
  return {
    id: 'planned-1',
    position: 1,
    sets: null,
    repetitionsMin: null,
    repetitionsMax: null,
    durationSeconds: null,
    restSeconds: null,
    loadValue: null,
    loadUnit: null,
    targetRpe: null,
    targetRir: null,
    tempo: null,
    side: null,
    note: null,
    exercise: {} as PlannedExerciseResponse['exercise'],
    ...overrides,
  }
}

describe('plannedDosage', () => {
  it('reads a repetition range', () => {
    expect(plannedDosage(exercise({ sets: 3, repetitionsMin: 8, repetitionsMax: 10 }))).toBe('3 × 8–10 rep')
  })

  it('collapses a range whose ends match, even when they arrive as numeric strings', () => {
    expect(plannedDosage(exercise({ sets: '3', repetitionsMin: '8', repetitionsMax: 8 }))).toBe('3 × 8 rep')
  })

  it('omits the missing end instead of printing null', () => {
    expect(plannedDosage(exercise({ sets: 3, repetitionsMin: 8 }))).toBe('3 × 8 rep')
  })

  it('falls back to the number of sets when nothing counts the work', () => {
    expect(plannedDosage(exercise({ sets: 3 }))).toBe('3 series')
  })

  it('says «por lado» for a unilateral exercise counted in repetitions', () => {
    expect(plannedDosage(exercise({ sets: 3, repetitionsMin: 8, repetitionsMax: 8, side: 'each' })))
      .toBe('3 × 8 rep por lado')
  })

  it('says «por lado» for a unilateral exercise counted in time', () => {
    expect(plannedDosage(exercise({ sets: 2, durationSeconds: '25.00', side: 'each' }))).toBe('2 × 25 s por lado')
  })
})

describe('plannedRest', () => {
  it('counts a short rest in seconds', () => {
    expect(plannedRest(exercise({ restSeconds: 45 }))).toBe('Descanso 45 s')
  })

  it('counts a whole minute as minutes', () => {
    expect(plannedRest(exercise({ restSeconds: '60.00' }))).toBe('Descanso 1 min')
  })

  it('keeps the seconds that do not complete a minute', () => {
    expect(plannedRest(exercise({ restSeconds: 90 }))).toBe('Descanso 1 min 30 s')
  })

  it('says nothing when no rest was prescribed', () => {
    expect(plannedRest(exercise({ restSeconds: null }))).toBeNull()
  })
})
