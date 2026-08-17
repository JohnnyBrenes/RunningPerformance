import { describe, expect, it } from 'vitest'
import type { ActivitySummaryResponse, PlannedSessionResponse, TargetRaceResponse, TrainingPlanDetailResponse, WeeklyEvaluationDetailResponse } from '../api/generated'
import { buildCoachReview } from './coach'

describe('goal-directed weekly coach', () => {
  it('counts unplanned running and adjusts progression toward the A race', () => {
    const review = buildCoachReview({
      detail: evaluation('yellow', 'Falta respuesta de 24–48 h de una sesión clave.', true),
      plan: plan(),
      races: races(),
      activities: [...historyRuns(), run('linked-1', '2026-08-10', 5030), run('linked-2', '2026-08-12', 7583), run('extra', '2026-08-14', 10008), run('linked-3', '2026-08-16', 14116)],
    })

    expect(review.grade).toBe('adjust')
    expect(review.week.actualDistanceM).toBe(36_737)
    expect(review.week.unplannedRunCount).toBe(1)
    expect(review.primaryRace?.name).toBe('ESPN Runholics')
    expect(review.preparatoryRace?.name).toBe('Batman Day Run')
    expect(review.phase).toBe('Específica para carrera preparatoria')
    expect(review.runnerProfile.level).toBe('Intermedio recreativo')
    expect(review.runnerProfile.dominantType).toBe('Orientado a resistencia')
    expect(review.runnerProfile.specificity).toBe('Mixto, con predominio de cinta')
    expect(review.runnerProfile.currentLimiter).toBe('Especificidad exterior y control de carga')
    expect(review.nextWeek.sessions.find((item) => item.session.sessionType === 'quality')?.action).toBe('adjust')
    expect(review.nextWeek.sessions.find((item) => item.session.obligation === 'optional')?.action).toBe('omit')
  })

  it('allows progression only when the week is complete and has no adverse gate', () => {
    const review = buildCoachReview({
      detail: evaluation('green', 'Sin señales pendientes.', false),
      plan: plan(),
      races: races(),
      activities: [run('linked-1', '2026-08-10', 5000), run('linked-2', '2026-08-12', 7000), run('linked-3', '2026-08-16', 14000)],
    })

    expect(review.grade).toBe('progress')
    expect(review.recommendedDecision).toBe('execute_plan')
    expect(review.nextWeek.sessions.every((item) => item.action === 'keep')).toBe(true)
  })
})

function evaluation(trafficLight: string, rationale: string, missing: boolean) {
  return {
    evaluation: { id: 'evaluation', weekStart: '2026-08-10', weekEnd: '2026-08-16', formatVersion: 'v1', planVersionId: 'version', cutoffAt: '2026-08-17T12:00:00Z', status: 'provisional', trafficLight, rationale, createdAt: '2026-08-17T12:00:00Z', hasDecision: false },
    sessions: [
      { id: 'one', plannedSessionId: 'past-1', activityId: null, classification: 'planned', executionStatus: 'completed_as_planned', scheduledDate: '2026-08-10', sessionType: 'easy_run', modality: 'treadmill', objective: 'Fácil', actualStartedAtLocal: '2026-08-10T07:00:00' },
      { id: 'two', plannedSessionId: 'past-2', activityId: null, classification: 'planned', executionStatus: 'completed_modified', scheduledDate: '2026-08-12', sessionType: 'easy_run', modality: 'treadmill', objective: 'Fácil', actualStartedAtLocal: '2026-08-12T07:00:00' },
      { id: 'three', plannedSessionId: 'past-3', activityId: null, classification: 'planned', executionStatus: 'completed_modified', scheduledDate: '2026-08-15', sessionType: 'long_run', modality: 'outdoor', objective: 'Larga', actualStartedAtLocal: '2026-08-16T07:00:00' },
    ],
    metrics: [
      metric('P2', 'actual_distance_m:all', 'available', [{ sourceType: 'activity', sourceId: 'linked-1', label: '', href: '' }, { sourceType: 'activity', sourceId: 'linked-2', label: '', href: '' }, { sourceType: 'activity', sourceId: 'linked-3', label: '', href: '' }]),
      metric('P4', 'total', missing ? 'missing' : 'available', []),
      metric('P5', 'responses_24_to_48_hours', missing ? 'missing' : 'available', []),
    ],
    decision: null,
  } as WeeklyEvaluationDetailResponse
}

