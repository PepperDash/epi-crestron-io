#requires -Version 7.3
<#
.SYNOPSIS
    Export, install, and update pdt-copilot-agents packages (agents and skills) in other repos.

.DESCRIPTION
    Makes agents and skills from pdt-copilot-agents portable. An agent or skill is installed
    into a target repo as a committed, self-contained copy that Claude Code (and VS Code
    Copilot) discover natively:

        target-repo/
        |-- .claude/
        |   |-- agents/{name}.md            picker entry (paths rewritten to the vendor root)
        |   |-- skills/{skill}/SKILL.md     generated thin stubs for bundled skills
        |   |-- pd/                         vendor root -- mirrors the source repo layout
        |   |   |-- sub-agents/{name}/...   verbatim copies; internal relative links unchanged
        |   |   |-- skills/{dep}/...
        |   |   `-- shared/...
        |   `-- pd-agents.lock.json         per-package pin (commit SHA) + per-file hashes
        `-- scripts/pd-agents.ps1           this script, bootstrapped so the repo can call home

    Dependency closures are computed dynamically by parsing relative links out of the
    package's markdown files (no hand-maintained manifest). Links to sibling agents are
    recorded as peer references and NOT followed. An optional pack.json in a package root
    (keys: extraDeps, exclude) covers anything the parser cannot see.

    Updates generalize the scripts/sync-feature-dev-agents.ps1 lock-file model: each package
    pins the commit it was installed from; `check` reports drift and upstream movement;
    `update` re-fetches (gh tarball or a local clone), refuses to clobber locally modified
    files unless -Force, and advances the pin.

.PARAMETER Command
    One of: list, install, check, update, remove. Omit entirely to launch an interactive
    menu (pick a command, package, and target by number).

.PARAMETER Name
    Package name (an agent under sub-agents/ or a skill under skills/). Optional for
    check/update (defaults to all installed packages).

.PARAMETER All
    With install: install every agent package.

.PARAMETER TargetPath
    With install (run from a source clone): the repo to install into. Defaults to the
    current repo when it is already a target (has .claude/pd-agents.lock.json).

.PARAMETER Ref
    Git ref (branch or SHA) in the source repo to install/update from. Defaults to the
    default branch head (gh transport) or HEAD (local transport).

.PARAMETER Release
    Install/update from a tagged GitHub Release instead of a branch head. Pass a tag
    (e.g. v1.2.0) or 'latest'. Records the resolved tag in the lock as a human-readable
    pin; `check` then reports whether a newer release exists (not merely newer commits).
    A plain `update` on a release-pinned package stays on the release track (latest release).

.PARAMETER SourcePath
    Path to a local clone of pdt-copilot-agents to copy from instead of fetching via gh.
    Used by the dev loop and the network-free smoke tests.

.PARAMETER NoVsCode
    Skip merging chat.agentFilesLocations into the target's .vscode/settings.json.

.PARAMETER Force
    With update: overwrite locally modified files instead of skipping them.

.PARAMETER Json
    Emit a single JSON object to stdout instead of human-readable text.

.EXAMPLE
    pwsh scripts/pd-agents.ps1 list

.EXAMPLE
    pwsh scripts/pd-agents.ps1 install crestron-processor -TargetPath C:\src\my-av-project

.EXAMPLE
    pwsh scripts/pd-agents.ps1 check          # from a target repo; exit 1 if behind/drifted

.EXAMPLE
    pwsh scripts/pd-agents.ps1 update crestron-processor -Force

.NOTES
    gh transport requires the GitHub CLI installed and authenticated (`gh auth status`).
#>
[CmdletBinding()]
param(
    # Not [Mandatory] / not [ValidateSet] so that a bare invocation drops into interactive mode;
    # the value is validated manually below.
    [Parameter(Position = 0)]
    [string] $Command,

    [Parameter(Position = 1)]
    [string] $Name,

    [switch] $All,
    [string] $TargetPath,
    [string] $Ref,
    [string] $Release,
    [string] $SourcePath,
    [switch] $NoVsCode,
    [switch] $Force,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'

$DefaultSourceRepo = 'PepperDash-Engineering/pdt-copilot-agents'
$VendorRoot        = '.claude/pd'
$LockRelPath       = '.claude/pd-agents.lock.json'
$InstallerRelPath  = 'scripts/pd-agents.ps1'

# Set by Get-PackageSource when -Release resolves to a concrete tag; stamped into lock entries.
$script:ResolvedRelease = $null

# --- Output helpers ------------------------------------------------------------------------------

$script:Result = [ordered]@{ command = $Command; ok = $true; packages = @(); warnings = @(); messages = @() }

function Write-Info([string]$Text, [string]$Color = 'Gray') {
    $script:Result.messages += $Text
    if (-not $Json) { Write-Host $Text -ForegroundColor $Color }
}
function Write-Warning2([string]$Text) {
    $script:Result.warnings += $Text
    if (-not $Json) { Write-Host "[WARN] $Text" -ForegroundColor Yellow }
}
function Complete-Run([int]$ExitCode = 0) {
    if ($ExitCode -ne 0) { $script:Result.ok = $false }
    if ($Json) { $script:Result | ConvertTo-Json -Depth 10 }
    exit $ExitCode
}

# --- Role / path resolution ----------------------------------------------------------------------

$RepoRoot = Split-Path -Parent $PSScriptRoot

function Test-IsSourceRepo([string]$Root) {
    (Test-Path (Join-Path $Root 'sub-agents')) -and (Test-Path (Join-Path $Root '.github/copilot-instructions.md'))
}
function Test-IsTargetRepo([string]$Root) {
    Test-Path (Join-Path $Root $LockRelPath)
}

# --- gh / git preflight (only when the relevant transport is needed) ------------------------------

function Assert-GhReady {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI ('gh') is not installed or not on PATH. Install it from https://cli.github.com/ and run 'gh auth login'. Alternatively pass -SourcePath <local clone of pdt-copilot-agents>."
    }
    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Native commands set $LASTEXITCODE rather than throwing, so a try/catch would never fire here.
        throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
    }
}

function Assert-GitReady {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "Git is not installed or not on PATH. Install it from https://git-scm.com/ - the -SourcePath transport shells out to git (rev-parse, status) against the local clone."
    }
}

# --- Source acquisition ----------------------------------------------------------------------------
# Returns @{ Root = <dir containing sub-agents/, skills/, shared/>; Sha = <full commit sha> }

# Resolves the -Release value to a concrete tag ('latest' -> the newest release tag).
function Resolve-ReleaseTag([string]$Repo) {
    if ($Release -ne 'latest') { return $Release }
    if ($SourcePath) {
        $src = (Resolve-Path $SourcePath).Path
        $tag = git -C $src describe --tags --abbrev=0 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $tag) { throw "No tags in local source '$src' to resolve -Release latest." }
        return ($tag -join '').Trim()
    }
    Assert-GhReady
    $t = gh api "repos/$Repo/releases/latest" --jq '.tag_name' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh api releases/latest failed for $Repo (no published release?): $t" }
    return ($t -join '').Trim()
}

function Get-PackageSource([string]$Repo) {
    # -Release wins over -Ref: resolve the tag, record it, and treat it as the ref to fetch.
    $effectiveRef = $Ref
    if ($Release) {
        $tag = Resolve-ReleaseTag $Repo
        $script:ResolvedRelease = $tag
        $effectiveRef = $tag
    }

    if ($SourcePath) {
        Assert-GitReady
        $src = (Resolve-Path $SourcePath).Path
        if (-not (Test-IsSourceRepo $src)) { throw "-SourcePath '$src' does not look like a pdt-copilot-agents clone (missing sub-agents/ or .github/copilot-instructions.md)." }
        $refArg = if ($effectiveRef) { $effectiveRef } else { 'HEAD' }
        # ^{commit} dereferences annotated tags to their commit; a no-op for branches/HEAD/SHAs.
        $out = git -C $src rev-parse "$refArg^{commit}" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "git rev-parse $refArg failed in '$src': $out" }
        $sha = (@($out)[-1] -join '').Trim()   # last line: git may emit warnings before the sha
        # Files are copied from $src's actual working tree below, not a checkout of $sha - if the
        # clone isn't sitting at -Ref, the lock would pin a commit that doesn't match what got copied.
        if ($Ref) {
            $headOut = git -C $src rev-parse HEAD 2>&1
            if ($LASTEXITCODE -ne 0) { throw "git rev-parse HEAD failed in '$src': $headOut" }
            $headSha = (@($headOut)[-1] -join '').Trim()
            if ($headSha -ne $sha) {
                throw "-SourcePath '$src' is checked out at $($headSha.Substring(0,7)), but -Ref '$Ref' resolves to $($sha.Substring(0,7)). Check out that ref in the source clone first (git -C '$src' checkout $Ref), then re-run."
            }
        }
        $dirty = git -C $src status --porcelain
        if ($dirty) { Write-Warning2 "Source working tree at '$src' has uncommitted changes; the pin ($($sha.Substring(0,7))) may not match copied content." }
        return @{ Root = $src; Sha = $sha }
    }

    Assert-GhReady
    $refArg = if ($effectiveRef) { $effectiveRef } else {
        $branch = gh api "repos/$Repo" --jq '.default_branch' 2>&1
        if ($LASTEXITCODE -ne 0) { throw "gh api repos/$Repo failed: $branch" }
        ($branch -join '').Trim()
    }
    $shaJson = gh api "repos/$Repo/commits/$refArg" --jq '.sha' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh api failed resolving ref '$refArg': $shaJson" }
    $sha = ($shaJson -join '').Trim()

    $work = Join-Path ([System.IO.Path]::GetTempPath()) "pd-agents-$([guid]::NewGuid().ToString('n').Substring(0,8))"
    New-Item -ItemType Directory -Path $work | Out-Null
    $tarPath = Join-Path $work 'src.tar.gz'
    try {
        # gh api cannot write binary safely from PowerShell; use the token with Invoke-WebRequest instead.
        $token = (gh auth token).Trim()
        Invoke-WebRequest -Uri "https://api.github.com/repos/$Repo/tarball/$sha" `
            -Headers @{ Authorization = "Bearer $token"; Accept = 'application/vnd.github+json' } `
            -OutFile $tarPath
        # Extract with .NET (pwsh 7.3+) — an external `tar` may resolve to Git Bash's GNU tar, which
        # misreads C:\ paths as remote hosts.
        $gzStream = $null; $fileStream = $null
        try {
            $fileStream = [System.IO.File]::OpenRead($tarPath)
            $gzStream = [System.IO.Compression.GZipStream]::new($fileStream, [System.IO.Compression.CompressionMode]::Decompress)
            [System.Formats.Tar.TarFile]::ExtractToDirectory($gzStream, $work, $false)
        } catch {
            throw "tarball extraction failed for $tarPath`: $_"
        } finally {
            if ($gzStream) { $gzStream.Dispose() }
            if ($fileStream) { $fileStream.Dispose() }
        }
        $extracted = Get-ChildItem $work -Directory | Select-Object -First 1
        if (-not $extracted) { throw "Tarball extraction produced no directory under $work" }
    } catch {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
    return @{ Root = $extracted.FullName; Sha = $sha; Temp = $work }
}

