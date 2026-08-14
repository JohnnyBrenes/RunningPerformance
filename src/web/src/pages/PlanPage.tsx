import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { PlansService, ProfileService, type PlannedExerciseResponse, type PlannedSessionResponse, type TrainingPlanDetailResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { SessionCompletionPanel } from '../components/SessionCompletionPanel'
import { getCurrentTrainingPlanOrNull, readableApiError } from '../lib/api'
import { selectExerciseMedia } from '../lib/exerciseMedia'

export function PlanPage() {
  const queryClient = useQueryClient()
  const [versionId, setVersionId] = useState<string | null>(() => new URLSearchParams(window.location.search).get('version'))
  const [sessionId, setSessionId] = useState<string | null>(() => new URLSearchParams(window.location.search).get('session'))
  const [creatingDraft, setCreatingDraft] = useState(false)
  const [draftReason, setDraftReason] = useState('Ajuste semanal a partir de la versión publicada.')
  const [editing, setEditing] = useState<PlannedSessionResponse | null>(null)
  const [editDate, setEditDate] = useState('')
  const [editObjective, setEditObjective] = useState('')

  const summaries = useQuery({ queryKey: ['plans'], queryFn: () => PlansService.getTrainingPlans() })
  const current = useQuery({ queryKey: ['plan-current'], queryFn: getCurrentTrainingPlanOrNull })
  const profile = useQuery({ queryKey: ['profile'], queryFn: () => ProfileService.getProfile() })
  const planSummary = summaries.data?.[0]
  const selectedVersion = planSummary?.versions.find((version) => version.id === versionId)
  const historical = useQuery({
    queryKey: ['plan-version', planSummary?.id, selectedVersion?.id],
    queryFn: () => PlansService.getTrainingPlanVersion({ planId: planSummary!.id, versionId: selectedVersion!.id }),
    enabled: Boolean(selectedVersion && selectedVersion.id !== current.data?.version.id),
  })
  const detail = selectedVersion && selectedVersion.id !== current.data?.version.id ? historical.data : current.data

  useEffect(() => {
    if (!versionId && current.data) setVersionId(current.data.version.id)
  }, [current.data, versionId])

  useEffect(() => {
    if (!detail) return
    const today = localDateKey()
    const preferred = detail.sessions.find((session) => session.scheduledDate === today) ?? detail.sessions[0]
    if (!detail.sessions.some((session) => session.id === sessionId)) setSessionId(preferred?.id ?? null)
  }, [detail, sessionId])

  const refreshPlans = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['plans'] }),
      queryClient.invalidateQueries({ queryKey: ['plan-current'] }),
      queryClient.invalidateQueries({ queryKey: ['plan-version'] }),
    ])
  }

  const cloneDraft = useMutation({
    mutationFn: () => PlansService.cloneTrainingPlanDraft({
      planId: current.data!.id,
      requestBody: { sourceVersionId: current.data!.version.id, rationale: draftReason },
    }),
    onSuccess: async (created) => {
      setCreatingDraft(false)
      setVersionId(created.version.id)
      await refreshPlans()
    },
  })

  const publish = useMutation({
    mutationFn: () => PlansService.publishTrainingPlanVersion({ planId: detail!.id, versionId: detail!.version.id }),
    onSuccess: async (published) => {
      setVersionId(published.version.id)
      await refreshPlans()
    },
  })

  const saveSession = useMutation({
    mutationFn: () => PlansService.updatePlannedSession({
      planId: detail!.id,
      versionId: detail!.version.id,
      sessionId: editing!.id,
      requestBody: { scheduledDate: editDate, objective: editObjective },
    }),
    onSuccess: async () => {
      setEditing(null)
      await refreshPlans()
    },
  })

  const openSessionEdit = (session: PlannedSessionResponse) => {
    setEditing(session)
    setEditDate(session.scheduledDate)
    setEditObjective(session.objective)
  }

  if (summaries.isPending || current.isPending || profile.isPending) return <LoadingState label="Cargando plan" />
  if (summaries.isError || current.isError || profile.isError) {
    return <ErrorState message={readableApiError(summaries.error ?? current.error ?? profile.error)} retry={() => void Promise.all([summaries.refetch(), current.refetch(), profile.refetch()])} />
  }
  if (historical.isPending && selectedVersion?.id !== current.data?.version.id) return <LoadingState label="Cargando versión" />
  if (historical.isError) return <ErrorState message={readableApiError(historical.error)} retry={() => void historical.refetch()} />
  if (!planSummary || !detail || !current.data) return <EmptyState title="Todavía no hay un plan">La primera versión publicada aparecerá aquí.</EmptyState>

  const selectedSession = detail.sessions.find((session) => session.id === sessionId) ?? detail.sessions[0]
  const draft = planSummary.versions.find((version) => version.status === 'draft')

  return (
    <div className="page plan-page">
      <header className="page-heading split-heading">
        <div><p className="eyebrow">Plan de entrenamiento</p><h1>{detail.name}</h1><p>{detail.purpose}</p></div>
        <span className={`version-status ${detail.version.status}`}>v{detail.version.versionNumber} · {versionStatusLabel(detail.version.status)}</span>
      </header>

      <section className="version-panel" aria-labelledby="versions-title">
        <div><span className="section-label">Trazabilidad</span><h2 id="versions-title">Versiones del plan</h2><p>Ninguna publicación se reescribe. Los cambios empiezan en un borrador.</p></div>
        <div className="version-tabs" role="list" aria-label="Versiones disponibles">
          {planSummary.versions.map((version) => <button className={version.id === detail.version.id ? 'active' : ''} type="button" key={version.id} onClick={() => setVersionId(version.id)}>v{version.versionNumber}<small>{versionStatusLabel(version.status)}</small></button>)}
        </div>
        <div className="version-actions">
          {detail.version.status === 'draft' ? <button className="button primary" type="button" disabled={publish.isPending} onClick={() => publish.mutate()}>{publish.isPending ? 'Publicando…' : 'Publicar esta versión'}</button> : draft ? <button className="button secondary" type="button" onClick={() => setVersionId(draft.id)}>Continuar borrador v{draft.versionNumber}</button> : <button className="button secondary" type="button" onClick={() => setCreatingDraft(true)}>Crear borrador desde v{current.data.version.versionNumber}</button>}
        </div>
        {creatingDraft && <form className="draft-form" onSubmit={(event) => { event.preventDefault(); cloneDraft.mutate() }}><label>Motivo de la nueva versión<textarea rows={2} required maxLength={2000} value={draftReason} onChange={(event) => setDraftReason(event.target.value)} /></label><div className="form-actions"><button className="button ghost" type="button" onClick={() => setCreatingDraft(false)}>Cancelar</button><button className="button primary" disabled={cloneDraft.isPending}>{cloneDraft.isPending ? 'Copiando…' : 'Crear borrador'}</button></div></form>}
        {(cloneDraft.isError || publish.isError) && <p className="form-alert" role="alert">{readableApiError(cloneDraft.error ?? publish.error)}</p>}
      </section>

      <section className="section-block">
        <div className="section-heading"><div><span className="section-label">Semana activa</span><h2>Calendario</h2></div><span className="date-chip">{formatPeriod(detail.version.periodStart, detail.version.periodEnd)}</span></div>
        <div className="week-strip" role="list" aria-label="Sesiones de la semana">
          {detail.sessions.map((session) => <button className={session.id === selectedSession?.id ? 'active' : ''} type="button" key={session.id} onClick={() => setSessionId(session.id)}><span>{weekday(session.scheduledDate)}</span><strong>{dayNumber(session.scheduledDate)}</strong><small>{sessionTypeLabel(session.sessionType)}</small>{session.scheduledDate === localDateKey() && <i>Hoy</i>}</button>)}
        </div>
      </section>

      {selectedSession && <><SessionGuide detail={detail} session={selectedSession} sex={profile.data.sex} edit={detail.version.status === 'draft' ? () => openSessionEdit(selectedSession) : undefined} /><SessionCompletionPanel session={selectedSession} planVersionStatus={detail.version.status} /></>}

      {editing && <div className="drawer-backdrop" role="presentation" onMouseDown={() => setEditing(null)}><form className="history-drawer session-edit" role="dialog" aria-modal="true" aria-labelledby="session-edit-title" onMouseDown={(event) => event.stopPropagation()} onSubmit={(event) => { event.preventDefault(); saveSession.mutate() }}><div className="form-title"><div><span className="section-label">Borrador v{detail.version.versionNumber}</span><h2 id="session-edit-title">Ajustar sesión</h2><p>La versión publicada permanece intacta.</p></div><button className="icon-button" type="button" aria-label="Cerrar" onClick={() => setEditing(null)}>×</button></div><label>Fecha<input type="date" required min={detail.version.periodStart} max={detail.version.periodEnd} value={editDate} onChange={(event) => setEditDate(event.target.value)} /></label><label>Objetivo<textarea rows={4} required maxLength={2000} value={editObjective} onChange={(event) => setEditObjective(event.target.value)} /></label>{saveSession.isError && <p className="form-alert" role="alert">{readableApiError(saveSession.error)}</p>}<div className="form-actions"><button className="button ghost" type="button" onClick={() => setEditing(null)}>Cancelar</button><button className="button primary" disabled={saveSession.isPending}>{saveSession.isPending ? 'Guardando…' : 'Guardar en borrador'}</button></div></form></div>}
    </div>
  )
}