function metric(metricCode: string, dimension: string, status: string, evidence: Array<{ sourceType: string; sourceId: string; label: string; href: string }>) {
  return { id: `${metricCode}-${dimension}`, metricCode, dimension, numericValue: status === 'available' ? 1 : null, booleanValue: null, textValue: null, unit: null, status, formulaVersion: 'v1', evidence }
}

function plan() {
  const sessions = [
    session('past-1', '2026-08-10', 'easy_run', 'required', 5000),
    session('past-2', '2026-08-12', 'easy_run', 'required', 7000),
    session('past-3', '2026-08-15', 'long_run', 'required', 14000),
    session('next-1', '2026-08-17', 'quality', 'required', 8000),
    session('next-2', '2026-08-18', 'strength_mobility_plyometrics', 'required', null),
    session('next-3', '2026-08-19', 'easy_run', 'required', 7000),
    session('next-4', '2026-08-20', 'strength_mobility_plyometrics', 'optional', null),
    session('next-5', '2026-08-22', 'long_run', 'required', 14000),
  ]
  return { id: 'plan', name: 'Plan ESPN', purpose: 'Preparar el medio maratón', planStatus: 'active', version: { id: 'version', versionNumber: 1, periodStart: '2026-08-10', periodEnd: '2026-12-06', status: 'published', rationale: '', supersedesId: null, publishedAt: '2026-08-10T00:00:00Z', createdAt: '2026-08-10T00:00:00Z', sessionCount: sessions.length }, sessions } as TrainingPlanDetailResponse
}

function session(id: string, scheduledDate: string, sessionType: string, obligation: string, distanceM: number | null) {
  return { id, scheduledDate, sessionType, modality: sessionType.includes('run') || sessionType === 'quality' ? 'running' : 'strength', obligation, objective: 'Objetivo planificado', distanceM, durationSeconds: null, targetRpeMin: 2, targetRpeMax: 3, terrain: null, warmup: null, mainSet: null, recoveries: null, cooldown: null, blocks: [] } as PlannedSessionResponse
}

function races() {
  return [race('Batman Day Run', '2026-09-06', 'B', 10_000), race('ESPN Runholics', '2026-11-01', 'A', 21_097.5)]
}

function race(name: string, raceDate: string, priority: string, distanceM: number) {
  return { id: name, name, raceDate, distanceM, location: 'CDMX', priority, status: 'planned', timezoneName: 'America/Mexico_City', updatedAt: '2026-08-10T00:00:00Z', currentGoal: null } as TargetRaceResponse
}

function run(id: string, date: string, distanceM: number) {
  return { id, provisionalActivityKey: id, garminActivityId: null, activityType: 'running', activityCategory: 'running', modality: 'outdoor', startedAtLocal: `${date}T07:00:00`, title: 'Carrera', distanceM, durationSeconds: 3600, averagePaceSecondsPerKm: null, averageHeartRateBpm: null, maxHeartRateBpm: null, validationStatus: 'published' } as ActivitySummaryResponse
}

function historyRuns() {
  return ['2026-06-01', '2026-06-08', '2026-06-15', '2026-06-22', '2026-06-29', '2026-07-06', '2026-07-13', '2026-07-20', '2026-07-27', '2026-08-03']
    .map((date, index) => ({ ...run(`history-${index}`, date, 15_000), modality: 'treadmill' }))
}
