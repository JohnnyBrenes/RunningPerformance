import { expect, test } from '@playwright/test'

test('keeps the ten-exercise technical guidance reachable inside the session that prescribes it', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-a@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-a')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL('/')

  // The catalogue stopped being a section of its own: technique now lives in
  // the day that prescribes it, so nothing links to /exercises any more.
  await expect(page.getByRole('link', { name: /Ejercicios/ })).toHaveCount(0)

  await page.goto('/plan')
  await expect(page.getByRole('heading', { name: 'Semana de base y fuerza' })).toBeVisible({ timeout: 10_000 })
  await page.locator('.week-strip button').filter({ hasText: 'Fuerza' }).first().click()
  await expect(page.locator('.session-guide h2')).toContainText('Fuerza, movilidad y pliometría')

  const expected = [
    ['Plancha lateral', 'side-plank-male-v1.png'],
    ['Press Pallof medio arrodillado', 'pallof-press-male-v1.png'],
    ['Pogos de tobillo', 'ankle-pogos-male-v1.png'],
    ['Sentadilla goblet', 'goblet-squat-male-v1.png'],
    ['Peso muerto rumano', 'romanian-deadlift-male-v1.png'],
    ['Step-up con mancuernas', 'dumbbell-step-up-male-v1.png'],
    ['Jalón al pecho', 'lat-pulldown-male-v1.png'],
    ['Remo sentado en polea', 'seated-cable-row-male-v1.png'],
    ['Press de pecho en máquina', 'machine-chest-press-male-v1.png'],
    ['Extensión de rodilla en máquina', 'machine-knee-extension-male-v1.png'],
  ] as const

  for (const [name, image] of expected) {
    const planned = page.locator('.planned-exercise').filter({ hasText: name })
    await expect(planned).toHaveCount(1)
    await expect(planned.locator('img')).toHaveAttribute('src', new RegExp(`${image.replace('.', '\\.')}$`))
  }

  const stepUp = page.locator('.planned-exercise').filter({ hasText: 'Step-up con mancuernas' })
  await expect(stepUp).toContainText('RPE 7')
  await expect(stepUp).toContainText('5 kg')
  await stepUp.getByText('Preparación y seguridad').click()
  await expect(stepUp).toContainText('dos mancuernas de 5 kg')
  await expect(stepUp).toContainText('step estable y bajo')
  await expect(stepUp).toContainText('Seguridad:')

  const horizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(horizontalOverflow).toBe(false)
})
