import { expect, test } from '@playwright/test'

test('renders profile, races, exercise media and the published plan without horizontal overflow', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-a@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-a')
  await page.getByRole('button', { name: 'Entrar' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: 'Hola, Synthetic' })).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.today-card')).toContainText('Hoy')
  await expect(page.locator('.today-card')).toContainText(/Descanso|Gimnasio|Caminadora|Correr/)
  await expect(page.locator('.next-race-card')).toContainText('Fecha')
  await expect(page.locator('.next-race-card')).toContainText('Distancia')
  await expect(page.locator('.next-race-card')).toContainText('Tiempo objetivo')
  await expect(page.locator('.next-race-card')).toContainText('Lugar')
  await expect(page.getByRole('heading', { name: 'Distancia por semana' })).toBeVisible()
  await expect(page.getByText('Mis datos')).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Consumo gratuito' })).toHaveCount(0)
  await expectNoHorizontalOverflow(page)

  await page.goto('/calendar')
  await expect(page.getByRole('heading', { name: 'Calendario', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Mes', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await expect(page.getByRole('button', { name: 'Semana', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Día', exact: true })).toBeVisible()
  await expect(page.locator('.calendar-session.executed').first()).toBeVisible()
  await expectNoHorizontalOverflow(page)

  await page.goto('/profile')
  await expect(page.getByRole('heading', { name: 'Perfil', exact: true })).toBeVisible()
  await expect(page.getByText('Synthetic Athlete A', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('Masculino', { exact: true })).toBeVisible()
  await expectNoHorizontalOverflow(page)

  await page.goto('/races')
  await expect(page.getByRole('heading', { name: 'Carreras', exact: true })).toBeVisible()
  await expect(page.locator('.race-card').first()).toBeVisible()
  await expectNoHorizontalOverflow(page)

  await page.goto('/exercises')
  await expect(page.getByRole('heading', { name: 'Ejercicios', exact: true })).toBeVisible()
  const squat = page.locator('.exercise-card').filter({ hasText: 'Sentadilla goblet' })
  await expect(squat.locator('img')).toHaveAttribute('src', /goblet-squat-male-v1\.png/)
  const pogos = page.locator('.exercise-card').filter({ hasText: 'Pogos de tobillo' })
  await expect(pogos.locator('img')).toHaveAttribute('src', /ankle-pogos-male-v1\.png/)
  await expectNoHorizontalOverflow(page)

  await page.goto('/plan')
  await expect(page.getByRole('heading', { name: 'Semana de base y fuerza' })).toBeVisible()
  await expect(page.locator('.version-status')).toContainText('Publicada')
  await expect(page.locator('.session-guide h2')).toContainText('Fuerza, movilidad y pliometría')
  await expect(page.locator('.planned-exercise img').first()).toHaveAttribute('src', /-male-v1\.png/)
  const completion = page.locator('.completion-panel')
  await expect(completion).toContainText('Sesión lógica')
  await expect(completion.locator('.logical-load')).toContainText('2')
  await expect(completion.locator('.logical-load')).toContainText('25 min')
  await expect(completion.locator('.logical-load')).toContainText('125 UA')
  await completion.getByRole('button', { name: 'Registrar entrenamiento' }).click()
  await completion.getByRole('tab', { name: '24 h' }).click()
  await completion.getByLabel('Respuesta posterior').selectOption('normal')
  await completion.getByRole('button', { name: 'Guardar check-in 24 h' }).click()
  await expect(completion.getByRole('button', { name: 'Guardar check-in 24 h' })).toBeEnabled()
  await expectNoHorizontalOverflow(page)

  await page.goto('/evaluations')
  await expect(page.getByRole('heading', { name: 'Evaluación P1–P5' })).toBeVisible()
  await expect(page.locator('.traffic-card')).toContainText('Amarillo')
  await expect(page.getByRole('heading', { name: 'Cumplimiento por tipo' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Volumen de running' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Tirada larga exterior' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Carga interna sRPE' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Seguridad y recuperación' })).toBeVisible()
  await expect(page.locator('.weekly-metric-grid article.missing').first()).toContainText('ND')
  await expectNoHorizontalOverflow(page)

  if ((page.viewportSize()?.width ?? 1000) < 896) {
    await expect(page.getByRole('button', { name: 'Salir' })).toBeVisible()
  } else {
    await expect(page.getByRole('button', { name: 'Cerrar sesión' })).toBeVisible()
  }
})

test('keeps account creation unavailable from the public access screen', async ({ page }) => {
  await page.goto('/login')
  await expect(page.getByText('El registro público está desactivado.')).toBeVisible()
  await expect(page.getByRole('link', { name: 'Olvidé mi contraseña' })).toBeVisible()
  await expect(page.getByRole('link', { name: /registr/i })).toHaveCount(0)
})

test('publishes the metadata required for an iPhone home-screen web app', async ({ page, request }) => {
  await page.goto('/login')

  await expect(page.locator('link[rel="manifest"]')).toHaveAttribute('href', '/manifest.webmanifest')
  await expect(page.locator('link[rel="apple-touch-icon"]')).toHaveAttribute('href', '/icons/apple-touch-icon-180-v1.png')
  await expect(page.locator('meta[name="apple-mobile-web-app-capable"]')).toHaveAttribute('content', 'yes')

  const manifestResponse = await request.get('/manifest.webmanifest')
  expect(manifestResponse.ok()).toBe(true)
  const manifest = await manifestResponse.json()
  expect(manifest).toMatchObject({
    id: '/',
    start_url: '/',
    scope: '/',
    display: 'standalone',
    name: 'Running Performance',
  })
  expect(manifest.icons).toEqual(expect.arrayContaining([
    expect.objectContaining({ src: '/icons/app-icon-192-v1.png', sizes: '192x192' }),
    expect.objectContaining({ src: '/icons/app-icon-512-v1.png', sizes: '512x512' }),
    expect.objectContaining({ src: '/icons/app-icon-maskable-512-v1.png', purpose: 'maskable' }),
  ]))
})

