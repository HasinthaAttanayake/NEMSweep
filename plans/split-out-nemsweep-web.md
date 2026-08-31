# RUNBOOK: Split NEMSweep.Web into its own private repository

**Read this entire file before running a single command.**

You are moving one project out of one repository and into a new one. This runbook tells you exactly
what to type. Do not improvise. Do not "fix" anything you notice along the way. Do not skip
verification steps.

If any command produces output that does not match what this runbook says to expect, **STOP** and
report it. Do not try to work around it.

---

## PART 0 — THE RULES

### Rule 1: Do not make any change that is not written in this runbook.

You will see things that look wrong or untidy. Leave them alone. "No other changes" is a hard
requirement from the person who asked for this work.

### Rule 2: Never use `git push --force` or `git push -f`.

Not once. Not anywhere. If you think you need it, you have made a mistake earlier. STOP and report.

### Rule 3: Never delete anything outside the two repository folders named in this runbook.

### Rule 4: Do not merge any pull request.

You raise pull requests. A human merges them. If you are tempted to merge, STOP.

### Rule 5: When this runbook says STOP, actually stop.

Stop means: do not run the next command. Report what you found to the human and wait for an answer.

### Rule 6: Do not edit any of these files, in either repository.

Even though they mention `NEMSweep.Web` and it will look to you like they need updating:

- `NEMSweep.CLI.Tests/Scenarios/RunMetadataTests.cs` — it only contains the *text*
  `"NEMSweep.Web"` inside a made-up test path. It does not read any real file. It will still pass
  after the move. If you "fix" it you will break a passing test.
- `README.md`, `CITATION.cff`, `DATA-LICENSE.md`, anything under `docs/`, `.gitattributes`,
  `.gitignore`, `NEMSweep.CLI/appsettings.example.json`.

These are follow-up work for a human. They are **not** part of this task.

---

## PART 1 — FACTS YOU NEED

You must understand this before you start or you will produce a repository that does not compile.

### The dependency chain

```
NEMSweep.Web.Tests  ──references──>  NEMSweep.Web
NEMSweep.Web        ──references──>  NEMSweep.Contracts
NEMSweep.Web        ──references──>  NEMSweep.Model
NEMSweep.Contracts  ──references──>  (nothing)
NEMSweep.Model      ──references──>  (nothing)
```

This means:

1. **`NEMSweep.Web` cannot compile on its own.** It needs `NEMSweep.Contracts` and `NEMSweep.Model`
   sitting next to it. So the new repository gets **four** project folders, not one.
2. **`NEMSweep.Web.Tests` must move too.** If you leave it behind it will point at a project that no
   longer exists and the old repository will stop building.
3. `NEMSweep.Contracts` and `NEMSweep.Model` are **copied**, not moved. They stay in the old
   repository as well, because `NEMSweep.CLI` needs them there.

> Yes, this duplicates two projects across two repositories. That is a known, accepted consequence
> of this task. Do not try to solve it with a git submodule, a NuGet package, or a symbolic link.
> Copy the folders.

### The four folders that go into the new repository

| Folder | Why |
|---|---|
| `NEMSweep.Web/` | The thing being moved. **103 MB** — most of it published data files. |
| `NEMSweep.Web.Tests/` | Tests it. Breaks the old repo if left behind. |
| `NEMSweep.Contracts/` | Copied. `NEMSweep.Web` will not compile without it. |
| `NEMSweep.Model/` | Copied. `NEMSweep.Web.csproj` declares a reference to it. |

### The five loose files that go into the new repository

`.gitignore`, `.gitattributes`, `Directory.Build.props`, `LICENSE.md`, `DATA-LICENSE.md`

Copy all five **exactly as they are**. Do not edit them.

### ⚠️ TRAP 1 — the folder name must stay `NEMSweep.Web`

In the new repository the folder **must** be at the top level and **must** still be called
`NEMSweep.Web`. Do not "flatten" it. Do not put its contents at the root of the new repo.

Two things break if you rename or flatten it:

