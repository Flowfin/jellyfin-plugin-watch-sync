# Code scanning on this board

What is analysed, with which queries, when, and what the check runs are called.
Every one of those was decided inside a shared workflow in another organisation's
repository until `.github/workflows/code-scanning.yaml` replaced the call, and
this file is where each answer is written down and argued.

## The disposition register

One entry per rule the security page has carried, each naming what was decided
about the rule, where, and the argument for it. #271 read the open alerts as a
set and decided each rule on 2026-09-05, and this register is where those
decisions live in the tree: a reader asking why a class of finding stands finds
an argument here rather than a click on the security page, and the next reading
of that page does not re-derive what this one already decided. The readings
below this section are older than the register and are left as they were
written, dated; where one of them and an entry here disagree, the entry is the
decision and the reading is what was true on its day.

An entry is one heading carrying the rule id, a `Disposition:` line, and, where
the disposition is a decline, a `Quoted by a dismissal:` line holding the one
sentence a dismissal of that rule on the security page carries as its comment.
The page and this file are then two copies of one sentence rather than two
paraphrases, so a reader comparing them is comparing the page against the
argument. Where an entry names a rule declined as a set, the argument is written
once here and the dismissals point at it; nothing on the page argues on its own.

### What the suite reads, and what it does not

The suite is headless and reaches no network, so nothing in it reads the
security page. What it reads is
`Jellyfin.Plugin.WatchSync.Tests/CodeScanning/page-rules.txt`, which is the
page's rule set as one command returned it, with that command, the day and the
mainline commit in the file's header. `CodeScanningRegisterTests` holds this
register to that file in both directions: a rule the file carries with no entry
here is refused, naming the rule, and an entry here naming a rule the file does
not carry is refused, naming the entry. A decline with nothing for a dismissal
to quote is refused as well.

What that cannot see is the page moving after the file was written. A rule
raised tomorrow is unread by anything in this tree until somebody re-runs the
command in that header and commits its output, and the suite then reports the
entry the register lacks. The page itself is read by hand and by nothing else,
which is the same gap `### What ends a dismissal` records further down, and it
is stated here because this is where a reader of the register meets it.

Every alert state is in that file rather than the open ones alone. A dismissed
alert is a decision this register has to carry, a fixed one is a decision it
has to carry as fixed, and a register held to the open alerts alone would owe
nothing for a rule on the day its last alert closed, which is the day its entry
is most worth having.

### `cs/path-combine`

Disposition: declined as a set. Decided on #271, 2026-09-05; applied on the
security page by #371 on the same day.

