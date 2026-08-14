import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { RacesService, type CreateRaceGoalRequest, type SaveTargetRaceRequest, type TargetRaceResponse } from '../api/generated'
import { EmptyState, ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'

type RaceForm = Omit<SaveTargetRaceRequest, 'distanceM'> & { distanceM: string }
type GoalForm = { goalTime: string; confidence: string; rationale: string }

const preferredTimeZones = ['America/Mexico_City', 'America/Los_Angeles', 'America/Tijuana', 'America/Monterrey', 'America/Cancun', 'UTC']
const supportedTimeZones = (Intl as typeof Intl & { supportedValuesOf?: (key: 'timeZone') => string[] })
  .supportedValuesOf?.('timeZone') ?? []
const timeZones = [...preferredTimeZones, ...supportedTimeZones.filter((zone) => !preferredTimeZones.includes(zone))]
const timeZoneNames: Record<string, string> = {
  'America/Mexico_City': 'Ciudad de México',
  'America/Los_Angeles': 'Pacífico · San Diego/Los Ángeles',
  'America/Tijuana': 'Tijuana',
  'America/Monterrey': 'Monterrey',
  'America/Cancun': 'Cancún',
  UTC: 'UTC',
}

function timeZoneLabel(zone: string) {
  return timeZoneNames[zone] ?? zone.replaceAll('_', ' ').replace('/', ' · ')
}

function localTimeZone() {
  const detected = Intl.DateTimeFormat().resolvedOptions().timeZone
  return timeZones.includes(detected) ? detected : 'America/Mexico_City'
}

const raceDefaults: RaceForm = { name: '', raceDate: '', distanceM: '10000', location: '', priority: 'A', status: 'planned', timezoneName: localTimeZone() }
const goalDefaults: GoalForm = { goalTime: '', confidence: 'medium', rationale: '' }

export function secondsFromClock(value: string): number | null {
  if (!value) return null
  const parts = value.split(':').map(Number)
  if (parts.some(Number.isNaN) || parts.length !== 3) return null
  const [hours, minutes, seconds] = parts
  if (hours < 0 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59) return null
  return hours * 3600 + minutes * 60 + seconds
}

function clockFromSeconds(value: number | string | null | undefined): string {
  if (value == null) return '—'
  const seconds = Math.round(Number(value))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}` : `${minutes}:${String(rest).padStart(2, '0')}`
}

export function goalTimeInputFromSeconds(value: number | string | null | undefined): string {
  if (value == null || !Number.isFinite(Number(value))) return ''
  const seconds = Math.round(Number(value))
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.floor((seconds % 3600) / 60)
  const rest = seconds % 60
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(rest).padStart(2, '0')}`
}