- `.gitignore` contains a line `!NEMSweep.Web/wwwroot/data/`. That line is what allows the 103 MB of
  data files to be committed at all. Another line above it says `data/` is ignored. If the folder
  path changes, **git will silently ignore 103 MB of data and you will not get an error message.**
- `NEMSweep.Web/build.sh` runs `dotnet publish ./NEMSweep.Web/NEMSweep.Web.csproj` — a path relative
  to the repository root.

### ⚠️ TRAP 2 — the data must actually be committed

After your first `git add` in the new repo, you will run a command that counts the data files. If
the count is not about 300 files, the `.gitignore` trap above has bitten you. STOP.

### Source commit

Everything you copy comes from the `main` branch of `HasinthaAttanayake/NEMSweep` at commit
`d881c5f542bc`. Write that commit into the pull request description so a human can trace it.

### History is not preserved

You are copying files, not rewriting git history. The new repository starts with a fresh history.
This is deliberate — preserving history requires `git filter-repo`, which is dangerous and is not
part of this task. Do not attempt it.

---

## PART 2 — SETTINGS

Read these. You will use them throughout.

```
NEW_REPO_NAME   = nemsweep-web
NEW_REPO_DIR    = /git/nemsweep-web
OLD_REPO_DIR    = /git/NEMSweep          (see Step 2.3 if it is somewhere else)
GITHUB_OWNER    = HasinthaAttanayake
SOURCE_COMMIT   = d881c5f542bc
```

If you are on Windows using Git Bash or WSL, `/git` means the folder `C:\git`. Use `/git` in
commands; Git Bash translates it.

---

## PART 3 — PREFLIGHT CHECKS

Run every one of these. If any fails, STOP.

### Step 3.1 — Check the tools exist

```bash
git --version
gh --version
dotnet --version
```

**Expect:** three version numbers. `dotnet` must report `10.` followed by something.

**If `dotnet` reports 8 or 9:** STOP. This project needs the .NET 10 SDK.

### Step 3.2 — Check you are logged in to GitHub

```bash
gh auth status
```

**Expect:** `Logged in to github.com account ...` and a token with `repo` scope.

**If it says you are not logged in:** STOP. A human must run `gh auth login`.

### Step 3.3 — Find the existing NEMSweep repository

```bash
ls -d /git/NEMSweep
```

**If that works:** good, `OLD_REPO_DIR` is `/git/NEMSweep`.

**If it says "No such file or directory":** find it:

```bash
find / -maxdepth 6 -type d -name "NEMSweep" -not -path "*/node_modules/*" 2>/dev/null
```

Pick the one that contains a file called `NEMSweep.slnx`. Use that path as `OLD_REPO_DIR` everywhere
below. **If you find more than one, or none, STOP and ask.**

### Step 3.4 — Check the old repository is clean and up to date

```bash
cd /git/NEMSweep
git status
```

**Expect:** `nothing to commit, working tree clean`.

**If there are uncommitted changes:** STOP. Do not stash them. Do not commit them. Ask the human.

```bash
git checkout main
git pull origin main
git log --oneline -1
```

**Expect:** you are on `main` and it pulled without conflict.

### Step 3.5 — Confirm the old repository builds BEFORE you touch anything

This matters. If it is already broken, you need to know now, so you do not get blamed for it.

```bash
cd /git/NEMSweep
dotnet build NEMSweep.slnx
```

**Expect:** `Build succeeded`.

**If the build fails:** STOP. Report the error. Do not continue. Nothing in this runbook will fix a
pre-existing build failure.

### Step 3.6 — Confirm `/git` exists

```bash
mkdir -p /git
ls -d /git
```

### Step 3.7 — Confirm the new folder does NOT already exist

```bash
ls -d /git/nemsweep-web
```

**Expect:** `No such file or directory`. That is the correct, good answer here.

**If the folder already exists:** STOP. Do not delete it. Ask the human what it is.

---

## PART 4 — BUILD THE NEW REPOSITORY LOCALLY

### Step 4.1 — Create the folder

```bash
mkdir -p /git/nemsweep-web
cd /git/nemsweep-web
```

