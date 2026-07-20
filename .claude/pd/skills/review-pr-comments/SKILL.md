---
name: review-pr-comments
description: "Triage a PR's review comments (Copilot or human): implement the ones that make sense, then reply + resolve them; for the rest, leave a reply and let the author resolve manually. Use when: 'review the PR comments', 'address Copilot comments', 'handle the review feedback', /pd-review-pr-comments."
disable-model-invocation: true
---

# Review & Resolve PR Comments

Triage the inline review comments on a pull request. For each comment, decide whether it's a valid,
low-risk fix. If it is, **implement it, reply with the fix commit, and resolve the thread**. If it
isn't (subjective, ambiguous, wrong, or needs human judgment), **leave a reply explaining why and
leave the thread open** for the author to resolve manually.

## Trigger Phrases

- "review the PR comments" / "address the review feedback"
- "handle the Copilot comments on the PR"
- "/pd-review-pr-comments"

## Inputs

Define these once and reference them in every command below (avoids literal `OWNER`/`REPO`
placeholders that are easy to mis-paste):

```powershell
$PR    = <number>                                            # or: $PR = gh pr view --json number -q .number
$REPO  = gh repo view --json nameWithOwner -q .nameWithOwner # "owner/name" — for REST gh api paths
$OWNER = $REPO.Split('/')[0]                                 # GraphQL owner arg
$NAME  = $REPO.Split('/')[1]                                 # GraphQL name arg
$BASE  = gh pr view $PR --json baseRefName -q .baseRefName   # PR's actual base branch — don't hardcode 'main'
```

- **PR number** — if the user gave one, use it. Otherwise resolve from the current branch via
  `gh pr view --json number,headRefName,baseRefName,url` (reuse its `baseRefName` for `$BASE` instead
  of re-querying). If there's no PR for the branch, stop and say so.

---

## Step 1 — Record the starting branch (safety)

```powershell
$START_BRANCH = git branch --show-current
git status --short    # note any pre-existing WIP that is NOT yours
```

You will return to `START_BRANCH` at the end. **Any uncommitted WIP in the working tree that you did
not create is off-limits** — never stage it, and make sure it survives the branch switches below.

## Step 2 — Wait for a pending Copilot review, then fetch comments + thread IDs

**Check whether Copilot's review of the current head commit is still in flight before fetching
anything.** Fetching immediately after a push races Copilot's own review pass — it can take a couple
of minutes to post, so an immediate fetch reliably misses comments that show up moments later.
GitHub adds `"Copilot"` to the PR's `requested_reviewers` while a review is pending and removes it
once Copilot submits a review for that commit — that's a real completion signal, not a guess:

```powershell
$headSha = gh pr view $PR --json headRefOid -q .headRefOid

$MaxWaitSeconds = 300   # ~5 min ceiling — observed Copilot reviews land in under 3 min on this repo
$PollSeconds    = 20
$elapsed        = 0
while ($true) {
    # --jq, not ConvertFrom-Json: -Depth on ConvertFrom-Json is a PowerShell 6.2+ addition (unlike
    # ConvertTo-Json's -Depth, which has existed since 5.1) - a 5.1 session would throw on it
    # entirely. --jq's boolean output is the literal string "true"/"false" (both non-empty, so
    # compare with -eq 'true' rather than a naive [bool] cast, which treats "false" as truthy too).
    $reqReviewersFilter = "[.requested_reviewers[].login] | contains([`"Copilot`"])"
    $copilotPending = (gh api repos/$REPO/pulls/$PR --jq $reqReviewersFilter) -eq 'true'
    if (-not $copilotPending) { break }   # never requested, or already finished

    # --jq (not ConvertFrom-Json) deliberately: --paginate emits one JSON array/object per page
    # back-to-back, which isn't valid input for a single ConvertFrom-Json call once a PR has more
    # than one page of reviews (~30). --jq processes each page's stream correctly on its own,
    # matching the inline-comments fetch's proven pattern below.
    $jqFilter = ".[] | select(.user.login == `"copilot-pull-request-reviewer[bot]`" and .commit_id == `"$headSha`")"
    $copilotDoneForHead = gh api --paginate repos/$REPO/pulls/$PR/reviews --jq $jqFilter
    if ($copilotDoneForHead) { break }

    if ($elapsed -ge $MaxWaitSeconds) {
        Write-Warning "Copilot review still pending after ${MaxWaitSeconds}s for $headSha — proceeding anyway; comments may still arrive after this check."
        break
    }
    Start-Sleep -Seconds $PollSeconds
    $elapsed += $PollSeconds
}
```

- If `"Copilot"` was never a requested reviewer (repo/PR doesn't have Copilot review enabled), the
  first check exits the loop immediately — no wait.
- This is a single bounded loop in one command invocation, not a chain of separate sleep calls.
- Re-run this same block (with a fresh `$headSha`) at the top of **Step 7's** re-check, not just here
  — a loop iteration that just pushed a fix commit needs to wait on *that* push's review the same way.

Review-summary bodies and inline comments come from different endpoints; thread node IDs (needed to
resolve) only come from GraphQL.

```powershell
# Inline review comments (the ones attached to a file/line).
# --paginate is REQUIRED: gh api returns ~30 per page; large PRs have more.
gh api --paginate repos/$REPO/pulls/$PR/comments `
  --jq '.[] | {id, user: .user.login, path, line, body, in_reply_to: .in_reply_to_id}'

# Top-level review summaries (Copilot's "overview", approvals, etc.).
# READ-ONLY CONTEXT: these are NOT review threads — they have no thread node ID, cannot be
# replied to via the inline-comment replies endpoint, and cannot be resolved with
# resolveReviewThread. Use them to understand the reviewer's intent; act only on the inline
# threads below. (If a summary genuinely warrants a public response, post a normal PR
# conversation comment with `gh pr comment $PR --body '...'` — that is separate from thread resolution.)
gh pr view $PR --json reviews --jq '.reviews[] | {author: .author.login, state, body}'

# Thread node IDs + resolved state (map databaseId -> threadId).
# first:100 is the GraphQL max page size. pageInfo.hasNextPage tells you if there are more —
# if true, re-run with `after:"<endCursor>"` and merge (rare; only PRs with >100 threads).
gh api graphql `
  -F owner="$OWNER" -F name="$NAME" -F pr="$PR" `
  -f query='
query($owner:String!, $name:String!, $pr:Int!) {
  repository(owner:$owner, name:$name) {
    pullRequest(number:$pr) {
      reviewThreads(first:100) {
        pageInfo { hasNextPage endCursor }
        nodes { id isResolved comments(first:1){ nodes { databaseId } } }
      } } } }' `
  --jq '.data.repository.pullRequest.reviewThreads.nodes[] | {threadId:.id, resolved:.isResolved, commentId:.comments.nodes[0].databaseId}'
