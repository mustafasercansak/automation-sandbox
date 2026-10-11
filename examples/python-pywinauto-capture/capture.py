"""Reference capture script for non-.NET callers of automation-sandbox-heal (#532):
Python + pywinauto -> UiElementInfo-shaped JSON for Windows desktop apps, the same role
examples/js-playwright-capture plays for the web.

Uses pywinauto's `uia` backend (pywinauto.windows.uia_element_info.UIAElementInfo),
which wraps the same Windows UI Automation COM API FlaUI/UIA3 uses - so this produces
genuinely equivalent data, not just a matching JSON shape. API verified against
pywinauto's actual source before writing this (control_type/name/auto_id/class_name/
rectangle/parent/children()), not guessed.

Windows-only: pywinauto's uia backend needs real Windows UI Automation COM APIs, the
same constraint Discovery/FlaUI have. See
https://mustafasercansak.github.io/automation-sandbox/schemas/ui-element-info.schema.json
for the full JSON Schema this output follows.

Usage:
    python capture.py <process-path-or-name> <output.json>                 # full --tree
    python capture.py <process-path-or-name> <output.json> --name "X"      # single --expected element, matched by Name
    python capture.py <process-name> <output.json> --attach [...]          # attach instead of launching
"""
import argparse
import json
import sys

from pywinauto.application import Application


def map_node(element_info, parent_control_type, parent_automation_id, sibling_index, sibling_count):
    rect = element_info.rectangle
    children = element_info.children()
    return {
        "ControlType": element_info.control_type or "",
        "Name": element_info.name or "",
        "AutomationId": element_info.auto_id or "",
        "ClassName": element_info.class_name or "",
        "BoundingRectangle": {
            "X": rect.left,
            "Y": rect.top,
            "Width": rect.right - rect.left,
            "Height": rect.bottom - rect.top,
        },
        "ParentControlType": parent_control_type,
        "ParentAutomationId": parent_automation_id,
        "SiblingIndex": sibling_index,
        "SiblingCount": sibling_count,
        "Children": [
            map_node(child, element_info.control_type or "", element_info.auto_id or "", i, len(children))
            for i, child in enumerate(children)
        ],
    }


def find_by_name(element_info, name):
    if element_info.name == name:
        return element_info
    for child in element_info.children():
        found = find_by_name(child, name)
        if found is not None:
            return found
    return None


def main():
    parser = argparse.ArgumentParser(description="Capture a Windows desktop UI tree as UiElementInfo JSON.")
    parser.add_argument("target", help="Path to an executable to launch, or a process name/path to attach to with --attach")
    parser.add_argument("output", help="Output JSON file path")
    parser.add_argument("--name", help="Capture only the single element with this exact Name (for --expected); omit for a full tree (for --tree)")
    parser.add_argument("--attach", action="store_true", help="Attach to an already-running process instead of launching a new one")
    args = parser.parse_args()

    app = Application(backend="uia")
    if args.attach:
        app.connect(path=args.target)
    else:
        app.start(args.target)

    window = app.top_window().find()
    root_info = window.element_info

    if args.name:
        target = find_by_name(root_info, args.name)
        if target is None:
            print(f"No element found with Name: {args.name}", file=sys.stderr)
            sys.exit(2)
        node = map_node(target, "", "", 0, 1)
        del node["Children"]
        result = node
    else:
        result = map_node(root_info, "", "", 0, 1)

    with open(args.output, "w", encoding="utf-8") as f:
        json.dump(result, f, indent=2)
    print(f"Captured tree written to {args.output}")


if __name__ == "__main__":
    main()
