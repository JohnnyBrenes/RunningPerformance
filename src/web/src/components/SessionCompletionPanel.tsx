import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import {
  SessionsService,
  type SaveSessionCheckinRequest,
  type SessionActivityResponse,
  type SessionCompletionResponse,
  type PlannedSessionResponse,
} from '../api/generated'
import { ErrorState, LoadingState } from './States'
import { readableApiError } from '../lib/api'

const executionStatuses = [
  ['completed_as_planned', 'Completada según plan'],
  ['completed_modified', 'Modificada'],
  ['valid_substitution', 'Sustitución válida'],
  ['not_completed', 'No realizada'],
  ['optional_not_completed', 'Opcional no realizada'],
] as const

const checkinWindows = [
  ['immediate', 'Al terminar'],
  ['24h', '24 h'],
  ['48h', '48 h'],
] as const

type CheckinDraft = {
  sessionRpe: string
  pain: string
  painLocation: string
  gaitChanged: string
  fatigue: string
  sleepQuality: string
  perceivedRecovery: string
  hasIllnessOrSymptom: string
  symptomNote: string
  recoveryResponse: string
  note: string
}

const emptyCheckin: CheckinDraft = {
  sessionRpe: '', pain: '', painLocation: '', gaitChanged: '', fatigue: '',
  sleepQuality: '', perceivedRecovery: '', hasIllnessOrSymptom: '', symptomNote: '',
  recoveryResponse: '', note: '',
}

