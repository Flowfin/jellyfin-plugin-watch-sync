# What this board's gate takes from the other one, and what it does not

The gate of `Flowfin/jellyfin-plugin-sso` is the target this repository's quality
milestone aims at. This file says, check by check, what is adopted, what is
refused, and one line of reasoning for every difference in either direction. An
unexplained gap is a defect. An explained one is a decision.

Both boards have since moved under a new owner, and the commands below were
written against the old one. The old paths still answer, because the forge
redirects a moved repository, but a command that only works through a redirect is
one that stops working the day the old name is taken by somebody else. They are
now written with the name each repository reports for itself:

    gh api repos/iderex/jellyfin-plugin-sso --jq .full_name
    Flowfin/jellyfin-plugin-sso
    gh api repos/iderex/jellyfin-plugin-watch-sync --jq .full_name
    Flowfin/jellyfin-plugin-watch-sync

Nothing here is a plan for what the other board should do. It reads that board and
decides about this one.

## The two gates, measured

The other board's required set:

    gh api repos/Flowfin/jellyfin-plugin-sso/rulesets --jq '.[].id' \
      | xargs -I{} gh api repos/Flowfin/jellyfin-plugin-sso/rulesets/{} \
        --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context] | .[]'
    build
    ABI floor build
    Package (JPRM) / Build package
    Package (JPRM) / Generate SBOM
    CodeQL
    Analyze (csharp)
    DCO sign-off
    Deterministic PR-hygiene checks
    Enforce greppable invariants
    Reject Trojan Source Unicode
    Audit workflows (zizmor)
    prettier
    dependency-review

This board's:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/rulesets/20464507 \
      --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context] | .[]'
    call / build
    call / test
    Reject Trojan Source Unicode
    Audit workflows (zizmor)

Four against thirteen. The gap is the subject of this file and of the milestone
it belongs to, and most of it is work that has not been done rather than work that
was refused.

THIS PASSAGE QUOTED THREE AND SAID `Three against thirteen`, AND THE COMMAND
BESIDE IT RETURNS FOUR. That is rule one's own defect standing in the document
that tracks this board's gate. Neither quotation above is anchored to a commit,
because neither is about the tree: both are live readings of a repository
setting, so a stale one here is a reader told the gate is looser than it is.
`Audit workflows (zizmor)` had joined the required set and nothing in this tree
noticed, because nothing in this tree reads a ruleset. It was found by running
the command rather than by reading what was pasted under it, and the row for that
check further down carried the same claim and is corrected in the same change.
Both sets above were re-taken for this edit, the other board's with this board's
rather than carried across from the paragraph.

What actually reports on this board's mainline today, which is a different set
again, because a check can run without being required. Read at
`ab3b5387c8f26f1980358c10478428e4ab6436f0`:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/commits/ab3b5387c8f26f1980358c10478428e4ab6436f0/check-runs?per_page=100 \
      --jq '[.check_runs[].name] | sort | unique | .[]'
    Audit workflows (zizmor)
    Refuse a trigger naming a branch that does not exist
    Reject Trojan Source Unicode
    Scorecard analysis
    Suite (macos-latest)
    Suite (ubuntu-latest)
    Suite (windows-latest)
    call / Analyze (csharp)
    call / build
    call / test
    call / update_release_draft

`DCO sign-off`, `dependency-review` and `Deterministic PR-hygiene checks` are absent
from that list because all three are triggered on a pull request only:

    git grep -nE '^  (push|pull_request|schedule|workflow_dispatch|repository_dispatch):' \
      -- .github/workflows/dco.yml .github/workflows/dependency-review.yml \
         .github/workflows/pull-request-check.yml .github/workflows/code-scanning.yaml
    .github/workflows/code-scanning.yaml:18:  push:
    .github/workflows/code-scanning.yaml:20:  pull_request:
    .github/workflows/code-scanning.yaml:22:  schedule:
    .github/workflows/code-scanning.yaml:26:  workflow_dispatch:
    .github/workflows/dco.yml:10:  pull_request:
    .github/workflows/dependency-review.yml:4:  pull_request:
    .github/workflows/pull-request-check.yml:24:  pull_request:

The first file in that output is the reason the next paragraph exists.

`CodeQL` is absent for a different reason and the difference matters, because a
reader who takes the first reason for it would conclude the analysis stops on the
mainline. The scanning workflow runs on a push to `master` as well, which is where
`call / Analyze (csharp)` in the list above comes from. What only exists on a pull
request is the `CodeQL` check run, which the code-scanning service creates to report
findings against a diff. On a push the analysis lands as an entry under
`code-scanning/analyses` instead, and that is the thing #165 was read against.

