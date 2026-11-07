// Which common Playwright locator styles survive a real release upgrade? (#670)
//
//   node locator-survival.mjs <manifest.json> [--channel NAME | --executable-path PATH]
//
// Downloads the two pinned releases listed in the manifest (the #663 organic drift dataset: Swagger UI 4.19.1 and 5.17.14), refuses them unless
// their hashes match, and aims ten locator styles at the same control: the button that expands the "GET /pets" operation. A locator that finds
// exactly that control in the old release is "valid". For each valid one the script reports what it does in the new release:
//   still right          one match, and it is the same control
//   fails visibly        no match, or several (Playwright's strict mode refuses to act)
//   SILENTLY WRONG       one match, a different element: the test keeps running against the wrong control
// and, for a silently wrong one, whether clicking it still does what the old control did, which is why such a test can stay green.
//
// A locator that ends in .first() or .nth(0) never fails on "several matches", so for those the output also says how many elements the same
// selector matches without it: a "still right" that depends on the right element coming first in the DOM is luck, not robustness.
//
// The ten locators are the author's choice of plausible styles, not a sample of real suites: the output says nothing about how common each is.
import { chromium } from 'playwright';
import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { prepareRelease } from './organic-drift-capture.mjs';

const OPERATION = '#operations-pets-get_pets';

// What the right control is: the summary button inside the GET /pets operation block.
const isTheControl = (element, operation) => element.classList.contains('opblock-summary-control') && element.closest(operation) !== null;

const strategies = [
  ['getByRole button, name "get /pets"', (p) => p.getByRole('button', { name: 'get /pets' })],
  ['getByRole button, name /get.*pets/i', (p) => p.getByRole('button', { name: /get.*pets/i })],
  ['getByRole button, name /^GET \\/pets/', (p) => p.getByRole('button', { name: /^GET \/pets/ })],
  ['getByText "List pets"', (p) => p.getByText('List pets')],
  [`CSS ${OPERATION} .opblock-summary-control`, (p) => p.locator(`${OPERATION} .opblock-summary-control`)],
  [`CSS ${OPERATION} button  (.first())`, (p) => p.locator(`${OPERATION} button`).first(), (p) => p.locator(`${OPERATION} button`)],
  ['CSS .opblock-summary-get button  (.first())', (p) => p.locator('.opblock-summary-get button').first(), (p) => p.locator('.opblock-summary-get button')],
  ['CSS button.opblock-summary-control  (.nth(0))', (p) => p.locator('button.opblock-summary-control').nth(0), (p) => p.locator('button.opblock-summary-control')],
  ['XPath //button[contains(@aria-label,"get")]', (p) => p.locator('xpath=//button[contains(@aria-label,"get")]')],
  ['CSS button[aria-label*="pets"]  (.first())', (p) => p.locator('button[aria-label*="pets"]').first(), (p) => p.locator('button[aria-label*="pets"]')],
];

async function open(browser, manifest, appDir) {
  const page = await browser.newPage({ viewport: manifest.viewport });
  await page.goto(pathToFileURL(join(appDir, manifest.page)).href, { waitUntil: 'networkidle' });
  await page.locator(OPERATION).waitFor();
  await page.waitForTimeout(700);
  return page;
}

// What a locator resolves to: how many elements, and for a single one whether it is the control.
async function resolveOnce(page, make) {
  const locator = make(page);
  const count = await locator.count();
  if (count !== 1) {
    return { count, right: false };
  }
  const right = await locator.evaluate(isTheControl, OPERATION);
  const description = await locator.evaluate((e) => e.className.trim().replace(/\s+/g, '.'));
  return { count, right, description };
}

async function main() {
  const [, , manifestPath, ...flags] = process.argv;
  if (!manifestPath) {
    console.error('Usage: node locator-survival.mjs <manifest.json> [--channel NAME | --executable-path PATH]');
    process.exit(1);
  }
  const options = {};
  for (let i = 0; i < flags.length; i += 2) {
    const name = { '--channel': 'channel', '--executable-path': 'executablePath' }[flags[i]];
    if (!name || !flags[i + 1]) {
      console.error(`Unrecognized or incomplete argument: ${flags[i]}`);
      process.exit(1);
    }
    options[name] = flags[i + 1];
  }
  if (options.channel && options.executablePath) {
    console.error('--channel and --executable-path cannot be combined.');
    process.exit(1);
  }

  const manifestFile = resolve(manifestPath);
  const manifest = JSON.parse(await readFile(manifestFile, 'utf8'));
  const [oldRelease, newRelease] = manifest.releases;
  const oldDir = await prepareRelease(manifest, dirname(manifestFile), oldRelease);
  const newDir = await prepareRelease(manifest, dirname(manifestFile), newRelease);

  const browser = await chromium.launch(options);
  try {
    const oldPage = await open(browser, manifest, oldDir);
    const newPage = await open(browser, manifest, newDir);
    const tally = { valid: 0, stillRight: 0, stillRightByOrder: 0, failsVisibly: 0, silentlyWrong: 0 };

    console.log(`Old release ${oldRelease.version}, new release ${newRelease.version}. Target: the control that expands the GET /pets operation.\n`);
    for (const [name, make, rawMake] of strategies) {
      const before = await resolveOnce(oldPage, make);
      if (!(before.count === 1 && before.right)) {
        console.log(`${name.padEnd(52)} not valid in ${oldRelease.version} (matches ${before.count}${before.count === 1 ? ', the wrong element' : ''})`);
        continue;
      }
      tally.valid++;
      const after = await resolveOnce(newPage, make);
      let verdict;
      if (after.count !== 1) {
        tally.failsVisibly++;
        verdict = after.count === 0 ? 'fails visibly (no match)' : `fails visibly (strict mode: ${after.count} matches)`;
      } else if (after.right) {
        tally.stillRight++;
        verdict = 'still right';
        if (rawMake) {
          const matches = await rawMake(newPage).count();
          if (matches > 1) {
            tally.stillRightByOrder++;
            verdict += ` (only because it comes first: the same selector without it matches ${matches})`;
          }
        }
      } else {
        tally.silentlyWrong++;
        const locator = make(newPage);
        const operation = newPage.locator(OPERATION);
        const wasOpen = await operation.evaluate((e) => e.classList.contains('is-open'));
        await locator.click({ timeout: 3000 });
        await newPage.waitForTimeout(400);
        const isOpen = await operation.evaluate((e) => e.classList.contains('is-open'));
        const rawMatches = rawMake ? await rawMake(newPage).count() : after.count;
        verdict = `SILENTLY WRONG (${after.description}; the same selector without .first() matches ${rawMatches}); clicking it ${isOpen && !wasOpen ? 'still opens the operation' : 'does not open the operation'}`;
      }
      console.log(`${name.padEnd(52)} works in ${oldRelease.version}; in ${newRelease.version}: ${verdict}`);
    }

    console.log(`\n${strategies.length} locator styles tried, ${tally.valid} valid in ${oldRelease.version}. In ${newRelease.version}: ${tally.stillRight} still right (${tally.stillRightByOrder} of them only because the right element comes first in the DOM), ${tally.failsVisibly} fail visibly, ${tally.silentlyWrong} silently wrong.`);
  } finally {
    await browser.close();
  }
}

// Only runs when executed directly, like the other scripts here.
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  await main();
}
