import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { DataGovernanceService } from '../api/generated'
import { readableApiError } from '../lib/api'

function downloadJson(id: string, payload: unknown) {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = `running-performance-${id}.json`
  anchor.click()
  URL.revokeObjectURL(url)
}

function exportStatusLabel(status: string) {
  return status === 'completed' ? 'Lista' : status === 'failed' ? 'No disponible' : 'Preparando'
}

function requestStatusLabel(status: string) {
  return status === 'requested' ? 'Pendiente de revisión' : status === 'completed' ? 'Completada' : status
}

export function DataManagementPanel() {
  const queryClient = useQueryClient()
  const [requestType, setRequestType] = useState<'archive' | 'delete'>('archive')
  const [rationale, setRationale] = useState('')
  const [message, setMessage] = useState<string | null>(null)
  const exportsQuery = useQuery({ queryKey: ['exports'], queryFn: () => DataGovernanceService.getExports() })
  const lifecycleQuery = useQuery({ queryKey: ['lifecycle-requests'], queryFn: () => DataGovernanceService.getLifecycleRequests() })

  const createExport = useMutation({
    mutationFn: () => DataGovernanceService.createExport({ idempotencyKey: crypto.randomUUID() }),
    onSuccess: async () => {
      setMessage('Tu copia privada está lista y vence en 24 horas.')
      await queryClient.invalidateQueries({ queryKey: ['exports'] })
    },
    onError: (error) => setMessage(readableApiError(error)),
  })
  const downloadExport = useMutation({
    mutationFn: async (id: string) => ({ id, payload: await DataGovernanceService.downloadExport({ exportId: id }) }),
    onSuccess: ({ id, payload }) => downloadJson(id, payload),
    onError: (error) => setMessage(readableApiError(error)),
  })
  const createLifecycle = useMutation({
    mutationFn: () => DataGovernanceService.createLifecycleRequest({
      requestBody: { requestType, scopeType: 'all', scopeId: null, rationale },
    }),
    onSuccess: async () => {
      setRationale('')
      setMessage('Solicitud registrada para revisión. No se eliminó ningún dato.')
      await queryClient.invalidateQueries({ queryKey: ['lifecycle-requests'] })
    },
    onError: (error) => setMessage(readableApiError(error)),
  })

  return <details className="quiet-card governance-section">
    <summary>Mis datos</summary>
    <p>Descarga una copia o solicita archivar/eliminar tu información. Estas opciones no afectan tu plan de entrenamiento.</p>
    {message && <p className="form-message" role="status">{message}</p>}
    <div className="governance-grid">
      <article className="feature-card">
        <div className="card-top"><div><span className="section-label">Copia privada</span><h3>Descargar mis datos</h3></div><button className="button primary" type="button" disabled={createExport.isPending} onClick={() => createExport.mutate()}>{createExport.isPending ? 'Preparando…' : 'Preparar descarga'}</button></div>
        <p>La copia se descarga solo con tu sesión, vence en 24 horas y nunca tiene un enlace público.</p>
        <div className="governance-list">{exportsQuery.data?.length ? exportsQuery.data.map((item) => <div key={item.id}>
          <div><strong>Copia de tus datos</strong><span>{exportStatusLabel(item.status)} · {item.expiresAt ? `vence ${new Date(item.expiresAt).toLocaleString('es')}` : 'sin descarga'}</span></div>
          {item.downloadHref && <button type="button" onClick={() => downloadExport.mutate(item.id)} disabled={downloadExport.isPending}>Descargar</button>}
        </div>) : <p>Aún no has preparado ninguna copia.</p>}</div>
      </article>

      <article className="quiet-card">
        <span className="section-label">Archivo o eliminación</span>
        <h3>Solicitar un cambio</h3>
        <p>La solicitud queda pendiente para revisión y no borra nada automáticamente.</p>
        <form className="lifecycle-form" onSubmit={(event) => { event.preventDefault(); createLifecycle.mutate() }}>
          <label>Acción<select value={requestType} onChange={(event) => setRequestType(event.target.value as 'archive' | 'delete')}><option value="archive">Archivar todos mis datos</option><option value="delete">Solicitar eliminación total</option></select></label>
          <label>Justificación<textarea minLength={12} maxLength={2000} required value={rationale} onChange={(event) => setRationale(event.target.value)} placeholder="Explica por qué solicitas esta acción." /></label>
          <button className="button secondary" disabled={createLifecycle.isPending || rationale.trim().length < 12}>Registrar solicitud</button>
        </form>
        <div className="governance-list compact-list">{lifecycleQuery.data?.map((item) => <div key={item.id}><div><strong>{item.requestType === 'archive' ? 'Archivo' : 'Eliminación'}</strong><span>{requestStatusLabel(item.status)} · {new Date(item.createdAt).toLocaleString('es')}</span></div></div>)}</div>
      </article>
    </div>
  </details>
}
