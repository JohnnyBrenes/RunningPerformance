import type { PlannedSessionBlockResponse, PlannedSessionResponse } from '../api/generated'

export type SessionStep = {
  key: string
  label: string
  detail: string | null
  /** Present only when the step is a structured block. */
  block: PlannedSessionBlockResponse | null
}

const blockTypeLabels: Record<string, string> = {
  warmup: 'Calentamiento',
  main: 'Bloque principal',
  cooldown: 'Vuelta a la calma',
  circuit: 'Circuito',
  mobility: 'Movilidad',
}

/**
 * Lays a day out as the ordered list of things to do, start to finish.
 *
 * The session used to be a grid of four labelled paragraphs beside a separate
 * list of blocks, which asks the athlete to work out the order for himself.
 * A day is a sequence — warm up, then each block, then the recoveries and the
 * cool down — so it is built here as one, and the same shape covers a session
 * that only has prose and one that has structured exercises.
 *
 * `mainSet` becomes a step of its own only when there are no blocks: with
 * blocks it is their summary, and repeating it would state the day twice.
 */
export function buildSessionSteps(session: PlannedSessionResponse): SessionStep[] {
  const steps: SessionStep[] = []
  const add = (key: string, label: string, detail: string | null, block: PlannedSessionBlockResponse | null = null) => {
    if (detail == null && block == null) return
    steps.push({ key, label, detail, block })
  }

  add('warmup', 'Calentamiento', session.warmup)

  if (session.blocks.length > 0) {
    for (const block of session.blocks) {
      const rounds = Number(block.repeatCount) > 1 ? ` · ${block.repeatCount} vueltas` : ''
      add(`block-${block.id}`, `${blockTypeLabels[block.blockType] ?? 'Bloque'}${rounds}`, block.instructions, block)
    }
  } else {
    add('main-set', 'Bloque principal', session.mainSet)
  }

  add('recoveries', 'Recuperaciones', session.recoveries)
  add('cooldown', 'Vuelta a la calma', session.cooldown)
  return steps
}

/** The short line that introduces the blocks, when the session carries one. */
export function blocksSummary(session: PlannedSessionResponse): string | null {
  return session.blocks.length > 0 ? session.mainSet : null
}
