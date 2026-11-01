# Agent overview

The triage agent reads one issue and its board card, then drafts a proposal that a second model checks.
It is read-only unless you ask otherwise.

## Posting a comment

Pass `--post-comment` to post the report as one comment on the issue.
The comment is written by `IssueNoteWriter`, which escapes everything a model produced.

## Running it

Use `AgentTriageRun` to run the whole flow from code.