# --- Package discovery -----------------------------------------------------------------------------

function Get-ExportablePackages([string]$SrcRoot) {
    $pkgs = @()
    Get-ChildItem (Join-Path $SrcRoot 'sub-agents') -Directory | Where-Object Name -ne 'template' | ForEach-Object {
        $pkgs += [pscustomobject]@{ Name = $_.Name; Type = 'agent'; Root = "sub-agents/$($_.Name)" }
    }
    Get-ChildItem (Join-Path $SrcRoot 'skills') -Directory | ForEach-Object {
        $pkgs += [pscustomobject]@{ Name = $_.Name; Type = 'skill'; Root = "skills/$($_.Name)" }
    }
    return $pkgs
}

function Resolve-Package([string]$SrcRoot, [string]$PkgName) {
    $pkg = Get-ExportablePackages $SrcRoot | Where-Object Name -eq $PkgName | Select-Object -First 1
    if (-not $pkg) { throw "Unknown package '$PkgName'. Run 'pd-agents.ps1 list' to see exportable agents and skills." }
    return $pkg
}

# --- Dependency closure -----------------------------------------------------------------------------
# Parses markdown for relative links and inline-code path tokens, classifies each resolved
# repo-relative path, and recurses into dependency units until fixpoint.

$MdLinkPattern   = '\]\(([^)\s]+?)(?:#[^)]*)?\)'
$CodePathPattern = '`((?:\.\./|(?:\./)?(?:skills|shared|sub-agents)/)[^`\s]+?\.(?:md|ps1|json))`'

