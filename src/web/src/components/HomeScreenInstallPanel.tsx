import { useEffect, useState } from 'react'
import { resolveInstallExperience, type InstallContext, type InstallExperience } from '../lib/install'

type NavigatorWithStandalone = Navigator & { standalone?: boolean }

function readInstallExperience(): InstallExperience {
  const context: InstallContext = {
    userAgent: navigator.userAgent,
    platform: navigator.platform,
    maxTouchPoints: navigator.maxTouchPoints,
    displayModeStandalone: window.matchMedia('(display-mode: standalone)').matches,
    navigatorStandalone: (navigator as NavigatorWithStandalone).standalone === true,
  }

  return resolveInstallExperience(context)
}

export function HomeScreenInstallPanel() {
  const [experience, setExperience] = useState<InstallExperience>(() => readInstallExperience())

  useEffect(() => {
    const displayMode = window.matchMedia('(display-mode: standalone)')
    const update = () => setExperience(readInstallExperience())
    displayMode.addEventListener('change', update)
    return () => displayMode.removeEventListener('change', update)
  }, [])

  if (experience === 'standalone') {
    return (
      <article className="install-card installed" aria-labelledby="home-screen-title">
        <span className="install-icon" aria-hidden="true">R</span>
        <div><span className="section-label">Acceso rápido</span><h2 id="home-screen-title">Instalada en este dispositivo</h2><p>Running Performance se abrió desde tu pantalla de inicio.</p></div>
      </article>
    )
  }

  const instruction = experience === 'ios-browser'
    ? 'En Safari, toca Compartir y después “Añadir a pantalla de inicio”.'
    : 'En tu iPhone, abre esta dirección en Safari, toca Compartir y elige “Añadir a pantalla de inicio”.'

  return (
    <article className="install-card" aria-labelledby="home-screen-title">
      <span className="install-icon" aria-hidden="true">R</span>
      <div>
        <span className="section-label">Acceso rápido</span>
        <h2 id="home-screen-title">Añadir al inicio del iPhone</h2>
        <p>{instruction}</p>
        <small>Se abrirá como aplicación independiente. La conexión a internet seguirá siendo necesaria.</small>
      </div>
    </article>
  )
}