```

Skip threads already `isResolved: true` and comments that are replies (`in_reply_to` set).

## Step 3 — Triage each comment

For each open thread, judge it on its merits (read the cited file/lines first — don't trust the
comment blindly). Classify as:

| Verdict | Criteria | Action |
|---------|----------|--------|
| **Implement** | Correct, in-scope, low-risk (bug, unused code, wrong exception/type, doc error, missing null check, typo) | Fix it (Step 4), reply + resolve (Step 6) |
| **Decline/defer** | Subjective style, out of scope, factually wrong, needs design decision, or risky | Reply explaining; **leave open** (Step 6) — the author resolves manually |
| **Known false positive** | Matches an entry in the **Known False Positives** section below | Reply with the documented explanation; **resolve without asking** (Step 6) |

When unsure whether the user wants a change applied, lean toward **implementing** clearly-correct
low-risk fixes and **replying-only** for anything judgment-heavy. If a comment is wrong, say why in
the reply — don't silently dismiss it. The "known false positive" list below is the one exception:
those are pre-approved to decline *and* resolve without checking in first, because the same reviewer
misreading has already been confirmed wrong once and is expected to recur verbatim.

> Note any fix that also applies to code **outside this PR's diff** (e.g. the reviewed pattern was
> copied from elsewhere). Mention it in your summary; don't expand the PR's scope to chase it.

**Sweep for siblings before fixing.** If a comment reveals a *class* of issue — a repeated phrasing
pattern, a redundant regex flag, or any other literal string that recurs verbatim — grep the **whole
PR diff** for other instances of that exact pattern before you commit the fix, not just the flagged
line. For classes where each instance is a *different* literal (a broken-link shape, a stale reference
format) a single grep pattern won't catch siblings — generalize the pattern into a regex matching the
*shape* (e.g. the URL/reference structure) instead, or manually scan the files the broader sweep below
already surfaces. Waiting for Copilot to flag each sibling in its own separate round costs an extra
round per sibling for something you could catch in one pass. (Confirmed 2026-07-09: a missing-verb
grammar pattern on PR #40 was flagged, fixed, then flagged again twice more elsewhere in the same file
across two more rounds — a full-diff grep would have caught all three in the first pass.)

**Ripple-check your own fix before pushing** (distinct from the sibling sweep above). The sibling
sweep hunts for *the same issue* elsewhere; the ripple check asks the opposite question — *does the
edit I'm about to make create a **new** inconsistency in some other section that describes, parallels,
or references what I'm changing?* A review fix often edits wording or behavior in one place, silently
putting a downstream paragraph, a sample/output block, a parallel doc, or a summary sentence out of
sync — which Copilot then flags on the next push. Before committing, re-read the neighbours of every
line you changed and any doc that restates the same fact, and reconcile them in the *same* commit.
(Confirmed 2026-07-10, PR #49: adding `node --version` to a verify line left a "core vs deferrable"
paragraph two lines down inconsistent → flagged a round later; rewording a verify line exposed a
`Get-Module`-in-`pwsh` gap → flagged the round after that. Each fix seeded the next round's comment;
a ripple check would have folded them into one round.)

```powershell
# Fetch the PR's head commit via GitHub's special ref (refs/pull/<PR>/head), not a branch name.
# This always resolves on `origin`, even for a fork PR where the head branch itself was never
# pushed to origin — as long as `origin` points at the PR's base repo, which this skill assumes
# throughout ($REPO, $BASE, Step 5's `git push`, etc.). Never rely on local HEAD here: this step
# runs in Step 3, before Step 4 switches your working copy to the PR branch, so local HEAD is
# still whatever branch you started on.
git fetch origin "pull/$PR/head:pr-review-head"

# Also refresh the base ref locally — it may have moved since your last fetch.
git fetch origin "$BASE"

# Search the PR's diff hunks (added/removed/context lines around each change) for the pattern:
git diff "origin/$BASE...pr-review-head" | Select-String -Pattern '<distinctive phrase or regex>'

# Broader: search the full content of every non-deleted file this PR touched, AT THE PR'S HEAD
# COMMIT — not the local working tree. --diff-filter=d excludes deleted paths (git show would
# error on those; they don't exist at the head commit):
git diff "origin/$BASE...pr-review-head" --name-only --diff-filter=d | ForEach-Object {
    git show "pr-review-head:$_" | Select-String -Pattern '<pattern>'
}
```

## Step 4 — Implement the accepted fixes (on the PR head branch)

1. Switch to the PR's head branch if not already on it: `git checkout <headRefName>`.
   - **Fork PR** (head branch never pushed to `origin`): `<headRefName>` won't resolve directly —
     reuse the `pr-review-head` ref fetched in Step 3 instead: `git checkout -B <headRefName> pr-review-head`.
   - The branch switch resets editor file state — **re-Read any file before editing it.**
   - Carried-over WIP from `START_BRANCH` rides along; that's fine as long as you don't stage it.
2. Make the edits.
3. **Stage only the files you changed** — specific paths, never `git add -A` (the working tree may
   hold unrelated WIP). Verify with `git diff --cached --name-only`.

## Step 5 — Commit & push

Group the fixes into one commit (or a few logical commits). Use a Conventional-Commit message.

PowerShell can't pipe a heredoc the way bash does, so write the multi-line message to a temp file and
commit with `-F`:

```powershell
$msg = New-TemporaryFile
@'
docs: address review feedback on <area>

