#!/usr/bin/env python3
"""Regenerates docs/assets/demo.gif — the README hero.

It is a terminal-style render of a REAL `HeuristicHealingQuickstart` run. Every
number shown (the 1.00/1.00/1.00/0.54/0.98 signal weights, score 0.90, runner-up
0.60, Outcome "accepted", Source "heuristic", EvidenceCoverage 1.0) is verbatim
from the healing report that run emits.

Reproduce the source numbers, then regenerate:

    SELF_HEALING_REPORT_PATH=/tmp/r.json \
      dotnet run --project samples/HeuristicHealingQuickstart
    # confirm /tmp/r.json matches the SEQ / BARS values below, then:
    python3 docs/blog/gen-demo-gif.py            # writes ./demo.gif
    magick demo.gif -layers optimize docs/assets/demo.gif

Needs Pillow and a DejaVu Sans Mono font. See docs/blog/demo-storyboard.md for
the shot list this follows.
"""
from PIL import Image, ImageDraw, ImageFont

W, H = 1040, 648
S = 2
PAD = 30
LH = 27
FS = 18
FONT = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", FS * S)
BOLD = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf", FS * S)

BG = (13, 17, 23)
BAR = (22, 27, 34)
FG = (201, 209, 217)
DIM = (125, 133, 144)
GREEN = (63, 185, 80)
BLUE = (88, 166, 255)
PROMPT = (57, 197, 187)
RUNNER = (88, 110, 150)

BARS = [  # label, weight (real ScoreBreakdown), winner frac, runner-up frac (best decoy)
    ("control type",     "1.00", 1.00, 1.00),
    ("parent type",      "1.00", 1.00, 1.00),
    ("sibling position", "1.00", 1.00, 0.67),
    ("name similarity",  "0.54", 0.54, 0.08),
    ("position",         "0.98", 0.98, 0.31),
]

SEQ = [
    [("$ ", PROMPT, True), ("dotnet run --project samples/HeuristicHealingQuickstart", FG, False)],
    None,
    [("[SelfHealingEngine] ", DIM, False), ("locator 'Checkout.SubmitButton' threw ElementNotFoundException", FG, False)],
    [("                    classified as a locator-resolution failure. Mode is AutoHeal.", DIM, False)],
    None,
    [("[SelfHealing] ", DIM, False), ("'btn-submit' (Button) not found — scoring 5 live candidates", FG, False)],
    None,
    "BARS",
    [("   → best  ", FG, False), ("checkout-confirm", BLUE, True), ("  “Confirm order”", FG, False),
     ("     score 0.90", GREEN, True), ("   runner-up 0.60", DIM, False)],
    None,
    [("[SelfHealing] ", DIM, False), ("healed → retrying the click on checkout-confirm …", FG, False)],
    [("✓ ", GREEN, True), ("retry passed — locator repository updated  (heuristic, 0 tokens)", FG, False)],
    None,
    [("  healing-report.json  ", DIM, False), ("(schema v8)", DIM, False)],
    [("    \"Outcome\": ", DIM, False), ("\"accepted\"", GREEN, False),
     ("   \"Score\": ", DIM, False), ("0.90", FG, False),
     ("   \"RunnerUpScore\": ", DIM, False), ("0.60", FG, False)],
    [("    \"Source\": ", DIM, False), ("\"heuristic\"", FG, False),
     ("   \"AgreedProviders\": ", DIM, False), ("null", FG, False),
     ("   \"EvidenceCoverage\": ", DIM, False), ("1.0", FG, False)],
]


def base():
    img = Image.new("RGB", (W * S, H * S), BG)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W * S, 38 * S], fill=BAR)
    for i, c in enumerate([(255, 95, 86), (255, 189, 46), (39, 201, 63)]):
        cx = (PAD + i * 22) * S
        d.ellipse([cx, 13 * S, cx + 13 * S, 26 * S], fill=c)
    d.text(((W / 2 - 165) * S, 11 * S), "automation-sandbox  —  self-healing demo", font=FONT, fill=DIM)
    return img, d


def draw_bars(d, y):
    lx = PAD * S + 12 * S
    wx = lx + 200 * S
    b1 = wx + 74 * S
    b2 = b1 + 165 * S
    mw = 130 * S
    d.text((lx, y), "signal", font=BOLD, fill=DIM)
    d.text((wx, y), "weight", font=BOLD, fill=DIM)
    d.text((b1, y), "candidate", font=BOLD, fill=DIM)
    d.text((b2, y), "best decoy", font=BOLD, fill=DIM)
    y += LH * S
    for label, wt, wf, rf in BARS:
        d.text((lx, y), label, font=FONT, fill=FG)
        d.text((wx, y), wt, font=FONT, fill=FG)
        oy = y + 3 * S
        bh = 14 * S
        d.rectangle([b1, oy, b1 + int(mw * wf), oy + bh], fill=GREEN)
        d.rectangle([b2, oy, b2 + int(mw * rf), oy + bh], fill=RUNNER)
        y += LH * S
    return y


def render(upto):
    img, d = base()
    y = 54 * S
    for idx in range(upto):
        seg = SEQ[idx]
        if seg is None:
            y += LH * S
            continue
        if seg == "BARS":
            y = draw_bars(d, y) + 4 * S
            continue
        x = PAD * S
        for txt, col, b in seg:
            f = BOLD if b else FONT
            d.text((x, y), txt, font=f, fill=col)
            x += int(d.textlength(txt, font=f))
        y += LH * S
    return img.resize((W, H), Image.LANCZOS)


frames, dur = [], []

cmd = "dotnet run --project samples/HeuristicHealingQuickstart"
_, dm = base()
pw = int(dm.textlength("$ ", font=BOLD))
for i in range(0, len(cmd) + 1, 3):
    im, d = base()
    d.text((PAD * S, 54 * S), "$ ", font=BOLD, fill=PROMPT)
    d.text((PAD * S + pw, 54 * S), cmd[:i], font=FONT, fill=FG)
    cx = PAD * S + pw + int(d.textlength(cmd[:i], font=FONT))
    d.rectangle([cx, 54 * S, cx + 11 * S, 54 * S + FS * S], fill=BLUE)
    frames.append(im.resize((W, H), Image.LANCZOS)); dur.append(55)
dur[-1] = 500

for upto in range(2, len(SEQ) + 1):
    frames.append(render(upto))
    seg = SEQ[upto - 1]
    if seg is None:
        dur.append(150)
    elif seg == "BARS":
        dur.append(1000)
    elif "→ best" in seg[0][0] or "retry passed" in seg[0][0]:
        dur.append(1000)
    elif "healing-report" in seg[0][0]:
        dur.append(500)
    elif seg[0][0].strip().startswith('"'):
        dur.append(650)
    else:
        dur.append(480)

frames.append(render(len(SEQ))); dur.append(3200)

frames[0].save("demo.gif", save_all=True, append_images=frames[1:],
               duration=dur, loop=0, optimize=False, disposal=1)
print("frames", len(frames), "ms", sum(dur))
