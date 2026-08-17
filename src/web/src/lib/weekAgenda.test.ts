import { describe, expect, it } from 'vitest'
import type { PlannedSessionResponse } from '../api/generated'
import { buildRemainingWeekAgenda, sessionAgendaDetail, sessionAgendaTitle } from './weekAgenda'

describe('remaining week agenda', () => {
  it('starts today, ends on Sunday and removes prior days', () => {
    const sessions = [
      session('monday', '2026-08-17', 'easy_run', 6000),
      session('wednesday', '2026-08-19', 'quality', 8000, 'Series de 6 x 400 m'),
      session('saturday', '2026-08-22', 'long_run', 15000, 'Tirada larga', 'outdoor'),
    ]

    const agenda = buildRemainingWeekAgenda('2026-08-19', sessions)

    expect(agenda.map((day) => day.date)).toEqual([
      '2026-08-19', '2026-08-20', '2026-08-21', '2026-08-22', '2026-08-23',
    ])
    expect(agenda[0].sessions.map((item) => item.id)).toEqual(['wednesday'])
    expect(agenda[2].sessions).toEqual([])
    expect(agenda.flatMap((day) => day.sessions).some((item) => item.id === 'monday')).toBe(false)
  })

  it('turns plan detail into short runner-facing instructions', () => {
    const quality = session('quality', '2026-08-19', 'quality', 8000, 'Series de 6 x 400 m')
    const longRun = session('long', '2026-08-22', 'long_run', 15000, 'Continuidad', 'outdoor')
    const gym = session('gym', '2026-08-20', 'strength_mobility_plyometrics', null, 'Fuerza')
    gym.blocks = [{ id: 'block', position: 1, blockType: 'main', repeatCount: 1, instructions: 'Sentadilla goblet y pogos de tobillo', exercises: [] }]

    expect(sessionAgendaTitle(quality)).toBe('Correr 8 km')
    expect(sessionAgendaDetail(quality)).toBe('Series')
    expect(sessionAgendaTitle(longRun)).toBe('Correr exterior 15 km')
    expect(sessionAgendaDetail(longRun)).toBe('Tirada larga')
    expect(sessionAgendaTitle(gym)).toBe('Gimnasio')
    expect(sessionAgendaDetail(gym)).toBe('Piernas, Pliometría')
  })
})

function session(
  id: string,
  scheduledDate: string,
  sessionType: string,
  distanceM: number | null,
  objective = 'Objetivo planificado',
  modality = 'running',
): PlannedSessionResponse {
  return {
    id,
    scheduledDate,
    sessionType,
    modality,
    obligation: 'required',
    objective,
    distanceM,
    durationSeconds: null,
    targetRpeMin: null,
    targetRpeMax: null,
    terrain: modality === 'outdoor' ? 'Ruta exterior' : null,
    warmup: null,
    mainSet: objective,
    recoveries: null,
    cooldown: null,
    blocks: [],
  }
}