- <what changed and why, per comment>
'@ | Set-Content -Encoding utf8 $msg
try {
    git commit -F $msg
    git push
    $CommitSHA = git rev-parse --short HEAD
} finally {
    Remove-Item $msg
}
```

> **bash fallback:** in a bash shell the same thing is a heredoc — `git commit -F - <<'EOF' … EOF`.
> Do **not** paste that heredoc into PowerShell; it fails with `Missing file specification after
> redirection operator`.

End the commit body with the workspace co-author trailer if this repo uses one.

## Step 6 — Reply, then resolve

**Reply** to every triaged **inline review-comment thread** (both implemented and declined),
referencing the fix commit for implemented ones. The replies endpoint below targets the thread's
root `commentId` — it works only for inline review comments, **not** the review summaries from Step 2
(those have no thread to reply into; respond to a summary, if needed, with `gh pr comment $PR --body
'...'` instead):

```powershell
gh api repos/$REPO/pulls/$PR/comments/<commentId>/replies `
  -f body='Fixed in <CommitSHA> — <one line on the fix>.'
```

> **Keep the body in a *single-quoted* string.** In a double-quoted PowerShell string a backtick is
> the escape char, so markdown backticks in `-f body="...`code`..."` get eaten and the reply posts
> empty. If a batch reply loop errors partway it may have already posted some replies — re-query the
> thread and delete any duplicate before retrying.

**Resolve the threads you implemented, plus any known false positive** (see the Known False Positives section below).
Leave every other declined/deferred thread OPEN so the author decides:

```powershell
gh api graphql `
  -f query='mutation($id:ID!){ resolveReviewThread(input:{threadId:$id}){ thread{ isResolved } } }' `
  -f id='<threadId>'
```

## Step 7 — Loop-check before restoring, then restore

**Do not restore `START_BRANCH` right after one round.** Copilot re-reviews on every push (see Notes
& Gotchas below) and frequently posts a fresh round within moments to a few minutes. Having just
finished this round's Step 6 (a round with only Decline/defer or Known-False-Positive verdicts skips
Steps 4–5 and pushes nothing — re-check anyway, since a human reviewer can post new comments
independent of a push), **re-run Step 2's wait-for-Copilot loop first** (fresh `$headSha` — if this
round pushed a fix commit, that's a new commit to wait on), then re-run Step 2's thread fetch on the
**same PR**:

- **New threads you haven't already triaged?** Loop back to Step 3 for them — don't restore the
  branch yet. Repeat Step 3 → 4 → 5 → 6 → this check until a fetch comes back with **no threads you
  haven't already triaged this pass**. Declined/deferred threads intentionally stay unresolved (Step
  6) — "zero unresolved threads" is *not* the success condition; a PR with any legitimate decline will
  never reach it. Track which thread/comment IDs you've already handled and compare against those.
- **Nothing new?** Proceed below.

