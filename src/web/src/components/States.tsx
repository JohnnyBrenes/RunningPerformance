import { useEffect, useState, type ReactNode } from 'react'

export function LoadingState({ label = 'Cargando' }: { label?: string }) {
  const [isColdStart, setIsColdStart] = useState(false)

  useEffect(() => {
    const timer = window.setTimeout(() => setIsColdStart(true), 8_000)
    return () => window.clearTimeout(timer)
  }, [])

  return (
    <div className="state-card" role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{isColdStart ? 'Despertando el servicio gratuito…' : label}{isColdStart && <small>Puede tardar cerca de un minuto. No cierres esta pantalla.</small>}</span>
    </div>
  )
}

export function ErrorState({ message, retry }: { message: string; retry?: () => void }) {
  return (
    <div className="state-card state-error" role="alert">
      <div><strong>No salió como esperábamos.</strong><p>{message}</p></div>
      {retry && <button className="button secondary" type="button" onClick={retry}>Intentar de nuevo</button>}
    </div>
  )
}

export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return <div className="empty-state"><span aria-hidden="true">◇</span><h3>{title}</h3><p>{children}</p></div>
}
