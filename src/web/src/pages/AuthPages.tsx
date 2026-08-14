import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthProvider'
import { hasSupabaseConfiguration } from '../lib/supabase'

function AuthLayout({ eyebrow, title, copy, children }: React.PropsWithChildren<{ eyebrow: string; title: string; copy: string }>) {
  return (
    <main className="auth-page">
      <section className="auth-story" aria-label="Running Performance">
        <a className="brand" href="/"><span className="brand-mark">R</span><span>Running<br />Performance</span></a>
        <div>
          <p className="eyebrow">Entrena con contexto</p>
          <h1>Tu progreso,<br /><em>bien leído.</em></h1>
          <p>Una vista serena de tu entrenamiento, tu salud y la carrera que tienes por delante.</p>
        </div>
        <small>Datos privados · decisiones explicables</small>
      </section>
      <section className="auth-panel">
        <div className="auth-card">
          <p className="eyebrow">{eyebrow}</p>
          <h2>{title}</h2>
          <p className="muted">{copy}</p>
          {children}
        </div>
      </section>
    </main>
  )
}

export function LoginPage() {
  const { session, signIn } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  if (session) return <Navigate to="/" replace />

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      await signIn(email, password)
      const destination = (location.state as { from?: string } | null)?.from ?? '/'
      navigate(destination, { replace: true })
    } catch {
      setError('Correo o contraseña incorrectos. Revisa los datos e inténtalo otra vez.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthLayout eyebrow="Bienvenido" title="Vuelve a tu plan" copy="Accede con la cuenta que ya tienes. El registro público está desactivado.">
      {!hasSupabaseConfiguration && <p className="form-alert">Falta VITE_SUPABASE_PUBLISHABLE_KEY en la configuración local.</p>}
      <form className="stack-form" onSubmit={(event) => void submit(event)}>
        <label>Correo electrónico<input autoComplete="email" inputMode="email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></label>
        <label>Contraseña<input autoComplete="current-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} required /></label>
        {error && <p className="form-alert" role="alert">{error}</p>}
        <button className="button primary" type="submit" disabled={busy || !hasSupabaseConfiguration}>{busy ? 'Entrando…' : 'Entrar'}</button>
      </form>
      <Link className="text-link" to="/recover">Olvidé mi contraseña</Link>
    </AuthLayout>
  )
}

export function RecoveryPage() {
  const { recoveryMode, requestPasswordReset, updatePassword } = useAuth()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError('')
    try {
      if (recoveryMode) {
        await updatePassword(password)
        setMessage('Contraseña actualizada. Ya puedes volver a tu cuenta.')
      } else {
        await requestPasswordReset(email)
        setMessage('Si el correo pertenece a una cuenta, recibirás el enlace de recuperación.')
      }
    } catch {
      setError('No pudimos completar la solicitud. Inténtalo de nuevo en unos minutos.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthLayout eyebrow="Acceso" title={recoveryMode ? 'Elige una contraseña nueva' : 'Recupera tu cuenta'} copy={recoveryMode ? 'Usa ocho caracteres o más.' : 'Te enviaremos un enlace de un solo uso.'}>
      <form className="stack-form" onSubmit={(event) => void submit(event)}>
        {recoveryMode
          ? <label>Nueva contraseña<input type="password" autoComplete="new-password" minLength={8} value={password} onChange={(event) => setPassword(event.target.value)} required /></label>
          : <label>Correo electrónico<input type="email" autoComplete="email" value={email} onChange={(event) => setEmail(event.target.value)} required /></label>}
        {message && <p className="form-success" role="status">{message}</p>}
        {error && <p className="form-alert" role="alert">{error}</p>}
        <button className="button primary" type="submit" disabled={busy}>{busy ? 'Procesando…' : recoveryMode ? 'Guardar contraseña' : 'Enviar enlace'}</button>
      </form>
      <Link className="text-link" to="/login">Volver al acceso</Link>
    </AuthLayout>
  )
}