The wait loop already bounds itself (Step 2's `$MaxWaitSeconds`) — you don't need a separate "don't
sleep-loop indefinitely" caveat here anymore, but the ceiling is a best-effort proceed-anyway, not a
guarantee: if it's hit, restore and report "no new threads as of this check" rather than promising no
more comments will ever come; the user can re-invoke the skill later, or ask you to wait/poll longer.

**When to stop iterating on a docs/prose PR.** Copilot re-reviews the newly-pushed diff every round,
and on prose it will keep surfacing successively finer *correct-but-minor* wording refinements on
lines you've already made accurate — a bot-reviewing-a-bot's-edits loop that has no natural zero. Its
inline comments are advisory, not merge-blocking (CI is the gate). So once the flagged content is
factually correct and internally consistent (you've done the sibling sweep and ripple check above),
and a round is producing only stylistic polish rather than real errors, **say so and recommend merging
instead of pushing again** — each push just mints the next micro-nit. Keep applying rounds only while
they surface genuine mistakes. (Confirmed 2026-07-10, PR #49 ran 8 rounds this way; most rounds were
follow-ons to the previous fix's own wording.)

```powershell
git checkout $START_BRANCH
git status --short   # confirm the original WIP is intact, untouched
```

Report a table: each comment → verdict (Implement / Decline/defer / Known false positive, matching the
Step 3 labels) → what you did → resolved? Include the fix commit SHA(s) and PR URL. Flag anything
genuinely declined so the user can resolve those manually, and anything in-scope-but-deferred — but
don't ask them to re-approve a known false positive you already resolved; just report it as done.

## Known False Positives (pre-approved to decline and resolve)

Comments matching one of these get the documented reply and are **resolved immediately, without
asking the user first** — the reviewer misreading has already been checked and confirmed wrong once;
re-litigating it every time it recurs isn't worth the round-trip. Still leave a reply so the record is
clear. Add a new entry here (with the date it was confirmed) whenever you decline-and-resolve something
without approval, so the next occurrence is pre-approved too — this list is meant to grow.

### `EROR` flagged as a misspelling of `ERROR`

**Trigger:** a review comment claims a grep/search pattern like `EROR` is a typo for `ERROR` in
content that comes from a Crestron/Essentials console or file-sink log.

**Why it's wrong:** `EROR` is the real, truncated 4-character log-level tag Crestron's Essentials
sinks emit (consistent width with `INFO`/`WARN`) — not a typo. The full word `ERROR` does not appear
in these logs. Already independently documented in `sub-agents/crestron-processor/context/console-commands.md`
(`[EROR]`) and confirmed against many real `startup.log` captures (2026-07-07).

**Reply template:**
```
Not a misspelling — `EROR` is the actual truncated 4-character log-level tag Crestron's Essentials
console/file sinks emit (consistent width with `INFO`/`WARN`), not the full word. Already documented
independently in `sub-agents/crestron-processor/context/console-commands.md`. Leaving the grep pattern
as-is (or broadening it to also match `ERROR` defensively, if the diagnostic step isn't
Crestron-log-specific) — resolving since this is a confirmed recurring false positive.
```

**Action:** reply with the template above (adjust the last sentence to whatever was actually done —
left as-is, or broadened defensively), then resolve. No approval needed.

## Notes & Gotchas

- **`@me` / Copilot:** Copilot review comments come from user `Copilot` (or
  `copilot-pull-request-reviewer` for the summary). Treat them like any reviewer — verify before applying.
- **Fetching threads before Copilot's review finishes silently misses comments.** `gh pr view
  $PR --json reviews` and the inline-comments endpoint only show what's been posted so far — there's
  no "review in progress, come back later" signal from those endpoints themselves. The real signal is
  `requested_reviewers`: GitHub keeps `"Copilot"` listed there while a review is pending and drops it
  once Copilot posts a review for the current head commit. Step 2's wait loop polls that instead of
  fetching immediately. (Confirmed 2026-07-15 on PR #58: pushing a fix commit at 14:56:07 UTC, Copilot's
  review for that commit didn't land until 14:58:52 — about 2m45s later; `requested_reviewers` held
  `"Copilot"` the entire time in between.)
- **Don't resolve what you didn't fix** — except a documented known false positive (see the Known
  False Positives section below), which is pre-approved to resolve on sight. Everything else:
  resolving signals "handled"; the user asked to resolve manually otherwise.
- **Copilot re-reviews after every push and re-raises fixed issues as NEW threads.** Pushing your
  fix commits triggers a fresh Copilot pass on the new diff, which often duplicates comments you
  already handled (same complaint, new thread ID). Verify the fix is on the branch, reply
  referencing the original fix commit, and resolve as a duplicate. A duplicate of a thread the
  **author** already resolved (their decision) can also be resolved, citing that decision.
- **Branch hygiene:** if the PR branch differs from where the user is working, always return them to
  `START_BRANCH` and confirm their uncommitted work is intact.
- **Selective staging is mandatory** — see [`contribute`](../contribute/SKILL.md) for the rationale.
- **A comment flagging changes that look out-of-scope for the PR isn't automatically "implement a
  revert."** Before triaging, run `git log main..HEAD -- <path>` on the flagged file(s) to see whose
  commit(s) introduced them. If they're the human author's own commits (not something an agent
  edited in this session), that's a **decline/defer** — reply naming the commit(s) and explaining
  the likely origin (e.g. "made directly while testing in the EDH"), and leave the thread open for
  the author's judgment call. Don't revert someone else's deliberate commits on their behalf just
  because a reviewer flagged them as unrelated (confirmed 2026-07-08,
  `vsce-essentials-version-manager` PR #49).
- **Checking whether a branch will merge/rebase cleanly before doing either:**
  `git merge-tree --write-tree <ref1> <ref2>` (Git ≥2.38) simulates a merge without touching the
  working tree — exit 0 with no conflict output means clean. Useful when a user asks "does this
  need a rebase" and you want to answer before committing to the merge/rebase itself.
