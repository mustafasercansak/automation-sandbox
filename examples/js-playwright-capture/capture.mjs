// Reference capture script for non-.NET callers of automation-sandbox-heal (#522):
// Node.js + Playwright -> UiElementInfo-shaped JSON, usable directly with
// `automation-sandbox-heal --expected`/`--tree`. See
// https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json
// for the full JSON Schema this output follows.
//
// Mirrors (not a byte-for-byte port of) TestAutomation/WebDiscovery's
// PlaywrightDomCaptureScript.cs (roleOf/textOf/accessibleNameOf, same ARIA-role-to-
// ControlType table) and WebElementMapper.cs (ControlType/AutomationId priority), so a
// tree captured here scores consistently with the .NET-captured kind. Deliberately
// minimal: regular DOM only - no open Shadow DOM traversal, no same-origin iframe
// traversal, no MaxDepth/MaxElements bounding. For those, use
// AutomationSandbox.WebDiscovery from .NET.
//
// Usage:
//   node capture.mjs <url-or-local-html-path> <output.json>                # full --tree
//   node capture.mjs <url-or-local-html-path> <output.json> --selector CSS # single --expected element

import { chromium } from 'playwright';
import { writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';

function captureTree(selector) {
  const roleMap = {
    A: 'link', BUTTON: 'button', FORM: 'form', MAIN: 'main',
    SELECT: 'combobox', TABLE: 'table', TEXTAREA: 'textbox',
  };

  const ariaRoleToControlType = {
    alert: 'Text', alertdialog: 'Window', appbar: 'AppBar', article: 'Document',
    banner: 'Header', button: 'Button', calendar: 'Calendar', cell: 'DataItem',
    checkbox: 'CheckBox', columnheader: 'HeaderItem', combobox: 'ComboBox',
    custom: 'Custom', datagrid: 'DataGrid', dataitem: 'DataItem', dialog: 'Window',
    document: 'Document', edit: 'Edit', form: 'Group', grid: 'DataGrid',
    gridcell: 'DataItem', group: 'Group', header: 'Header', headeritem: 'HeaderItem',
    heading: 'Text', hyperlink: 'Hyperlink', image: 'Image', img: 'Image',
    link: 'Hyperlink', list: 'List', listbox: 'List', listitem: 'ListItem',
    log: 'StatusBar', main: 'Document', menu: 'Menu', menubar: 'MenuBar',
    menuitem: 'MenuItem', menuitemcheckbox: 'MenuItem', menuitemradio: 'MenuItem',
    navigation: 'Group', option: 'ListItem', pane: 'Pane', progressbar: 'ProgressBar',
    radio: 'RadioButton', radiobutton: 'RadioButton', radiogroup: 'Group',
    region: 'Group', row: 'DataItem', rowheader: 'HeaderItem', scrollbar: 'ScrollBar',
    search: 'Group', searchbox: 'Edit', semanticzoom: 'SemanticZoom',
    separator: 'Separator', slider: 'Slider', spinbutton: 'Spinner', spinner: 'Spinner',
    splitbutton: 'SplitButton', status: 'StatusBar', statusbar: 'StatusBar',
    tab: 'TabItem', tabitem: 'TabItem', tablist: 'Tab', tabpanel: 'Pane',
    switch: 'CheckBox', table: 'DataGrid', text: 'Text', textbox: 'Edit',
    thumb: 'Thumb', timer: 'StatusBar', titlebar: 'TitleBar', toolbar: 'ToolBar',
    tooltip: 'ToolTip', tree: 'Tree', treegrid: 'DataGrid', treeitem: 'TreeItem',
    window: 'Window',
  };

  const tagControlTypeFallback = {
    button: 'Button', input: 'Edit', textarea: 'Edit', select: 'ComboBox',
    table: 'DataGrid', a: 'Hyperlink', form: 'Group', main: 'Document',
    section: 'Group', fieldset: 'Group',
  };

  function roleOf(element) {
    if (element.getAttribute('role')) return element.getAttribute('role');
    if (element.tagName === 'INPUT') {
      const type = (element.getAttribute('type') || 'text').toLowerCase();
      if (type === 'button' || type === 'submit' || type === 'reset') return 'button';
      if (type === 'checkbox') return 'checkbox';
      if (type === 'radio') return 'radio';
      return 'textbox';
    }
    return roleMap[element.tagName] || '';
  }

  function textOf(element) {
    return (element.innerText || element.textContent || '').trim().replace(/\s+/g, ' ').slice(0, 160);
  }

  function accessibleNameOf(element) {
    return (element.getAttribute('aria-label')
      || element.getAttribute('title')
      || element.getAttribute('placeholder')
      || element.getAttribute('value')
      || textOf(element)
      || '').trim();
  }

  function controlTypeOf(element) {
    const role = roleOf(element);
    if (role) {
      const normalized = role.trim().toLowerCase();
      if (ariaRoleToControlType[normalized]) return ariaRoleToControlType[normalized];
      return normalized.charAt(0).toUpperCase() + normalized.slice(1);
    }
    const tag = element.tagName.toLowerCase();
    return tagControlTypeFallback[tag] || tag;
  }

  function automationIdOf(element) {
    return element.getAttribute('data-testid')
      || element.getAttribute('data-test')
      || element.id
      || element.getAttribute('name')
      || '';
  }

  function rectOf(element) {
    const rect = element.getBoundingClientRect();
    return { X: rect.x, Y: rect.y, Width: rect.width, Height: rect.height };
  }

  function isVisible(element) {
    const style = window.getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    const hiddenByAttribute = element.hidden || element.getAttribute('aria-hidden') === 'true';
    const hiddenByStyle = style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0;
    return !(hiddenByAttribute || hiddenByStyle || rect.width <= 0 || rect.height <= 0);
  }

  function mapNode(element, parentControlType, parentAutomationId, siblingIndex, siblingCount) {
    const visible = isVisible(element);
    const node = {
      ControlType: controlTypeOf(element),
      Name: accessibleNameOf(element),
      AutomationId: automationIdOf(element),
      ClassName: element.className && typeof element.className === 'string' ? element.className : element.tagName.toLowerCase(),
      BoundingRectangle: visible ? rectOf(element) : { X: 0, Y: 0, Width: 0, Height: 0 },
      ParentControlType: parentControlType,
      ParentAutomationId: parentAutomationId,
      SiblingIndex: siblingIndex,
      SiblingCount: siblingCount,
      Children: [],
    };

    const children = Array.from(element.children);
    node.Children = children.map((child, i) => mapNode(child, node.ControlType, node.AutomationId, i, children.length));
    return node;
  }

  if (selector) {
    // Single-element --expected snapshot: the stale locator's last known state, with
    // no Children - matches a single UiElementInfo object, not a tree.
    const target = document.querySelector(selector);
    if (!target) {
      throw new Error(`No element matched selector: ${selector}`);
    }
    const parent = target.parentElement;
    const siblings = parent ? Array.from(parent.children) : [target];
    const node = mapNode(target, '', '', siblings.indexOf(target), siblings.length);
    if (parent) {
      node.ParentControlType = controlTypeOf(parent);
      node.ParentAutomationId = automationIdOf(parent);
    }
    delete node.Children;
    return node;
  }

  return mapNode(document.body, '', '', 0, 1);
}

async function main() {
  const [, , target, outputPath, selectorFlag, selectorValue] = process.argv;
  if (!target || !outputPath) {
    console.error('Usage: node capture.mjs <url-or-local-html-path> <output.json> [--selector CSS]');
    process.exit(1);
  }
  if (selectorFlag && selectorFlag !== '--selector') {
    console.error(`Unrecognized argument: ${selectorFlag}`);
    process.exit(1);
  }
  const selector = selectorFlag === '--selector' ? selectorValue : undefined;
  if (selectorFlag === '--selector' && !selector) {
    console.error('--selector requires a CSS selector value.');
    process.exit(1);
  }

  // A bare local path (e.g. fixtures/page-old.html) works on every OS without the
  // caller having to construct a file:// URL by hand - resolve() + pathToFileURL()
  // handles Windows drive letters and backslashes correctly, which naive string
  // concatenation ("file://" + path) does not.
  const url = /^[a-z][a-z0-9+.-]*:/i.test(target) || !existsSync(target)
    ? target
    : pathToFileURL(resolve(target)).href;

  const browser = await chromium.launch();
  try {
    const page = await browser.newPage();
    await page.goto(url, { waitUntil: 'networkidle' });
    const tree = await page.evaluate(captureTree, selector);
    await writeFile(outputPath, JSON.stringify(tree, null, 2));
    console.log(`Captured tree written to ${outputPath}`);
  } finally {
    await browser.close();
  }
}

main();
