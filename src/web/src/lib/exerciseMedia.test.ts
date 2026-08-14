import { describe, expect, it } from 'vitest'
import type { ExerciseMediaResponse } from '../api/generated'
import { selectExerciseMedia } from './exerciseMedia'

function media(presentationSex: string, position: number): ExerciseMediaResponse {
  return {
    id: crypto.randomUUID(), position, assetUri: `/${presentationSex}.png`, altText: presentationSex,
    mimeType: 'image/png', source: 'synthetic', author: null, license: 'project', sha256: null,
    presentationSex, widthPx: 1024, heightPx: 1024,
  }
}

describe('exercise media selection', () => {
  const variants = [media('male', 1), media('female', 2)]

  it('selects the variant matching the profile sex', () => {
    expect(selectExerciseMedia(variants, 'female')?.presentationSex).toBe('female')
    expect(selectExerciseMedia(variants, 'male')?.presentationSex).toBe('male')
  })

  it('uses an explicit neutral variant before the first available image', () => {
    const neutral = media('unspecified', 2)
    expect(selectExerciseMedia([variants[0], neutral], 'unspecified')).toBe(neutral)
  })

  it('supports a text-only exercise with no media', () => {
    expect(selectExerciseMedia([], 'female')).toBeUndefined()
  })
})