export function RacesPage() {
  const queryClient = useQueryClient()
  const [editingRace, setEditingRace] = useState<TargetRaceResponse | 'new' | null>(null)
  const [goalRace, setGoalRace] = useState<TargetRaceResponse | null>(null)
  const [historyRace, setHistoryRace] = useState<TargetRaceResponse | null>(null)
  const races = useQuery({ queryKey: ['races'], queryFn: () => RacesService.getRaces() })
  const history = useQuery({ queryKey: ['race-goals', historyRace?.id], queryFn: () => RacesService.getRaceGoals({ id: historyRace!.id }), enabled: Boolean(historyRace) })
  const raceForm = useForm<RaceForm>({ defaultValues: raceDefaults })
  const goalForm = useForm<GoalForm>({ defaultValues: goalDefaults })
  const watchedGoalTime = goalForm.watch('goalTime')
  const calculatedGoalPace = goalRace
    ? calculatePace(watchedGoalTime, Number(goalRace.distanceM))
    : null

  const saveRace = useMutation({
    mutationFn: (values: RaceForm) => {
      const requestBody = cleanRace(values)
      return editingRace === 'new' ? RacesService.createRace({ requestBody }) : RacesService.updateRace({ id: editingRace!.id, requestBody })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['races'] })
      setEditingRace(null)
    },
  })

  const saveGoal = useMutation({
    mutationFn: (values: GoalForm) => {
      const requestBody: CreateRaceGoalRequest = {
        goalTimeSeconds: secondsFromClock(values.goalTime),
        goalPaceSecondsPerKm: calculatePace(values.goalTime, Number(goalRace!.distanceM)),
        confidence: values.confidence || null,
        rationale: values.rationale,
      }
      return RacesService.createRaceGoal({ id: goalRace!.id, requestBody })
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['races'] })
      await queryClient.invalidateQueries({ queryKey: ['race-goals'] })
      setGoalRace(null)
    },
  })

  const openRace = (race: TargetRaceResponse | 'new') => {
    setEditingRace(race)
    raceForm.reset(race === 'new' ? raceDefaults : {
      name: race.name, raceDate: race.raceDate, distanceM: race.distanceM.toString(), location: race.location ?? '', priority: race.priority, status: race.status, timezoneName: race.timezoneName ?? '',
    })
  }

  const openGoal = (race: TargetRaceResponse) => {
    setGoalRace(race)
    goalForm.reset({
      goalTime: goalTimeInputFromSeconds(race.currentGoal?.goalTimeSeconds),
      confidence: race.currentGoal?.confidence ?? 'medium',
      rationale: '',
    })
  }

  if (races.isPending) return <LoadingState label="Cargando carreras" />
  if (races.isError) return <ErrorState message={readableApiError(races.error)} retry={() => void races.refetch()} />

  return (
    <div className="page">
      <header className="page-heading split-heading"><div><p className="eyebrow">Horizonte</p><h1>Carreras</h1><p>Objetivos claros, con un historial que no se reescribe.</p></div><button className="button primary" type="button" onClick={() => openRace('new')}>Nueva carrera</button></header>

      {editingRace && (
        <form className="form-card form-grid" onSubmit={raceForm.handleSubmit((values) => saveRace.mutate(values))}>
          <div className="form-title span-2"><div><span className="section-label">{editingRace === 'new' ? 'Nuevo objetivo' : 'Editar carrera'}</span><h2>{editingRace === 'new' ? 'Añade una carrera' : editingRace.name}</h2></div><button className="icon-button" type="button" aria-label="Cerrar" onClick={() => setEditingRace(null)}>×</button></div>
          <label className="span-2">Nombre<input {...raceForm.register('name', { required: true, maxLength: 160 })} /></label>
          <label>Fecha<input type="date" {...raceForm.register('raceDate', { required: true })} /></label>
          <label>Distancia (m)<input type="number" min="100" max="500000" step="1" {...raceForm.register('distanceM', { required: true })} /></label>
          <label>Ciudad o sede (opcional)<input maxLength={160} placeholder="Ej. Toluca, Estado de México" {...raceForm.register('location', { maxLength: 160 })} /></label>
          <label>Zona horaria<select {...raceForm.register('timezoneName', { required: true })}>{timeZones.map((zone) => <option key={zone} value={zone}>{timeZoneLabel(zone)}</option>)}</select></label>
          <label>Prioridad<select {...raceForm.register('priority')}><option value="A">A · Principal</option><option value="B">B · Importante</option><option value="C">C · Preparación</option></select></label>
          <label>Estado<select {...raceForm.register('status')}><option value="planned">Planeada</option><option value="completed">Completada</option><option value="cancelled">Cancelada</option></select></label>
          {saveRace.isError && <p className="form-alert span-2">{readableApiError(saveRace.error)}</p>}
          <div className="form-actions span-2"><button className="button ghost" type="button" onClick={() => setEditingRace(null)}>Cancelar</button><button className="button primary" disabled={saveRace.isPending}>{saveRace.isPending ? 'Guardando…' : 'Guardar carrera'}</button></div>
        </form>
      )}

      {goalRace && (
        <form className="form-card goal-form form-grid" onSubmit={goalForm.handleSubmit((values) => saveGoal.mutate(values))}>
          <div className="form-title span-2"><div><span className="section-label">Nueva versión de meta</span><h2>{goalRace.name}</h2></div><button className="icon-button" type="button" aria-label="Cerrar" onClick={() => setGoalRace(null)}>×</button></div>
          <label>Tiempo objetivo (h:mm:ss)<input placeholder="00:48:00" pattern="[0-9]{1,3}:[0-5][0-9]:[0-5][0-9]" title="Usa horas:minutos:segundos, por ejemplo 01:45:00" {...goalForm.register('goalTime', { required: true })} /></label>
          <label>Ritmo calculado<input aria-label="Ritmo calculado" value={clockFromSeconds(calculatedGoalPace)} readOnly /></label>
          <label>Confianza<select {...goalForm.register('confidence')}><option value="low">Baja</option><option value="medium">Media</option><option value="high">Alta</option></select></label>
          <label className="span-2">Por qué cambia esta meta<textarea rows={3} {...goalForm.register('rationale', { required: true, maxLength: 2000 })} /></label>
          {saveGoal.isError && <p className="form-alert span-2">{readableApiError(saveGoal.error)}</p>}
          <div className="form-actions span-2"><button className="button ghost" type="button" onClick={() => setGoalRace(null)}>Cancelar</button><button className="button primary" disabled={saveGoal.isPending}>Guardar nueva versión</button></div>
        </form>
      )}

      {!editingRace && !goalRace && (races.data.length === 0 ? <EmptyState title="Ninguna carrera todavía">Crea tu primer objetivo para empezar a construir el horizonte.</EmptyState> : (
        <div className="race-list">
          {races.data.map((race) => (
            <article className="race-card" key={race.id}>
              <div className="race-date"><strong>{new Date(`${race.raceDate}T00:00:00`).getDate()}</strong><span>{new Intl.DateTimeFormat('es', { month: 'short' }).format(new Date(`${race.raceDate}T00:00:00`))}</span></div>
              <div className="race-main"><div className="race-title-line"><div><span className="tag">Prioridad {race.priority}</span><h2>{race.name}</h2><p>{race.location || 'Ubicación por confirmar'} · {(Number(race.distanceM) / 1000).toLocaleString('es')} km</p></div><span className={`race-status ${race.status}`}>{race.status === 'planned' ? 'Planeada' : race.status === 'completed' ? 'Completada' : 'Cancelada'}</span></div>
                <div className="goal-strip"><div><span>Tiempo meta</span><strong>{clockFromSeconds(race.currentGoal?.goalTimeSeconds)}</strong></div><div><span>Ritmo</span><strong>{clockFromSeconds(race.currentGoal?.goalPaceSecondsPerKm)} /km</strong></div><div><span>Versión</span><strong>{race.currentGoal ? `v${race.currentGoal.versionNumber}` : '—'}</strong></div></div>
                <div className="race-actions"><button type="button" onClick={() => openGoal(race)}>{race.currentGoal ? 'Revisar meta' : 'Definir meta'}</button><button type="button" onClick={() => setHistoryRace(race)}>Historial</button><button type="button" onClick={() => openRace(race)}>Editar</button></div>
              </div>
            </article>
          ))}
        </div>
      ))}

      {historyRace && <div className="drawer-backdrop" role="presentation" onMouseDown={() => setHistoryRace(null)}><section className="history-drawer" role="dialog" aria-modal="true" aria-labelledby="history-title" onMouseDown={(event) => event.stopPropagation()}><div className="form-title"><div><span className="section-label">Evolución</span><h2 id="history-title">Historial de meta</h2><p>{historyRace.name}</p></div><button className="icon-button" type="button" aria-label="Cerrar" onClick={() => setHistoryRace(null)}>×</button></div>{history.isPending ? <LoadingState /> : history.isError ? <ErrorState message={readableApiError(history.error)} /> : history.data.length === 0 ? <EmptyState title="Sin versiones">La primera meta aparecerá aquí.</EmptyState> : <ol className="version-list">{history.data.map((goal, index) => <li key={goal.id}><span className="version-number">v{goal.versionNumber}</span><div><strong>{clockFromSeconds(goal.goalTimeSeconds)} · {clockFromSeconds(goal.goalPaceSecondsPerKm)} /km</strong><p>{goal.rationale}</p><small>{new Intl.DateTimeFormat('es', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(goal.effectiveAt))}{index === 0 ? ' · Actual' : ''}</small></div></li>)}</ol>}</section></div>}
    </div>
  )
}

function cleanRace(values: RaceForm): SaveTargetRaceRequest {
  return { ...values, distanceM: values.distanceM, location: values.location?.trim() || null, timezoneName: values.timezoneName }
}

export function calculatePace(time: string, distanceM: number): number | null {
  const totalSeconds = secondsFromClock(time)
  if (totalSeconds == null || !Number.isFinite(distanceM) || distanceM <= 0) return null
  return totalSeconds / (distanceM / 1000)
}