Quoted by a dismissal: Declined as a set (#271, 2026-09-05; register #368, docs/code-scanning.md): a validated path segment joined with Path.Combine, and a table walked for the row matching a key, are idioms this code uses on purpose; the rule misreads them. No finding of this rule here is a defect.

The rule is right wherever a later argument of `Path.Combine` can be absolute,
because an absolute later argument silently drops the earlier ones. No site the
rule reaches in this tree can take one. In the suite the later argument is a
literal written at the call, a constant of the test project, a path git printed
against the same root, a fixture name every call site passes as a literal or as
a row of a tracked file, or a name composed from a format whose first characters
are literal. In the plugin it is a document name that
`RefuseANameThisStoreMayNotCompose` in `Storage/DocumentStore.cs` has already
refused unless it is lower case letters, digits and hyphens, which is a closed
set that carries no separator, no drive letter and no two dots. The sections
below, dated as readings, carry each kind site by site.

What ends the decline is a site whose later argument arrives from an
environment variable, a command line, a configuration file or a peer. The
second plugin site, the in-flight path, rests on the guard having run in the
one method that reaches it rather than on a line of its own. The first note on
#271 asked for a guard in that method as well, and #377 is that hardening; it
is a change to the store and not a change to this decision.

The forty six sites dismissed before the decision carry a sentence per kind
rather than the sentence above, and the readings below record which site is
which. They stand: a comment on a dismissed alert cannot be edited, and the
argument each one points at is this file.

### `cs/linq/missed-where`

Disposition: declined as a set. Decided on #271, 2026-09-05; applied on the
security page by #371 on the same day. Alerts of this rule closed before the
decision are in the fixed state and were rewritten under #182 and #222, where
the loop body was nothing but the guarded statement.

Quoted by a dismissal: Declined as a set (#271, 2026-09-05; register #368, docs/code-scanning.md): a validated path segment joined with Path.Combine, and a table walked for the row matching a key, are idioms this code uses on purpose; the rule misreads them. No finding of this rule here is a defect.

The rule offers to move a loop's condition into a filter. `### The five loops
whose body is the guard` below is the split this tree takes: the rewrite is
taken where the body is nothing but the guarded statement, so the loop becomes
one line over a filtered sequence, and it is refused where the fix would
relocate a statement without removing one, because the site then reads worse
and nothing behavioural is at stake. A loop that walks a table for the row
matching a key and then does more with it is the second case and is what the
open alerts were, so the set is declined rather than rewritten. The split is
not reopened by the decline: a new loop whose body is only the guarded
statement is still written as the filter.

### `cs/linq/missed-select`

Disposition: declined as a set. Decided on #271, 2026-09-05; applied on the
security page by #371 on the same day. Alerts of this rule closed before the
decision are in the fixed state, rewritten under #182 and the reading dated
2026-08-10 below, and one is dismissed as `won't fix` under #222.

Quoted by a dismissal: Declined as a set (#271, 2026-09-05; register #368, docs/code-scanning.md): a validated path segment joined with Path.Combine, and a table walked for the row matching a key, are idioms this code uses on purpose; the rule misreads them. No finding of this rule here is a defect.

The same split as the rule above, on projection rather than on filtering: a
loop mapping its element on the first line of a body that then does more is
the idiom, and the rule's projection would move that line out of the loop
without removing it. Where the body was only the mapping the rewrite was taken,
which is what the fixed alerts are.

### `cs/catch-of-all-exceptions`

Disposition: sorted by site under #370, which is open as this entry is written
and rewrites it when it lands: fixed at the two sites in production code, in
`Apply/CappedApply.cs` and `Apply/ItemByItemApply.cs`, and declined at the four
sites in the fuzz harness, `EnvelopeFuzz.cs` in the suite.

Quoted by a dismissal: Declined at this site (#271, 2026-09-05; #370; register docs/code-scanning.md): this catch is in a fuzz harness whose subject is what the reader does with bytes nobody wrote, and an exception the reader did not declare is the finding the sweep exists to record rather than an error to narrow away.

The four harness sites catch everything because that is what the harness is
for: narrowing a clause to the types the reader is known to throw would make
the sweep blind to exactly the case it exists to find, and the finding it
records carries the type name and the message. The two apply sites are the
pair #370 argues rather than sorts. An apply that catches everything is how one
bad item stops taking the whole run down, which is #54's own rule, and it is
also how a defect in this plugin becomes a per-item refusal; what the catch is
for, what it hides and why the answer is the one taken are written there and
into this entry when it lands.

### `cs/local-not-disposed`

Disposition: fixed, under #370, which is open as this entry is written and
rewrites it when it lands. Four sites, all `ManualResetEventSlim` in
`DocumentStoreTests.cs`.

The query is right. That type allocates a kernel event lazily and does so
exactly when a waiter blocks past its spin count, which is what both cases make
it do, so the handle is real rather than theoretical and nothing releases it.
The repair is not a blind `using` and it is not a disposal at the end of the
method: both events are handed to a stream the store opens on another thread,
so they may not be disposed while that thread can still touch them, and both
cases already wait for that thread. The disposal goes after that wait.

### `cs/empty-catch-block`

Disposition: fixed, under #370, which is open as this entry is written and
rewrites it when it lands. Two sites, both in `Discard` in
`Storage/DocumentStore.cs`.

The swallow is the behaviour rather than an oversight. `Discard` runs on the
failing path of a write that has already decided to answer with a refusal, and
a delete that also fails may not turn that refusal into a different one: a
leftover in-flight file is written over by the next attempt, which the
in-flight naming already fixes, and the failure nobody hears of is answered
one level up by what the write returns. The repair is that sentence written
inside each block, because the next reader meets an empty block before they
meet this entry. The query itself excludes a block that carries a comment, so
the fix is the reader's rather than the tool's, and whether the alert closes on
it is a reading to take after the change rather than a claim to make here.

### `cs/dispose-not-called-on-throw`

Disposition: declined at both sites, under #370, which is open as this entry
is written and applies it on the security page when it lands. Two sites, both
in `OneExchangeAtATimeTests.cs` in the suite.

Quoted by a dismissal: Declined at this site (#271, 2026-09-05; #370; register docs/code-scanning.md): the moment this admission is released is what the test asserts, and a using block would move that moment to the end of the method and leave the test asserting nothing.

At the first site the second `Admit` has to happen while the first place is
still held, which is the fact the test asserts, and a `using` over the first
admission would move its release to the end of the method. At the second the
second release is the subject: the test exists to prove that releasing twice
releases once, and a `using` would make the compiler write that second release
rather than the test. What bounds the decline is what would leak: an
`ExchangeAdmission` holds no handle, its `Dispose` gives a place back to the
`OneExchangeAtATime` it came from, and at both sites that object is a local of
the same method, so a throw between the two calls loses a place in an exclusion
nothing outside the failing test can reach. In the plugin the same shape is
the failure that type's own comment is written against, which is why this is a
decline of two sites and not of the query.

### `cs/equality-on-floats`

Disposition: fixed, under #370, which is open as this entry is written and
rewrites it when it lands. One site, in `ConfigurationDocumentTests.cs` in the
suite.

The query is right about the class. The comparison holds today because every
value on both sides is a whole number of seconds small enough to be exact as a
double, which is a property of the fixtures rather than of the code. The
repair compares ticks, which are whole numbers on both sides, and removes the
class rather than the alert.

### `cs/inefficient-containskey`

Disposition: fixed. Two sites under #369, landed as #372 on `9f2678d`; one
earlier site in the suite rewritten under #182.

A membership test followed by an indexer asks a table twice for one answer,
and `TryGetPropertyValue` asks once and keeps the distinction the two sites
were written for: a member that is absent and a member carrying a null value
are different documents, and the indexer answers null for both.

### `cs/useless-upcast`

Disposition: fixed. One site under #369, landed as #373 on `336aed6`; one
earlier site closed on 2026-09-03.

The parameter was already the type the cast named and the method had one
overload, so the cast decided nothing.

### `cs/useless-tostring-call`

Disposition: declined, `won't fix`, under #222 on 2026-08-18. One site, in
`Peer/PeerText.cs`.

Quoted by a dismissal: No Append overload takes a rune, so removing this call binds to Append(object), which boxes the rune and calls the same ToString inside it. The result is identical and the change is not an improvement. docs/code-scanning.md carries the overload set and what ends this.

`### Appending a rune, alert 115` below carries the overload set the argument
rests on and what ends it: an overload that takes a rune, or the helper being
rewritten to encode into a span.

### `MaintainedID`

Disposition: out of #271's scope by the decision there, which names it as not a
code rule, and open. It ends by itself: the finding is the repository's age,
which no commit changes, and the first Scorecard run after 2026-11-03 no longer
raises it.

    gh api repos/Flowfin/jellyfin-plugin-watch-sync --jq '.created_at'
    2026-08-05T15:09:11Z

Ninety days after that date is 2026-11-03. Nothing in this tree fixes it and
nothing here dismisses it; an entry is owed because the page carries it.

### `BranchProtectionID`

Disposition: declined, `won't fix`, dismissed on the security page before this
register existed.

Quoted by a dismissal: One maintainer, so approvers, codeowners, stale-dismissal and last-push-approval cannot be met; a rule nobody can satisfy gets switched off. Up-to-date branches is covered more strongly: the watcher builds the merge locally against the current base and judges that.

### `CIIBestPracticesID`

Disposition: declined, `won't fix`, dismissed on the security page before this
register existed.

Quoted by a dismissal: This check wants an OpenSSF Best Practices badge, whose criteria include a second reviewer and a vulnerability-response process with a named team. Neither exists for a one-maintainer project. The reasoning is in iderex/operations#1348.

### `CodeReviewID`

Disposition: declined, `won't fix`, dismissed on the security page before this
register existed.

Quoted by a dismissal: This check wants a change approved by somebody other than its author. There is one maintainer here, and iderex/operations#30 records that the rule is not satisfied rather than pretending it is. Unsatisfiable by construction; the reasoning is in iderex/operations#1348.

### `FuzzingID`

Disposition: declined, `won't fix`, dismissed on the security page before this
register existed.

Quoted by a dismissal: This check wants a fuzzing harness registered with OSS-Fuzz or equivalent. There is no untrusted input surface here that a fuzzer would reach, and onboarding needs maintainers who can be paged. The reasoning is in iderex/operations#1348.

The tree carries a fuzz harness of its own since that dismissal, over the
envelope reader, and `docs/fuzz.md` is where it is argued; what the check wants
is a registration with an outside service, which is a different thing and is
still not taken.

### `PinnedDependenciesID`

Disposition: fixed. Every reference in the workflows is pinned to a commit,
which `CONTRIBUTING.md` states under `## Changing a workflow` and the workflow
audit refuses a departure from.

### `SecurityPolicyID`

Disposition: fixed. `SECURITY.md` is in the tree.

### `TokenPermissionsID`

Disposition: fixed. Every workflow declares `permissions: {}` at the top and
grants a scope on the job that needs it, which is the second property
`CONTRIBUTING.md` states under `## Changing a workflow`.

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

One entry per language the tree contains, derived rather than assumed. What each
command below answers is whether a language is present at all, and none of those
four answers has moved since this section was written. Their outputs move every
time the tree grows a file, so the outputs are not written here and the commands
are handed over instead. Nothing in this repository re-extracts them:

    git ls-files -- '*.cs' | wc -l
    git ls-files -- '*.py'
    git ls-files -- '.github/workflows/' | wc -l
    git grep -l -- '<script' -- '*.html'

Three of the four were pasted with their outputs until #262, and all three had
gone stale by growth alone: more C# files, more workflows, and two Python files
added beside the four that were listed. Not one of them changed which languages
the tree holds, which is the whole reason the paste was not worth its drift.

So `csharp`, `python`, `actions` and `javascript-typescript`. Nothing else in the
tree is a language CodeQL has an extractor for:

    git ls-files -- '*.js' '*.ts' '*.go' '*.java' '*.rb' '*.rs' '*.swift' '*.kt' '*.c' '*.cpp' '*.h' | wc -l
    0

That last output is written down where the four above are not, because it is the
claim rather than a measurement beside one. A number other than zero there is a
language nobody added to the matrix, which is the finding this section exists to
make findable, and a reader who cannot compare against what it said last is left
running the command with nothing to compare it to.

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
repaired: the query is right about the general case, and the day one of those
guards reads a path from somewhere other than git is the day the alert should be
a defect again.

This paragraph also said they were not dismissed. Thirty of them were, hours
after the reading was taken, and the section on that class at the end of this
file is where the alert page's state is written.

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

Nothing had been dismissed and no alert state had been changed at the moment this
reading was taken. It stopped being true the same evening, for `cs/path-combine`
and for nothing else, and the section at the end of this file carries that. This
is a reading and it records what the analyses held; it is not a claim that the
count will be the same tomorrow. The count moves with every change, which is why
the commands are here and the numbers are dated.

Three of the four languages have never been analysed on this board, so the first
run of this workflow produces findings this reading says nothing about. Reading
them is the next triage rather than a defect in this one.

## A fifth loop of the same shape, raised after that reading

The reading above is dated 2026-08-09 and the analyses have run since. A fifth
`cs/linq/missed-select` was raised on 2026-08-10 against the table reader in
`LoggingDocumentTests`, which arrived with `docs/logging.md` after the four were
listed:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq '.[]|select(.rule.id=="cs/linq/missed-select")|"\(.number)\t\(.created_at)\t\(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)"'
    78      2026-08-10T08:01:59Z    Jellyfin.Plugin.WatchSync.Tests/LoggingDocumentTests.cs:231
    74      2026-08-09T07:04:07Z    Jellyfin.Plugin.WatchSync.Tests/InvariantGuardTests.cs:500
    67      2026-08-09T04:34:50Z    Jellyfin.Plugin.WatchSync.Tests/MovieMatchKeyTests.cs:156
    59      2026-08-06T23:11:44Z    Jellyfin.Plugin.WatchSync.Tests/StorageIdentityGuardTests.cs:375
    44      2026-08-06T06:24:24Z    Jellyfin.Plugin.WatchSync.Tests/HeadlessGuardTests.cs:345

That list was taken on 2026-08-10, before the rewrite landed. It is the order the
service returns rather than the order a reader would sort them into, and it is
written that way because a re-sorted paste and a page that has moved underneath
look identical to whoever runs the command next. A later run returns fewer rows
as each site closes, and an empty answer is this block having done its work.

It is the same helper shape as the other four, a loop trimming and skipping its
element on the first lines of the body, so it is rewritten with them rather than
left for a later pass. That is five sites of that rule and not four, and the
sentence above counting four is the reading of 2026-08-09 rather than a claim
about today.

Nothing else in that reading is re-measured here. The `cs/path-combine` count it
quotes has moved and this change does not say by how much or why; that class is
decided on its own and the count above is not to be read as current.

## What the alert page holds for `cs/path-combine`

Read on 2026-08-11. Both readings above describe that class as open and
undismissed. It is neither of those things, and this section is what the page
holds rather than a second argument about the class.

Thirty are dismissed as a false positive. Five of the same shape, raised after
that happened, are open:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length, reasons:([.[].dismissed_reason]|unique)})'
    [{"count":30,"reasons":["false positive"],"rule":"cs/path-combine"}]

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length})|sort_by(-.count)|.[]|"\(.count)\t\(.rule)"'
    5       cs/path-combine

Thirty five sites of one rule, and nothing else under CodeQL in either state. The
eight `cs/linq` and `cs/inefficient-containskey` alerts the readings above list
are closed by the rewrites those readings describe rather than by a dismissal,
which is the difference this section exists to keep visible:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=fixed&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length})|sort_by(-.count)|.[]|"\(.count)\t\(.rule)"'
    5       cs/linq/missed-select
    2       cs/linq/missed-where
    1       cs/inefficient-containskey

