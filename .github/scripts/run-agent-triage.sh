#!/usr/bin/env bash
# Runs the read-only triage agent (#585) on one issue and writes the report to the job summary. Shared by the manual
# (agent-triage.yml) and the label-triggered (agent-triage-label.yml) workflows so the two cannot drift apart.
#
# Inputs arrive only as environment variables, never as arguments built from workflow expressions:
#   ISSUE_NUMBER   required, a positive whole number
#   POST_COMMENT   optional, "true" posts the report as one comment (needs issues: write; the label workflow never sets it)
#   PROJECT_ID     required, the project board id
# The model keys and tokens are read by the agent itself from the environment.
set -u

[[ "${ISSUE_NUMBER:-}" =~ ^[1-9][0-9]{0,8}$ ]] || { echo "ISSUE_NUMBER must be a positive whole number"; exit 2; }
: "${PROJECT_ID:?PROJECT_ID is required}"

set +e
# --docs-root: the checkout this script runs in, where the docs task reads the source files an issue names (only files git tracks).
args=(triage --repo "$GITHUB_REPOSITORY" --issue "$ISSUE_NUMBER" --project-id "$PROJECT_ID" --docs-root "$PWD")
if [ "${POST_COMMENT:-}" = "true" ]; then args+=(--comment); fi
# --framework: the runner image carries SDK 10 as well, so the tool project multi-targets net8.0 and net10.0 and
# "dotnet run" cannot choose (the first real run of this workflow failed exactly there).
dotnet run --project TestAutomation/AgentCli -c Release --framework net8.0 -- "${args[@]}" > report.json 2> summary.txt
code=$?
set -e
# An exit code of 0 or 1 is a finished run only if the agent printed its JSON report. A build or startup failure can
# also exit with 1; without a report that is a failure of the run, not a result.
if [ "$code" -le 1 ] && ! python3 -c 'import json; json.load(open("report.json"))' 2>/dev/null; then
  echo "The agent produced no valid report (exit code $code); the run failed." >> summary.txt
  code=2
fi
# The report holds text a model wrote after reading an issue. Backticks are replaced so that text cannot close the
# code fences around it in the job summary.
safe() { sed "s/\`/'/g" "$1"; }
{
  echo "## Agent triage, issue #$ISSUE_NUMBER"
  echo
  echo "Exit code: $code (0 accepted, 1 not eligible or blocked or failed, 2 could not run)"
  echo
  echo '```'
  safe summary.txt
  echo '```'
  echo
  echo '```json'
  safe report.json
  echo '```'
} >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
safe summary.txt
if [ "$code" -eq 2 ]; then exit 2; fi