function SessionGuide({ detail, session, sex, edit }: { detail: TrainingPlanDetailResponse; session: PlannedSessionResponse; sex: string; edit?: () => void }) {
  return (
    <section className="session-guide" aria-labelledby="session-title">
      <header className="session-hero">
        <div><span className="section-label">{fullDate(session.scheduledDate)}</span><h2 id="session-title">{sessionTypeLabel(session.sessionType)}</h2><p>{session.objective}</p></div>
        <div className="session-metrics"><span><small>Duración</small><strong>{minutes(session.durationSeconds)}</strong></span><span><small>Esfuerzo</small><strong>{rpe(session.targetRpeMin, session.targetRpeMax)}</strong></span>{edit && <button className="button secondary" type="button" onClick={edit}>Ajustar sesión</button>}</div>
      </header>

      <div className="session-overview">
        <InfoStep label="Calentamiento" value={session.warmup} />
        <InfoStep label="Bloque principal" value={session.mainSet} />
        <InfoStep label="Recuperaciones" value={session.recoveries} />
        <InfoStep label="Vuelta a la calma" value={session.cooldown} />
      </div>

      {session.blocks.length > 0 && <div className="plan-blocks">{session.blocks.map((block) => <article className="plan-block" key={block.id}><header><span className="block-index">{String(block.position).padStart(2, '0')}</span><div><span className="section-label">{blockTypeLabel(block.blockType)} · {Number(block.repeatCount) > 1 ? `${block.repeatCount} vueltas` : '1 vuelta'}</span><p>{block.instructions}</p></div></header><div className="planned-exercises">{block.exercises.map((planned) => <PlannedExercise key={planned.id} planned={planned} sex={sex} />)}</div></article>)}</div>}

      <footer className="immutable-note"><strong>Referencia estable</strong><span>Esta guía pertenece a {detail.name}, versión {detail.version.versionNumber}. {detail.version.status === 'draft' ? 'Aún puede ajustarse antes de publicar.' : 'Su contenido publicado no puede editarse.'}</span></footer>
    </section>
  )
}