export function SessionCompletionPanel({ session, planVersionStatus }: {
  session: PlannedSessionResponse
  planVersionStatus: string
}) {
  const queryClient = useQueryClient()
  const [captureOpen, setCaptureOpen] = useState(false)
  const [executionStatus, setExecutionStatus] = useState('completed_as_planned')
  const [reason, setReason] = useState('')
  const [checkinWindow, setCheckinWindow] = useState('immediate')
  const [checkin, setCheckin] = useState<CheckinDraft>(emptyCheckin)
  const queryKey = useMemo(() => ['session-completion', session.id], [session.id])
  const completion = useQuery({
    queryKey,
    queryFn: () => SessionsService.getSessionCompletion({ sessionId: session.id }),
    enabled: planVersionStatus !== 'draft',
  })

  useEffect(() => {
    setExecutionStatus(completion.data?.outcome?.executionStatus ?? 'completed_as_planned')
    setReason(completion.data?.outcome?.reason ?? '')
  }, [completion.data?.outcome])

  useEffect(() => {
    const saved = completion.data?.checkins.find((item) => item.checkinWindow === checkinWindow)
    setCheckin(saved ? {
      sessionRpe: value(saved.sessionRpe),
      pain: value(saved.pain),
      painLocation: saved.painLocation ?? '',
      gaitChanged: booleanValue(saved.gaitChanged),
      fatigue: value(saved.fatigue),
      sleepQuality: value(saved.sleepQuality),
      perceivedRecovery: value(saved.perceivedRecovery),
      hasIllnessOrSymptom: booleanValue(saved.hasIllnessOrSymptom),
      symptomNote: saved.symptomNote ?? '',
      recoveryResponse: saved.recoveryResponse ?? '',
      note: saved.note ?? '',
    } : emptyCheckin)
  }, [checkinWindow, completion.data?.checkins])

  const store = (data: SessionCompletionResponse) => {
    queryClient.setQueryData(queryKey, data)
  }
  const propose = useMutation({
    mutationFn: () => SessionsService.createAutomaticSessionLinkProposal({ sessionId: session.id }),
    onSuccess: store,
  })
  const link = useMutation({
    mutationFn: (activityId: string) => SessionsService.linkSessionActivity({
      sessionId: session.id,
      requestBody: { activityId },
    }),
    onSuccess: store,
  })
  const changeLink = useMutation({
    mutationFn: ({ linkId, status }: { linkId: string; status: string }) =>
      SessionsService.changeSessionActivityLink({
        sessionId: session.id,
        linkId,
        requestBody: { status },
      }),
    onSuccess: store,
  })
  const saveOutcome = useMutation({
    mutationFn: () => SessionsService.savePlannedSessionOutcome({
      sessionId: session.id,
      requestBody: { executionStatus, reason: reason.trim() || null },
    }),
    onSuccess: store,
  })
  const saveCheckin = useMutation({
    mutationFn: () => SessionsService.saveSessionCheckin({
      sessionId: session.id,
      checkinWindow,
      requestBody: toCheckinRequest(checkin, checkinWindow),
    }),
    onSuccess: store,
  })
  const mutationError = propose.error ?? link.error ?? changeLink.error ?? saveOutcome.error ?? saveCheckin.error

  if (planVersionStatus === 'draft') {
    return <section id="completion" className="completion-panel draft-capture-note"><span className="section-label">Ejecución</span><p>La captura se habilita cuando esta versión se publique.</p></section>
  }
  if (completion.isPending) return <section id="completion" className="completion-panel"><LoadingState label="Cargando ejecución" /></section>
  if (completion.isError) return <section id="completion" className="completion-panel"><ErrorState message={readableApiError(completion.error)} retry={() => void completion.refetch()} /></section>

  const data = completion.data
  const activeLinks = data.links.filter((item) => item.status === 'proposed' || item.status === 'confirmed')
  const history = data.links.filter((item) => item.status === 'withdrawn' || item.status === 'rejected')
  const availableCandidates = data.candidates.filter((candidate) =>
    !activeLinks.some((item) => item.activity.id === candidate.activity.id))

  return (
    <section id="completion" className="completion-panel" aria-labelledby="completion-title">
      <header className="completion-heading">
        <div><span className="section-label">Planificado vs. realizado</span><h2 id="completion-title">Sesión lógica</h2><p>Varias actividades pueden formar una sola sesión y una sola carga.</p></div>
        <button className="button primary quick-capture" type="button" onClick={() => setCaptureOpen((open) => !open)}>{captureOpen ? 'Cerrar captura' : 'Registrar entrenamiento'}</button>
      </header>

      <div className="logical-load" aria-label="Resumen realizado">
        <Metric label="Actividades" value={String(data.load.activityCount)} />
        <Metric label="Distancia" value={distance(data.load.distanceM)} />
        <Metric label="Duración" value={duration(data.load.durationSeconds)} />
        <Metric label="RPE" value={data.load.sessionRpe == null ? 'ND' : String(data.load.sessionRpe)} />
        <Metric label="sRPE" value={data.load.srpeLoad == null ? 'ND' : `${data.load.srpeLoad} UA`} />
      </div>
      <p className="rpe-help"><strong>RPE</strong>: percepción del esfuerzo, de 1 (muy fácil) a 10 (máximo). <strong>sRPE</strong>: duración en minutos × RPE.</p>

      <div className="linked-activities">
        <div className="subheading"><h3>Actividades relacionadas</h3><button className="text-button" type="button" disabled={propose.isPending} onClick={() => propose.mutate()}>Proponer coincidencia única</button></div>
        {activeLinks.length === 0 ? <p className="capture-empty">Aún no hay actividades vinculadas. Puedes confirmar el resultado incluso si la sesión no tuvo archivo.</p> : activeLinks.map((item) => (
          <article className="activity-link" key={item.id}>
            <ActivityCopy activity={item.activity} />
            <div className="link-actions"><span className={`link-status ${item.status}`}>{item.status === 'confirmed' ? 'Confirmada' : 'Propuesta'}</span>{item.status === 'proposed' && <><button type="button" onClick={() => changeLink.mutate({ linkId: item.id, status: 'confirmed' })}>Confirmar</button><button type="button" onClick={() => changeLink.mutate({ linkId: item.id, status: 'rejected' })}>Rechazar</button></>} {item.status === 'confirmed' && <button type="button" onClick={() => changeLink.mutate({ linkId: item.id, status: 'withdrawn' })}>Retirar</button>}</div>
          </article>
        ))}
      </div>

      {captureOpen && <div className="capture-workspace">
        <section className="capture-section" aria-labelledby="activity-candidates-title">
          <div className="subheading"><h3 id="activity-candidates-title">Agregar actividad</h3><small>±2 días de la sesión</small></div>
          {availableCandidates.length === 0 ? <p className="capture-empty">No hay otras actividades candidatas.</p> : <div className="candidate-list">{availableCandidates.map((candidate) => (
            <button className="candidate-button" type="button" key={candidate.activity.id} disabled={link.isPending} onClick={() => link.mutate(candidate.activity.id)}>
              <ActivityCopy activity={candidate.activity} />
              <span>{candidate.activePlannedSessionId ? 'Mover aquí' : 'Vincular'}{candidate.isExactMatch ? ' · exacta' : ''}</span>
            </button>
          ))}</div>}
        </section>

        <form className="capture-section outcome-form" onSubmit={(event) => { event.preventDefault(); saveOutcome.mutate() }}>
          <div className="subheading"><h3>Resultado TRN-003</h3><small>Cuenta una sola vez en P1</small></div>
          <label>Estado<select value={executionStatus} onChange={(event) => setExecutionStatus(event.target.value)}>{executionStatuses.map(([code, label]) => <option key={code} value={code} disabled={code === 'optional_not_completed' && data.obligation !== 'optional'}>{label}</option>)}</select></label>
          {executionStatus !== 'completed_as_planned' && executionStatus !== 'optional_not_completed' && <label>Motivo<textarea rows={3} maxLength={2000} required value={reason} onChange={(event) => setReason(event.target.value)} /></label>}
          <div className="form-actions"><button className="button secondary" disabled={saveOutcome.isPending}>{saveOutcome.isPending ? 'Guardando…' : 'Confirmar resultado'}</button></div>
        </form>

        <form className="capture-section checkin-form" onSubmit={(event) => { event.preventDefault(); saveCheckin.mutate() }}>
          <div className="subheading"><h3>Check-in</h3><small>ND permanece vacío</small></div>
          <div className="checkin-tabs" role="tablist" aria-label="Ventana del check-in">{checkinWindows.map(([code, label]) => <button type="button" role="tab" aria-selected={checkinWindow === code} className={checkinWindow === code ? 'active' : ''} key={code} onClick={() => setCheckinWindow(code)}>{label}</button>)}</div>
          <div className="checkin-grid">
            {checkinWindow === 'immediate' && <label>RPE global <small>Percepción del esfuerzo, 1–10</small><input aria-label="RPE global" inputMode="decimal" type="number" min="1" max="10" step="0.5" value={checkin.sessionRpe} onChange={(event) => update(setCheckin, 'sessionRpe', event.target.value)} /></label>}
            <label>Dolor máximo<input inputMode="decimal" type="number" min="0" max="10" step="0.5" value={checkin.pain} onChange={(event) => update(setCheckin, 'pain', event.target.value)} /></label>
            <label>Ubicación<input maxLength={120} value={checkin.painLocation} onChange={(event) => update(setCheckin, 'painLocation', event.target.value)} /></label>
            <label>¿Cambió la zancada?<select value={checkin.gaitChanged} onChange={(event) => update(setCheckin, 'gaitChanged', event.target.value)}><option value="">ND</option><option value="false">No</option><option value="true">Sí</option></select></label>
            <label>Fatiga<input inputMode="decimal" type="number" min="0" max="10" step="0.5" value={checkin.fatigue} onChange={(event) => update(setCheckin, 'fatigue', event.target.value)} /></label>
            <label>Calidad de sueño<input inputMode="decimal" type="number" min="1" max="5" step="0.5" value={checkin.sleepQuality} onChange={(event) => update(setCheckin, 'sleepQuality', event.target.value)} /></label>
            <label>Recuperación percibida<input inputMode="decimal" type="number" min="0" max="10" step="0.5" value={checkin.perceivedRecovery} onChange={(event) => update(setCheckin, 'perceivedRecovery', event.target.value)} /></label>
            <label>¿Enfermedad o síntoma?<select value={checkin.hasIllnessOrSymptom} onChange={(event) => update(setCheckin, 'hasIllnessOrSymptom', event.target.value)}><option value="">ND</option><option value="false">No</option><option value="true">Sí</option></select></label>
            {checkinWindow !== 'immediate' && <label>Respuesta posterior<select value={checkin.recoveryResponse} onChange={(event) => update(setCheckin, 'recoveryResponse', event.target.value)}><option value="">ND</option><option value="normal">Normal</option><option value="incomplete">Incompleta</option><option value="adverse">Adversa</option></select></label>}
          </div>
          <label>Detalle de síntomas<input maxLength={500} value={checkin.symptomNote} onChange={(event) => update(setCheckin, 'symptomNote', event.target.value)} /></label>
          <label>Nota<textarea rows={3} maxLength={2000} value={checkin.note} onChange={(event) => update(setCheckin, 'note', event.target.value)} /></label>
          <div className="form-actions"><button className="button primary" disabled={saveCheckin.isPending}>{saveCheckin.isPending ? 'Guardando…' : `Guardar check-in ${checkinWindows.find(([code]) => code === checkinWindow)?.[1]}`}</button></div>
        </form>
      </div>}

      {mutationError && <p className="form-alert" role="alert">{readableApiError(mutationError)}</p>}
      {history.length > 0 && <details className="link-history"><summary>Historial de vínculos ({history.length})</summary><ul>{history.map((item) => <li key={item.id}>{activityName(item.activity)} · {item.status === 'withdrawn' ? 'retirado' : 'rechazado'}</li>)}</ul></details>}
    </section>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return <div><span>{label}</span><strong>{value}</strong></div>
}

function ActivityCopy({ activity }: { activity: SessionActivityResponse }) {
  return <span className="activity-copy"><strong>{activityName(activity)}</strong><small>{formatDateTime(activity.startedAtLocal)} · {distance(activity.distanceM)} · {duration(activity.durationSeconds)}</small></span>
}

function activityName(activity: SessionActivityResponse) {
  return activity.title || activity.activityType.replaceAll('_', ' ')
}

export function toCheckinRequest(draft: CheckinDraft, window: string): SaveSessionCheckinRequest {
  return {
    sessionRpe: window === 'immediate' ? numberOrNull(draft.sessionRpe) : null,
    pain: numberOrNull(draft.pain),
    painLocation: textOrNull(draft.painLocation),
    gaitChanged: booleanOrNull(draft.gaitChanged),
    fatigue: numberOrNull(draft.fatigue),
    sleepQuality: numberOrNull(draft.sleepQuality),
    perceivedRecovery: numberOrNull(draft.perceivedRecovery),
    hasIllnessOrSymptom: booleanOrNull(draft.hasIllnessOrSymptom),
    symptomNote: textOrNull(draft.symptomNote),
    recoveryResponse: window === 'immediate' ? null : textOrNull(draft.recoveryResponse),
    note: textOrNull(draft.note),
  }
}

function update(setter: React.Dispatch<React.SetStateAction<CheckinDraft>>, key: keyof CheckinDraft, next: string) {
  setter((current) => ({ ...current, [key]: next }))
}

function numberOrNull(value: string) { return value.trim() === '' ? null : Number(value) }
function booleanOrNull(value: string) { return value === '' ? null : value === 'true' }
function textOrNull(value: string) { return value.trim() || null }
function value(input: number | string | null) { return input == null ? '' : String(input) }
function booleanValue(input: boolean | null) { return input == null ? '' : String(input) }
function distance(input: number | string | null) { return input == null || Number(input) === 0 ? '—' : `${(Number(input) / 1000).toFixed(2)} km` }
function duration(input: number | string | null) { return input == null || Number(input) === 0 ? '—' : `${Math.round(Number(input) / 60)} min` }
function formatDateTime(input: string) { return new Intl.DateTimeFormat('es', { weekday: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(input)) }