### Step 4.2 — Copy the four project folders

Run these one at a time. The trailing slashes matter.

```bash
cp -R /git/NEMSweep/NEMSweep.Web /git/nemsweep-web/NEMSweep.Web
cp -R /git/NEMSweep/NEMSweep.Web.Tests /git/nemsweep-web/NEMSweep.Web.Tests
cp -R /git/NEMSweep/NEMSweep.Contracts /git/nemsweep-web/NEMSweep.Contracts
cp -R /git/NEMSweep/NEMSweep.Model /git/nemsweep-web/NEMSweep.Model
```

### Step 4.3 — Copy the five loose files

```bash
cp /git/NEMSweep/.gitignore /git/nemsweep-web/.gitignore
cp /git/NEMSweep/.gitattributes /git/nemsweep-web/.gitattributes
cp /git/NEMSweep/Directory.Build.props /git/nemsweep-web/Directory.Build.props
cp /git/NEMSweep/LICENSE.md /git/nemsweep-web/LICENSE.md
cp /git/NEMSweep/DATA-LICENSE.md /git/nemsweep-web/DATA-LICENSE.md
```

### Step 4.4 — Delete any build leftovers that came along

`bin` and `obj` folders must not be committed.

```bash
cd /git/nemsweep-web
find . -type d -name bin -prune -exec rm -rf {} +
find . -type d -name obj -prune -exec rm -rf {} +
```

### Step 4.5 — Verify the copy is complete

```bash
cd /git/nemsweep-web
ls
```

**Expect exactly these nine entries** (order may differ):

```
DATA-LICENSE.md   Directory.Build.props   LICENSE.md
NEMSweep.Contracts   NEMSweep.Model   NEMSweep.Web   NEMSweep.Web.Tests
```

plus the two hidden files. Check them:

```bash
ls -a | grep -E '^\.git(ignore|attributes)$'
```

**Expect:** both `.gitignore` and `.gitattributes`.

Now check the data came across:

```bash
find NEMSweep.Web/wwwroot/data -name '*.json' | wc -l
du -sh NEMSweep.Web/wwwroot/data
```

**Expect:** roughly **300** json files and about **102M**.

**If the count is 0 or very small:** STOP. The copy failed.

### Step 4.6 — Create the solution file

The old `NEMSweep.slnx` lists seven projects. The new repository has four. You are writing a new
file, not copying the old one.

```bash
cd /git/nemsweep-web
cat > NEMSweep.Web.slnx <<'EOF'
<Solution>
  <Project Path="NEMSweep.Contracts/NEMSweep.Contracts.csproj" />
  <Project Path="NEMSweep.Model/NEMSweep.Model.csproj" />
  <Project Path="NEMSweep.Web/NEMSweep.Web.csproj" />
  <Project Path="NEMSweep.Web.Tests/NEMSweep.Web.Tests.csproj" />
</Solution>
EOF
cat NEMSweep.Web.slnx
```

**Expect:** the file prints back with four `<Project` lines.

### Step 4.7 — ⚠️ CRITICAL: prove the new repository compiles

Do not skip this. Do not continue until it passes.

```bash
cd /git/nemsweep-web
dotnet restore NEMSweep.Web.slnx
dotnet build NEMSweep.Web.slnx
```

**Expect:** `Build succeeded` with `0 Error(s)`.

**If the build fails:** STOP and report the exact error. Do not start editing `.csproj` files to make
it pass. The most likely cause is a folder that did not copy in Step 4.2.

### Step 4.8 — Prove the tests pass

```bash
cd /git/nemsweep-web
dotnet test NEMSweep.Web.slnx
```

**Expect:** `Passed!` and zero failed tests.

**If any test fails:** STOP and report which one.

### Step 4.9 — Prove the website actually runs

```bash
cd /git/nemsweep-web
dotnet build NEMSweep.Web/NEMSweep.Web.csproj
```

**Expect:** `Build succeeded`.

