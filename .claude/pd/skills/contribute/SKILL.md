---
name: contribute
description: 'Contribute updates to the agent workspace — new agents, skills, context, or fixes. Use when: contributing, adding to agents, updating an agent, improving an agent, creating a new agent, sharing knowledge with the team.'
disable-model-invocation: true
---

# Contribute to Agent Workspace

Guide a developer through contributing updates to the shared agent workspace. Ensures consistency across all contributors by validating structure, naming, and content before committing.

---

## Trigger Phrases

- "Contribute this to the agents"
- "Add this to the agent workspace"
- "Update the agent with what we learned"
- "Create a new agent for this domain"
- "Share this with the team"
- "Improve the [agent-name] agent"

---

## Contribution Types

| Type | What to Do |
|------|------------|
| **New sub-agent** | Scaffold from template, populate `.instructions.md` + context |
| **New skill** | Create `SKILL.md` with YAML frontmatter in the right location |
| **Context update** | Add/modify files in `sub-agents/{name}/context/` |
| **Reference update** | Add/modify files in `sub-agents/{name}/reference/` |
| **Agent fix** | Fix errors in `.instructions.md`, skills, or context docs |
| **Shared export** | Curated session findings promoted to reference docs |

---

## Step-by-Step Process

### Step 1: Identify the Change

Determine what's being contributed and which sub-agent it affects:

1. **Which sub-agent?** — Match to an existing agent or determine if a new one is needed
2. **What type?** — New agent, new skill, context update, reference addition, or bug fix
3. **What files?** — List the files that will be created or modified

### Step 2: Create a Branch

```powershell
cd <agents-workspace-path>
git checkout main
git pull origin main
git checkout -b agent/{sub-agent-name}/{brief-description}
```

**Branch naming convention**: `agent/{sub-agent-name}/{description}`

Examples:
- `agent/essentials-epi/add-http-device-pattern`
- `agent/new/construct-ui`
- `agent/simpl-plus/fix-compilation-docs`
- `agent/shared/add-new-skill`

### Step 3: Make Changes

Follow the rules for each contribution type:

#### New Sub-Agent

1. Copy `sub-agents/template/` to `sub-agents/{name}/`
2. Write `.instructions.md` with YAML frontmatter:
   ```yaml
   ---
   description: "Use when: [keywords and trigger phrases for this domain]"
   ---
   ```
