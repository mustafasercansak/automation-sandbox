#!/usr/bin/env python3
"""
Lightweight DORA & engine-reliability metrics calculator for Automation Sandbox.

Computes, over a rolling window:
1. Deployment Frequency (DF)      - GitHub Releases published in the window
2. Lead Time for Changes (LTTC)   - PR created -> merged, for PRs merged in the window
3. Change Failure Rate (CFR)      - share of releases/merges that needed a hotfix
4. Time to Restore Service (MTTR) - bug/correctness issue created -> closed, in the window

It also surfaces the self-healing engine's configured reliability gates. Those are
configuration, not measurements: the values are read straight out of SimilarityWeights.cs
so the report cannot silently drift from the code.

No external services. Reads the public GitHub REST API (a token lifts the rate limit).
On a fetch failure the script exits non-zero rather than emitting a report built on
empty data - a green "Elite" report drawn from a failed API call is worse than none.
"""

import argparse
import datetime
import json
import os
import re
import sys
import urllib.error
import urllib.request


class GitHubFetchError(RuntimeError):
    pass


def _parse_ts(value):
    """Parse a GitHub ISO-8601 timestamp (always UTC, 'Z' suffix)."""
    return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))


def fetch_all(repo_owner, repo_name, endpoint, token=None):
    """Fetch every page of a list endpoint, following the RFC-5988 Link header."""
    headers = {
        "User-Agent": "DORA-Metrics-Calculator",
        "Accept": "application/vnd.github+json",
    }
    if token:
        headers["Authorization"] = f"token {token}"

    url = (
        f"https://api.github.com/repos/{repo_owner}/{repo_name}/{endpoint}"
        "?state=all&per_page=100&sort=created&direction=desc"
    )
    items = []
    while url:
        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=30) as resp:
                page = json.loads(resp.read().decode())
                link = resp.headers.get("Link", "")
        except (urllib.error.URLError, TimeoutError, ValueError) as exc:
            raise GitHubFetchError(f"GitHub API request failed for '{endpoint}': {exc}") from exc

        if not isinstance(page, list):
            raise GitHubFetchError(
                f"GitHub API returned {type(page).__name__}, not a list, for '{endpoint}': "
                f"{str(page)[:200]}"
            )
        items.extend(page)

        # Follow the Link header to the end. We can't stop early on created-desc order:
        # a PR created long ago can still have merged within the window.
        next_match = re.search(r'<([^>]+)>;\s*rel="next"', link)
        url = next_match.group(1) if next_match else None

    return items


def get_github_data(repo_owner, repo_name, token=None):
    prs = fetch_all(repo_owner, repo_name, "pulls", token)
    releases = fetch_all(repo_owner, repo_name, "releases", token)
    raw_issues = fetch_all(repo_owner, repo_name, "issues", token)
    # The issues endpoint also returns pull requests; drop them so PRs are not
    # double-counted as "restored bugs" in MTTR.
    issues = [i for i in raw_issues if "pull_request" not in i]
    return prs, releases, issues


def _rate(count, total):
    return (count / total * 100.0) if total else None