Optionally, if you can run a background process and open a URL: `dotnet run --project NEMSweep.Web`
should serve on `http://localhost:5021`. If you cannot do that, the build succeeding is enough.

### Step 4.10 — Clean up build output again before committing

```bash
cd /git/nemsweep-web
find . -type d -name bin -prune -exec rm -rf {} +
find . -type d -name obj -prune -exec rm -rf {} +
```

---

## PART 5 — PUSH THE NEW REPOSITORY AND RAISE ITS PULL REQUEST

A brand-new repository has nothing to open a pull request *against*. So you will do this in two
pushes: a small starting commit on `main`, then the real content on a branch, then a PR from the
branch into `main`.

### Step 5.1 — Start the git repository

```bash
cd /git/nemsweep-web
git init -b main
```

**Expect:** `Initialized empty Git repository`.

### Step 5.2 — Make the first commit contain only the licences

```bash
cd /git/nemsweep-web
git add LICENSE.md DATA-LICENSE.md
git status --short
```

**Expect:** exactly two lines, both starting with `A`.

```bash
git commit -m "Add licences"
```

### Step 5.3 — Create the private repository on GitHub and push `main`

```bash
cd /git/nemsweep-web
gh repo create nemsweep-web --private --source=. --remote=origin --push
```

**Expect:** it prints the new repository URL and pushes.

**If it says the repository already exists:** STOP. Do not pick a different name on your own. Ask.

Verify:

```bash
gh repo view HasinthaAttanayake/nemsweep-web --json name,visibility,defaultBranchRef
```

**Expect:** `"visibility":"PRIVATE"`.

**If it says PUBLIC:** STOP immediately and report it. This repository must be private.

### Step 5.4 — Put the real content on a branch

```bash
cd /git/nemsweep-web
git checkout -b import/nemsweep-web
git add .
```

Now check the trap from Part 1:

```bash
git status --short | grep -c "NEMSweep.Web/wwwroot/data"
```

**Expect:** roughly **300**.

**If it prints 0:** STOP. The `.gitignore` has swallowed the data. Do not fix it by editing
`.gitignore`. Report it.

Also confirm no build output sneaked in:

```bash
git status --short | grep -E "/(bin|obj)/" | head
```

**Expect:** no output at all.

### Step 5.5 — Commit

```bash
cd /git/nemsweep-web
git commit -F - <<'EOF'
Import NEMSweep.Web from the NEMSweep repository

Copied verbatim from HasinthaAttanayake/NEMSweep at commit d881c5f542bc.
No file contents were changed.

Four projects are present. NEMSweep.Web cannot compile without them:

- NEMSweep.Web            the Blazor WebAssembly results site
- NEMSweep.Web.Tests      its tests
- NEMSweep.Contracts      referenced by NEMSweep.Web
- NEMSweep.Model          referenced by NEMSweep.Web.csproj

NEMSweep.Contracts and NEMSweep.Model are copies. They remain in the
NEMSweep repository as well, because NEMSweep.CLI depends on them there.
Keeping the two copies in step is follow-up work, not part of this import.

NEMSweep.Web.slnx is new: the original solution listed seven projects and
this repository has four.

Git history was not preserved.
EOF
```

### Step 5.6 — Push the branch

```bash
cd /git/nemsweep-web
git push -u origin import/nemsweep-web
```

This pushes about 103 MB. It may take several minutes. Let it finish.

**If it fails with a network error:** wait 5 seconds and run the same command again. Try at most
4 times. If it still fails, STOP and report.

### Step 5.7 — Raise the pull request

