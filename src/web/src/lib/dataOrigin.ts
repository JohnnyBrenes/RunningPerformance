import type { ActivitySourceResponse } from '../api/generated'

/**
 * Says where a session's numbers came from in plain Spanish.
 *
 * The stored `sourceClass` is an internal identifier (`normalized_csv_row` and
 * friends); showing it raw is the same jargon problem this phase exists to fix.
 * Unknown values fall back to the raw string rather than being hidden, so a new
 * source class is visible instead of silently unlabelled.
 */
export function describeSource(source: ActivitySourceResponse): string {
  const origin = originLabels[source.sourceClass] ?? source.sourceClass
  const file = source.originalName == null ? null : `«${source.originalName}»`
  const row = source.sourceRowNumber == null ? null : `fila ${source.sourceRowNumber}`
  return [origin, file, row].filter((part) => part != null).join(' · ')
}

const originLabels: Record<string, string> = {
  fit_session: 'Archivo FIT de Garmin',
  normalized_csv_row: 'Historial importado en CSV',
  manual: 'Registro hecho a mano',
}