That measurement was taken while the scan was a call into a shared workflow, which
is why the name in it carries a `call /` prefix. #96 replaced the call with
`.github/workflows/code-scanning.yaml`, so the names that list will report next are
the four in `docs/code-scanning.md`, one per language, and `call / Analyze (csharp)`
is gone. The list above is left as it was measured rather than edited to predict the
next reading.

Every one of them is green on the pull request this section was last measured
against:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/commits/922311a7e7715bb05d1c9644ef055d5861526f61/check-runs?per_page=100 \
      --jq '[.check_runs[] | select(.conclusion != "success") | .name] | length'
    0

The list grew by three since it was first pasted, and one entry in it changed
meaning without changing its spelling. The three are the suite legs from #75. The
one is `call / Analyze (csharp)`, which appeared here while the job behind it was
being skipped: the workflow named this repository by the name it carried before it
moved, the shared workflow it calls guards its only job on that name, and a skipped
job reports success rather than absence. #165 repaired the name. So a row of this
table saying a check is `here` is a claim about the check run and not about the
analysis behind it, and the two came apart for two days on the one row where a
reader would least expect it.

One entry in that list is still that shape and is left there on purpose.
`call / update_release_draft` comes from `changelog.yaml`, which passes the same
former name, and it reports a conclusion rather than a result:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/commits/ab3b5387c8f26f1980358c10478428e4ab6436f0/check-runs?per_page=100 \
      --jq '.check_runs[] | select(.conclusion != "success") | "\(.name) \(.conclusion)"'
    call / update_release_draft skipped

Every other name in the list is `success`. That one is `skipped`, and it stays that
way by the answer on #176 rather than because nobody looked: it is kept, unrun,
until #116 decides what the release route reads its change list from.

## The rows

`state` is one of `here`, meaning it runs on this board today; `owed`, meaning it
is adopted and an issue lands it; or `refused`, meaning this board does not take
it and the reason is in the row.

### Adopted from the other gate

| check there | state here | what it is here, or what lands it |
| --- | --- | --- |
| `build` | here | `call / build`, a call into the shared plugin workflow. It is required. #90 replaces the call with this repository's own workflow so warnings become errors, which the shared one does not do. |
| `ABI floor build` | refused, #91 | A floor build repeats the build against the oldest package a plugin claims to support, and here that is the package it already builds against. `build.yaml` carries one `targetAbi` per line, `BuildTargetsTests` reads each one off the assembly that target compiled against and refuses the two disagreeing, so a second job would compile the same bytes twice and report green having measured nothing. There is no window between the floor and the build for a missing member to appear in: a member added after the floor does not compile, which is what that board's check catches at load and this one catches at the compiler. The row said `owed, #91` and named a doubled build, and what was owed turned out to be the other direction, a floor that RISES and strands installs below it. That is refused by `raising-an-abi-floor-carries-a-changelog-entry` in `Deterministic PR-hygiene checks` rather than by a build. |
| `Package (JPRM) / Build package` | here | `package / Package and inventory`, from #101. Packaging runs on every pull request, at the packager commit and the framework the release route uses, which `PackagingGateTests` holds the two files to. It builds the one line `build.yaml` names at its top level: the packager stamps the archive with that top level ABI rather than with the framework it was told to build, so a second call for the second line would ship an archive claiming a server it cannot run on. The per-line half is what is left of #101 and #117 is the release half. Not required by the ruleset; #105. |
| `Package (JPRM) / Generate SBOM` | here | The component inventory, written in the same run as the package and attached to it, from #101. It is read off the locked restore rather than off a fetch of its own, so it describes the graph `packages.lock.json` holds. Nothing publishes it yet; #118 is the release half. Not required by the ruleset; #105. |
| `CodeQL` | here | Reports as `CodeQL` on a pull request. The code-scanning service creates it against the diff; no workflow in this repository names it. Not required by the ruleset; #105 is where the adopted checks become required. It reported for two days while analysing nothing, which #165 repaired and the section above describes. |
| `Analyze (csharp)` | here | Reports as `Code scanning (csharp)`, from `.github/workflows/code-scanning.yaml`. #96 replaced the call into the shared workflow, so the language set, the query suite and the check-run names are decided here; `docs/code-scanning.md` carries all three with the commands they were measured with. Three further languages are analysed that the call did not cover, each with its own check run. Not required by the ruleset; #105. |
| `DCO sign-off` | here | Runs on every pull request and is green. Not required by the ruleset; #105. |
| `Deterministic PR-hygiene checks` | here | `.github/workflows/pull-request-check.yml`, from #100. It judges the pull request itself on rules that need no judgement and says which rule refused. Not required by the ruleset; #105. |
| `Enforce greppable invariants` | here, in the suite | Adopted in #99 and built in #148, which closed as completed on 2026-08-09. The rules are listed in `Jellyfin.Plugin.WatchSync.Tests/Invariants/register.txt`, argued one by one in `docs/invariants.md`, and `InvariantGuardTests` refuses the register, the vocabulary and the document disagreeing in any direction. This row said `owed, #148` for as long as that issue has been closed, which is the same drift the passage at the top of this file records. It runs inside the suite rather than in a workflow of its own, so unlike the other board's it produces no check run of its own to require: it reports as part of `call / test` and the three `Suite` legs. That is a difference #105 has to know about rather than a smaller adoption, because a context that does not exist cannot be added to the required set. |
| `Reject Trojan Source Unicode` | here | Required today. This is the one row where the two gates already agree completely. |
| `Audit workflows (zizmor)` | here | Runs on every pull request. Required by the ruleset, read with the command at the top of this file. This row said `Not required; #105` while the ruleset already named it, which is the drift that passage records and the reason both were repaired together. #98 is where the permissions and pinning it reads were landed, and it is closed. |
| `dependency-review` | here | Runs on a pull request and fails closed on any severity. It only sees what a pull request changes, so an advisory published against an unchanged dependency is invisible to it. The scheduled half is now here too, in the row below; what is left of #97 is making this one required, which is #105, and picking up a scheduled failure out of band, which is #121. |