```bash
cd /git/nemsweep-web
gh pr create --repo HasinthaAttanayake/nemsweep-web \
  --base main --head import/nemsweep-web \
  --title "Import NEMSweep.Web from the NEMSweep repository" \
  --body "$(cat <<'EOF'
Copied verbatim from `HasinthaAttanayake/NEMSweep` at commit `d881c5f542bc`. No file contents were changed.

## What is here

| Project | Why it is in this repository |
|---|---|
| `NEMSweep.Web` | The project being moved. |
| `NEMSweep.Web.Tests` | Tests `NEMSweep.Web`. Would break the old repository if left behind. |
| `NEMSweep.Contracts` | `NEMSweep.Web` references it. Copy. |
| `NEMSweep.Model` | `NEMSweep.Web.csproj` declares a reference to it. Copy. |

`NEMSweep.Web.slnx` is new — the original solution listed seven projects and this one has four.

## Verified before raising this

- `dotnet build NEMSweep.Web.slnx` succeeds.
- `dotnet test NEMSweep.Web.slnx` passes.
- The ~300 published data files under `NEMSweep.Web/wwwroot/data` are committed.

## Known consequences, for a human to decide on later

- `NEMSweep.Contracts` and `NEMSweep.Model` now exist in two repositories and can drift apart.
- Git history was not preserved; this is a fresh import.
- `Directory.Build.props` still records `RepositoryUrl` as the NEMSweep repository.
- There is no CI workflow in this repository yet.

Paired with a pull request on `HasinthaAttanayake/NEMSweep` that removes `NEMSweep.Web` and `NEMSweep.Web.Tests`. **Do not merge that one before this one.**
EOF
)"
```

**Expect:** it prints a pull request URL. Write it down; you need it in Part 7.

---

## PART 6 — REMOVE NEMSweep.Web FROM THE OLD REPOSITORY

Do not start this until Part 5 finished and you have the new pull request URL.

### Step 6.1 — Fresh branch off main

```bash
cd /git/NEMSweep
git checkout main
git pull origin main
git checkout -b remove/nemsweep-web
git status
```

**Expect:** on branch `remove/nemsweep-web`, working tree clean.

### Step 6.2 — Delete the two folders

Only these two. `NEMSweep.Contracts` and `NEMSweep.Model` **stay** — `NEMSweep.CLI` needs them.

```bash
cd /git/NEMSweep
git rm -r --quiet NEMSweep.Web
git rm -r --quiet NEMSweep.Web.Tests
```

Confirm they are gone and nothing else was touched:

```bash
ls
```

**Expect:** `NEMSweep.Web` and `NEMSweep.Web.Tests` are absent. `NEMSweep.CLI`, `NEMSweep.CLI.Tests`,
`NEMSweep.Contracts`, `NEMSweep.Model`, `NEMSweep.Model.Tests`, `docs`, `scenarios`, `schema`,
`sweeps` are all still there.

### Step 6.3 — Remove the two projects from the solution file

Open `/git/NEMSweep/NEMSweep.slnx`. Delete exactly these two lines:

```xml
  <Project Path="NEMSweep.Web/NEMSweep.Web.csproj" />
  <Project Path="NEMSweep.Web.Tests/NEMSweep.Web.Tests.csproj" />
```

Change nothing else in the file. Then check it:

```bash
cd /git/NEMSweep
cat NEMSweep.slnx
```

**Expect exactly this:**

```xml
<Solution>
  <Project Path="NEMSweep.CLI.Tests/NEMSweep.CLI.Tests.csproj" />
  <Project Path="NEMSweep.CLI/NEMSweep.CLI.csproj" Id="6038508c-acff-46b7-8578-4b90cb7b132d" />
  <Project Path="NEMSweep.Contracts/NEMSweep.Contracts.csproj" Id="072a96da-d7d5-43cd-9401-381803305916" />
  <Project Path="NEMSweep.Model.Tests/NEMSweep.Model.Tests.csproj" Id="d4c06f12-2c71-467b-ba1d-17f825d4cfc7" />
  <Project Path="NEMSweep.Model/NEMSweep.Model.csproj" Id="0003c1e1-66a2-4216-b184-468f4f7e8733" />
</Solution>
```

**Do not** remove or change the `Id=` attributes on the remaining lines.

### Step 6.4 — Check nothing else changed

```bash
cd /git/NEMSweep
git status --short | grep -v "^D " | head
```

**Expect:** exactly one line — the modified `NEMSweep.slnx`:

```
M  NEMSweep.slnx
```

**If any other file is modified:** you have edited something you should not have. STOP, run
`git checkout -- <that file>`, and re-read Rule 6.