test('persists health context, a race and its immutable goal history', async ({ page }, testInfo) => {
  const suffix = `${testInfo.project.name}-${Date.now()}`
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-b@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-b')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL('/')

  await page.goto('/profile')
  await page.getByRole('button', { name: 'Añadir contexto' }).click()
  await page.getByLabel('Tipo').selectOption('discomfort')
  await page.getByLabel('Descripción').fill(`Molestia sintética ${suffix}`)
  await page.getByRole('button', { name: 'Guardar', exact: true }).click()
  await expect(page.getByText(`Molestia sintética ${suffix}`)).toBeVisible()

  await page.goto('/races')
  await page.getByRole('button', { name: 'Nueva carrera' }).click()
  await page.getByLabel('Nombre').fill(`Carrera smoke ${suffix}`)
  await page.getByLabel('Fecha').fill('2027-04-18')
  await page.getByLabel('Distancia (m)').fill('10000')
  await page.getByRole('button', { name: 'Guardar carrera' }).click()

  const raceCard = page.locator('.race-card').filter({ hasText: `Carrera smoke ${suffix}` })
  await expect(raceCard).toBeVisible()
  await raceCard.getByRole('button', { name: 'Definir meta' }).click()
  await page.getByLabel('Tiempo objetivo (h:mm:ss)').fill('00:49:30')
  await expect(page.getByLabel('Ritmo calculado')).toHaveValue('4:57')
  await page.getByLabel('Por qué cambia esta meta').fill(`Primera meta sintética ${suffix}`)
  await page.getByRole('button', { name: 'Guardar nueva versión' }).click()
  await expect(raceCard.getByText('49:30')).toBeVisible()

  await raceCard.getByRole('button', { name: 'Historial' }).click()
  await expect(page.getByRole('dialog')).toContainText(`Primera meta sintética ${suffix}`)
  await expect(page.getByRole('dialog')).toContainText('v1')
})

test('requires a human decision and creates a new plan draft for an adjustment', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium-desktop', 'The auditable mutation runs once against the shared synthetic database.')
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-a@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-a')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL('/')
  await page.goto('/evaluations')

  const decisionInput = page.getByRole('combobox', { name: 'Decisión' })
  if (await decisionInput.count()) {
    await decisionInput.selectOption('adapt')
    await page.getByLabel('Observación').fill('La semana sintética conserva resultados y respuesta posterior pendientes.')
    await page.getByLabel('Evidencia', { exact: true }).fill('P1 y P5 muestran los faltantes como ND con enlaces a sus sesiones fuente.')
    await page.getByLabel('Comparación histórica').fill('No existe todavía una semana sintética comparable; no se infiere una tendencia.')
    await page.getByLabel('Interpretación').fill('La incertidumbre justifica adaptar una sesión sin añadir carga.')
    await page.getByLabel('Recomendación').fill('Mantener la carga total y revisar la respuesta de 24 a 48 horas.')
    await page.getByLabel('Nuevo objetivo').fill('Objetivo sintético adaptado; conservar carga y priorizar respuesta posterior.')
    await page.getByLabel('Motivo exacto').fill('Decisión humana APP-011 sobre evidencia sintética.')
    await page.getByLabel('Criterio de revisión').fill('Revisar cuando exista respuesta posterior completa sin señal adversa.')
    await page.getByRole('button', { name: 'Confirmar decisión' }).click()
  }

  await expect(page.getByRole('heading', { name: 'Adaptar', exact: true })).toBeVisible()
  await expect(page.getByText('Auditable', { exact: true })).toBeVisible()
  await expect(page.getByText('Nueva versión sin publicar', { exact: true })).toBeVisible()
  await page.getByRole('link', { name: 'Revisar borrador' }).click()
  await expect(page).toHaveURL(/\/plan\?version=/)
  await expect(page.locator('.version-status')).toContainText('Borrador')
  await expect(page.locator('.version-tabs')).toContainText('Publicada')
  await expect(page.locator('.version-tabs')).toContainText('Borrador')
  await expectNoHorizontalOverflow(page)
})

test('creates a private expiring export and an auditable lifecycle request', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'chromium-desktop', 'The governance mutations run once against the shared synthetic database.')
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-a@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-a')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL('/')

  await page.goto('/profile')
  await page.getByText('Mis datos', { exact: true }).click()

  await page.getByRole('button', { name: 'Preparar descarga' }).click()
  await expect(page.getByRole('status')).toContainText('copia privada está lista')
  const privateExport = page.locator('.governance-list > div').filter({ hasText: 'Copia de tus datos' }).first()
  await expect(privateExport).toContainText('Lista')
  await expect(privateExport).toContainText('vence')
  const download = page.waitForEvent('download')
  await privateExport.getByRole('button', { name: 'Descargar' }).click()
  await download

  await page.getByLabel('Acción').selectOption('archive')
  await page.getByLabel('Justificación').fill('Solicitud sintética E2E que solo debe quedar pendiente para revisión humana.')
  await page.getByRole('button', { name: 'Registrar solicitud' }).click()
  await expect(page.getByRole('status')).toContainText('No se eliminó ningún dato')
  await expect(page.locator('.compact-list')).toContainText('Pendiente de revisión')
  await expect(page.getByText(/enlace público/i)).toBeVisible()
  await expectNoHorizontalOverflow(page)
})

async function expectNoHorizontalOverflow(page: import('@playwright/test').Page) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true)
}
