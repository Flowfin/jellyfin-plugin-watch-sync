# Code scanning on this board

What is analysed, with which queries, when, and what the check runs are called.
Every one of those was decided inside a shared workflow in another organisation's
repository until `.github/workflows/code-scanning.yaml` replaced the call, and
this file is where each answer is written down and argued.

## Why the call was replaced rather than corrected

The call passed one input, the name of this repository, and took everything else
from the file it called. That is a workable default for a plugin whose whole
surface is one C# project. It is the wrong shape here for three reasons, and each
one is a condition of #96.

The language set was fixed at one language in the callee:

    gh api repos/jellyfin/jellyfin-meta-plugins/contents/.github/workflows/scan-codeql.yaml?ref=eb99033a7ff644881b014bc0b4169916c854a68b \
      --jq '.content' | base64 -d | grep -nE 'language:|queries:'
    22:        language: [ 'csharp' ]
    37:          queries: +security-and-quality

This repository holds four languages that CodeQL analyses, and three of them were
outside what the call covered. Two of those three are where the security-relevant
code actually is: the workflow files that hold the release secrets, and the Python
that decides whether a pull request may merge.

The check-run name was chosen there as well, and it moved once without any change
here: `call / Analyze (csharp)` became `call / Analyze` upstream. A required
context matches a check-run name literally, so a name decided in another
repository is a required check that can be removed by somebody who has never seen
this board. #105 is where these names become required, and this file is what it
reads them from.

The whole job was also guarded on this repository's own name, so moving the
repository silently stopped the analysis for twenty one consecutive runs while
every one of them reported success. #165 repaired the name. Nothing about that
repair stops it happening again, because the guard is still in the callee. There
is no such guard here.

## The languages

One entry per language the tree contains, measured rather than assumed:

    git ls-files -- '*.cs' | wc -l
    36

    git ls-files -- '*.py'
    .github/assemble-pull-request.py
    .github/check-pull-request.py
    .github/check-vulnerable-packages.py
    .github/check-workflow-branches.py

    git ls-files -- '.github/workflows/' | wc -l
    15

    git grep -l -- '<script' -- '*.html'
    Jellyfin.Plugin.WatchSync/Configuration/configPage.html

So `csharp`, `python`, `actions` and `javascript-typescript`. Nothing else in the
tree is a language CodeQL has an extractor for:

    git ls-files -- '*.js' '*.ts' '*.go' '*.java' '*.rb' '*.rs' '*.swift' '*.kt' '*.c' '*.cpp' '*.h' | wc -l
    0

The last of the four is one inline script in the configuration page and is the
thinnest of them. It is included rather than left out because that page is the
plugin's only markup, it runs in an administrator's browser against the server's
own API client, and the day it grows a setting is the day it starts handling
values that came from somewhere else.

`csharp` is the only one that is built. The other three are read from source,
which is what `build-mode: none` says in the matrix. The C# build installs both
SDK lines, because the project multi-targets one framework per supported server
line and an autobuild handed one SDK builds one of the two.

## The query suite

`+security-and-quality`, which is the default suite plus the security-and-quality
pack. It is the same suite the call passed, on line 37 of the output above, and it
is a superset of the `default` suite that GitHub's own default setup runs. So this
change adds languages and takes nothing away, and the claim that replacing the
default is not a weakening is made against a command rather than against memory.

## When it runs

Every push to `master`, every pull request against `master`, weekly on a schedule,
and on demand.

There is no path filter, and the previous file had one on Markdown. What a filter
buys is an analysis skipped on a change that carries nothing to analyse. What it
costs is the check run being absent rather than green on exactly those changes,
and an absent context is what a required check waits on forever. That is the
failure #134 was opened about, on the two workflows that are required today.
These names are not required yet; the filter is removed now so that requiring them
in #105 is a ruleset edit and nothing else.

There is no concurrency group either, and the reason is in the file: cancelling a
superseded run cancels a mainline analysis, and the mainline analyses are the only
thing the code-scanning tab reports the default branch's state from.

## The check-run names

    Code scanning (csharp)
    Code scanning (python)
    Code scanning (actions)
    Code scanning (javascript-typescript)

These are the strings #105 requires, and they are written here so that issue reads
them from the tree rather than from a run list.

The name is the job name with the matrix value in brackets, which is how the forge
builds a check-run name for a matrix job. Two properties follow from that and both
are the reason for the shape rather than an accident of it. Adding a language adds
a check run and renames none, so the set can grow after it is required without
removing a required context. And no name carries a `call /` prefix, which the
previous names did while describing a job in a repository this one does not own.

`CodeQL` is a fifth check run and is not produced by this workflow. The
code-scanning service creates it on a pull request to report findings against the
diff. It appears in the other board's required set and is listed in
`docs/parity.md`; nothing in this file decides it.

It does not report `success` on the pull request that lands this change, and the
reason is worth carrying rather than discovering later:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/commits/6b57225a6445b7890323ecb2b9cb55a1dba6329b/check-runs?per_page=100 \
      --jq '.check_runs[]|select(.name=="CodeQL")|{conclusion, output: .output.title}'
    {"conclusion":"neutral","output":"1 configuration not found"}

