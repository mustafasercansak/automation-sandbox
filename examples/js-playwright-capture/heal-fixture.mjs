// `@playwright/test` fixture: when a test fails waiting for a locator, print and attach a
// heal suggestion (#618). Use it instead of the stock `test`:
//
//   import { test, expect } from './heal-fixture.mjs';
//
// Needs `npm install --save-dev @playwright/test` in the suite that uses it.

import { test as base, expect } from '@playwright/test';
import { locatorFromError, suggestHeal } from './heal-helper.mjs';

export { expect };

export const test = base.extend({
  healSuggestion: [async ({ page }, use, testInfo) => {
    await use();
    if (testInfo.status === testInfo.expectedStatus) {
      return;
    }
    const locator = locatorFromError(testInfo.error?.message);
    if (!locator) {
      return;
    }
    const suggestion = await suggestHeal(page, locator);
    console.log(suggestion);
    await testInfo.attach('heal-suggestion', { body: suggestion, contentType: 'text/plain' });
  }, { auto: true }],
});
