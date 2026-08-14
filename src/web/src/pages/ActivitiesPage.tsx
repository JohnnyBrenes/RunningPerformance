import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { ActivitiesService } from '../api/generated'
import { ErrorState, LoadingState } from '../components/States'
import { readableApiError } from '../lib/api'
import { formatPace } from '../lib/dashboard'

function duration(value: number | string | null): string {
  if (value == null) return 'ND'
  return `${Math.round(Number(value) / 60)} min`
}

function distance(value: number | string | null): string {
  if (value == null) return 'ND'
  return `${(Number(value) / 1000).toFixed(2)} km`
}

export function ActivitiesPage() {
  const [params, setParams] = useSearchParams()
  const [page, setPage] = useState(1)
  const [modality, setModality] = useState('')
  const selectedId = params.get('activity')
  const activities = useQuery({
    queryKey: ['activities', page, modality],
    queryFn: () => ActivitiesService.getActivities({
      page,
      pageSize: 25,
      category: 'running',
      modality: modality || undefined,
      sort: 'startedAt',
      direction: 'desc',
    }),
  })
  const selected = useQuery({
    queryKey: ['activity', selectedId],
    queryFn: () => ActivitiesService.getActivity({ id: selectedId! }),
    enabled: selectedId != null,
  })

  const totalPages = useMemo(
    () => activities.data ? Math.max(1, Math.ceil(Number(activities.data.total) / Number(activities.data.pageSize))) : 1,
    [activities.data],
  )

  if (activities.isPending) return <LoadingState label="Cargando historial" />
  if (activities.isError) return <ErrorState message={readableApiError(activities.error)} retry={() => void activities.refetch()} />

  return <div className="page activities-page">
    <header className="page-heading split-heading">
      <div><p className="eyebrow">Evidencia histórica</p><h1>Actividades</h1><p>{Number(activities.data.total).toLocaleString('es')} carreras disponibles para tendencias.</p></div>
      <label className="activity-filter">Modalidad<select value={modality} onChange={(event) => { setModality(event.target.value); setPage(1) }}><option value="">Todas</option><option value="treadmill">Caminadora</option><option value="outdoor">Exterior</option></select></label>
    </header>

    {selectedId && <section className="feature-card activity-detail" aria-labelledby="activity-detail-heading">
      {selected.isPending ? <LoadingState label="Abriendo evidencia" /> : selected.isError ? <ErrorState message={readableApiError(selected.error)} /> : selected.data ? <>
        <div className="card-top"><div><span className="section-label">Actividad fuente</span><h2 id="activity-detail-heading">{selected.data.activity.title ?? selected.data.activity.activityType}</h2></div><button type="button" onClick={() => setParams({})}>Cerrar</button></div>
        <p>{new Date(selected.data.activity.startedAtLocal).toLocaleString('es')} · {selected.data.activity.modality ?? 'modalidad ND'}</p>
        <div className="metric-row compact-metrics"><div><span>Distancia</span><strong>{distance(selected.data.activity.distanceM)}</strong></div><div><span>Duración</span><strong>{duration(selected.data.activity.durationSeconds)}</strong></div><div><span>Ritmo</span><strong>{formatPace(selected.data.activity.averagePaceSecondsPerKm)}</strong></div><div><span>FC media</span><strong>{selected.data.activity.averageHeartRateBpm ?? 'ND'}</strong></div></div>
        <details><summary>Procedencia</summary><ul>{selected.data.sources.map((source, index) => <li key={`${source.id}-${index}`}>{source.sourceClass} · {source.originalName ?? 'archivo ND'} · fila {source.sourceRowNumber ?? 'ND'}</li>)}</ul></details>
      </> : null}
    </section>}

    <section className="feature-card activity-history-card">
      <div className="table-scroll"><table className="accessible-table activity-table"><caption>Historial de running; los ausentes permanecen ND.</caption><thead><tr><th>Fecha</th><th>Actividad</th><th>Modalidad</th><th>Distancia</th><th>Duración</th><th>Ritmo</th><th>Evidencia</th></tr></thead><tbody>{activities.data.items.map((activity) => <tr key={activity.id}><td>{new Date(activity.startedAtLocal).toLocaleDateString('es')}</td><th>{activity.title ?? activity.activityType}</th><td>{activity.modality ?? 'ND'}</td><td>{distance(activity.distanceM)}</td><td>{duration(activity.durationSeconds)}</td><td>{formatPace(activity.averagePaceSecondsPerKm)}</td><td><button type="button" onClick={() => setParams({ activity: activity.id })}>Abrir</button></td></tr>)}</tbody></table></div>
      <div className="pagination"><button type="button" disabled={page === 1} onClick={() => setPage((value) => value - 1)}>Anterior</button><span>Página {page} de {totalPages}</span><button type="button" disabled={page >= totalPages} onClick={() => setPage((value) => value + 1)}>Siguiente</button></div>
    </section>
  </div>
}
