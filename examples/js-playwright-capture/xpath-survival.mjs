// Which XPath styles survive a release upgrade? (#670)
//
//   node xpath-survival.mjs <xpath-manifest.json> [--channel NAME | --executable-path PATH]
//
// The sibling of locator-survival.mjs, for XPath. It downloads every release in the manifest (pinned by hash, refused on a mismatch) and
// compares each release with the next one. The target is always the same control: the button that expands the "GET /pets" operation. An
// XPath is "valid" when, in the older release, it matches exactly that control and nothing else. For each valid one the script reports what
// it does in the newer release: still the same control, fails visibly (no match or several), or silently a different element.
//
// The "absolute path" style is what a browser's "Copy full XPath" gives: an index at every level, taken from the older page of each pair.
// The seven styles are the author's choice, written to include the usual mistakes (a per-parent index, a position), so the count of styles
// that are not even valid before the upgrade says nothing about how often real suites make them.
import { chromium } from 'playwright';
import { readFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { prepareRelease } from './organic-drift-capture.mjs';

const OPERATION = '#operations-pets-get_pets';
const isTheControl = (element, operation) => element.classList.contains('opblock-summary-control') && element.closest(operation) !== null;

// An XPath with an index at every level, as a browser's "Copy full XPath" writes it.
function fullXPathOf(element) {
  const parts = [];
  for (let node = element; node && node.nodeType === 1; node = node.parentElement) {
    let index = 1;
    for (let sibling = node.previousElementSibling; sibling; sibling = sibling.previousElementSibling) {
      if (sibling.tagName === node.tagName) {
        index++;
      }
    }
    parts.unshift(`${node.tagName.toLowerCase()}[${index}]`);
  }
  return '/' + parts.join('/');
}

// [label, XPath or null for the absolute path taken from the older page]
const styles = [
  ['absolute path (Copy full XPath)', null],
  ['global index  (//button[contains(@class,"opblock-summary-control")])[1]', `(//button[contains(@class,'opblock-summary-control')])[1]`],
  ['per-parent index  //button[contains(@class,"opblock-summary-control")][1]', `//button[contains(@class,'opblock-summary-control')][1]`],
  ['aria-label contains  //button[contains(@aria-label,"pets")]', `//button[contains(@aria-label,'pets')]`],
  ['position  //div[3]//button', `//div[3]//button`],
  ['id + class  //*[@id="operations-pets-get_pets"]//button[contains(@class,"opblock-summary-control")]', `//*[@id='operations-pets-get_pets']//button[contains(@class,'opblock-summary-control')]`],
  ['content  //button[.//span[normalize-space()="GET"] and .//span[normalize-space()="/pets"]]', `//button[.//span[normalize-space()='GET'] and .//span[normalize-space()='/pets']]`],
];

async function open(browser, manifest, appDir) {
  const page = await browser.newPage({ viewport: manifest.viewport });
  await page.goto(pathToFileURL(join(appDir, manifest.page)).href, { waitUntil: 'networkidle' });
  await page.locator(OPERATION).waitFor();
  await page.waitForTimeout(700);
  return page;
}

async function resolveOnce(page, xpath) {
  const locator = page.locator(`xpath=${xpath}`);
  const count = await locator.count();
  if (count !== 1) {
    return { count, right: false };
  }
  return { count, right: await locator.evaluate(isTheControl, OPERATION), what: await locator.evaluate((e) => e.className.trim().replace(/\s+/g, '.')) };
}

async function main() {
  const [, , manifestPath, ...flags] = process.argv;
  if (!manifestPath) {
    console.error('Usage: node xpath-survival.mjs <xpath-manifest.json> [--channel NAME | --executable-path PATH]');
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
  const apps = [];
  for (const release of manifest.releases) {
    apps.push({ version: release.version, dir: await prepareRelease(manifest, dirname(manifestFile), release) });
  }

  const browser = await chromium.launch(options);
  try {
    const summary = styles.map(([label]) => ({ label, cells: [] }));
    const pairs = [];
    for (let p = 0; p + 1 < apps.length; p++) {
      const older = apps.at(p), newer = apps.at(p + 1);
      const pair = `${older.version} -> ${newer.version}`;
      pairs.push(pair);
      const oldPage = await open(browser, manifest, older.dir);
      const newPage = await open(browser, manifest, newer.dir);
      const absolute = await oldPage.locator(`${OPERATION} .opblock-summary-control`).evaluate(fullXPathOf);
      console.log(`\n== ${pair}`);
      let valid = 0, stillRight = 0, failsVisibly = 0, silentlyWrong = 0;
      for (const [index, [label, xpath]] of styles.entries()) {
        const expression = xpath ?? absolute;
        const before = await resolveOnce(oldPage, expression);
        if (!(before.count === 1 && before.right)) {
          console.log(`${label}\n    not valid in ${older.version} (matches ${before.count}${before.count === 1 ? ', the wrong element' : ''})`);
          summary.at(index).cells.push('not valid');
          continue;
        }
        valid++;
        const after = await resolveOnce(newPage, expression);
        let verdict, cell;
        if (after.count !== 1) {
          failsVisibly++;
          cell = 'fails visibly';
          verdict = after.count === 0 ? 'FAILS visibly (no match)' : `FAILS visibly (strict mode: ${after.count} matches)`;
        } else if (after.right) {
          stillRight++;
          cell = 'still right';
          verdict = 'still right';
        } else {
          silentlyWrong++;
          cell = 'SILENTLY WRONG';
          verdict = `SILENTLY WRONG (${after.what})`;
        }
        summary.at(index).cells.push(cell);
        console.log(`${label}\n    works in ${older.version}; in ${newer.version}: ${verdict}`);
      }
      console.log(`${styles.length} styles tried, ${valid} valid in ${older.version}. In ${newer.version}: ${stillRight} still right, ${failsVisibly} fail visibly, ${silentlyWrong} silently wrong.`);
      await oldPage.close();
      await newPage.close();
    }

    console.log(`\n== summary (${pairs.join(' | ')})`);
    for (const row of summary) {
      console.log(`${row.cells.join(' | ').padEnd(54)} ${row.label}`);
    }
  } finally {
    await browser.close();
  }
}

// Only runs when executed directly, like the other scripts here.
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  await main();
}
