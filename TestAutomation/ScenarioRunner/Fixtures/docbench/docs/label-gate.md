# The label gate

An issue is processed only when it carries the label Agent::Allowed.
`AgentIssueGate` decides this and never writes anything.
Add `--label-needs-human` to mark a blocked run; it is off by default.