All thirty five are in the test project and none is in the plugin, which is the
same split the reading above found for this class:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq '[.[]|.most_recent_instance.location.path|split("/")[0]]|group_by(.)|map({(.[0]):length})|add'
    {"Jellyfin.Plugin.WatchSync.Tests":30}

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq '[.[]|.most_recent_instance.location.path|split("/")[0]]|group_by(.)|map({(.[0]):length})|add'
    {"Jellyfin.Plugin.WatchSync.Tests":5}

### Why the query is right and none of these sites is a defect

What the rule says, read from the service rather than from memory:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts/45 --jq .rule.full_description
    'Path.Combine' may silently drop its earlier arguments if its later arguments are absolute paths.

That is a real defect anywhere a later argument can be absolute. It cannot be at
these sites, and the reason is what the later argument is at each one. The lines
under the alerts read with:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq '.[]|"\(.most_recent_instance.location.path)\t\(.most_recent_instance.location.start_line)"' \
      | sort | while IFS="$(printf '\t')" read -r path line; do printf '%s:%s ' "$path" "$line"; sed -n "${line}p" "$path"; done

Reading all of them gives four kinds and no fifth. A literal written at the call,
as in `Path.Combine(directory.FullName, ".git")`. A `const string` of the test
project, as in `Path.Combine(root, TestProject)`. A repository-relative path that
git printed or that `Path.GetRelativePath` produced against the same root, which
is the source scan in `HeadlessGuardTests`. And a fixture name reaching a helper
as a parameter, in `ReleaseRoute.Fixture` and in the headless fixture reader,
where every call site passes a literal. None of the four reaches an environment
variable, a command line, a configuration file or a peer, so none can be rooted
while the tree it came from is this one.

