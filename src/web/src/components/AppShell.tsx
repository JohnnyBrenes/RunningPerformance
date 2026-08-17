import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/AuthProvider'

const navigation = [
  { to: '/', label: 'Inicio', glyph: '⌁', end: true },
  { to: '/calendar', label: 'Calendario', glyph: '▦', end: false },
  { to: '/plan', label: 'Plan', glyph: '≋', end: false },
  { to: '/activities', label: 'Actividades', glyph: '↗', end: false },
  { to: '/evaluations', label: 'Cierre', glyph: '◉', end: false },
  { to: '/exercises', label: 'Ejercicios', glyph: '◇', end: false },
  { to: '/races', label: 'Carreras', glyph: '△', end: false },
  { to: '/profile', label: 'Perfil', glyph: '◯', end: false },
] as const

const mobileNavigation = navigation.slice(0, 5)

export function AppShell() {
  const { user, signOut } = useAuth()

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
          <a className="brand compact" href="/" aria-label="Running Performance, inicio"><span className="brand-mark">R</span></a>
          <div className="mobile-actions">
            <span className="connection-pill"><i /> Sincronizado</span>
            <details className="mobile-menu">
              <summary>Menú</summary>
              <nav aria-label="Navegación secundaria móvil">
                {navigation.map((item) => (
                  <NavLink key={item.to} to={item.to} end={item.end} onClick={(event) => event.currentTarget.closest('details')?.removeAttribute('open')}>
                    <span aria-hidden="true">{item.glyph}</span>{item.label}
                  </NavLink>
                ))}
                <button type="button" onClick={() => void signOut()}>Cerrar sesión</button>
              </nav>
            </details>
          </div>
        </header>
        <main className="app-content"><Outlet /></main>
      </div>

      <nav className="bottom-nav" aria-label="Navegación principal móvil">
        {mobileNavigation.map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end}>
            <span aria-hidden="true">{item.glyph}</span>{item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