function Get-RepoRelative([string]$SrcRoot, [string]$FullPath) {
    [System.IO.Path]::GetRelativePath($SrcRoot, $FullPath).Replace('\', '/')
}

# Path-segment-bounded, platform-appropriate-case containment check - a plain string StartsWith
# would treat 'C:\repo2\x' as inside 'C:\repo' (no segment boundary) and mismatch on casing
# (Windows paths are case-insensitive; StartsWith's default comparison is not).
function Test-PathIsWithinRoot([string]$Root, [string]$Path) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('/', '\')
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $pathFull.Equals($rootFull, $comparison) -or $pathFull.StartsWith($rootFull + [System.IO.Path]::DirectorySeparatorChar, $comparison)
}

function Resolve-PathCandidate([string]$SrcRoot, [string]$ContainingDir, [string]$Candidate) {
    if ($Candidate -match '^(https?|mailto):') { return $null }
    foreach ($base in @($ContainingDir, $SrcRoot)) {
        $full = [System.IO.Path]::GetFullPath((Join-Path $base $Candidate))
        if ((Test-Path $full) -and (Test-PathIsWithinRoot $SrcRoot $full)) {
            return Get-RepoRelative $SrcRoot $full
        }
    }
    return $null
}

# Maps a repo-relative dependency path to its closure unit (a directory copied whole).
function Get-DependencyUnit([string]$RepoRel) {
    if ($RepoRel -match '^skills/([^/]+)/') { return "skills/$($Matches[1])" }
    if ($RepoRel -match '^shared/essentials/skills/([^/]+)/') { return "shared/essentials/skills/$($Matches[1])" }
    if ($RepoRel -match '^shared/([^/]+)') { return "shared/$($Matches[1])" }
    return $null
}

function Get-DependencyClosure([string]$SrcRoot, [pscustomobject]$Pkg) {
    $units    = [System.Collections.Generic.HashSet[string]]::new()   # dep dirs, repo-relative
    $peerRefs = [System.Collections.Generic.HashSet[string]]::new()
    $warnings = @()
    $queue    = [System.Collections.Generic.Queue[string]]::new()
    $scanned  = [System.Collections.Generic.HashSet[string]]::new()

    $queue.Enqueue($Pkg.Root)
    if ($Pkg.Type -eq 'agent') {
        $picker = ".claude/agents/$($Pkg.Name).md"
        if (Test-Path (Join-Path $SrcRoot $picker)) { $queue.Enqueue($picker) }
        else { $warnings += "No picker entry found at $picker" }
    }

    while ($queue.Count -gt 0) {
        $unit = $queue.Dequeue()
        if (-not $scanned.Add($unit)) { continue }
        $unitFull = Join-Path $SrcRoot $unit
        $mdFiles = if (Test-Path $unitFull -PathType Container) {
            Get-ChildItem $unitFull -Recurse -File -Filter '*.md'
        } elseif (Test-Path $unitFull) { @(Get-Item $unitFull) } else { @() }

        foreach ($file in $mdFiles) {
            $text = Get-Content $file.FullName -Raw
            $candidates = @()
            foreach ($m in [regex]::Matches($text, $MdLinkPattern))   { $candidates += $m.Groups[1].Value }
            foreach ($m in [regex]::Matches($text, $CodePathPattern)) { $candidates += $m.Groups[1].Value }

            foreach ($cand in ($candidates | Sort-Object -Unique)) {
                $repoRel = Resolve-PathCandidate $SrcRoot $file.DirectoryName $cand
                if (-not $repoRel) {
                    if ($cand -notmatch '^(https?|mailto):' -and $cand -match '\.(md|ps1|json)$') {
                        $warnings += "Broken link in $(Get-RepoRelative $SrcRoot $file.FullName): $cand"
                    }
                    continue
                }
                if ($repoRel.StartsWith($Pkg.Root + '/') -or $repoRel -eq $Pkg.Root) { continue }
                if ($repoRel -match '^sub-agents/([^/]+)') {
                    if ($Matches[1] -ne $Pkg.Name) { $peerRefs.Add($Matches[1]) | Out-Null }
                    continue
                }
                if ($repoRel -match '^(\.github|\.claude|\.vscode|sessions|docs|scripts)/') { continue }
                if ($repoRel -in @('README.md', 'GETTING-STARTED.md', 'CONTRIBUTING.md')) { continue }
                $depUnit = Get-DependencyUnit $repoRel
                if ($depUnit -and $units.Add($depUnit)) { $queue.Enqueue($depUnit) }
            }
        }
    }

    # Optional per-package override file for parser blind spots.
    $packJson = Join-Path $SrcRoot "$($Pkg.Root)/pack.json"
    if (Test-Path $packJson) {
        $pack = Get-Content $packJson -Raw | ConvertFrom-Json
        foreach ($extra in @($pack.extraDeps)) { if ($extra) { $units.Add($extra) | Out-Null } }
        foreach ($excl in @($pack.exclude))   { if ($excl)  { $units.Remove($excl) | Out-Null } }
    }

    return @{
        Deps     = @($units    | Sort-Object)
        PeerRefs = @($peerRefs | Sort-Object)
        Warnings = $warnings
    }
}

# --- Transforms ------------------------------------------------------------------------------------

function Get-VendorHeader([string]$Repo, [string]$SrcPath, [string]$ShortSha) {
    "`n<!-- Vendored from $Repo `u{00B7} $SrcPath `u{00B7} commit $ShortSha.`n     Managed by scripts/pd-agents.ps1 - do not hand-edit; run 'pwsh scripts/pd-agents.ps1 update' to refresh. -->`n"
}

# Rewrites repo-root paths (sub-agents/, skills/, shared/) in a picker entry so they resolve
# against the vendor root, and injects the vendored-attribution header after the frontmatter.
function Convert-PickerEntry([string]$Content, [string]$Repo, [string]$SrcPath, [string]$ShortSha) {
    $Content = $Content -replace '(?<![\w/.-])((?:sub-agents|skills|shared)/)', "$VendorRoot/`$1"
    $note = Get-VendorHeader $Repo $SrcPath $ShortSha
    $Content = [regex]::Replace($Content, "(?s)^(---\r?\n.*?\r?\n---\r?\n)", "`$1$note", 1)
    return $Content -replace "`r`n", "`n"
}

# Generates a thin .claude/skills/{name}/SKILL.md stub pointing at the vendored canonical copy,
# copying name/description/disable-model-invocation from the canonical frontmatter.
function New-SkillStub([string]$CanonicalSkillMd, [string]$VendoredRelPath, [string]$Repo, [string]$ShortSha) {
    $text = Get-Content $CanonicalSkillMd -Raw
    $fm = [regex]::Match($text, "(?s)^---\r?\n(.*?)\r?\n---")
    if (-not $fm.Success) { throw "No frontmatter in $CanonicalSkillMd" }
    $keep = @()
    foreach ($line in ($fm.Groups[1].Value -split "\r?\n")) {
        if ($line -match '^(name|description|disable-model-invocation)\s*:') { $keep += $line }
    }
    $note = (Get-VendorHeader $Repo $VendoredRelPath $ShortSha).Trim()
    return (@('---') + $keep + @('---', '', $note, '',
        "Read and follow ``$VendoredRelPath`` (relative to the repo root). That file is the canonical",
        "vendored source for this skill; this is a thin adapter so Claude Code discovers it natively.",
        '')) -join "`n"
}

function Get-UpdateSkillStub([string]$Repo) {
    @"
---
name: pd-agents-update
description: 'Check for and pull updates to the installed PepperDash agents/skills (vendored under .claude/pd/). Use when: update the pd agents, check for agent updates, agent pack out of date, pull latest agent knowledge.'
disable-model-invocation: true
---

# Update installed PepperDash agents

This repo vendors agents/skills from ``$Repo`` under ``.claude/pd/``, pinned per package in
``.claude/pd-agents.lock.json``.

## Check for updates

``````powershell
pwsh scripts/pd-agents.ps1 check
``````

Exit 0 = up to date. Exit 1 = behind upstream and/or local files drifted from the pinned copy.

## Pull updates

``````powershell
pwsh scripts/pd-agents.ps1 update            # all packages
pwsh scripts/pd-agents.ps1 update <name>     # one package
``````

## If update warns about locally modified files

The update skips files you have edited locally and does not advance the pin. Local edits to
vendored files belong upstream: open a PR against ``$Repo`` (see its contribute skill), then
re-run update. To discard local edits instead, re-run with ``-Force``.
"@
}

# --- Lock helpers -----------------------------------------------------------------------------------

function Get-Lock([string]$TgtRoot) {
    $p = Join-Path $TgtRoot $LockRelPath
    if (Test-Path $p) { return Get-Content $p -Raw | ConvertFrom-Json -AsHashtable }
    return [ordered]@{
        version   = 1
        source    = @{ repo = $DefaultSourceRepo }
        vendorRoot = $VendorRoot
        installer = @{ sourcePath = $InstallerRelPath; sha = '' }
        packages  = [ordered]@{}
    }
}

function Save-Lock([string]$TgtRoot, $Lock) {
    $p = Join-Path $TgtRoot $LockRelPath
    New-Item -ItemType Directory -Path (Split-Path $p) -Force | Out-Null
    (($Lock | ConvertTo-Json -Depth 10) -replace "`r`n", "`n") + "`n" | Set-Content -Path $p -NoNewline -Encoding utf8
}

function Get-FileSha([string]$Path) {
    'sha256:' + (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
}

# --- Install ---------------------------------------------------------------------------------------

# $FileHashes must stay IDictionary (not [hashtable]) — callers pass an OrderedDictionary, and a
# [hashtable] parameter type would coerce it into a new copy, silently discarding the mutations.
function Copy-UnitVerbatim([string]$SrcRoot, [string]$TgtRoot, [string]$Unit, [System.Collections.IDictionary]$FileHashes) {
    $srcDir = Join-Path $SrcRoot $Unit
    $dstDir = Join-Path $TgtRoot "$VendorRoot/$Unit"
    New-Item -ItemType Directory -Path $dstDir -Force | Out-Null
    Get-ChildItem $srcDir -Recurse -File | ForEach-Object {
        $rel = Get-RepoRelative $srcDir $_.FullName
        $dst = Join-Path $dstDir $rel
        New-Item -ItemType Directory -Path (Split-Path $dst) -Force | Out-Null
        Copy-Item $_.FullName $dst -Force
        $FileHashes["$VendorRoot/$Unit/$rel"] = Get-FileSha $dst
    }
}

function Install-Package([string]$SrcRoot, [string]$Sha, [string]$TgtRoot, [pscustomobject]$Pkg, $Lock) {
    $short = $Sha.Substring(0, 7)
    $closure = Get-DependencyClosure $SrcRoot $Pkg
    foreach ($w in $closure.Warnings) { Write-Warning2 $w }

    $files = [ordered]@{}

    # 1. Vendored verbatim copies: the package root + every dependency unit.
    Copy-UnitVerbatim $SrcRoot $TgtRoot $Pkg.Root $files
    foreach ($dep in $closure.Deps) { Copy-UnitVerbatim $SrcRoot $TgtRoot $dep $files }

    $sourcePaths = @($Pkg.Root)

    # 2. Picker entry (agents only) — the one rewritten file.
    if ($Pkg.Type -eq 'agent') {
        $pickerSrc = Join-Path $SrcRoot ".claude/agents/$($Pkg.Name).md"
        if (Test-Path $pickerSrc) {
            $sourcePaths = @(".claude/agents/$($Pkg.Name).md") + $sourcePaths
            $pickerDst = Join-Path $TgtRoot ".claude/agents/$($Pkg.Name).md"
            New-Item -ItemType Directory -Path (Split-Path $pickerDst) -Force | Out-Null
            $content = Convert-PickerEntry (Get-Content $pickerSrc -Raw) $Lock.source.repo ".claude/agents/$($Pkg.Name).md" $short
            [System.IO.File]::WriteAllText($pickerDst, $content)
            $files[".claude/agents/$($Pkg.Name).md"] = Get-FileSha $pickerDst
        }
    }

    # 3. Skill stubs for the package itself (if a skill) and every bundled skill dep - both plain
    #    skills/{x} and shared/essentials/skills/{x} (e.g. analyze-plugin, analyze-logging), which
    #    are vendored the same way but were previously silently skipped here, leaving them
    #    undiscoverable in the consumer repo despite being on disk.
    $stubUnits = @()
    if ($Pkg.Type -eq 'skill') { $stubUnits += $Pkg.Root }
    $stubUnits += @($closure.Deps | Where-Object { $_ -match '^(skills|shared/essentials/skills)/[^/]+$' })
    foreach ($unit in $stubUnits) {
        $canonical = Join-Path $SrcRoot "$unit/SKILL.md"
        if (-not (Test-Path $canonical)) { continue }
        $skillName = Split-Path $unit -Leaf
        $stubDst = Join-Path $TgtRoot ".claude/skills/$skillName/SKILL.md"
        New-Item -ItemType Directory -Path (Split-Path $stubDst) -Force | Out-Null
        [System.IO.File]::WriteAllText($stubDst, (New-SkillStub $canonical "$VendorRoot/$unit/SKILL.md" $Lock.source.repo $short))
        $files[".claude/skills/$skillName/SKILL.md"] = Get-FileSha $stubDst
    }

    # 4. Lock entry.
    $entry = [ordered]@{
        type        = $Pkg.Type
        sha         = $Sha
        installedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        sourcePaths = $sourcePaths
        deps        = $closure.Deps
        peerRefs    = $closure.PeerRefs
        files       = $files
    }
    # Human-readable release pin (present only when installed/updated via -Release). `check` uses
    # its presence to compare against the latest release rather than branch commits.
    if ($script:ResolvedRelease) { $entry.release = $script:ResolvedRelease }
    $Lock.packages[$Pkg.Name] = $entry

    $relNote = if ($script:ResolvedRelease) { " ($($script:ResolvedRelease))" } else { '' }
    $script:Result.packages += [ordered]@{
        name = $Pkg.Name; type = $Pkg.Type; sha = $Sha; release = $script:ResolvedRelease
        deps = $closure.Deps; peerRefs = $closure.PeerRefs; fileCount = $files.Count
    }
    Write-Info "[DONE] Installed $($Pkg.Name) @ $short$relNote ($($files.Count) files, deps: $($closure.Deps.Count))" 'Green'
    foreach ($peer in $closure.PeerRefs) {
        Write-Info "       references peer agent '$peer' (not installed - 'pd-agents.ps1 install $peer' to add)" 'DarkGray'
    }
}

function Install-Infrastructure([string]$SrcRoot, [string]$Sha, [string]$TgtRoot, $Lock) {
    # Bootstrap this script into the target so update works without the source workspace.
    $selfSrc = Join-Path $SrcRoot $InstallerRelPath
    if (-not (Test-Path $selfSrc)) { $selfSrc = $PSCommandPath }   # dev tree before first commit
    $selfDst = Join-Path $TgtRoot $InstallerRelPath
    if ([System.IO.Path]::GetFullPath($selfSrc) -ne [System.IO.Path]::GetFullPath($selfDst)) {
        New-Item -ItemType Directory -Path (Split-Path $selfDst) -Force | Out-Null
        Copy-Item $selfSrc $selfDst -Force
    }
    $Lock.installer = @{ sourcePath = $InstallerRelPath; sha = $Sha }

    # "Call home" skill stub.
    $updSkill = Join-Path $TgtRoot '.claude/skills/pd-agents-update/SKILL.md'
    New-Item -ItemType Directory -Path (Split-Path $updSkill) -Force | Out-Null
    [System.IO.File]::WriteAllText($updSkill, ((Get-UpdateSkillStub $Lock.source.repo) -replace "`r`n", "`n"))

    # Copilot discovery of .claude/agents (Claude Code discovers it natively).
    if (-not $NoVsCode) {
        $vs = Join-Path $TgtRoot '.vscode/settings.json'
        try {
            $settings = if (Test-Path $vs) { Get-Content $vs -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
            if (-not $settings.ContainsKey('chat.agentFilesLocations')) { $settings['chat.agentFilesLocations'] = @{} }
            $locations = $settings['chat.agentFilesLocations']
            # Force-enable rather than skip-if-present: a pre-existing `false` (or any other
            # falsy value) would otherwise silently leave Copilot unable to discover the picker
            # entries this very install just wrote.
            if ($locations['.claude/agents'] -ne $true) {
                $locations['.claude/agents'] = $true
                New-Item -ItemType Directory -Path (Split-Path $vs) -Force | Out-Null
                (($settings | ConvertTo-Json -Depth 10) -replace "`r`n", "`n") + "`n" | Set-Content -Path $vs -NoNewline -Encoding utf8
                Write-Info "       added chat.agentFilesLocations['.claude/agents'] to .vscode/settings.json" 'DarkGray'
            }
        } catch {
            Write-Warning2 ".vscode/settings.json could not be parsed (comments?). Add manually: `"chat.agentFilesLocations`": { `".claude/agents`": true }"
        }
    }
}

# --- Check helpers -----------------------------------------------------------------------------------

function Get-DriftedFiles([string]$TgtRoot, $Entry) {
    $drifted = @()
    foreach ($rel in $Entry.files.Keys) {
        $full = Join-Path $TgtRoot $rel
        if (-not (Test-Path $full)) { $drifted += "$rel (missing)"; continue }
        if ((Get-FileSha $full) -ne $Entry.files[$rel]) { $drifted += $rel }
    }
    return $drifted
}

# True if a package pinned to a release tag has a newer release available upstream.
function Test-ReleaseBehind([string]$Repo, $Entry) {
    $current = $Entry.release
    if ($SourcePath) {
        $src = (Resolve-Path $SourcePath).Path
        $latest = git -C $src describe --tags --abbrev=0 2>$null
        if ($LASTEXITCODE -ne 0 -or -not $latest) { return $false }
        return (($latest -join '').Trim() -ne $current)
    }
    Assert-GhReady
    $latest = gh api "repos/$Repo/releases/latest" --jq '.tag_name' 2>&1
    if ($LASTEXITCODE -ne 0) { return $false }   # no releases yet -> treat as not behind
    return (($latest -join '').Trim() -ne $current)
}

# Upstream movement check for one package. Release-pinned packages compare against the latest
# release tag; otherwise gh transport compares commit dates (a path's latest touching commit newer
# than the pin's commit = behind) and local transport uses git ancestry.
function Test-PackageBehind([string]$Repo, $Entry) {
    if ($Entry.release) { return (Test-ReleaseBehind $Repo $Entry) }
    $paths = @($Entry.sourcePaths) + @($Entry.deps)
    if ($SourcePath) {
        Assert-GitReady
        $src = (Resolve-Path $SourcePath).Path
        foreach ($p in $paths) {
            $latest = (git -C $src log -1 --format=%H -- $p 2>$null)
            if (-not $latest) { continue }
            if ($latest -eq $Entry.sha) { continue }
            git -C $src merge-base --is-ancestor $latest $Entry.sha 2>$null
            if ($LASTEXITCODE -ne 0) { return $true }
        }
        return $false
    }
    Assert-GhReady
    $pinDateRaw = gh api "repos/$Repo/commits/$($Entry.sha)" --jq '.commit.committer.date' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "gh api failed resolving pinned commit $($Entry.sha): $pinDateRaw" }
    $pinDate = [datetime]::Parse(($pinDateRaw -join '').Trim()).ToUniversalTime()
    foreach ($p in $paths) {
        $json = gh api "repos/$Repo/commits?path=$p&per_page=1" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "gh api failed for path '$p': $json" }
        $commits = $json | ConvertFrom-Json
        if (-not $commits -or $commits.Count -eq 0) { continue }
        if ($commits[0].sha -eq $Entry.sha) { continue }
        $d = [datetime]::Parse($commits[0].commit.committer.date).ToUniversalTime()
        if ($d -gt $pinDate) { return $true }
    }
    return $false
}