def calculate_dora(prs, releases, issues, days=30):
    now = datetime.datetime.now(datetime.timezone.utc)
    cutoff = now - datetime.timedelta(days=days)

    # 1. Deployment Frequency
    recent_releases = [
        r for r in releases
        if r.get("published_at") and _parse_ts(r["published_at"]) >= cutoff
    ]
    df_count = len(recent_releases)
    if df_count >= 4:
        df_rating = "Elite (weekly or more)"
    elif df_count >= 1:
        df_rating = "High (monthly)"
    else:
        df_rating = "No releases in window"

    # 2. Lead Time for Changes
    merged_prs = [
        pr for pr in prs
        if pr.get("merged_at") and _parse_ts(pr["merged_at"]) >= cutoff
    ]
    lead_times_hours = [
        (_parse_ts(pr["merged_at"]) - _parse_ts(pr["created_at"])).total_seconds() / 3600.0
        for pr in merged_prs
    ]
    avg_lead_time = sum(lead_times_hours) / len(lead_times_hours) if lead_times_hours else None
    if avg_lead_time is None:
        lttc_rating = "n/a - no PRs merged in window"
    elif avg_lead_time <= 4:
        lttc_rating = "Elite (< 4 hours)"
    elif avg_lead_time <= 24:
        lttc_rating = "High (< 1 day)"
    elif avg_lead_time <= 168:
        lttc_rating = "Medium (< 1 week)"
    else:
        lttc_rating = "Low (> 1 week)"

    # 3. Change Failure Rate: of the changes shipped, how many needed a follow-up
    # hotfix. Approximated by PRs/issues that are explicitly a hotfix or a regression
    # - not every bug-fix PR (in this repo almost every PR is a bug fix by design, so
    # that would carry no signal).
    def _is_hotfix(pr):
        ref = pr.get("head", {}).get("ref", "").lower()
        title = pr.get("title", "").lower()
        labels = {l.get("name", "").lower() for l in pr.get("labels", [])}
        return (
            ref.startswith("hotfix/")
            or "hotfix" in title
            or title.startswith("revert ")
            or "regression" in labels
            or "release-blocker" in labels
        )

    hotfix_prs = [pr for pr in merged_prs if _is_hotfix(pr)]
    total_merged = len(merged_prs)
    cfr_percentage = _rate(len(hotfix_prs), total_merged)
    if cfr_percentage is None:
        cfr_rating = "n/a - no PRs merged in window"
    elif cfr_percentage <= 15.0:
        cfr_rating = "Elite (0-15%)"
    elif cfr_percentage <= 30.0:
        cfr_rating = "High (16-30%)"
    else:
        cfr_rating = "Low (> 30%)"

    # 4. Time to Restore Service
    closed_bugs = [
        i for i in issues
        if i.get("state") == "closed" and i.get("closed_at")
        and any(l.get("name") in ("bug", "correctness") for l in i.get("labels", []))
        and _parse_ts(i["closed_at"]) >= cutoff
    ]
    mttr_hours = [
        (_parse_ts(b["closed_at"]) - _parse_ts(b["created_at"])).total_seconds() / 3600.0
        for b in closed_bugs
    ]
    avg_mttr = sum(mttr_hours) / len(mttr_hours) if mttr_hours else None
    if avg_mttr is None:
        mttr_rating = "n/a - no bug issues closed in window"
    elif avg_mttr <= 24:
        mttr_rating = "Elite (< 1 day)"
    elif avg_mttr <= 168:
        mttr_rating = "High (< 1 week)"
    else:
        mttr_rating = "Medium (> 1 week)"

    return {
        "period_days": days,
        "generated_at": now.strftime("%Y-%m-%d %H:%M:%S UTC"),
        "deployment_frequency": {"releases_count": df_count, "rating": df_rating},
        "lead_time_for_changes": {
            "merged_prs_count": total_merged,
            "average_hours": round(avg_lead_time, 2) if avg_lead_time is not None else None,
            "rating": lttc_rating,
        },
        "change_failure_rate": {
            "hotfix_prs_count": len(hotfix_prs),
            "percentage": round(cfr_percentage, 1) if cfr_percentage is not None else None,
            "rating": cfr_rating,
        },
        "time_to_restore": {
            "resolved_bugs_count": len(closed_bugs),
            "average_hours": round(avg_mttr, 2) if avg_mttr is not None else None,
            "rating": mttr_rating,
        },
        "engine_reliability_gates": read_engine_gates(),
    }


