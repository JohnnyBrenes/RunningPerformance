import { describe, expect, it } from 'vitest'
import type { ActivitySourceResponse } from '../api/generated'
import { describeSource } from './dataOrigin'

describe('data origin', () => {
  it('names each origin in plain Spanish instead of its internal identifier', () => {
    expect(describeSource(source({ sourceClass: 'fit_session', originalName: 'act-123.fit' })))
      .toBe('Archivo FIT de Garmin · «act-123.fit»')
    expect(describeSource(source({ sourceClass: 'normalized_csv_row', originalName: 'historial.csv', sourceRowNumber: 123 })))
      .toBe('Historial importado en CSV · «historial.csv» · fila 123')
    expect(describeSource(source({ sourceClass: 'manual' }))).toBe('Registro hecho a mano')
  })

  it('shows an unknown origin rather than hiding it', () => {
    expect(describeSource(source({ sourceClass: 'garmin_connect_api' }))).toBe('garmin_connect_api')
  })
})

function source(overrides: Partial<ActivitySourceResponse>): ActivitySourceResponse {
  return {
    id: 'source',
    sourceClass: 'manual',
    sourceRowNumber: null,
    linkingResult: null,
    observedAt: null,
    summary: {},
    sourceFileId: null,
    originalName: null,
    sha256: null,
    ingestionRunId: null,
    ...overrides,
  }
}
