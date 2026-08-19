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