# --- Command: list -----------------------------------------------------------------------------------

function Invoke-List {
    if (Test-IsSourceRepo $RepoRoot) {
        Write-Info "Exportable packages in $RepoRoot`:" 'Cyan'
        foreach ($pkg in (Get-ExportablePackages $RepoRoot | Sort-Object Type, Name)) {
            $closure = Get-DependencyClosure $RepoRoot $pkg
            $script:Result.packages += [ordered]@{ name = $pkg.Name; type = $pkg.Type; deps = $closure.Deps; peerRefs = $closure.PeerRefs }
            $depNote = if ($closure.Deps) { " deps: $($closure.Deps -join ', ')" } else { '' }
            Write-Info ("  {0,-42} {1,-6}{2}" -f $pkg.Name, $pkg.Type, $depNote)
        }
    } elseif (Test-IsTargetRepo $RepoRoot) {
        $lock = Get-Lock $RepoRoot
        Write-Info "Installed packages (source: $($lock.source.repo)):" 'Cyan'
        foreach ($name in $lock.packages.Keys) {
            $e = $lock.packages[$name]
            $script:Result.packages += [ordered]@{ name = $name; type = $e.type; sha = $e.sha; release = $e.release; installedAt = $e.installedAt }
            $pin = if ($e.release) { $e.release } else { $e.sha.Substring(0, 7) }
            Write-Info ("  {0,-42} {1,-6} pinned {2}" -f $name, $e.type, $pin)
        }
    } else {
        throw "This repo is neither a pdt-copilot-agents clone nor a target with $LockRelPath. Run install first."
    }
}

