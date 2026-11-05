// Runnable demo of heal-helper.mjs without a test runner (#618): the button's id changed from
// btn-submit-old to btn-submit-new, the "test" still looks for the old one, and the helper
// asks automation-sandbox-heal for the closest element on the live page.
//
//   node heal-demo.mjs        (needs automation-sandbox-heal on the PATH, or HEAL_CLI)

import { chromium } from 'playwright';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { suggestHeal } from './heal-helper.mjs';

const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  await page.goto(pathToFileURL(resolve('fixtures/page-new.html')).href);
  const locator = page.getByRole('button', { name: 'Submit' });
  console.log(`locator under test: ${locator}`);
  console.log(await suggestHeal(page, String(locator)));
} finally {
  await browser.close();
}
