import { describe, expect, it } from 'vitest'
import type { PlannedSessionBlockResponse, PlannedSessionResponse } from '../api/generated'
import { blocksSummary, buildSessionSteps } from './sessionSteps'

function block(overrides: Partial<PlannedSessionBlockResponse>): PlannedSessionBlockResponse {
  return {
    id: 'block-1',
    position: 1,
    blockType: 'main',
    repeatCount: 1,
    instructions: 'Instrucciones del bloque.',
    exercises: [],
    ...overrides,
  }
}

function session(overrides: Partial<PlannedSessionResponse>): PlannedSessionResponse {
  return {
    id: 'session-1',
    scheduledDate: '2026-08-25',
    sessionType: 'strength_mobility_plyometrics',
    modality: 'strength',
    obligation: 'planned',
    objective: 'Objetivo',
    distanceM: null,
    durationSeconds: 2400,
    targetRpeMin: 5,
    targetRpeMax: 7,
    terrain: null,
    warmup: null,
    mainSet: null,
    recoveries: null,
    cooldown: null,
    blocks: [],
    ...overrides,
  } as PlannedSessionResponse
}

describe('buildSessionSteps', () => {
  it('orders a prose session the way it is executed', () => {
    const steps = buildSessionSteps(session({
      warmup: '8 min suaves.',
      mainSet: '30 min continuos.',
      recoveries: 'No aplica.',
      cooldown: '2 min caminando.',
    }))
    expect(steps.map((step) => step.label)).toEqual([
      'Calentamiento', 'Bloque principal', 'Recuperaciones', 'Vuelta a la calma',
    ])
    expect(steps[1].detail).toBe('30 min continuos.')
  })

  it('skips the parts the session does not prescribe', () => {
    const steps = buildSessionSteps(session({ mainSet: '30 min continuos.' }))
    expect(steps.map((step) => step.label)).toEqual(['Bloque principal'])
  })

  it('turns every block into its own step, in order', () => {
    const steps = buildSessionSteps(session({
      warmup: 'Movilidad.',
      mainSet: 'Dos bloques.',
      blocks: [
        block({ id: 'b1', position: 1, blockType: 'mobility', instructions: 'Activa el tronco.' }),
        block({ id: 'b2', position: 2, instructions: 'Fuerza de piernas.', repeatCount: 3 }),
      ],
    }))
    expect(steps.map((step) => step.label)).toEqual([
      'Calentamiento', 'Movilidad', 'Bloque principal · 3 vueltas',
    ])
    expect(steps[2].block?.id).toBe('b2')
  })

  it('does not repeat mainSet as a step when the blocks already summarise it', () => {
    const steps = buildSessionSteps(session({
      mainSet: 'Dos bloques.',
      blocks: [block({ instructions: 'Fuerza de piernas.' })],
    }))
    expect(steps.some((step) => step.detail === 'Dos bloques.')).toBe(false)
  })
})

describe('blocksSummary', () => {
  it('introduces the blocks with mainSet', () => {
    expect(blocksSummary(session({ mainSet: 'Dos bloques.', blocks: [block({})] }))).toBe('Dos bloques.')
  })

  it('says nothing when the session has no blocks', () => {
    expect(blocksSummary(session({ mainSet: '30 min continuos.' }))).toBeNull()
  })
})