3. Add domain knowledge to `context/` as markdown files
4. Add reference material to `reference/`
5. Create skills in `skills/` with YAML frontmatter
6. **Create the picker entry** `.claude/agents/{name}.md` (required — this is what surfaces the agent
   in the VS Code Copilot picker **and** Claude Code). Self-contained body, Claude tool names; see
   [`.claude/README.md`](../../.claude/README.md). Do **not** create `.github/agents/{name}.agent.md`
   — VS Code reads both folders and the agent would list twice (issue #16).
7. Update the Sub-Agent Registry **and the Custom Agents table** in `.github/copilot-instructions.md`
8. Add routing keywords to the Keywords → Sub-Agent Mapping section

#### New Skill

1. Create folder: `sub-agents/{name}/skills/{skill-name}/SKILL.md` (or `skills/{skill-name}/SKILL.md` for universal skills)
2. Include YAML frontmatter:
   ```yaml
   ---
   name: skill-name
   description: 'What and when to use. Use "Use when:" pattern.'
   ---
   ```
3. Write step-by-step instructions with trigger phrases
4. Reference the skill from the sub-agent's `.instructions.md`
5. To surface a **universal** skill in Claude Code: a model-invocable *background* skill gets a thin
   `.claude/skills/{skill-name}/SKILL.md` stub with `user-invocable: false`; an explicit *user action*
   instead gets a `.github/prompts/pd-{name}.prompt.md` + `.claude/commands/pd-{name}.md` pair, with
   `disable-model-invocation: true` on the canonical skill (no skill stub). See `.claude/README.md`.

#### Context/Reference Update

1. Edit or create markdown files in the appropriate directory
2. Update `.instructions.md` if new files need to be referenced
3. If the update came from a session, note the source

#### Agent Fix

1. Identify the error in the existing content
2. Make the correction
3. Note what was wrong and why in your commit message

### Step 4: Validate

Before committing, verify:

- [ ] **YAML frontmatter** — All `.instructions.md` and `SKILL.md` files have valid frontmatter
  - `description` field is present and uses "Use when:" pattern
  - `name` field (skills only) matches the containing folder name
  - No unescaped colons in YAML values (wrap in quotes)
  - Domain-specific skills include `disable-model-invocation: true` (prevents unnecessary auto-loading)
- [ ] **Picker entry** — A new sub-agent has a `.claude/agents/{name}.md` definition (without it the agent won't appear in the VS Code Copilot picker or Claude Code), and there is **no** `.github/agents/{name}.agent.md` duplicate
- [ ] **File placement** — Files are in the correct directories
  - Agent picker entry → `.claude/agents/{name}.md`
  - Agent instructions → `sub-agents/{name}/.instructions.md`
  - Domain knowledge → `sub-agents/{name}/context/`
  - API docs/specs → `sub-agents/{name}/reference/`
  - Skills → `sub-agents/{name}/skills/{skill-name}/SKILL.md`
  - Universal skills → `skills/{skill-name}/SKILL.md`
  - Essentials shared skills → `shared/essentials/skills/{skill-name}/SKILL.md`
- [ ] **Naming conventions** — Skill folder names are lowercase, hyphen-separated
- [ ] **Relative paths** — All internal links use relative paths
  - Skills to parent folders (context/, reference/): use `../../`
  - Skills to sibling skills: use `../`
  - Never use absolute paths
- [ ] **Cross-references** — Any `/memories/repo/` references actually exist (check with memory tool)
- [ ] **No personal data** — No hardcoded paths, credentials, or personal preferences
- [ ] **Registry updated** — If a new sub-agent was added, the registry table and routing keywords are updated
- [ ] **PowerShell code blocks parse** — authored command blocks use the `powershell` fence per [Tool Preferences](../../.github/copilot-instructions.md) (bash only as a documented fallback). Run the validator; it must exit 0 (the same check runs in CI via `.github/workflows/validate-skill-powershell.yml`):

```powershell
pwsh -File scripts/Test-SkillPowerShell.ps1 -ShowBash
```

### Step 5: Commit and Push

```powershell
git add -A
git commit -m "<type>: <description>

<body explaining what changed and why>"
```

**Commit message prefixes**:
- `feat:` — New agent, skill, or significant content addition
- `fix:` — Correction to existing content
- `docs:` — Documentation-only updates (context, reference)
- `refactor:` — Restructuring without content changes

```powershell
git push --set-upstream origin agent/{sub-agent-name}/{description}
```

### Step 6: Open a Pull Request

Create a PR targeting `main`:

- **Title**: Use the commit message format
- **Body**: Fill out the PR template (what changed, which agent, how to verify)
- **Reviewers**: Tag a team member for review

---

## Quick Reference: Where Things Go

```
.agents/
├── .github/
│   ├── copilot-instructions.md    ← Root orchestrator (routing rules, registry)
│   └── prompts/                    ← Copilot slash commands (pd-*.prompt.md)
├── .claude/                        ← Claude Code + Copilot shared layer
│   ├── agents/                     ← Agent picker entries — one {name}.md per agent (REQUIRED)
│   ├── commands/                   ← Claude slash commands (mirror .github/prompts)
│   └── skills/                     ← Background skill stubs (user-invocable: false)
├── sessions/                       ← Personal session logs (gitignored)
├── skills/                         ← Universal skills (all agents)
├── shared/essentials/skills/       ← Essentials ecosystem shared skills
└── sub-agents/
    └── {name}/
        ├── .instructions.md        ← Agent definition (YAML frontmatter required)
        ├── context/                ← Domain knowledge (markdown)
        ├── reference/              ← API docs, specs, shared exports
        └── skills/                 ← Agent-specific skills
```
