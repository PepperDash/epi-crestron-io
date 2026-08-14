---
name: pd-agents-update
description: 'Check for and pull updates to the installed PepperDash agents/skills (vendored under .claude/pd/). Use when: update the pd agents, check for agent updates, agent pack out of date, pull latest agent knowledge.'
disable-model-invocation: true
---

# Update installed PepperDash agents

This repo vendors agents/skills from `PepperDash-Engineering/pdt-copilot-agents` under `.claude/pd/`, pinned per package in
`.claude/pd-agents.lock.json`.

## Check for updates

```powershell
pwsh scripts/pd-agents.ps1 check
```

Exit 0 = up to date. Exit 1 = behind upstream and/or local files drifted from the pinned copy.

## Pull updates

```powershell
pwsh scripts/pd-agents.ps1 update            # all packages
pwsh scripts/pd-agents.ps1 update <name>     # one package
```

## If update warns about locally modified files

The update skips files you have edited locally and does not advance the pin. Local edits to
vendored files belong upstream: open a PR against `PepperDash-Engineering/pdt-copilot-agents` (see its contribute skill), then
re-run update. To discard local edits instead, re-run with `-Force`.