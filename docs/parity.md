# What this board's gate takes from the other one, and what it does not

The gate of `iderex/jellyfin-plugin-sso` is the target this repository's quality
milestone aims at. This file says, check by check, what is adopted, what is
refused, and one line of reasoning for every difference in either direction. An
unexplained gap is a defect. An explained one is a decision.

Nothing here is a plan for what the other board should do. It reads that board and
decides about this one.

## The two gates, measured

The other board's required set:

    gh api repos/iderex/jellyfin-plugin-sso/rulesets --jq '.[].id' \
      | xargs -I{} gh api repos/iderex/jellyfin-plugin-sso/rulesets/{} \
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

    gh api repos/iderex/jellyfin-plugin-watch-sync/rulesets/20464507 \
      --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context] | .[]'
    call / build
    call / test
    Reject Trojan Source Unicode

Three against thirteen. The gap is the subject of this file and of the milestone
it belongs to, and most of it is work that has not been done rather than work that
was refused.

What actually reports on this board's mainline today, which is a different set
again, because a check can run without being required:

    gh api repos/iderex/jellyfin-plugin-watch-sync/commits/6d8d0afbff713ca7c2edd742c6f557fa0ad79e2e/check-runs \
      --jq '[.check_runs[].name] | sort | .[]'
    Audit workflows (zizmor)
    Refuse a trigger naming a branch that does not exist
    Reject Trojan Source Unicode
    Scorecard analysis
    call / Analyze (csharp)
    call / build
    call / test
    call / update_release_draft

`DCO sign-off` and `dependency-review` are absent from that list because both run
on a pull request only. Both are green on the pull requests this file was written
alongside.

## The rows

`state` is one of `here`, meaning it runs on this board today; `owed`, meaning it
is adopted and an issue lands it; or `refused`, meaning this board does not take
it and the reason is in the row.

### Adopted from the other gate

| check there | state here | what it is here, or what lands it |
| --- | --- | --- |
| `build` | here | `call / build`, a call into the shared plugin workflow. It is required. #90 replaces the call with this repository's own workflow so warnings become errors, which the shared one does not do. |
| `ABI floor build` | owed, #91 | This board declares two lines rather than one, so the floor build is doubled. Nothing runs it yet. |
| `Package (JPRM) / Build package` | owed, #101 | Packaging as a merge check rather than a release step, one artifact per line. #117 is the release half. |
| `Package (JPRM) / Generate SBOM` | owed, #101 | The component inventory in the same run as the package. #118 is the release half. |
| `CodeQL` | here | Reports as `CodeQL` on a pull request. Not required by the ruleset; #105 is where the adopted checks become required, and #96 is where the reported name is made stable enough to require. |
| `Analyze (csharp)` | here | Reports as `call / Analyze (csharp)`. Same workflow, same two issues. |
| `DCO sign-off` | here | Runs on every pull request and is green. Not required by the ruleset; #105. |
| `Deterministic PR-hygiene checks` | owed, #100 | Nothing of this shape runs here yet. |
| `Enforce greppable invariants` | owed, #148 | Adopted in #99. The shape is already in the suite twice; #148 is where the remaining invariants become rules. |
| `Reject Trojan Source Unicode` | here | Required today. This is the one row where the two gates already agree completely. |
| `Audit workflows (zizmor)` | here | Runs on every pull request. Not required; #105. #98 is the permissions and pinning work it reads. |
| `dependency-review` | here | Runs on a pull request and fails closed on any severity. It only sees what a pull request changes, so an advisory published against an unchanged dependency is invisible to it; #97 adds the scheduled half. |

### Refused, with the reason

| check there | state here | the one line |
| --- | --- | --- |
| `opengrep` | refused | A second general static analyser beside code scanning, over a plugin of this size, buys overlap rather than coverage. What this board wants from that family is rules about its own invariants rather than a second generic ruleset, and that is #148. |
| `prettier` | refused | Formatting of the markup, the workflow files and the documents, bought with a runtime nothing else in this tree needs and which the suite cannot run. #99 argues it below. |
| `wiki-lint` | refused | There is no wiki to lint. `git ls-remote https://github.com/iderex/jellyfin-plugin-watch-sync.wiki.git` reports the repository is not found, and this board's documents are in `docs/` and in the tree, where the suite can read them. |

### Carried here and not there

| what | state here | the one line |
| --- | --- | --- |
| `call / test` | here, required | A required suite. The other board runs `dotnet.yml` and does not require it; this board required a test check from #7 onward, because the failure mode here is silent data loss and a suite that can be skipped is not a control. |
| `Refuse a trigger naming a branch that does not exist` | here | A workflow guard from #94: a trigger naming a branch this repository does not have never fires, and a check that never fires is indistinguishable from one that passed. |
| The headless rule and its guard | here | `Jellyfin.Plugin.WatchSync.Tests/headless-rule.md` and the guard beside it. Neither gate has a check of this shape; this one is in the suite. The class is not hypothetical, and it was met and repaired on the other board rather than avoided there. |
| The build manifest guards | here | `BuildTargetsTests` and `CompatibilityMatrixTests` hold the declared ABI, the target list and the compatibility matrix to what was actually built. A plugin whose declared ABI is not what it compiled against fails at load on somebody's server, and no generic check in either gate looks for it. |
| The two-server behaviour | owed, #88 and #104 | This plugin writes into another server's users' data. No amount of static analysis reaches that class, and the other board does not have it. The container harness is where it is checked and #104 decides where that runs. |
| `changelog.yaml`, `command-dispatch.yaml`, `command-rebase.yaml`, `sync-labels.yaml` | here, undecided | Four workflows this repository carries from the plugin template rather than by a decision of its own. They are named here so the difference is visible; whether each is kept is not settled by this file. |

On the headless row, the class it refuses is one the other board met rather than
one imagined here:

    gh api repos/iderex/jellyfin-plugin-sso/issues/1227 --jq '"\(.number) \(.state) \(.title)"'
    1227 closed Test suite requires admin rights on Windows

It is closed there now. What this board took from it is the guard rather than the
repair, so the state cannot be reached in the first place.

### Beyond the gate, on that board's other workflows

| what | state here | the one line |
| --- | --- | --- |
| `fuzz` | owed, #102 | The inbound envelope reader is the one surface a peer controls, so it is fuzzed, outside the merge gate. |
| `stryker-mutation` | owed, #103 | Over the matcher and the conflict resolver, reported and never gating. |
| `manifest-freshness` | owed, #120 | A published manifest that no longer lists the newest release is a silent failure. |
| `publish-failure-alert` | owed, #121 | A green publish that shipped nothing is the failure this catches, and it has already happened on that board. |
| `e2e-login` | owed, #88 | Its analogue here is the container harness, because the behaviour worth proving end to end is different. |
| `scorecard` | here | `Scorecard analysis` reports on this board's mainline. |
| `nightly-betas`, `publish-beta`, `publish-jf12-beta`, `publish-jf12-stable`, `publish`, `regenerate-manifest` | owed, #117 and #122 | The release route. Two of the questions it depends on are open decisions and carry `blocked-on-decision`, which are #119 and #123. |

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