function PlannedExercise({ planned, sex }: { planned: PlannedExerciseResponse; sex: string }) {
  const media = selectExerciseMedia(planned.exercise.revision.media, sex)
  return (
    <article className="planned-exercise">
      <div className="planned-visual">{media ? <img src={media.assetUri} alt={media.altText} width={media.widthPx} height={media.heightPx} loading="lazy" /> : <span aria-hidden="true">{planned.exercise.revision.displayName.charAt(0)}</span>}</div>
      <div><div className="exercise-meta"><span>{dosage(planned)}</span><span>RPE {planned.targetRpe ?? '—'}</span></div><h3>{planned.exercise.revision.displayName}</h3><p>{planned.exercise.revision.execution}</p><details><summary>Preparación y seguridad</summary><p>{planned.exercise.revision.setup}</p><p><strong>Seguridad:</strong> {planned.exercise.revision.safetyCues}</p></details>{planned.note && <small className="coach-note">{planned.note}</small>}</div>
    </article>
  )
}

function InfoStep({ label, value }: { label: string; value: string | null }) {
  if (!value) return null
  return <div><span>{label}</span><p>{value}</p></div>
}

function dosage(planned: PlannedExerciseResponse) {
  if (planned.durationSeconds != null) return `${planned.sets ?? 1} × ${Number(planned.durationSeconds)} s${planned.side === 'each' ? ' por lado' : ''}`
  const repetitions = planned.repetitionsMin === planned.repetitionsMax ? planned.repetitionsMin : `${planned.repetitionsMin}–${planned.repetitionsMax}`
  return `${planned.sets ?? 1} × ${repetitions ?? '—'} rep`
}

function minutes(value: number | string | null) {
  return value == null ? '—' : `${Math.round(Number(value) / 60)} min`
}

function rpe(min: number | string | null, max: number | string | null) {
  if (min == null && max == null) return '—'
  return min === max || max == null ? `RPE ${min}` : `RPE ${min}–${max}`
}

function localDateKey() {
  const date = new Date()
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function parseDate(value: string) { return new Date(`${value}T00:00:00`) }
function weekday(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'short' }).format(parseDate(value)).replace('.', '') }
function dayNumber(value: string) { return parseDate(value).getDate() }
function fullDate(value: string) { return new Intl.DateTimeFormat('es', { weekday: 'long', day: 'numeric', month: 'long' }).format(parseDate(value)) }
function formatPeriod(start: string, end: string) { return `${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(start))} – ${new Intl.DateTimeFormat('es', { day: 'numeric', month: 'short' }).format(parseDate(end))}` }
function versionStatusLabel(value: string) { return ({ published: 'Publicada', draft: 'Borrador', superseded: 'Sustituida', archived: 'Archivada' } as Record<string, string>)[value] ?? value }
function blockTypeLabel(value: string) { return ({ warmup: 'Calentamiento', main: 'Principal', cooldown: 'Vuelta a la calma', circuit: 'Circuito', mobility: 'Movilidad' } as Record<string, string>)[value] ?? value }
function sessionTypeLabel(value: string) { return ({ strength_mobility_plyometrics: 'Fuerza, movilidad y pliometría', easy_run: 'Carrera fácil', long_run: 'Carrera larga' } as Record<string, string>)[value] ?? value.replaceAll('_', ' ') }
