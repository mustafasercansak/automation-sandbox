// Calls automation-sandbox-heal from a Node.js test when a locator stopped matching (#618).
//
// A `@playwright/test` failure for a missing element carries the locator in its message
// ("waiting for getByTestId('connect')"). Playwright prints a locator in the same
// getByTestId / getByRole / getByText form `automation-sandbox-heal --selector` reads, so
// no snapshot recorded while the test was green is needed (#616). The result is a review
// aid for a broken suite, not an auto-fix: with only a selector the evidence is partial.
//
// Set HEAL_CLI to run something other than `automation-sandbox-heal` on the PATH.

import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { captureTree } from './capture.mjs';

// The locator Playwright was waiting for, or undefined when the message names none
// (a CSS or XPath `locator(...)` is returned too; the CLI reports it as unsupported).
export function locatorFromError(message) {
  // Playwright colors the call log with ANSI escapes, which would hide the end of the line.
  const plain = (message ?? '').replace(/\[[0-9;]*m/g, '');
  const match = /waiting for ((?:getBy\w+|locator)\(.*\))\s*$/m.exec(plain);
  return match ? match[1] : undefined;
}

// Captures the live page, asks the CLI for the closest element and returns a printable
// suggestion. Never throws for a CLI problem: a heal suggestion must not mask the
// original test failure.
export async function suggestHeal(page, locator, { cli = process.env.HEAL_CLI ?? 'automation-sandbox-heal', profile = 'balanced' } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'heal-'));
  try {
    const treePath = join(dir, 'tree.json');
    writeFileSync(treePath, JSON.stringify(await page.evaluate(captureTree, undefined)));

    // --compact keeps the result small; without it the output can exceed the default
    // spawnSync buffer on a real page and the call comes back with status null.
    const run = spawnSync(cli, ['--selector', locator, '--tree', treePath, '--profile', profile, '--compact'], {
      encoding: 'utf8',
      maxBuffer: 16 * 1024 * 1024,
    });
    if (run.error || run.status === null) {
      return `heal: could not run '${cli}': ${run.error?.message ?? 'no exit status'}`;
    }
    if (run.status === 2) {
      return `heal: ${locator} not understood: ${run.stderr.trim()}`;
    }

    let result;
    try {
      result = JSON.parse(run.stdout).Result;
    } catch {
      return `heal: '${cli}' printed something that is not heal JSON (an older CLI without --selector/--compact?)`;
    }
    const best = result.Matched;
    if (!best) {
      return `heal: no candidate for ${locator}`;
    }
    const verdict = run.status === 0 ? 'CONFIDENT' : 'not confident - review it';
    return `heal: ${locator} (partial evidence)\n` +
      `  closest: ${best.ControlType} name='${best.Name}' id='${best.AutomationId}' score=${result.Score.toFixed(2)} (${verdict})`;
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}