### Step 6.5 — ⚠️ CRITICAL: prove the old repository still compiles

```bash
cd /git/NEMSweep
dotnet build NEMSweep.slnx
```

**Expect:** `Build succeeded`, `0 Error(s)`.

**If it fails:** STOP and report. Do not start editing files to make it pass.

### Step 6.6 — Prove the old repository's tests still pass

```bash
cd /git/NEMSweep
dotnet test NEMSweep.slnx
```

**Expect:** `Passed!`, zero failures.

The tests in `NEMSweep.CLI.Tests/Scenarios/RunMetadataTests.cs` mention `NEMSweep.Web` in strings.
**They will still pass.** They do not touch the filesystem. Leave them alone.

### Step 6.7 — Prove the documentation site still builds

CI builds the docs with warnings treated as errors, so check it now rather than finding out in CI.

```bash
cd /git/NEMSweep
dotnet tool restore
dotnet docfx docs/docfx.json --warningsAsErrors
```

**Expect:** it completes without error. The docs mention `NEMSweep.Web` in plain text, which is fine
— none of those are links, so nothing breaks.

**If it fails:** STOP and report.

### Step 6.8 — 🛑 STOP HERE AND TELL THE HUMAN THIS

There is one consequence of this removal that you must report **before** raising the pull request.
Do not fix it. Just report it and wait for a reply.

> **The command-line tool will lose its input data.**
>
> `NEMSweep.CLI/appsettings.example.json` sets `"dataRoot": "NEMSweep.Web/wwwroot/data"`. That
> folder is being deleted from this repository. The CLI will still **compile** and its tests will
> still **pass**, but after this pull request merges:
>
> ```
> dotnet run --project NEMSweep.CLI -- --run-scenario
> ```
>
> will fail at runtime, because the demand and weather files it reads are no longer in this
> repository.
>
> Fixing this means either editing `appsettings.example.json` or moving the data somewhere else.
> Both are changes beyond "remove NEMSweep.Web", so I have not made them.
>
> **How do you want to handle this?**

Wait for an answer. If the human says "just raise the PR and note it", continue to Step 6.9. The
pull request body below already describes the problem.

---

## PART 7 — PUSH AND RAISE THE REMOVAL PULL REQUEST

### Step 7.1 — Commit

```bash
cd /git/NEMSweep
git commit -F - <<'EOF'
Remove NEMSweep.Web and NEMSweep.Web.Tests

The Blazor results site now lives in its own private repository. Both
project folders are deleted here and their two entries are removed from
NEMSweep.slnx.

NEMSweep.Contracts and NEMSweep.Model stay: NEMSweep.CLI depends on them.

Nothing else is changed. Prose references to NEMSweep.Web in README.md,
CITATION.cff, DATA-LICENSE.md and docs/ are left as they are, as is the
dataRoot in NEMSweep.CLI/appsettings.example.json, which now points at a
path this repository no longer carries.
EOF
```

### Step 7.2 — Push

```bash
cd /git/NEMSweep
git push -u origin remove/nemsweep-web
```

**If it fails with a network error:** wait 5 seconds, retry. At most 4 attempts.

### Step 7.3 — Raise the pull request

Replace `<NEW_REPO_PR_URL>` with the URL you saved in Step 5.7.

