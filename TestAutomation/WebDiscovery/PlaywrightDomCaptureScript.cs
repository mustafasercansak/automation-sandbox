namespace WebDiscovery
{
    public static class PlaywrightDomCaptureScript
    {
        public const string JavaScript =
@"() => {
  const roleMap = {
    A: 'link',
    BUTTON: 'button',
    FORM: 'form',
    MAIN: 'main',
    SELECT: 'combobox',
    TABLE: 'table',
    TEXTAREA: 'textbox'
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

  function cssSelectorOf(element) {
    if (element.id) return '#' + CSS.escape(element.id);
    const testId = element.getAttribute('data-testid') || element.getAttribute('data-test');
    if (testId) return `[data-testid=""${CSS.escape(testId)}""]`;
    const name = element.getAttribute('name');
    if (name) return `${element.tagName.toLowerCase()}[name=""${CSS.escape(name)}""]`;
    return element.tagName.toLowerCase();
  }

  function rectOf(element) {
    const rect = element.getBoundingClientRect();
    return { X: rect.x, Y: rect.y, Width: rect.width, Height: rect.height };
  }

  function visibilityOf(element) {
    const win = (element.ownerDocument && element.ownerDocument.defaultView) || window;
    const style = win.getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    const hiddenByAttribute = element.hidden || element.getAttribute('aria-hidden') === 'true';
    const hiddenByStyle = style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse' || Number(style.opacity) === 0;
    const hasNoBox = rect.width <= 0 || rect.height <= 0;
    const offscreen = rect.bottom < 0 || rect.right < 0 || rect.top > win.innerHeight || rect.left > win.innerWidth;
    return {
      IsHidden: hiddenByAttribute || hiddenByStyle || hasNoBox,
      IsOffscreen: offscreen
    };
  }

  function frameSelectorOf(element) {
    const name = element.getAttribute('name');
    if (name) return `iframe[name='${name.replace(/'/g, '\\\'')}']`;
    const testId = element.getAttribute('data-testid') || element.getAttribute('data-test');
    if (testId) return `iframe[data-testid='${testId.replace(/'/g, '\\\'')}']`;
    if (element.id) return `iframe#${CSS.escape(element.id)}`;
    const src = element.getAttribute('src');
    if (src) return `iframe[src='${src.replace(/'/g, '\\\'')}']`;
    return 'iframe';
  }

  function scopeOf(element, parentScope) {
    const root = element.getRootNode && element.getRootNode();
    if (root && root.toString && root.toString() === '[object ShadowRoot]') return 'shadow-dom';
    if (element.ownerDocument && element.ownerDocument.defaultView && element.ownerDocument.defaultView.frameElement) return 'iframe';
    return parentScope || 'light-dom';
  }

  function frameUrlOf(element) {
    const frameElement = element.ownerDocument && element.ownerDocument.defaultView
      ? element.ownerDocument.defaultView.frameElement
      : null;
    if (frameElement) return element.ownerDocument.location.href || '';
    if (element.tagName === 'IFRAME') return element.getAttribute('src') || '';
    return '';
  }

  function walk(element, parentScope, frameAncestry) {
    const currentScope = scopeOf(element, parentScope);
    const visibility = visibilityOf(element);
    const currentAncestry = frameAncestry || [];

    const childItems = [];
    const directChildren = Array.from(element.children).filter(child => child && child.nodeType === 1);
    childItems.push(...directChildren.map(child => ({ element: child, ancestry: currentAncestry })));

    if (element.shadowRoot) {
      const shadowChildren = Array.from(element.shadowRoot.children).filter(child => child && child.nodeType === 1);
      childItems.push(...shadowChildren.map(child => ({ element: child, ancestry: currentAncestry })));
    }

    let crossOriginFrame = false;
    if (element.tagName === 'IFRAME') {
      try {
        const doc = element.contentDocument;
        if (doc && doc.body) {
          const iframeSelector = frameSelectorOf(element);
          const nestedAncestry = [...currentAncestry, iframeSelector];
          childItems.push({ element: doc.body, ancestry: nestedAncestry });
        } else {
          // contentDocument reads as null across an origin boundary instead of throwing.
          crossOriginFrame = true;
        }
      } catch {
        // Cross-origin iframes cannot be inspected from the parent page. Playwright can still
        // capture them by evaluating this script inside the frame context directly. Flagged so
        // the snapshot distinguishes a frame we were not allowed to read from an empty one.
        crossOriginFrame = true;
      }
    }

    const children = childItems.map(item => walk(item.element, currentScope, item.ancestry));
    return {
      TagName: element.tagName.toLowerCase(),
      Role: roleOf(element),
      AccessibleName: accessibleNameOf(element),
      Text: textOf(element),
      Id: element.id || '',
      NameAttribute: element.getAttribute('name') || '',
      InputType: element.getAttribute('type') || '',
      TestId: element.getAttribute('data-testid') || element.getAttribute('data-test') || '',
      ClassName: typeof element.className === 'string' ? element.className : '',
      CssSelector: cssSelectorOf(element),
      IsHidden: visibility.IsHidden,
      IsOffscreen: visibility.IsOffscreen,
      IsCrossOriginFrame: crossOriginFrame,
      TreeScope: currentScope,
      FrameUrl: frameUrlOf(element),
      FrameAncestry: currentAncestry,
      BoundingRectangle: rectOf(element),
      Children: children
    };
  }

  return walk(document.body, 'light-dom', []);
}";
    }
}
