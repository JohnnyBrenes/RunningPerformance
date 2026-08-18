import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ActivitiesService, IngestionService } from '../api/generated'
import type { ActivityDetailResponse } from '../api/generated'
import { ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'
import { formatPace } from '../lib/dashboard'
import { buildPlannedComparison } from '../lib/plannedComparison'
import type { PlannedComparisonRow, RpeStatus } from '../lib/plannedComparison'

type Period = 'all' | 'month' | 'quarter' | 'semester' | 'year' | 'custom'

function duration(value: number | string | null): string {
  if (value == null) return 'ND'
  return `${Math.round(Number(value) / 60)} min`
}

function distance(value: number | string | null): string {
  if (value == null) return 'ND'
  return `${(Number(value) / 1000).toFixed(2)} km`
}

function dateKey(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`
}

function rollingDates(period: Period): { from?: string; to?: string } {
  if (period === 'all' || period === 'custom') return {}
  const to = new Date()
  const from = new Date(to)
  const months = { month: 1, quarter: 3, semester: 6, year: 12 }[period]
  from.setMonth(from.getMonth() - months)
  return { from: dateKey(from), to: dateKey(to) }
}

function meters(value: string): number | undefined {
  if (!value.trim()) return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed * 1000 : undefined
}

export function ActivitiesPage() {
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [period, setPeriod] = useState<Period>('all')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [modality, setModality] = useState('')
  const [minDistanceKm, setMinDistanceKm] = useState('')
  const [maxDistanceKm, setMaxDistanceKm] = useState('')
  const [sort, setSort] = useState('startedAt')
  const [direction, setDirection] = useState('desc')
  const [fitFile, setFitFile] = useState<File | null>(null)
  const [garminActivityId, setGarminActivityId] = useState('')
  const [ingestionRunId, setIngestionRunId] = useState<string | null>(null)
  const selectedId = params.get('activity')
  const preset = rollingDates(period)
  const effectiveFrom = period === 'custom' ? from || undefined : preset.from
  const effectiveTo = period === 'custom' ? to || undefined : preset.to
  const activities = useQuery({
    queryKey: ['activities', page, period, effectiveFrom, effectiveTo, modality, minDistanceKm, maxDistanceKm, sort, direction],
    queryFn: () => ActivitiesService.getActivities({
      page,
      pageSize: 25,
      category: 'running',
      modality: modality || undefined,
      from: effectiveFrom,
      to: effectiveTo,
      minDistanceM: meters(minDistanceKm),
      maxDistanceM: meters(maxDistanceKm),
      sort,
      direction,
    }),
  })
  const selected = useQuery({
    queryKey: ['activity', selectedId],
    queryFn: () => ActivitiesService.getActivity({ id: selectedId! }),
    enabled: selectedId != null,
  })
  const fitImport = useMutation({
    mutationFn: () => IngestionService.enqueueFit({
      requestBody: fitFile!,
      fileName: fitFile!.name,
      garminActivityId: garminActivityId.trim() || undefined,
    }),
    onSuccess: (accepted) => setIngestionRunId(accepted.runId),
  })
  const ingestionRun = useQuery({
    queryKey: ['ingestion-run', ingestionRunId],
    queryFn: () => IngestionService.getIngestionRun({ id: ingestionRunId! }),
    enabled: Boolean(ingestionRunId),
    refetchInterval: (query) => ['queued', 'running', 'pending'].includes(query.state.data?.status ?? '') ? 2_000 : false,
  })

  useEffect(() => {
    if (ingestionRun.data?.status !== 'succeeded') return
    void Promise.all([
      queryClient.invalidateQueries({ queryKey: ['activities'] }),
      queryClient.invalidateQueries({ queryKey: ['calendar-activities'] }),
      queryClient.invalidateQueries({ queryKey: ['dashboard'] }),
    ])
  }, [ingestionRun.data?.status, queryClient])

  const totalPages = useMemo(
    () => activities.data ? Math.max(1, Math.ceil(Number(activities.data.total) / Number(activities.data.pageSize))) : 1,
    [activities.data],
  )

  const changeFilter = (change: () => void) => { change(); setPage(1) }
  const clearFilters = () => {
    setPeriod('all'); setFrom(''); setTo(''); setModality(''); setMinDistanceKm(''); setMaxDistanceKm(''); setSort('startedAt'); setDirection('desc'); setPage(1)
  }

  return <div className="page activities-page">
    <header className="page-heading"><p className="eyebrow">Historial</p><h1>Actividades</h1><p>Consulta, filtra y abre cada entrenamiento realizado.</p></header>

    <section className="activity-controls" aria-labelledby="activity-filters-title">
      <div className="section-heading"><div><span className="section-label">Búsqueda</span><h2 id="activity-filters-title">Filtrar actividades</h2></div><button className="button ghost" type="button" onClick={clearFilters}>Limpiar</button></div>
      <div className="activity-filter-grid">
        <label>Periodo<select value={period} onChange={(event) => changeFilter(() => setPeriod(event.target.value as Period))}><option value="all">Todo el historial</option><option value="month">Último mes</option><option value="quarter">Último trimestre</option><option value="semester">Último semestre</option><option value="year">Último año</option><option value="custom">Rango personalizado</option></select></label>
        {period === 'custom' && <><label>Desde<input type="date" value={from} onChange={(event) => changeFilter(() => setFrom(event.target.value))} /></label><label>Hasta<input type="date" value={to} onChange={(event) => changeFilter(() => setTo(event.target.value))} /></label></>}
        <label>Modalidad<select value={modality} onChange={(event) => changeFilter(() => setModality(event.target.value))}><option value="">Todas</option><option value="treadmill">Caminadora</option><option value="outdoor">Exterior</option></select></label>
        <label>Distancia mínima (km)<input type="number" min="0" step="0.1" value={minDistanceKm} onChange={(event) => changeFilter(() => setMinDistanceKm(event.target.value))} /></label>
        <label>Distancia máxima (km)<input type="number" min="0" step="0.1" value={maxDistanceKm} onChange={(event) => changeFilter(() => setMaxDistanceKm(event.target.value))} /></label>
        <label>Ordenar por<select value={sort} onChange={(event) => changeFilter(() => setSort(event.target.value))}><option value="startedAt">Fecha</option><option value="distance">Distancia</option><option value="duration">Duración</option></select></label>
        <label>Dirección<select value={direction} onChange={(event) => changeFilter(() => setDirection(event.target.value))}><option value="desc">Mayor o más reciente primero</option><option value="asc">Menor o más antigua primero</option></select></label>
      </div>
    </section>

    <details className="fit-import-panel">
      <summary>Importar archivo FIT de Garmin</summary>
      <form onSubmit={(event) => { event.preventDefault(); if (fitFile) fitImport.mutate() }}>
        <label>Archivo FIT<input type="file" accept=".fit,application/vnd.ant.fit" required onChange={(event) => setFitFile(event.target.files?.[0] ?? null)} /></label>
        <label>ID de actividad Garmin (opcional)<input inputMode="numeric" pattern="[0-9]*" value={garminActivityId} onChange={(event) => setGarminActivityId(event.target.value)} /></label>
        <button className="button primary" disabled={!fitFile || fitImport.isPending}>{fitImport.isPending ? 'Enviando…' : 'Importar FIT'}</button>
      </form>
      {fitImport.isError && <p className="form-alert">{readableApiError(fitImport.error)}</p>}
      {ingestionRun.data && <p className="form-success">{ingestionRun.data.status === 'succeeded' ? 'FIT importado; Actividades, Calendario e Inicio ya se actualizaron.' : `Estado: ${ingestionRun.data.status}.`}</p>}
    </details>

    {selectedId && <section className="feature-card activity-detail" aria-labelledby="activity-detail-heading">
      {selected.isPending ? <LoadingState label="Abriendo detalle" /> : selected.isError ? <ErrorState message={readableApiError(selected.error)} /> : selected.data ? <>
        <div className="card-top"><div><span className="section-label">Detalle de actividad</span><h2 id="activity-detail-heading">{selected.data.activity.title ?? selected.data.activity.activityType}</h2></div><button type="button" onClick={() => setParams({})}>Cerrar</button></div>
        <p>{new Date(selected.data.activity.startedAtLocal).toLocaleString('es')} · {selected.data.activity.modality ?? 'modalidad ND'}</p>
        <div className="metric-row compact-metrics"><div><span>Distancia</span><strong>{distance(selected.data.activity.distanceM)}</strong></div><div><span>Duración</span><strong>{duration(selected.data.activity.durationSeconds)}</strong></div><div><span>Ritmo</span><strong>{formatPace(selected.data.activity.averagePaceSecondsPerKm)}</strong></div><div><span>FC media</span><strong>{selected.data.activity.averageHeartRateBpm ?? 'ND'}</strong></div></div>
        <PlannedComparison detail={selected.data} />
        <details><summary>Origen de los datos</summary><ul>{selected.data.sources.map((source, index) => <li key={`${source.id}-${index}`}>{source.sourceClass} · {source.originalName ?? 'archivo ND'} · fila {source.sourceRowNumber ?? 'ND'}</li>)}</ul></details>
      </> : null}
    </section>}

    <section className="feature-card activity-history-card">
      {activities.isPending ? <LoadingState label="Cargando historial" /> : activities.isError ? <ErrorState message={readableApiError(activities.error)} retry={() => void activities.refetch()} /> : <>
        <p className="activity-result-count">{Number(activities.data.total).toLocaleString('es')} actividades encontradas</p>
        <div className="table-scroll"><table className="accessible-table activity-table"><caption>Historial de running; los ausentes permanecen ND.</caption><thead><tr><th>Fecha</th><th>Actividad</th><th>Modalidad</th><th>Distancia</th><th>Duración</th><th>Ritmo</th><th>Detalle</th></tr></thead><tbody>{activities.data.items.map((activity) => <tr key={activity.id}><td>{new Date(activity.startedAtLocal).toLocaleDateString('es')}</td><th>{activity.title ?? activity.activityType}</th><td>{activity.modality ?? 'ND'}</td><td>{distance(activity.distanceM)}</td><td>{duration(activity.durationSeconds)}</td><td>{formatPace(activity.averagePaceSecondsPerKm)}</td><td><button type="button" onClick={() => setParams({ activity: activity.id })}>Ver detalle</button></td></tr>)}</tbody></table></div>
        <div className="pagination"><button type="button" disabled={page === 1} onClick={() => setPage((value) => value - 1)}>Anterior</button><span>Página {page} de {totalPages}</span><button type="button" disabled={page >= totalPages} onClick={() => setPage((value) => value + 1)}>Siguiente</button></div>
      </>}
    </section>
  </div>
}

function PlannedComparison({ detail }: { detail: ActivityDetailResponse }) {
  const context = detail.plannedContext
  const comparison = useMemo(
    () => context == null ? null : buildPlannedComparison(detail.activity, context),
    [detail.activity, context],
  )
  if (context == null || comparison == null) return null

  return <section className="planned-comparison" aria-labelledby="planned-comparison-heading">
    <div className="card-top"><div><span className="section-label">Comparación con el plan</span><h3 id="planned-comparison-heading">{plannedDate(context.scheduledDate)}</h3></div>{context.linkStatus === 'proposed' && <span className="planned-flag">Vínculo propuesto</span>}</div>
    <p className="planned-objective">{context.objective}</p>
    <dl>{comparison.rows.map((row) => <ComparisonRow key={row.metric} row={row} />)}</dl>
    {comparison.activityCount > 1 && <small>Esta sesión reúne {comparison.activityCount} actividades; la comparación usa el total de la sesión, no solo esta actividad.</small>}
    {comparison.rows.some((row) => row.plannedIsDerived) && <small>El ritmo del plan es el que resulta de su distancia y duración; el plan no prescribe un ritmo.</small>}
    {context.executionStatus != null && <small>Resultado registrado: {executionLabels[context.executionStatus] ?? context.executionStatus}.</small>}
  </section>
}

function ComparisonRow({ row }: { row: PlannedComparisonRow }) {
  return <div>
    <dt>{row.label}</dt>
    <dd><strong>{row.actual}</strong><span>plan {row.planned}</span>{row.fulfilmentPercent != null && <em>{row.fulfilmentPercent}% de lo planeado</em>}{row.rpeStatus != null && <em>{rpeStatusLabels[row.rpeStatus]}</em>}</dd>
  </div>
}

const rpeStatusLabels: Record<RpeStatus, string> = {
  below: 'por debajo del rango',
  within: 'dentro del rango',
  above: 'por encima del rango',
}

const executionLabels: Record<string, string> = {
  completed_as_planned: 'completada según el plan',
  completed_modified: 'completada con modificaciones',
  valid_substitution: 'sustitución válida',
  not_completed: 'no realizada',
  optional_not_completed: 'opcional no realizada',
}

function plannedDate(value: string): string {
  const formatted = new Intl.DateTimeFormat('es', { weekday: 'long', day: 'numeric', month: 'long' })
    .format(new Date(`${value}T12:00:00`))
  return formatted.charAt(0).toUpperCase() + formatted.slice(1)
}
