// Reproduces the organic drift dataset (#663): downloads each pinned release listed in a manifest, checks it against the recorded
// hashes, opens a fixed probe page with it, replays the manifest's navigation steps and writes one UiElementInfo tree per state.
//
//   node organic-drift-capture.mjs <manifest.json> <output-dir> [--channel NAME | --executable-path PATH]
//
// Nothing is renamed, deleted or edited: the trees are what the browser reported for the real release. The page and the OpenAPI
// document inside it are ours (inputs); the application code is the downloaded release, unchanged.
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { copyFile, mkdir, mkdtemp, readFile, writeFile } from 'node:fs/promises';
import { tmpdir, platform, release, arch } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium } from 'playwright';
import { captureTreeFromPage } from './capture.mjs';

async function download(url, expectedSha256, expectedIntegrity, directory) {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`Download failed (${response.status}) for ${url}`);
  }
  const bytes = Buffer.from(await response.arrayBuffer());
  const sha256 = createHash('sha256').update(bytes).digest('hex');
  const integrity = `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
  if (sha256 !== expectedSha256 || integrity !== expectedIntegrity) {
    throw new Error(`Hash mismatch for ${url}: sha256 ${sha256}, integrity ${integrity}. The recorded release is not what was downloaded.`);
  }
  const file = join(directory, 'release.tgz');
  await writeFile(file, bytes);
  return file;
}

async function captureRelease(browser, manifest, manifestDir, releaseInfo, outputDir) {
  const work = await mkdtemp(join(tmpdir(), `drift-${releaseInfo.version}-`));
  const archive = await download(releaseInfo.tarball, releaseInfo.sha256, releaseInfo.integrity, work);
  const appDir = join(work, 'app');
  await mkdir(appDir);
  // The archive's top directory is "package"; its contents are served as-is next to the probe page.
  execFileSync('tar', ['-xzf', archive, '-C', appDir, '--strip-components=1']);
  await copyFile(join(manifestDir, manifest.page), join(appDir, manifest.page));

  const context = await browser.newContext({ viewport: manifest.viewport });
  const page = await context.newPage();
  await page.goto(pathToFileURL(join(appDir, manifest.page)).href, { waitUntil: 'networkidle' });

  const written = [];
  for (const state of manifest.states) {
    for (const step of state.steps) {
      await page.click(step.click);
    }
    await page.waitForSelector(state.waitFor, { state: 'visible' });
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(manifest.settleMs);
    const tree = await captureTreeFromPage(page, null);
    const target = join(outputDir, 'trees', releaseInfo.version, `${state.name}.json`);
    await mkdir(dirname(target), { recursive: true });
    await writeFile(target, JSON.stringify(tree, null, 2));
    written.push(target);
  }
  await context.close();
  return written;
}

async function main() {
  const [, , manifestPath, outputDir, ...flags] = process.argv;
  if (!manifestPath || !outputDir) {
    console.error('Usage: node organic-drift-capture.mjs <manifest.json> <output-dir> [--channel NAME | --executable-path PATH]');
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
  const browser = await chromium.launch(options);
  try {
    for (const releaseInfo of manifest.releases) {
      const files = await captureRelease(browser, manifest, dirname(manifestFile), releaseInfo, resolve(outputDir));
      console.log(`${releaseInfo.version}: ${files.length} state(s) captured`);
    }
    const environment = {
      capturedAt: new Date().toISOString(),
      browser: `chromium ${browser.version()}`,
      playwright: JSON.parse(await readFile(join(dirname(fileURLToPath(import.meta.url)), 'package.json'), 'utf8')).dependencies?.playwright,
      node: process.version,
      os: `${platform()} ${release()} ${arch()}`,
    };
    await mkdir(resolve(outputDir), { recursive: true });
    await writeFile(join(resolve(outputDir), 'capture-environment.json'), JSON.stringify(environment, null, 2));
  } finally {
    await browser.close();
  }
}

// Only runs when executed directly, like capture.mjs, so a test can import from it.
if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  await main();
}
