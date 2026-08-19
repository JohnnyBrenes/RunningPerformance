import type { PlannedExerciseResponse } from '../api/generated'

/**
 * Says how much of an exercise the plan prescribes, in one short phrase.
 *
 * Only one of sets/repetitions/duration is required by the database, so every
 * missing piece has to read as absent rather than as `null`: a prescription of
 * "8 repeticiones" (no upper bound) is legitimate content, and printing
 * «8–null rep» inside the session would be worse than printing nothing.
 *
 * «por lado» belongs to repetitions as much as to time: without it a unilateral
 * exercise counted in repetitions reads as if the dose covered both sides.
 */
export function plannedDosage(planned: PlannedExerciseResponse): string {
  const perSide = planned.side === 'each' ? ' por lado' : ''
  const sets = planned.sets == null ? null : Number(planned.sets)

  if (planned.durationSeconds != null) return `${sets ?? 1} × ${Number(planned.durationSeconds)} s${perSide}`

  const min = planned.repetitionsMin == null ? null : Number(planned.repetitionsMin)
  const max = planned.repetitionsMax == null ? null : Number(planned.repetitionsMax)
  if (min == null && max == null) return sets == null ? '—' : `${sets} series`

  const repetitions = min == null || max == null || min === max ? `${min ?? max}` : `${min}–${max}`
  return `${sets ?? 1} × ${repetitions} rep${perSide}`
}

/**
 * Says how long to rest after a set, in the units a person counts them in.
 *
 * The value is stored on every prescribed exercise and was never shown, so the
 * session listed the work and left the pause — half of what a strength block
 * actually is — to be guessed.
 */
export function plannedRest(planned: PlannedExerciseResponse): string | null {
  if (planned.restSeconds == null) return null
  const seconds = Math.round(Number(planned.restSeconds))
  if (seconds <= 0) return null
  if (seconds < 60) return `Descanso ${seconds} s`
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  return rest === 0 ? `Descanso ${minutes} min` : `Descanso ${minutes} min ${rest} s`
}