def read_engine_gates(repo_root=None):
    """Read the self-healing gate thresholds straight from SimilarityWeights.cs so the
    report stays honest if the defaults change. These are configuration, not measured
    outcomes."""
    root = repo_root or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    weights_path = os.path.join(
        root, "TestAutomation", "SelfHealing", "SimilarityWeights.cs"
    )
    gates = {
        "minimum_confidence": None,
        "minimum_evidence_weight": None,
        "minimum_consensus_votes": None,
    }
    try:
        with open(weights_path, "r", encoding="utf-8") as f:
            src = f.read()
    except OSError:
        return gates

    patterns = {
        "minimum_confidence": r"MinimumConfidence\s*{\s*get;\s*set;\s*}\s*=\s*([0-9.]+)",
        "minimum_evidence_weight": r"MinimumEvidenceWeight\s*{\s*get;\s*set;\s*}\s*=\s*([0-9.]+)",
        "minimum_consensus_votes": r"MinimumConsensusVotes\s*{\s*get;\s*set;\s*}\s*=\s*([0-9]+)",
    }
    for key, pat in patterns.items():
        m = re.search(pat, src)
        if m:
            gates[key] = m.group(1)
    return gates


def render_markdown(metrics):
    df = metrics["deployment_frequency"]
    lttc = metrics["lead_time_for_changes"]
    cfr = metrics["change_failure_rate"]
    ttr = metrics["time_to_restore"]
    gates = metrics["engine_reliability_gates"]

    def cell(value, suffix=""):
        return f"{value}{suffix}" if value is not None else "n/a"

    lines = [
        "# DORA & Engine Reliability Report",
        "",
        f"**Assessment window:** last {metrics['period_days']} days  ",
        f"**Generated:** {metrics['generated_at']}",
        "",
        "## Core DORA Metrics",
        "",
        "| Metric | Measured value | Rating |",
        "| :--- | :---: | :--- |",
        f"| Deployment Frequency | {df['releases_count']} releases | {df['rating']} |",
        f"| Lead Time for Changes | {cell(lttc['average_hours'], ' hrs')} ({lttc['merged_prs_count']} PRs) | {lttc['rating']} |",
        f"| Change Failure Rate | {cell(cfr['percentage'], '%')} ({cfr['hotfix_prs_count']} hotfix PRs) | {cfr['rating']} |",
        f"| Time to Restore Service | {cell(ttr['average_hours'], ' hrs')} ({ttr['resolved_bugs_count']} bugs) | {ttr['rating']} |",
        "",
        "## Self-Healing Reliability Gates",
        "",
        "_Configuration read from `SimilarityWeights.cs` - thresholds in effect, not measured outcomes._",
        "",
        "| Gate | Configured value |",
        "| :--- | :---: |",
        f"| Minimum confidence | {cell(gates['minimum_confidence'])} |",
        f"| Minimum evidence weight | {cell(gates['minimum_evidence_weight'])} |",
        f"| Minimum consensus votes | {cell(gates['minimum_consensus_votes'])} |",
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description="Calculate DORA metrics for the repository")
    parser.add_argument("--repo", default="mustafasercansak/automation-sandbox", help="owner/repo")
    parser.add_argument("--days", type=int, default=30, help="rolling window in days")
    parser.add_argument("--format", choices=["json", "markdown"], default="markdown")
    parser.add_argument("--output", help="output file path (default: stdout)")
    args = parser.parse_args()

    owner, repo = args.repo.split("/")
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")

    try:
        prs, releases, issues = get_github_data(owner, repo, token)
    except GitHubFetchError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    metrics = calculate_dora(prs, releases, issues, days=args.days)
    content = (
        json.dumps(metrics, indent=2)
        if args.format == "json"
        else render_markdown(metrics)
    )

    if args.output:
        out_dir = os.path.dirname(os.path.abspath(args.output))
        os.makedirs(out_dir, exist_ok=True)
        with open(args.output, "w", encoding="utf-8") as f:
            f.write(content + "\n")
        print(f"Report written to {args.output}")
    else:
        print(content)
    return 0


if __name__ == "__main__":
    sys.exit(main())
