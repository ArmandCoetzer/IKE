import { test, expect } from '@playwright/test';

test.describe('Tradion app', () => {
  test('login page loads and shows sign in form', async ({ page }) => {
    await page.goto('/login');

    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByLabel('Email')).toBeVisible();
    await expect(page.getByLabel('Password')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('unauthenticated user visiting root is redirected to login', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveURL(/\/login/);
  });
});