# --- Command: install --------------------------------------------------------------------------------

function Invoke-Install {
    $tgtRoot = if ($TargetPath) { (Resolve-Path $TargetPath).Path } else { $RepoRoot }
    if (-not $TargetPath -and (Test-IsSourceRepo $RepoRoot)) {
        throw "Running from the source repo: pass -TargetPath <repo to install into>."
    }
    if ((Test-IsSourceRepo $tgtRoot)) {
        throw "Refusing to install into '$tgtRoot' - it is the source workspace itself. Use the multi-root workspace there instead."
    }

    $lock = Get-Lock $tgtRoot
    $srcInfo = Get-PackageSource $lock.source.repo
    try {
        $names = if ($All) {
            (Get-ExportablePackages $srcInfo.Root | Where-Object Type -eq 'agent').Name
        } elseif ($Name) { @($Name) } else {
            throw "install requires a package name (or -All)."
        }
        foreach ($n in $names) {
            $pkg = Resolve-Package $srcInfo.Root $n
            # Re-install (already in the lock): remember the old closure so a shrunk one
            # (upstream dropped a dependency) doesn't leave orphaned vendored files behind -
            # same refcount-aware cleanup Invoke-Update already does.
            $oldFiles = if ($lock.packages.Contains($n)) { @($lock.packages[$n].files.Keys) } else { @() }
            Install-Package $srcInfo.Root $srcInfo.Sha $tgtRoot $pkg $lock
            if ($oldFiles.Count -gt 0) {
                $newFiles = @($lock.packages[$n].files.Keys)
                $othersFiles = @()
                foreach ($other in $lock.packages.Keys | Where-Object { $_ -ne $n }) { $othersFiles += @($lock.packages[$other].files.Keys) }
                foreach ($stale in ($oldFiles | Where-Object { $_ -notin $newFiles -and $_ -notin $othersFiles })) {
                    Remove-Item (Join-Path $tgtRoot $stale) -Force -ErrorAction SilentlyContinue
                    Write-Info "       removed stale $stale" 'DarkGray'
                }
            }
        }
        Install-Infrastructure $srcInfo.Root $srcInfo.Sha $tgtRoot $lock
        Save-Lock $tgtRoot $lock
        Write-Info "[DONE] Lock written to $LockRelPath. Review the diff and commit." 'Green'
    } finally {
        if ($srcInfo.Temp) { Remove-Item $srcInfo.Temp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# --- Command: check ----------------------------------------------------------------------------------

function Invoke-Check {
    if (-not (Test-IsTargetRepo $RepoRoot)) { throw "No $LockRelPath here - check runs from a target repo." }
    $lock = Get-Lock $RepoRoot
    $names = if ($Name) { @($Name) } else { @($lock.packages.Keys) }
    $anyBad = $false
    foreach ($n in $names) {
        if (-not $lock.packages.Contains($n)) { throw "Package '$n' is not installed (not in $LockRelPath)." }
        $e = $lock.packages[$n]
        $drifted = Get-DriftedFiles $RepoRoot $e
        $behind  = Test-PackageBehind $lock.source.repo $e
        $status = if (-not $behind -and -not $drifted) { 'ok' } elseif ($behind -and $drifted) { 'behind+drifted' } elseif ($behind) { 'behind' } else { 'drifted' }
        $pin = if ($e.release) { $e.release } else { $e.sha.Substring(0, 7) }
        $script:Result.packages += [ordered]@{ name = $n; sha = $e.sha; release = $e.release; status = $status; driftedFiles = $drifted }
        if ($status -eq 'ok') {
            Write-Info "[OK] $n is up to date (pinned $pin)." 'Green'
        } else {
            $anyBad = $true
            if ($behind) {
                $what = if ($e.release) { "a newer release than $pin is available" } else { "upstream has advanced past pin $pin" }
                Write-Info "[BEHIND] $n`: $what. Run: pwsh scripts/pd-agents.ps1 update $n" 'Yellow'
            }
            if ($drifted) {
                Write-Info "[DRIFT] $n`: locally modified files (edits belong upstream - PR them to $($lock.source.repo)):" 'Yellow'
                foreach ($f in $drifted) { Write-Info "        $f" 'Yellow' }
            }
        }
    }
    if ($anyBad) { Complete-Run 1 }
}

# --- Command: update ---------------------------------------------------------------------------------

function Invoke-Update {
    if (-not (Test-IsTargetRepo $RepoRoot)) { throw "No $LockRelPath here - update runs from a target repo." }
    $lock = Get-Lock $RepoRoot
    $names = if ($Name) { @($Name) } else { @($lock.packages.Keys) }

    # Stay on the release track: if every target package is release-pinned and the caller gave no
    # explicit -Ref/-Release, default to the latest release rather than the branch head. (Mixed
    # pinned/unpinned sets fall back to the branch and drop the release pin — an uncommon edge.)
    if (-not $Ref -and -not $Release) {
        $targets = @($names | Where-Object { $lock.packages.Contains($_) })
        $pinned  = @($targets | Where-Object { $lock.packages[$_].release })
        if ($pinned.Count -gt 0 -and $pinned.Count -eq $targets.Count) { $Release = 'latest' }
    }

    $srcInfo = Get-PackageSource $lock.source.repo
    $anySkipped = $false
    try {
        foreach ($n in $names) {
            if (-not $lock.packages.Contains($n)) { throw "Package '$n' is not installed." }
            $old = $lock.packages[$n]
            $upToDate = ($old.sha -eq $srcInfo.Sha)

            # Drift guard: compare recorded hashes against what's actually on disk. Computed even
            # when already at the pinned sha, so -Force can still restore a locally-edited file to
            # the pristine pinned copy instead of "nothing to update" masking the drift. A missing
            # file counts as drift for the "nothing to update" check (there's a local file to
            # restore), but doesn't require -Force to fix - restoring a deleted file overwrites
            # nothing, unlike a modified one, so only modified files block on -Force.
            $allDrifted  = @(Get-DriftedFiles $RepoRoot $old)
            $modDrifted  = @($allDrifted | Where-Object { $_ -notmatch '\(missing\)$' })
            if ($upToDate -and $allDrifted.Count -eq 0) {
                Write-Info "[OK] $n already at $($srcInfo.Sha.Substring(0,7)). Nothing to update." 'Green'
                continue
            }
            if ($modDrifted -and -not $Force) {
                $anySkipped = $true
                if ($upToDate) {
                    Write-Info "[DRIFT] $n`: already at $($srcInfo.Sha.Substring(0,7)) but locally modified files differ from the pinned copy:" 'Yellow'
                } else {
                    Write-Info "[DRIFT] $n`: skipping update - locally modified files would be overwritten:" 'Yellow'
                }
                foreach ($f in $modDrifted) { Write-Info "        $f" 'Yellow' }
                Write-Info "        PR local edits upstream (contribute skill in $($lock.source.repo)), or re-run with -Force." 'Yellow'
                continue
            }

            $pkg = Resolve-Package $srcInfo.Root $n
            $oldFiles = @($old.files.Keys)
            Install-Package $srcInfo.Root $srcInfo.Sha $RepoRoot $pkg $lock

            # Remove files that fell out of the closure (refcount-aware).
            $newFiles = @($lock.packages[$n].files.Keys)
            $othersFiles = @()
            foreach ($other in $lock.packages.Keys | Where-Object { $_ -ne $n }) { $othersFiles += @($lock.packages[$other].files.Keys) }
            foreach ($stale in ($oldFiles | Where-Object { $_ -notin $newFiles -and $_ -notin $othersFiles })) {
                Remove-Item (Join-Path $RepoRoot $stale) -Force -ErrorAction SilentlyContinue
                Write-Info "       removed stale $stale" 'DarkGray'
            }
        }

        # Installer self-update.
        $selfSrc = Join-Path $srcInfo.Root $InstallerRelPath
        $selfDst = Join-Path $RepoRoot $InstallerRelPath
        if ((Test-Path $selfSrc) -and ((-not (Test-Path $selfDst)) -or (Get-FileSha $selfSrc) -ne (Get-FileSha $selfDst))) {
            Copy-Item $selfSrc $selfDst -Force
            Write-Info "[NOTE] Installer script updated from source - re-run your command if behavior seems off." 'Cyan'
        }
        $lock.installer = @{ sourcePath = $InstallerRelPath; sha = $srcInfo.Sha }

        Save-Lock $RepoRoot $lock
        if ($anySkipped) {
            Write-Info "[DONE] Update finished with skipped packages (see DRIFT above)." 'Yellow'
            Complete-Run 1
        }
        Write-Info "[DONE] Update complete. Review the diff and commit." 'Green'
    } finally {
        if ($srcInfo.Temp) { Remove-Item $srcInfo.Temp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# --- Command: remove ---------------------------------------------------------------------------------

function Invoke-Remove {
    if (-not $Name) { throw "remove requires a package name." }
    if (-not (Test-IsTargetRepo $RepoRoot)) { throw "No $LockRelPath here - remove runs from a target repo." }
    $lock = Get-Lock $RepoRoot
    if (-not $lock.packages.Contains($Name)) { throw "Package '$Name' is not installed." }

    $othersFiles = @()
    foreach ($other in $lock.packages.Keys | Where-Object { $_ -ne $Name }) { $othersFiles += @($lock.packages[$other].files.Keys) }

    $removed = 0
    foreach ($rel in $lock.packages[$Name].files.Keys) {
        if ($rel -in $othersFiles) { continue }   # shared dep still claimed by another package
        $full = Join-Path $RepoRoot $rel
        if (Test-Path $full) { Remove-Item $full -Force; $removed++ }
    }
    $lock.packages.Remove($Name)

    # Prune now-empty directories under the vendor root and .claude/agents|skills.
    foreach ($dir in @("$VendorRoot", '.claude/agents', '.claude/skills')) {
        $full = Join-Path $RepoRoot $dir
        if (Test-Path $full) {
            Get-ChildItem $full -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
                Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } |
                ForEach-Object { Remove-Item $_.FullName -Force -Recurse }
        }
    }

    Save-Lock $RepoRoot $lock
    $script:Result.packages += [ordered]@{ name = $Name; removedFiles = $removed }
    Write-Info "[DONE] Removed $Name ($removed files; shared files claimed by other packages were kept)." 'Green'
    if ($lock.packages.Count -eq 0) {
        Write-Info "       No packages remain - scripts/pd-agents.ps1, the pd-agents-update skill, and the lock can be deleted manually if desired." 'DarkGray'
    }
}

# --- Interactive mode --------------------------------------------------------------------------------

function Read-Choice([string]$Prompt, [string[]]$Options) {
    for ($i = 0; $i -lt $Options.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $Options[$i]) }
    while ($true) {
        $sel = Read-Host $Prompt
        if ($sel -match '^\d+$' -and [int]$sel -ge 1 -and [int]$sel -le $Options.Count) { return $Options[[int]$sel - 1] }
        Write-Host "  Enter a number between 1 and $($Options.Count)." -ForegroundColor Yellow
    }
}

# Bare invocation (no command) drops here: a numbered menu that fills in the same script-scoped
# parameters the Invoke-* handlers read, then falls through to the dispatch switch. Network-free —
# package pickers enumerate the local source tree (source repo) or the lock file (target repo).
function Invoke-Interactive {
    if ($Json) { throw "Interactive mode cannot run with -Json. Pass a command: list | install | check | update | remove." }
    Write-Host "`n=== pd-agents (interactive) ===" -ForegroundColor Cyan

    $isSource = Test-IsSourceRepo $RepoRoot
    $isTarget = Test-IsTargetRepo $RepoRoot

    if ($isSource) {
        Write-Host "Running from the source workspace — installing a package into another repo.`n"
        $script:Command = 'install'
    } elseif ($isTarget) {
        $script:Command = Read-Choice 'Choose a command' @('list', 'check', 'update', 'remove')
    } else {
        throw "This directory is neither the pdt-copilot-agents source workspace nor a repo with $LockRelPath. Run from one of those."
    }

    switch ($script:Command) {
        'install' {
            $tp = Read-Host 'Target repo path to install into'
            if (-not $tp) { throw 'A target path is required.' }
            $script:TargetPath = $tp
            $pkgs = Get-ExportablePackages $RepoRoot | Sort-Object Type, Name
            $labels = $pkgs | ForEach-Object { "$($_.Name)  ($($_.Type))" }
            $chosen = Read-Choice 'Pick a package to install' $labels
            $script:Name = ($chosen -split '\s')[0]
        }
        'update' {
            $lock = Get-Lock $RepoRoot
            $opts = @('(all packages)') + @($lock.packages.Keys)
            $pick = Read-Choice 'Update which package' $opts
            if ($pick -ne '(all packages)') { $script:Name = $pick }
        }
        'remove' {
            $lock = Get-Lock $RepoRoot
            $keys = @($lock.packages.Keys)
            if (-not $keys) { throw 'Nothing installed to remove.' }
            $script:Name = Read-Choice 'Remove which package' $keys
        }
    }
}

# --- Dispatch ----------------------------------------------------------------------------------------

if (-not $Command) {
    Invoke-Interactive
} else {
    $valid = @('list', 'install', 'check', 'update', 'remove')
    if ($Command -notin $valid) {
        throw "Unknown command '$Command'. Use one of: $($valid -join ', ') — or omit the command for interactive mode."
    }
}

switch ($Command) {
    'list'    { Invoke-List }
    'install' { Invoke-Install }
    'check'   { Invoke-Check }
    'update'  { Invoke-Update }
    'remove'  { Invoke-Remove }
}
Complete-Run 0