### Refused, with the reason

| check there | state here | the one line |
| --- | --- | --- |
| `opengrep` | refused | A second general static analyser beside code scanning, over a plugin of this size, buys overlap rather than coverage. What this board wants from that family is rules about its own invariants rather than a second generic ruleset, and that is #148. |
| `prettier` | refused | Formatting of the markup, the workflow files and the documents, bought with a runtime nothing else in this tree needs and which the suite cannot run. #99 argues it below. |
| `wiki-lint` | refused | There is no wiki to lint. `git ls-remote https://github.com/Flowfin/jellyfin-plugin-watch-sync.wiki.git` reports the repository is not found, and this board's documents are in `docs/` and in the tree, where the suite can read them. |

### Carried here and not there

| what | state here | the one line |
| --- | --- | --- |
| `call / test` | here, required | A required suite. The other board runs `dotnet.yml` and does not require it; this board required a test check from #7 onward, because the failure mode here is silent data loss and a suite that can be skipped is not a control. |
| `Refuse a trigger naming a branch that does not exist` | here | A workflow guard from #94: a trigger naming a branch this repository does not have never fires, and a check that never fires is indistinguishable from one that passed. |
| `Suite (ubuntu-latest)`, `Suite (macos-latest)`, `Suite (windows-latest)` | here | `.github/workflows/suite-three-operating-systems.yaml`, from #75. The other board runs its suite on one operating system. The concrete case this catches is the path separator: a guard that shells out to git takes repository-relative paths back and compares them against paths it built itself, and on Windows those come apart unless the code says so. The Linux and macOS legs refuse a run as uid 0. The Windows leg runs the suite twice, because the hosted image starts a job in an elevated account and no job can drop the privilege it was started with: once from that account, and once from a local account in `Users` and nothing else. A green mark from an elevated account cannot fail on the machine-wide path or the privileged port the rule refuses, so it answers whether the suite passes on Windows and not whether it passes there unprivileged, and the second run is what answers the second question. What the macOS leg shows is that the suite passes without running as root rather than that the account could not have elevated, because on that image it could. |
| `Dependency scan` | here | `.github/workflows/dependency-scan.yml`, from #97. Weekly and on demand, over the whole resolved graph including transitive packages, with the verdict made by `.github/check-vulnerable-packages.py` so the same file runs against a clone by hand. Deliberately not a merge check: it reddens on a day nobody pushed, which is the point, and a required check that does that blocks every unrelated pull request. Acceptances carry a reason and an expiry in `.github/dependency-acceptances.txt`. |
| `Coverage floors` | here | `.github/workflows/coverage.yaml`, from #87. Neither gate on the other board measures coverage at all. This one measures it once per server line, because the facts differ per target and a line reachable on one of them is unmeasured on the other, and it holds the resolver and the matcher to a higher floor than the rest. What it prints for those two is the list of lines and branches nothing executed, because that list is what somebody acts on and the percentage is what somebody argues with. The floors are declared in `Coverage/floors.txt` and the verdict is `.github/check-coverage.py`, so a clone gets the same answer by running the same two files. It reports on every pull request and is not in the required set; which checks are required is #105. |
| The headless rule and its guard | here | `Jellyfin.Plugin.WatchSync.Tests/headless-rule.md` and the guard beside it. Neither gate has a check of this shape; this one is in the suite. The class is not hypothetical, and it was met and repaired on the other board rather than avoided there. |
| The build manifest guards | here | `BuildTargetsTests` and `CompatibilityMatrixTests` hold the declared ABI, the target list and the compatibility matrix to what was actually built. A plugin whose declared ABI is not what it compiled against fails at load on somebody's server, and no generic check in either gate looks for it. |
| The two-server behaviour | owed, #88 and #104 | This plugin writes into another server's users' data. No amount of static analysis reaches that class, and the other board does not have it. The container harness is where it is checked and #104 decides where that runs. |
| `changelog.yaml`, `sync-labels.yaml` | kept, #176 | Two workflows this repository carries from the plugin template rather than by a decision of its own. `changelog.yaml` is answered on #176 and stays, skipped, until #116 decides what the release route reads its change list from, because removing it first would settle that question with a deletion. THE CONDITION THIS ROW WAITS ON IS MET AND THE ROW'S CONCLUSION IS NOT THIS CHANGE'S TO MOVE. #116 answered it: the release route reads the fragments under `changelog.d/`, assembled by `.github/assemble-release-notes.py` in the publish route's gate job, and `docs/changelog.md` carries the argument. What is left is whether this workflow is then removed, which is #176's question and is not decided here. It is skipped on every push, because it passes the name this repository carried before it moved and the shared job is guarded on that name, which is why `call / update_release_draft` appears in the mainline list above with the conclusion `skipped`; #165 says why that name is deliberately not repaired, and being skipped is the whole of what makes keeping it safe. `sync-labels.yaml` is answered on #176 as well, on the same line: it is kept, so template parity with the plugin template stays whole rather than being narrowed one workflow at a time. What it is kept for is that parity; what makes keeping it safe is not established here, and the row says so rather than the opposite. It writes the template's label set onto this board on the first of each month, `cron: '0 0 1 * *'` in the file, and no run of it is recorded (`gh run list --workflow sync-labels.yaml --limit 10 --json conclusion,event --jq 'length'` returns `0`). This board carries eight `area:` labels the template has no reason to know about (`gh label list --limit 60 --json name --jq '[.[].name | select(startswith("area: "))] | length'` returns `8`), and what the shared workflow does to a label its set does not name is unmeasured here, because the only reading that answers it is a live run against this board. The decision is that the next first-of-month run is the observation rather than that the behaviour is known, so what this row owes after that run is a reading of it. |
| `command-dispatch.yaml`, `command-rebase.yaml` | removed, #155 | The row above once named four. The slash command pair came from the same template and no command was ever raised through it, so what it produced was a runner on every comment and a check run on every commit. Removed rather than filtered. |

