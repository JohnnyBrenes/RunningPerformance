import { describe, expect, it, vi } from 'vitest'
import { ApiError, PlansService } from '../api/generated'
import { apiRetryDelay, getCurrentTrainingPlanOrNull, readableApiError, shouldRetryApiQuery } from './api'

function apiError(status: number) {
  return Object.assign(Object.create(ApiError.prototype), { status }) as ApiError
}

describe('API cold-start retry policy', () => {
  it('retries network, throttling and server failures but not client validation errors', () => {
    expect(shouldRetryApiQuery(0, new TypeError('network'))).toBe(true)
    expect(shouldRetryApiQuery(0, apiError(429))).toBe(true)
    expect(shouldRetryApiQuery(0, apiError(503))).toBe(true)
    expect(shouldRetryApiQuery(0, apiError(400))).toBe(false)
    expect(shouldRetryApiQuery(7, apiError(503))).toBe(false)
  })

  it('caps exponential delay at ten seconds', () => {
    expect(apiRetryDelay(0)).toBe(1_000)
    expect(apiRetryDelay(3)).toBe(8_000)
    expect(apiRetryDelay(8)).toBe(10_000)
  })

  it('explains throttling and a sleeping free backend without exposing provider details', () => {
    expect(readableApiError(apiError(429))).toContain('demasiadas solicitudes')
    expect(readableApiError(apiError(503))).toContain('despertando')
  })

  it('treats a missing current plan as an empty state and preserves other failures', async () => {
    const request = vi.spyOn(PlansService, 'getCurrentTrainingPlan')
    request.mockRejectedValueOnce(apiError(404))
    await expect(getCurrentTrainingPlanOrNull()).resolves.toBeNull()

    request.mockRejectedValueOnce(apiError(500))
    await expect(getCurrentTrainingPlanOrNull()).rejects.toMatchObject({ status: 500 })
  })
})
