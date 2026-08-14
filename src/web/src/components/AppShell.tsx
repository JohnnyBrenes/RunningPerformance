import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../auth/AuthProvider'

const navigation = [
  { to: '/', label: 'Inicio', glyph: '⌁', end: true },
  { to: '/profile', label: 'Perfil', glyph: '◯', end: false },
  { to: '/races', label: 'Carreras', glyph: '△', end: false },
  { to: '/calendar', label: 'Calendario', glyph: '▦', end: false },
  { to: '/evaluations', label: 'Evaluar', glyph: '◉', end: false },
  { to: '/exercises', label: 'Ejercicios', glyph: '◇', end: false },
] as const

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
            <button className="mobile-signout" type="button" onClick={() => void signOut()}>Salir</button>
          </div>
        </header>
        <main className="app-content"><Outlet /></main>
      </div>

      <nav className="bottom-nav" aria-label="Navegación principal móvil">
        {navigation.map((item) => (
          <NavLink key={item.to} to={item.to} end={item.end}>
            <span aria-hidden="true">{item.glyph}</span>{item.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