The fourth is the one worth naming separately. Its `Path.Combine` line does not
change when a caller starts passing something else, so it is the site where this
argument could stop being true without the alert being raised again.

The count of four is corrected in the next section. The paragraph above is left
as the reading of 2026-08-11 rather than rewritten, because a reader who followed
its command then got what it says.

### A fifth kind, and the fourth is wider than it says

Read on 2026-08-12. One site fits none of the four, and it is the one where the
later argument is not written in the source at all:

    git grep -n 'Path.Combine(root, withoutFragment' -- Jellyfin.Plugin.WatchSync.Tests/ReadmeLinkTests.cs
    Jellyfin.Plugin.WatchSync.Tests/ReadmeLinkTests.cs:231:            var path = Path.Combine(root, withoutFragment.Replace('/', Path.DirectorySeparatorChar));

`withoutFragment` is a link target taken out of `README.md`. What `Readme.IsAbsolute`
holds back before it is a scheme, a leading double slash and `mailto:`, so a target
beginning with a single slash reaches `Path.Combine` rooted, and the earlier
argument is dropped, which is what the query is about:

    powershell -NoProfile -Command "[System.IO.Path]::Combine('C:\repo', '/docs/x.md'); [System.IO.Path]::Combine('C:\repo', 'docs/x.md'); [System.IO.Path]::Combine('C:\repo', 'C:/other/x.md')"
    /docs/x.md
    C:\repo\docs/x.md
    C:/other/x.md

