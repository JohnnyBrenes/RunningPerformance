import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { useAuth } from '../auth/AuthProvider'

const navigation = [
  { to: '/', label: 'Inicio', glyph: '⌁', end: true },
  { to: '/calendar', label: 'Calendario', glyph: '▦', end: false },
  { to: '/plan', label: 'Plan', glyph: '≋', end: false },
  { to: '/activities', label: 'Actividades', glyph: '↗', end: false },
  { to: '/evaluations', label: 'Revisión semanal', glyph: '✓', end: false },
  { to: '/exercises', label: 'Ejercicios', glyph: '◇', end: false },
  { to: '/races', label: 'Carreras', glyph: '△', end: false },
  { to: '/profile', label: 'Perfil', glyph: '◯', end: false },
] as const

const mobileNavigation = navigation.slice(0, 4)
const mobileMoreNavigation = navigation.slice(4)

export function AppShell() {
  const { user, signOut } = useAuth()
  const location = useLocation()
  const [moreOpen, setMoreOpen] = useState(false)
  const moreIsActive = mobileMoreNavigation.some((item) => location.pathname.startsWith(item.to))

  useEffect(() => setMoreOpen(false), [location.pathname])
  useEffect(() => {
    if (!moreOpen) return
    const closeOnEscape = (event: KeyboardEvent) => { if (event.key === 'Escape') setMoreOpen(false) }
    document.addEventListener('keydown', closeOnEscape)
    return () => document.removeEventListener('keydown', closeOnEscape)
  }, [moreOpen])

  return (
    <div className="app-frame">
      <aside className="sidebar">
        <a className="brand" href="/" aria-label="Running Performance, inicio">
          <span className="brand-mark" aria-hidden="true">R</span>
          <span>Running<br />Performance</span>
        </a>
        <nav aria-label="Navegación principal">
          {navigation.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end}>
              <span aria-hidden="true">{item.glyph}</span>{item.label}
            </NavLink>
          ))}
        </nav>
        <div className="account-block">
          <span>{user?.email}</span>
          <button type="button" onClick={() => void signOut()}>Cerrar sesión</button>
        </div>
      </aside>

      <div className="content-frame">
        <header className="mobile-header">
          <a className="brand compact" href="/" aria-label="Running Performance, inicio"><span className="brand-mark">R</span><span>Running Performance</span></a>
          <span className="connection-pill"><i /> Sincronizado</span>
        </header>
        <main className="app-content"><Outlet /></main>
      </div>

      <nav className="bottom-nav" aria-label="Navegación principal móvil">
        {mobileNavigation.map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end}>
            <span aria-hidden="true">{item.glyph}</span>{item.label}
          </NavLink>
        ))}
        <button className={moreIsActive || moreOpen ? 'active' : ''} type="button" aria-expanded={moreOpen} aria-controls="mobile-more-menu" onClick={() => setMoreOpen((open) => !open)}>
          <span aria-hidden="true">•••</span>Más
        </button>
      </nav>

      {moreOpen && <>
        <button className="mobile-more-backdrop" type="button" aria-label="Cerrar menú Más" onClick={() => setMoreOpen(false)} />
        <section className="mobile-more-sheet" id="mobile-more-menu" role="dialog" aria-modal="true" aria-labelledby="mobile-more-title">
          <header><div><span className="section-label">Navegación</span><h2 id="mobile-more-title">Más</h2></div><button className="icon-button" type="button" aria-label="Cerrar menú" onClick={() => setMoreOpen(false)}>×</button></header>
          <nav aria-label="Más secciones">
            {mobileMoreNavigation.map((item) => (
              <NavLink key={item.to} to={item.to} end={item.end} onClick={() => setMoreOpen(false)}>
                <span aria-hidden="true">{item.glyph}</span><span>{item.label}</span><i aria-hidden="true">→</i>
              </NavLink>
            ))}
          </nav>
          <div className="mobile-account"><span>{user?.email}</span><button type="button" onClick={() => void signOut()}>Cerrar sesión</button></div>
        </section>
      </>}
    </div>
  )
}
