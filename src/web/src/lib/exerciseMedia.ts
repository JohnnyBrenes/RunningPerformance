import type { ExerciseMediaResponse } from '../api/generated'

export function selectExerciseMedia(media: Array<ExerciseMediaResponse>, sex: string): ExerciseMediaResponse | undefined {
  return media.find((item) => item.presentationSex === sex)
    ?? media.find((item) => item.presentationSex === 'unspecified')
    ?? media[0]
}