What follows from it is narrower than the general case, and saying so is the point
of naming it rather than folding it into the first three. The composed path reaches
`File.Exists` and `Directory.Exists` and nothing else, so a rooted target ends as a
link reported unresolved rather than as a file outside the tree being read. It
fails closed, and it fails with the wrong reason printed.

It is safe today because every target in the document is relative, and that is a
property of a tracked document rather than of this line:

    grep -oE '\]\([^)]+\)' README.md | sed 's/^](//; s/)$//' | sort -u
    CONTRIBUTING.md
    docs/compatibility.md
    docs/matching.md
    docs/parity.md
    LICENSE
    NOTICE.md

So it belongs beside the fourth rather than beside the first three: a link written
`/docs/matching.md` in a later edit voids the dismissal while the `Path.Combine`
line stands unchanged and the alert is not raised again.

The fourth kind is wider than its sentence allows, for the same reason. Not every
call site passes a literal:

    git grep -n 'InvariantGuard.Fixture(' -- Jellyfin.Plugin.WatchSync.Tests/InvariantGuardTests.cs
    Jellyfin.Plugin.WatchSync.Tests/InvariantGuardTests.cs:103:            new[] { ($"Invariants/{invariant}-near-miss.txt", InvariantGuard.Fixture($"{invariant}-near-miss.txt")) },
    Jellyfin.Plugin.WatchSync.Tests/InvariantGuardTests.cs:111:            new[] { ($"Invariants/{invariant}-near-miss-repaired.txt", InvariantGuard.Fixture($"{invariant}-near-miss-repaired.txt")) },
    Jellyfin.Plugin.WatchSync.Tests/InvariantGuardTests.cs:226:        var sources = new[] { ("Invariants/injected-clock-near-miss.txt", InvariantGuard.Fixture("injected-clock-near-miss.txt")) };

`invariant` is a theory parameter fed from `Invariants/register.txt`, so two of the
three names are rows of a tracked file rather than literals at the call. Neither
can be rooted while that register holds what it holds, and that is the same kind of
condition as the one above rather than a stronger one.

### What a reader meets on the alert page

That argument belongs on the alerts as well as here, because somebody who opens
one and finds no reason cannot tell a decision from an oversight. Twenty nine of
the thirty carry a comment written in another language, pointing at a tracker
outside this repository, and arguing about a different shape: a path composed
from a temporary directory and a generated name, which is not what any of these
thirty five sites does. It cannot be followed from here and it is not about them.

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq '[.[].dismissed_comment]|group_by(.)|map(length)'
    [29,1]

The one is alert 45, which carries the replacement in English and points at this
file. The other twenty nine still carry the original and the five open ones carry
nothing, so the page is in three states for one class. #192 holds that and it is
not repaired here. The count in that output has moved since, and the reading
dated 2026-08-13 below is where the page's state is now.

### The open half of the class, read on 2026-08-12

Five is no longer the number. Thirteen sites of this rule are open and none of them
has been triaged:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length, numbers:[.[].number]})'
    [{"count":1,"numbers":[84],"rule":"cs/linq/missed-select"},{"count":13,"numbers":[92,91,90,89,88,87,86,85,83,82,81,80,79],"rule":"cs/path-combine"}]

Each of the thirteen was read at its own line, and each is one of the first three
kinds or the fourth. The whole set of sites the rule can reach is in the test
project and none is in the plugin, which is the split every reading here has found:

    git grep -c 'Path\.Combine' origin/master -- 'Jellyfin.Plugin.WatchSync.Tests/*.cs' | awk -F: '{s+=$NF} END {print s}'
    43

    git grep -n 'Path\.Combine' origin/master -- 'Jellyfin.Plugin.WatchSync/**/*.cs' ; echo "exit=$?"
    exit=1

The twenty nine comments are unchanged, and the thirteen open ones carry nothing,
so what a reader meets on the page is still the three states above rather than one
argument per site. #192 holds it. The write that would repair it is refused on the
route available here, so this section records the state rather than closing it.

That last sentence is wrong. One site has been written since, which is what the
next section is, and the sentence is left standing so that a reader who acted on
it can see what replaced it.

The `cs/linq/missed-select` alert in that output is a different rule and a
different question. It is raised against a site added after the rewrites the
sections above describe, it is untriaged, and nothing here decides it.

### The split stopped holding, read on 2026-09-01

THE SECTION ABOVE SAYS THE WHOLE SET OF SITES THIS RULE CAN REACH IS IN THE TEST
PROJECT AND PASTES `exit=1` UNDER THE COMMAND THAT SHOWED IT. That reading was taken
on 2026-08-12; the store folder landed on 2026-08-17 and the store itself on
2026-08-23, so it was true when it was written and has not been true since. It is
left standing above rather than edited, so that a reader who acted on it can see
what replaced it.