On the headless row, the class it refuses is one the other board met rather than
one imagined here:

    gh api repos/iderex/jellyfin-plugin-sso/issues/1227 --jq '"\(.number) \(.state) \(.title)"'
    1227 closed Test suite requires admin rights on Windows

It is closed there now. What this board took from it is the guard rather than the
repair, so the state cannot be reached in the first place.

### Beyond the gate, on that board's other workflows

| what | state here | the one line |
| --- | --- | --- |
| `fuzz` | here | `.github/workflows/fuzz.yaml`, from #102. The inbound envelope reader is the one surface a peer controls, so it is fuzzed, weekly and on demand and never on a pull request: a fuzz run reddens on an input nobody's change produced, and a required check that does that blocks every unrelated pull request until somebody triages a crasher. Not required by the ruleset; #105. It is not coverage guided, which the run prints on every execution and `docs/fuzz.md` argues, so a green job is the absence of a defect these mutations reached rather than the absence of one. The run that landed it found a crasher, which is #253 and is deliberately not repaired in the change that brought the harness. |
| `stryker-mutation` | here | `Mutation` runs weekly and on demand, over the matcher and over the conflict resolver once it exists, reported and never gating. The scope is in `stryker-config.json` and the components are declared in `Mutation/scope.txt`, so a component the run does not reach is a red suite rather than a score for half of what it claims. `docs/mutation.md` carries the triage. |
| `manifest-freshness` | owed, #120 | A published manifest that no longer lists the newest release is a silent failure. |
| `publish-failure-alert` | owed, #121 | A green publish that shipped nothing is the failure this catches, and it has already happened on that board. |
| `e2e-login` | owed, #88 | Its analogue here is the container harness, because the behaviour worth proving end to end is different. |
| `scorecard` | here | `Scorecard analysis` reports on this board's mainline. |
| `nightly-betas`, `publish-beta`, `publish-jf12-beta`, `publish-jf12-stable`, `publish`, `regenerate-manifest` | owed, #117 and #122 | The release route. #119 and #123 waited on the publication route in #1 and no longer do: that decision has been answered there, and it separates a stable channel from a pre-release one, so the two-channel shape those rows assume is the answer rather than an assumption. What is still open is the packaging, because a release run here produces one artifact for one of the two lines this repository declares, which is #117. |