```bash
cd /git/NEMSweep
gh pr create --repo HasinthaAttanayake/NEMSweep \
  --base main --head remove/nemsweep-web \
  --title "Remove NEMSweep.Web and NEMSweep.Web.Tests" \
  --body "$(cat <<'EOF'
The Blazor results site has moved to its own private repository. This removes it from here.

Paired with <NEW_REPO_PR_URL>. **Merge that one first.**

## What changed

- Deleted `NEMSweep.Web/`
- Deleted `NEMSweep.Web.Tests/`
- Removed those two `<Project>` entries from `NEMSweep.slnx`

Nothing else. `NEMSweep.Contracts` and `NEMSweep.Model` stay, because `NEMSweep.CLI` depends on them.

## Verified before raising this

- `dotnet build NEMSweep.slnx` succeeds.
- `dotnet test NEMSweep.slnx` passes.
- `dotnet docfx docs/docfx.json --warningsAsErrors` succeeds.

## ⚠️ Known consequence — needs a decision

`NEMSweep.CLI/appsettings.example.json` sets `"dataRoot": "NEMSweep.Web/wwwroot/data"`, which no longer exists in this repository.

The CLI still compiles and its tests still pass, but `dotnet run --project NEMSweep.CLI -- --run-scenario` will fail at runtime because the demand and weather artifacts it reads have moved out with the web project.

This was left unfixed deliberately: changing it is outside "remove NEMSweep.Web". It needs a decision about where the input data should live.

## Left alone on purpose

These still mention `NEMSweep.Web` in prose and were not edited:

`README.md`, `CITATION.cff`, `DATA-LICENSE.md`, `docs/index.md`, `docs/guide/index.md`, `docs/guide/outputs.md`, `docs/exploring/sensitivity-analysis.md`, `.gitignore`, `.gitattributes`

`NEMSweep.CLI.Tests/Scenarios/RunMetadataTests.cs` contains the string `"NEMSweep.Web"` in synthetic test paths. It does not touch the filesystem and still passes.
EOF
)"
```

**Expect:** a pull request URL.

---

## PART 8 — FINAL CHECKS

Run all of these. Report the results.

```bash
# 1. New repo is private
gh repo view HasinthaAttanayake/nemsweep-web --json visibility

# 2. Both pull requests are open
gh pr list --repo HasinthaAttanayake/nemsweep-web
gh pr list --repo HasinthaAttanayake/NEMSweep

# 3. New repo builds and tests
cd /git/nemsweep-web && dotnet build NEMSweep.Web.slnx && dotnet test NEMSweep.Web.slnx

# 4. Old repo builds and tests
cd /git/NEMSweep && dotnet build NEMSweep.slnx && dotnet test NEMSweep.slnx

# 5. Old repo main branch is untouched
cd /git/NEMSweep && git log origin/main --oneline -1
```

### Checklist — every line must be YES

- [ ] `/git/nemsweep-web` exists and contains four project folders
- [ ] The new GitHub repository is **PRIVATE**
- [ ] About 300 data files under `NEMSweep.Web/wwwroot/data` are committed in the new repo
- [ ] New repo: `dotnet build` succeeds, `dotnet test` passes
- [ ] Old repo: `dotnet build` succeeds, `dotnet test` passes, docfx succeeds
- [ ] Two pull requests are open, each linking to the other
- [ ] Neither pull request has been merged
- [ ] `git push --force` was never used
- [ ] `NEMSweep.Contracts` and `NEMSweep.Model` still exist in the **old** repo
- [ ] No file outside `NEMSweep.slnx` was edited in the old repo
- [ ] The Step 6.8 warning was reported to the human

### Report back with

1. Both pull request URLs.
2. The result of every command in Part 8.
3. Anything that made you STOP, and what you did about it.

---

## APPENDIX — WHEN THINGS GO WRONG

**"Build succeeded" but with warnings.** Warnings are fine. Only `error` matters.

**The push is slow / seems stuck.** 103 MB is genuinely slow. Wait 10 minutes before worrying.

**A push fails with a network error.** Wait 5 seconds, run the same command again. Up to 4 attempts,
then STOP. Never add `--force`.

**`gh repo create` says the name is taken.** STOP. Do not invent another name.

**You committed something you should not have, and have NOT pushed yet.**

```bash
git reset --soft HEAD~1
```

Then unstage the wrong file with `git restore --staged <file>` and commit again. **Only if you have
not pushed.** If you have pushed, STOP and ask.

**You edited a file you were told not to, and have NOT committed yet.**

```bash
git checkout -- <that file>
```

**You are lost, confused, or something does not match this runbook.** STOP. Report where you are,
the last command you ran, and its exact output. Do not guess. Do not improvise. Do not delete
anything to "start clean".
