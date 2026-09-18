using System.Globalization;

namespace AutomationSandbox.WebDiscovery
{
    /// <summary>Browser-side DOM capture source used with Playwright page evaluation.</summary>
    public static class PlaywrightDomCaptureScript
    {
        // Shared between the unbounded JavaScript constant below and BuildJavaScript's bounded
        // template, so the ~10 pure DOM-reading helpers (role/name/selector/visibility/frame
        // detection) have exactly one definition instead of two copies drifting apart.
        private const string HelperFunctionsJavaScript =
@"
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

  function structuralSelectorOf(element) {
    const parts = [];
    for (let current = element; current && current.nodeType === 1; current = current.parentElement) {
      const tagName = current.tagName.toLowerCase();
      const siblings = Array.from(current.parentElement ? current.parentElement.children : []).filter(
        sibling => sibling.tagName === current.tagName);
      const index = siblings.indexOf(current) + 1;
      parts.unshift(siblings.length > 1 ? `${tagName}:nth-of-type(${index})` : tagName);
      if (tagName === 'body') break;
    }
    return parts.join(' > ');
  }

  function cssSelectorOf(element) {
    if (element.id) return '#' + CSS.escape(element.id);
    const testId = element.getAttribute('data-testid') || element.getAttribute('data-test');
    if (testId) return `[data-testid=""${CSS.escape(testId)}""]`;
    const name = element.getAttribute('name');
    if (name) return `${element.tagName.toLowerCase()}[name=""${CSS.escape(name)}""]`;
    return structuralSelectorOf(element);
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
    return structuralSelectorOf(element);
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
";

        /// <summary>JavaScript expression that captures the regular DOM, open shadow roots, and accessible same-origin frames.</summary>
        public const string JavaScript =
            "() => {" + HelperFunctionsJavaScript +
@"
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
      IsStructuralCssSelector: !element.id
        && !element.getAttribute('data-testid')
        && !element.getAttribute('data-test')
        && !element.getAttribute('name'),
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

        // Placeholder-substituted rather than C#-interpolated: the JS below is full of its own
        // `${...}` template-literal syntax (inherited from HelperFunctionsJavaScript), and making
        // this an interpolated C# string would require escaping every brace in that unrelated
        // syntax. Token replacement keeps the JS readable as JS.
        private const string BoundedWalkTemplate =
@"
  const __budget = {
    maxDepth: __MAX_DEPTH__,
    maxElements: __MAX_ELEMENTS__,
    deadline: Date.now() + __TIMEOUT_MS__,
    capturedCount: 0,
    hitMaxDepth: false,
    hitMaxElements: false,
    timedOut: false
  };

  function walk(element, parentScope, frameAncestry, depth) {
    const currentScope = scopeOf(element, parentScope);
    const visibility = visibilityOf(element);
    const currentAncestry = frameAncestry || [];

    __budget.capturedCount++;
    if (__budget.capturedCount >= __budget.maxElements) { __budget.hitMaxElements = true; }
    if (depth >= __budget.maxDepth) { __budget.hitMaxDepth = true; }
    if (Date.now() > __budget.deadline) { __budget.timedOut = true; }

    const withinBudget = !__budget.hitMaxElements && !__budget.hitMaxDepth && !__budget.timedOut;
    let crossOriginFrame = false;
    let children = [];

    if (withinBudget) {
      const childItems = [];
      const directChildren = Array.from(element.children).filter(child => child && child.nodeType === 1);
      childItems.push(...directChildren.map(child => ({ element: child, ancestry: currentAncestry })));

      if (element.shadowRoot) {
        const shadowChildren = Array.from(element.shadowRoot.children).filter(child => child && child.nodeType === 1);
        childItems.push(...shadowChildren.map(child => ({ element: child, ancestry: currentAncestry })));
      }

      if (element.tagName === 'IFRAME') {
        try {
          const doc = element.contentDocument;
          if (doc && doc.body) {
            const iframeSelector = frameSelectorOf(element);
            const nestedAncestry = [...currentAncestry, iframeSelector];
            childItems.push({ element: doc.body, ancestry: nestedAncestry });
          } else {
            crossOriginFrame = true;
          }
        } catch {
          crossOriginFrame = true;
        }
      }

      for (const item of childItems) {
        if (__budget.capturedCount >= __budget.maxElements) { __budget.hitMaxElements = true; break; }
        if (Date.now() > __budget.deadline) { __budget.timedOut = true; break; }
        children.push(walk(item.element, currentScope, item.ancestry, depth + 1));
      }
    }

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
      IsStructuralCssSelector: !element.id
        && !element.getAttribute('data-testid')
        && !element.getAttribute('data-test')
        && !element.getAttribute('name'),
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

  const __root = walk(document.body, 'light-dom', [], 0);
  __root.HitMaxDepth = __budget.hitMaxDepth;
  __root.HitMaxElements = __budget.hitMaxElements;
  __root.TimedOut = __budget.timedOut;
  __root.CapturedCount = __budget.capturedCount;
  return __root;
}";

        /// <summary>Builds a DOM capture script that enforces <see cref="WebDiscoveryOptions" /> traversal
        /// bounds instead of the unbounded <see cref="JavaScript" /> walk, stamping <c>HitMaxDepth</c>,
        /// <c>HitMaxElements</c>, <c>TimedOut</c>, and <c>CapturedCount</c> onto the returned root element
        /// so a cut-short capture is observable rather than silently missing nodes. Defaults to
        /// <see cref="WebDiscoveryOptions.Default" /> (MaxDepth 25, MaxElements 5000, Timeout 10s) when
        /// <paramref name="options" /> is omitted, mirroring <c>Discovery.DiscoveryOptions</c>.</summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="options" /> has a negative <c>MaxDepth</c>, a <c>MaxElements</c> less than one,
        /// or a non-positive <c>Timeout</c>.</exception>
        public static string BuildJavaScript(WebDiscoveryOptions? options = null)
        {
            var effective = options ?? WebDiscoveryOptions.Default;
            if (effective.MaxDepth < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(effective.MaxDepth), "MaxDepth must be zero or greater.");
            }

            if (effective.MaxElements < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(effective.MaxElements), "MaxElements must be at least one.");
            }

            if (effective.Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(effective.Timeout), "Timeout must be greater than zero.");
            }

            var timeoutMs = Math.Max(1, (long)effective.Timeout.TotalMilliseconds);
            var boundedWalk = BoundedWalkTemplate
                .Replace("__MAX_DEPTH__", effective.MaxDepth.ToString(CultureInfo.InvariantCulture))
                .Replace("__MAX_ELEMENTS__", effective.MaxElements.ToString(CultureInfo.InvariantCulture))
                .Replace("__TIMEOUT_MS__", timeoutMs.ToString(CultureInfo.InvariantCulture));

            return "() => {" + HelperFunctionsJavaScript + boundedWalk;
        }
    }
}