## The two the table once left open

Two rows sat under a heading saying the question was real and was answered
elsewhere. #99 is where it was answered, and both rows above now say what the
answer was. The argument is here rather than only in the issue, because a table
row is one line and neither of these is settled by one line.

### The invariant lint is adopted

The other gate's version turns a repository's own rules into lint rules, one per
invariant, added as each is discovered. This repository already does it once and
is doing it a second time. `HeadlessGuardTests` refuses a test that would need a
display, elevation, a trust store, the network, a real wait or the machine clock,
and it is in the tree. The guard refusing a key derived from where or how a file
is stored is #25. Both read a vocabulary held as a data file, both scan what the
tree holds rather than a list of file names, both carry a register of departures
that fails closed in either direction, and both are proven on a near-miss one line
away from correct.

So the adoption is a continuation and not a new apparatus, which is the reason it
is cheap here and would not be on a repository starting from nothing. #148 lands
the invariants this plan names and does not yet refuse: no user name compared to
decide who a change belongs to, no wall clock read in the plugin's own sources
outside the injected one, and no log statement carrying an item title next to a
user.

It also answers the `opengrep` row above. What this board wants from that family
is rules about its own invariants rather than a second generic ruleset, and that
sentence was pointing at a decision. It now points at the issue that lands them.

### The formatter check is refused

What it would buy is consistent formatting of the three kinds of file no compiler
reads here: the configuration page's markup, the workflow files and the documents.
That is a real gap and this row does not pretend otherwise.

What it costs is a runtime this tree does not carry, for a check the suite cannot
run. Every other rule this repository enforces is a test: a contributor runs
`dotnet test` and gets the same verdict the gate gets, and the guards are built so
that the vocabulary, the departures and the proof all sit in the same place a
reader is already looking. A formatter check would be the one required rule with
none of that, reproducible only by installing a second toolchain, at a moment when
this table's last section records that there is no local gate command at all.

The failures a formatter would actually catch here are also mostly caught already
and by narrower things. A malformed workflow file fails the workflow. Invisible
and bidirectional characters are refused by the unicode guard, which is the one
formatting-shaped defect on this board with a security argument behind it. Workflow
permissions and pinning are audited by zizmor. A document falling out of step with
what it describes is refused by the guards that hold each table against the thing
it is a table of, which is a class no formatter reads at all. What is left is
whitespace and line breaks.

This is a deviation downward and it is not a claim that formatting does not
matter. If the markup grows to where a person cannot read a diff of it, the row to
revisit is this one, and the thing that would change the answer is a local gate
command that could run the check, which is #114.

The other gate has a command a contributor runs before pushing that runs the same
legs the gate runs, in the same order. This board has no such command.

That is a gap in this table rather than a decision. `dotnet test` runs the suite
and nothing else: the unicode guard, the workflow audit and the dependency review
are workflows, and a contributor has no way to run the gate's legs locally in
order. #114 is where the wider contributing note lands, and the command belongs
with it. Until it exists, this row says there is no local gate rather than naming
something weaker and calling it one.

## How this file is kept true

By a reading, at the review of the change that touches it. Nothing in the tree
compares this table against either ruleset, and nothing could without reaching
across repositories on every run. Both gates move, so re-run the commands at the
top rather than trusting the outputs pasted under them: they record the state on
the day this was written and they are dated by the commit that carries them.