Read before this section was written:

    git grep -n 'Path\.Combine' origin/master -- 'Jellyfin.Plugin.WatchSync/**/*.cs'
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:492:        return Path.Combine(_folder.CreateIfAbsent(), name + DocumentSuffix);
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:510:        return Path.Combine(
    origin/master:Jellyfin.Plugin.WatchSync/Storage/StoreFolder.cs:48:    public string FullPath => Path.Combine(_applicationPaths.DataPath, FolderName);

Two of the three are open alerts of this rule, and they are the only two of its open
alerts outside the test project:

    gh api --paginate 'repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&per_page=100' | jq '[.[] | select(.rule.id=="cs/path-combine")] | length'
    64

    gh api --paginate 'repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&per_page=100' | jq -r '.[] | select(.rule.id=="cs/path-combine") | select(.most_recent_instance.location.path | startswith("Jellyfin.Plugin.WatchSync/")) | "\(.number) \(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)"'
    142 Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:510
    141 Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:492

### Why those two are a different question from the sixty two beside them

Every kind above rests on the same argument: the later argument is a literal in the
source, or a name a test composed for itself, so it cannot be rooted and no earlier
argument can be dropped. Neither of these two is that. The later argument at both is a
document name a caller chose, and what stands between a caller and an absolute path is
a guard rather than the shape of the source.

The guard exists and it is at one of the two sites rather than at both:

    git grep -n 'RefuseANameThisStoreMayNotCompose' origin/master -- Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:365:    private static void RefuseANameThisStoreMayNotCompose(string name)
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:490:        RefuseANameThisStoreMayNotCompose(name);

It refuses anything but lower case letters, digits and hyphens, which is a closed set
rather than a search for separators, so a name that would traverse is refused whatever
platform this runs on. Alert 141 is the site that calls it, at line 492, two lines
below the call.

Alert 142 is `InFlightPathFor` at line 510, and nothing in that method guards. What
makes it safe today is the order of two statements in the one method that reaches it:

    git grep -n 'PathFor(name)' origin/master -- Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:158:    public DocumentReading? Read(string name) => ReadWhereItIsThere(PathFor(name));
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:228:        var path = PathFor(name);
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:257:        var path = PathFor(name);
    origin/master:Jellyfin.Plugin.WatchSync/Storage/DocumentStore.cs:284:            var inFlight = InFlightPathFor(name);

`Write` composes the guarded path at 257 and the in-flight path at 284 out of the same
name, so the guard has run by the time the second one is composed. That is a true
statement about this method and not a property of the site, and it stops being true if
anybody calls `InFlightPathFor` from anywhere else or moves the two lines apart.

So a dismissal of these two written in the words of the kinds above would be wrong
about them, and a dismissal of the whole rule taken on the strength of the sentence in
the earlier section would rest on a reading that no longer reproduces. Nothing is
decided here and no alert was touched: which of the three answers each rule gets is
what #271 is for, and this is a reading for whoever takes it.

### The write is not refused, read on 2026-08-13

Alert 79 was open and untriaged when the section above was written. It now
carries the argument for its own kind, in English, pointing at this file:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq '[.[]|select(.number==79)|{number, reason: .dismissed_reason, comment: .dismissed_comment}]'
    [{"comment":"The later argument at this call is a literal in the source, so it cannot be rooted and no earlier argument is dropped. The query is right wherever a later argument can be absolute. docs/code-scanning.md carries the class and the condition that makes this a defect again.","number":79,"reason":"false positive"}]

That comment is written in the singular and its site composes two later
arguments, both of them literals, so it is right about the site and narrower
than the kind it belongs to.

What is true and is not the same thing is that a comment cannot be changed on an
alert that is already dismissed. The service answers that with a 400, which the
reading on #192 quotes, so each of the twenty nine already-dismissed sites is a
reopen and a fresh dismissal rather than one write. That is what the repair
costs and it is not what stops it.

Forty five of the forty six sites of this class were not written in this pass, so
the page now holds four states rather than the three the sections above describe:
twenty nine carrying the original comment, one carrying a general English
replacement, one carrying the argument for its own kind, and fifteen open and
carrying nothing.

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq '[.[].dismissed_comment]|group_by(.)|map(length)'
    [29,1,1]

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length, numbers:[.[].number]})'
    [{"count":1,"numbers":[84],"rule":"cs/linq/missed-select"},{"count":1,"numbers":[93],"rule":"cs/linq/missed-where"},{"count":15,"numbers":[96,95,94,92,91,90,89,88,87,86,85,83,82,81,80],"rule":"cs/path-combine"}]

A `cs/linq/missed-where` alert is in that output and was not in the one above it.
It is the same case as the `cs/linq/missed-select` beside it: a different rule,
raised against a site added later, untriaged, and not decided here.

### Which kind each site of the class is

The five kinds are what a comment has to say, and nothing recorded which site was
which, so writing them was a re-reading of forty six lines rather than a
mechanical pass. Every site of the class in both states, with the line the alert
names:

    for state in dismissed open; do
      gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?tool_name=CodeQL&per_page=100&state=$state" \
        --jq '.[]|select(.rule.id=="cs/path-combine")|"\(.number)\t\(.most_recent_instance.location.path)\t\(.most_recent_instance.location.start_line)"'
    done | sort -t"$(printf '\t')" -k2,2 -k3,3n | while IFS="$(printf '\t')" read -r number path line; do
      printf '%s\t%s:%s\t' "$number" "$path" "$line"; sed -n "${line}p" "$path"
    done

Twelve of the forty six calls open on the line the alert names and close on a
later one, so for those the command prints the opening and none of the arguments
the kind is decided by. Those twelve were read with the lines that follow them.

