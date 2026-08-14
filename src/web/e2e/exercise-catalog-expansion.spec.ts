import { expect, test } from '@playwright/test'

test('shows the practical ten-exercise catalog with usable technical guidance', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill('athlete-a@example.invalid')
  await page.getByLabel('Contraseña').fill('synthetic-only-a')
  await page.getByRole('button', { name: 'Entrar' }).click()
  await expect(page).toHaveURL('/')

  await page.goto('/exercises')
  await expect(page.getByRole('heading', { name: 'Ejercicios', exact: true })).toBeVisible()
  await expect(page.getByText('10 ejercicios', { exact: true })).toBeVisible()

  const expected = [
    ['Step-up con mancuernas', 'dumbbell-step-up-male-v1.png'],
    ['Jalón al pecho', 'lat-pulldown-male-v1.png'],
    ['Remo sentado en polea', 'seated-cable-row-male-v1.png'],
    ['Press Pallof medio arrodillado', 'pallof-press-male-v1.png'],
    ['Press de pecho en máquina', 'machine-chest-press-male-v1.png'],
    ['Extensión de rodilla en máquina', 'machine-knee-extension-male-v1.png'],
  ] as const

  for (const [name, image] of expected) {
    const card = page.locator('.exercise-card').filter({ hasText: name })
    await expect(card).toHaveCount(1)
    await expect(card.locator('img')).toHaveAttribute('src', new RegExp(`${image.replace('.', '\\.')}$`))
  }

  const stepUp = page.locator('.exercise-card').filter({ hasText: 'Step-up con mancuernas' })
  await stepUp.getByRole('button', { name: 'Ver técnica' }).click()
  const guide = page.getByRole('dialog', { name: 'Step-up con mancuernas' })
  await expect(guide).toContainText('dos mancuernas de 5 kg')
  await expect(guide.getByRole('heading', { name: 'Preparación' })).toBeVisible()
  await expect(guide.getByRole('heading', { name: 'Ejecución' })).toBeVisible()
  await expect(guide.getByRole('heading', { name: 'Puntos de seguridad' })).toBeVisible()
  await expect(guide.locator('img')).toHaveAttribute('src', /dumbbell-step-up-male-v1\.png$/)

  const horizontalOverflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth)
  expect(horizontalOverflow).toBe(false)
})
