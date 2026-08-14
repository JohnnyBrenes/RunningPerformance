import { describe, expect, it } from 'vitest'
import { toCheckinRequest } from './SessionCompletionPanel'

const draft = {
  sessionRpe: '5',
  pain: '0',
  painLocation: '',
  gaitChanged: 'false',
  fatigue: '4',
  sleepQuality: '5',
  perceivedRecovery: '7',
  hasIllnessOrSymptom: 'false',
  symptomNote: '',
  recoveryResponse: 'normal',
  note: '',
}

describe('session check-in mapping', () => {
  it('preserves explicit zero and false without inventing missing text', () => {
    expect(toCheckinRequest(draft, 'immediate')).toEqual({
      sessionRpe: 5,
      pain: 0,
      painLocation: null,
      gaitChanged: false,
      fatigue: 4,
      sleepQuality: 5,
      perceivedRecovery: 7,
      hasIllnessOrSymptom: false,
      symptomNote: null,
      recoveryResponse: null,
      note: null,
    })
  })

  it('keeps post-session response separate and does not repeat RPE at 24 hours', () => {
    expect(toCheckinRequest(draft, '24h')).toMatchObject({
      sessionRpe: null,
      recoveryResponse: 'normal',
    })
  })
})