Reading all forty six gives this split. It is a reading of that output rather
than something a command decides, and the alert numbers are here so the writing
that is still owed does not repeat the reading:

    24  every later argument is a literal in the source
        41 42 43 45 50 52 53 54 56 57 60 63 68 70 72 75 79 80 85 86 87 89 90 96
     9  a const string of the test project, beside literals
        46 49 55 61 81 82 88 91 94
     2  a repository-relative path git printed or Path.GetRelativePath produced
        48 64
    10  a fixture name reaching a helper as a parameter
        47 51 62 69 71 73 76 83 92 95
     1  a link target read out of README.md
        58

The last of those is `ReadmeLinkTests.cs:231`, which the section above argues,
and it is the only site of the class where the query is right about what the
line does. It carries the original comment and a reason of `false positive`
today. A site the query is right about is not a false positive, so what it is
owed is a reason of `used in tests` and a comment saying what the consequence
there actually is, rather than the sentence the other forty five get.

The first two kinds take one sentence each and the third and fourth take their
own, because a sentence true of a literal is not true of a parameter and the
fourth is the one that can stop being true without its line moving.

### What ends a dismissal

Nothing that runs. A dismissed alert stays dismissed while its site keeps its
fingerprint, and no part of the suite reads the alert page at all:

    git grep -l -i -n 'code-scanning' -- 'Jellyfin.Plugin.WatchSync.Tests/' ; echo "exit=$?"
    exit=1

So the condition is carried by this paragraph and by the comment on each alert,
and by neither reliably. It is the one the reading above already gives: the day a
site takes its later argument from an environment variable, a command line, a
configuration file or a peer, the alert is a defect again and the dismissal is
wrong.

Nobody else has read this. The commands above stand in place of a second reader.

### One comment per kind on the page, read on 2026-08-13

Every site of the class is dismissed and carries the argument for its own kind,
in English, pointing at this file. What a reader meets is one comment per kind
rather than the four states the sections above record:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=dismissed&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.dismissed_comment)|map({count:length, reason:(.[0].dismissed_reason), numbers:([.[].number]|sort)})|sort_by(-.count)|.[]|"\(.count)\t\(.reason)\t\(.numbers|join(" "))"'
    24      false positive  41 42 43 45 50 52 53 54 56 57 60 63 68 70 72 75 79 80 85 86 87 89 90 96
    10      false positive  47 51 62 69 71 73 76 83 92 95
    9       false positive  46 49 55 61 81 82 88 91 94
    2       false positive  48 64
    1       used in tests   58

Those five sets are the five kinds recorded above, number for number, and a
reader comparing the two lists is comparing a claim in this file against the
page rather than against a second copy of the claim. No site of the class is
open:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq 'group_by(.rule.id)|map({rule:.[0].rule.id, count:length, numbers:[.[].number]})'
    [{"count":1,"numbers":[84],"rule":"cs/linq/missed-select"},{"count":1,"numbers":[93],"rule":"cs/linq/missed-where"}]

The two alerts left in that output are the other rules the sections above name.
They are untriaged, they are raised against sites added after the rewrites, and
nothing here decides them.

A comment on an alert is a sentence rather than a section, so each carries what
its kind rests on and points here for the argument. The two that say more than
the query being right in general are the last two kinds: the fixture name says
that it can stop being true without the `Path.Combine` line moving, and the link
target says what actually follows at that site.

The one site the query is right about no longer reads as a false positive. Alert
58 is `ReadmeLinkTests.cs:231`, and it carries `used in tests` with a comment
saying that a target beginning with a single slash reaches the call rooted and
drops the root, and that the composed path reaches `File.Exists` and
`Directory.Exists` and nothing else, so it ends as an unresolved link. That is
bounded rather than harmless, which is why the reason moved and the dismissal
stayed.

Thirty one of the forty six were already dismissed, so each of those is a reopen
and a fresh dismissal rather than one write, for the reason the section above
gives. The other fifteen were open and took one write each.

Every one of the forty six was read at its own line before its comment was
written, and each alert's most recent instance is at the commit the mainline is
on, so the line an alert names is the line in the tree:

    for state in dismissed open; do
      gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?tool_name=CodeQL&per_page=100&state=$state" \
        --jq '.[]|select(.rule.id=="cs/path-combine")|.most_recent_instance.commit_sha'
    done | sort | uniq -c
         46 b924140906061f0bfd9a71b8a0b3b2b7f0077a9d

    git rev-parse origin/master
    b924140906061f0bfd9a71b8a0b3b2b7f0077a9d

That reading came out as the split recorded above rather than a different one,
which is what the writing depended on and is not a second person having checked
it.

What ends one of these dismissals is unchanged and is the section above: nothing
that runs. The page now carries the condition on every site instead of on one,
which is where a reader meets it, and this file is still the only place it is
written down in full.

Nobody else has read this. The commands above stand in place of a second reader.

## The eight findings open on 2026-08-18, read at their sites