The service compares a pull request against the analysis configurations it has
seen on the base branch, and the configuration it is looking for is the one the
call produced. That one stops existing when this change lands, and the four here
become the baseline on the first mainline run afterwards. So the neutral verdict
is the transition rather than a property of the new workflow.

It is also a fact #105 needs before it requires this name. A required context
counts a neutral verdict as not-success, so requiring `CodeQL` while a
configuration is being replaced blocks every pull request until the mainline has
run once under the new one. That is an ordering constraint on the ruleset edit and
not on this workflow.

## The workflow audit's own findings

`Audit workflows (zizmor)` uploads into the same code-scanning tab, and its upload
step was conditioned on a push to `main`. This repository's default branch is
`master`, so the first clause was false on every push and the step ran only for
pull requests:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/analyses?per_page=100" \
      --jq '[.[]|select(.tool.name=="zizmor")|.ref]|unique|map(select(startswith("refs/heads/")))|length'
    0

Nothing was unenforced by that. The step that fails the build on an actionable
finding runs unconditionally and is a different step. What was missing is the
default branch's state, and an alert raised against a pull request ref goes away
with the ref, so there was nothing for a triage to read. The condition now names
the branch this repository has.

What the tab holds for that tool today is nothing to triage:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=zizmor&per_page=100" \
      --jq 'length'
    0

That is a reading of a tool that had never uploaded from the mainline, so it says
the pull request refs carried no open finding rather than that the mainline is
clean. The first mainline upload after this change is what makes the second
statement sayable.

## The triage

The fourth condition of #96 is that findings are triaged rather than accumulated,
and that the triage is a reading recorded in the repository. This is that reading.
It was taken on 2026-08-09, against the analyses the call produced before it was
replaced, which are C# only.

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, severity:.[0].rule.severity, security:.[0].rule.security_severity_level, count:length})|sort_by(-.count)|.[]|"\(.count)\t\(.severity)\t\(.security // "none")\t\(.rule)"'
    30      note    none    cs/path-combine
    4       note    none    cs/linq/missed-select
    2       note    none    cs/linq/missed-where
    1       note    none    cs/inefficient-containskey

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq '[.[]|.most_recent_instance.location.path|split("/")[0]]|group_by(.)|map({(.[0]):length})|add'
    {"Jellyfin.Plugin.WatchSync":2,"Jellyfin.Plugin.WatchSync.Tests":35}

Thirty seven open, all at `note`, none carrying a security severity, and
thirty five of them in the suite rather than in the plugin.

`cs/path-combine`, thirty of the thirty seven, is one shape repeated. Each is a
repository-relative path composed inside a guard or a document test from a value
read out of the tracked tree, and the query is about a second argument that could
be rooted and would then discard the first. The inputs here are paths git prints,
so none can be rooted while the tree they came from is this one. Kept rather than
repaired, and not dismissed: the query is right about the general case, and the
day one of those guards reads a path from somewhere other than git is the day the
alert should be a defect again.

`cs/linq/missed-select`, `cs/linq/missed-where` and `cs/inefficient-containskey`
are seven more, and six of them are in the suite. The two in the plugin are both
`cs/linq/missed-where`:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq '.[]|select(.most_recent_instance.location.path|startswith("Jellyfin.Plugin.WatchSync/"))|"\(.rule.id)\t\(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)"'
    cs/linq/missed-where    Jellyfin.Plugin.WatchSync/Matching/ProviderIdentifier.cs:166
    cs/linq/missed-where    Jellyfin.Plugin.WatchSync/Matching/PreferredIdentifier.cs:102

Both are a loop that returns on the first element meeting a condition, which the
query offers to write as a filter. That offer is refused and the sites are still
rewritten, in #182, because a filter is not what either loop is. `IsDigits` walks
a candidate identifier and refuses on the first character that is not a digit,
which is a quantifier over the whole string and is what `All` says in one line.
`TryRead` reads the first value stored under a key equal to one it holds ignoring
case, which is a filter, a projection and a first, and the loop it replaced
assigned an out parameter from inside a branch to say so. The other five are the
same kind of reading: four loops mapping their element on the first line of the
body, and one register asked `ContainsKey` and then indexed with the same key.

The bar every one of them had to pass is that the site reads better afterwards.
A rewrite bought with a reader's time is refused here whatever it does to the
count, which is why `cs/path-combine` above is kept and these are not. The suite
is the evidence that nothing moved with them: it stays green with no test edited
to make it so.

Nothing was dismissed and no alert state was changed. This is a reading and it
records what the analyses hold; it is not a claim that the count will be the same
tomorrow. The count moves with every change, which is why the commands are here
and the numbers are dated.

Three of the four languages have never been analysed on this board, so the first
run of this workflow produces findings this reading says nothing about. Reading
them is the next triage rather than a defect in this one.