Every alert below was read at the line it names. Each one's most recent instance
is at the commit the mainline is on, so the line an alert names is the line in
the tree rather than a line that has since moved:

    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100" \
      --jq '.[]|"\(.number)\t\(.rule.id)\t\(.most_recent_instance.location.path):\(.most_recent_instance.location.start_line)\t\(.most_recent_instance.commit_sha[0:7])"' | sort -n
    115	cs/useless-tostring-call	Jellyfin.Plugin.WatchSync/Peer/PeerText.cs:93	c08b73a
    116	cs/linq/missed-where	Jellyfin.Plugin.WatchSync/Document/DocumentUpgradeStep.cs:120	c08b73a
    117	cs/linq/missed-where	Jellyfin.Plugin.WatchSync/Document/DocumentUpgradeStep.cs:130	c08b73a
    118	cs/linq/missed-where	Jellyfin.Plugin.WatchSync/Document/DocumentUpgradeStep.cs:159	c08b73a
    119	cs/path-combine	Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs:432	c08b73a
    120	cs/path-combine	Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs:443	c08b73a
    121	cs/linq/missed-where	Jellyfin.Plugin.WatchSync.Tests/ConfigurationPageControlsTests.cs:175	c08b73a
    122	cs/linq/missed-where	Jellyfin.Plugin.WatchSync.Tests/ConfigurationPageControlsTests.cs:184	c08b73a

    git rev-parse origin/master
    c08b73acc622e3ec6a6ed372e8bbff7161173482

Five are repaired in the change that adds this section and three are dismissed
with the reason written on the alert. The split follows the one already recorded
here for this rule family: the repair is taken where the loop body is nothing but
the guarded statement, so moving the condition into the sequence leaves the body a
single line, and it is refused where the fix would relocate a statement without
removing one.

### The five loops whose body is the guard

Four of them are a `foreach` over a sequence whose whole body is one statement
under one condition. Moving the condition into a `Where` leaves the body as that
statement and nothing else, which is what the rule asks for and what alert 93 was
repaired for. 116 keeps the members the step declared and its body is the
assignment; 118 carries over the members the step added and its body is the
assignment; 121 and 122 are the two directions of the page-against-settings
comparison and each body is the `Add` of one finding.

The fifth, 117, is not a filter. It walks the members a step left behind and
throws on the first one the step did not declare, so the `Where` the rule proposes
would produce a loop that can never reach a second element. It is written as the
question it asks, `mine.Any(...)`, which removes the loop rather than filtering it.

118 is the one to read twice, because its condition reads the object its body
writes. `Where` is lazy, so the condition is evaluated immediately before the body
for each element exactly where the `if` stood, and the keys of a `JsonObject` are
unique, so no element's condition can be moved by an earlier element's write. The
sequencing is therefore unchanged, and what the repair costs is that a reader has
to know `Where` is lazy to see that.

Each changed site was perturbed and the suite run, so what is claimed is that the
tests reach these lines rather than that the change looked equivalent. Dropping
116's condition turns two legs red, inverting 117's turns six red, inverting 118's
turns one red, and inverting the two conditions of 121 and 122 turns two red. Each
was restored before the next, and with all four in place the suite is green:

    dotnet test Jellyfin.Plugin.WatchSync.Tests/Jellyfin.Plugin.WatchSync.Tests.csproj -f net10.0 --nologo
    Bestanden!   : Fehler:     0, erfolgreich:   355, übersprungen:     1, gesamt:   356

That output is a German locale, quoted as it came, and it is one of the two
targets. The .NET 9 runtime is not installed on the machine that ran it, so the
net9.0 leg was not run there and nothing here says it passed.

### Appending a rune, alert 115

The line is `kept.Append(rune.ToString())` in the helper that bounds and strips
what a peer sent. The rule is right that the same string reaches the builder
without the call, and acting on it costs more than it saves, because no `Append`
overload takes a rune. Reflected off `System.Text.StringBuilder`, from a console
project outside this tree so that nothing here restores a package for it:

    overloads with one param: AppendInterpolatedStringHandler&, Boolean, Byte, Char, Char[], Decimal, Double, Int16, Int32, Int64, Object, ReadOnlyMemory`1, ReadOnlySpan`1, SByte, Single, String, StringBuilder, UInt16, UInt32, UInt64

That is the .NET 10 runtime installed here, and the plugin's net9.0 leg was not
probed. Removing the call therefore binds to `Append(object)`, which boxes the
rune and calls the same `ToString` inside it, so the string allocation stays and a
box is added. It is dismissed as `won't fix` rather than as a false positive,
because the rule is not wrong about the result being identical, only about the
change being an improvement.

What ends it is an overload that takes a rune, or this line being rewritten to
encode into a span, which is a decision about how the helper is written rather
than a response to this alert.

### The two path-combine sites, and a sixth kind

119 is the second kind already recorded above. Its later arguments are two const
strings of the test project, so it cannot be rooted while those declarations
stand:

    git grep -n 'const string TestProject\|const string FixtureDirectory' -- Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs
    Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs:30:    private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";
    Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs:31:    private const string FixtureDirectory = "Document";

120 is a sixth kind, and it is a stronger argument than the fixture-name kind
rather than a variation of it. The later argument is composed in the same method
that combines it, from a format whose first characters are literal:

    sed -n '437,444p' Jellyfin.Plugin.WatchSync.Tests/DocumentUpgradeTests.cs
        private static string Fixture(int version, string? suffix)
        {
            var name = suffix is null
                ? string.Format(CultureInfo.InvariantCulture, "version-{0}.json", version)
                : string.Format(CultureInfo.InvariantCulture, "version-{0}-{1}.json", version, suffix);

            return Path.Combine(FixtureRoot(), name);
        }

The fixture-name kind says that every call site passes something that cannot be
rooted today, and that this can stop being true without the `Path.Combine` line
moving. Here a rooted `suffix` would not root the argument either, because it
arrives after the literal `version-` in both branches. What ends this one is the
format losing its literal prefix, which is a change to the line above the combine
rather than to the combine.

Nobody else has read this. The commands above stand in place of a second reader.
