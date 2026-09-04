# Contributing

## Sign off what you contribute

Every commit in a pull request has to carry a `Signed-off-by` trailer whose name
and email match the commit's author. Git writes it for you:

    git commit -s

If you have already committed without one, add it to the commits on your branch:

    git rebase --signoff <base>

The check reads every non-merge commit in the pull request and fails if any of
them is missing the trailer, so adding a signed commit on top does not repair an
unsigned one below it.

## What you are asserting

The trailer is an assertion of the Developer Certificate of Origin, version 1.1,
which is in this repository at [DCO](DCO). Read it once. In short, you are
certifying that you wrote the contribution or have the right to submit it under
this project's licence, and that your contribution and the personal information
in your sign-off become part of a public record kept indefinitely.

The name and email in the trailer are the ones that go into that record, so use
ones you are content to have published. GitHub's `users.noreply.github.com`
address is fine and is what the existing history uses.

## Changing a workflow

Four properties hold for every file under `.github/workflows/`. They are written
here because the audit only refuses what it already knows about, and a rule that
lives only inside the tool is a rule nobody reads before writing the change.

Permissions are declared at the top of the file, as `permissions: {}` or as a
read-only scope. A job that has to write something declares that scope on itself,
next to the steps that use it, so the grant never reaches the rest of the file.

Every reference to something outside this repository is pinned to a commit, with
a comment saying which version, or which branch and date, that commit was. A tag
can be moved onto different bytes; a commit cannot.

Every checkout runs with `persist-credentials: false`. A job that does not push
has no reason to leave a usable token behind in the clone.

No job restores a cache in a run that publishes a release. The reasoning is in
the header of `.github/workflows/zizmor.yml` and is not repeated here.

The audit is the check run named `Audit workflows (zizmor)`. It runs zizmor's
regular persona at `--min-severity=low` on every push and every pull request and
fails on any actionable finding. Run the same thing against a clone before you
push, at the version the workflow pins rather than at whichever is newest:

    ZIZMOR_VERSION=$(sed -n 's/.*ZIZMOR_VERSION: "\(.*\)"/\1/p' .github/workflows/zizmor.yml)
    uvx --no-build "zizmor@${ZIZMOR_VERSION}" --strict-collection --min-severity=low --format=plain .

A workflow that calls a shared workflow in another repository is pinned the same
way, but what runs inside that call is not this repository's to harden. Where such
a call remains, the four properties above cover the caller and stop at the call.

## What an issue has to say before work starts on it

Work here starts as an issue and lands as a pull request, and the pull-request
check refuses one that names no issue. So the issue is read before anything is
built, and it has to be worth reading.

An issue says three things. What is wrong, which is the state of the tree rather
than the absence of a feature. What the evidence is, which is what makes the first
half checkable by somebody who did not write it. And what "done" means, as
conditions somebody else can decide the truth of without asking the author.

**A number in an issue carries the command that produced it.** A figure with no
command behind it is a claim about a tree nobody can go and look at, and the ones
that are wrong are wrong in the direction that made them worth quoting. That
applies to a count, a size, a duration and a version alike, and it applies to the
comments on an issue as much as to its body.

Nothing refuses any of this. The check reads whether an issue is named, never what
it says, so this section is a rule a reader follows and not one a machine keeps.
The half a machine does keep is elsewhere: `docs/configuration.md` refuses a
number the plugin declares with no row, and a record that quotes a figure is held
to the source it came from.

## A closing link opens its own line

The forge ends an issue when a pull request body carries one of its closing
keywords in front of an issue reference. It reads those two words and nothing
around them. A heading asking why a change does not finish an issue ends that
issue; a sentence saying plainly that it does not ends it too; and a line saying
a change finishes one CONDITION of an issue ends the whole issue.

That is not a hypothetical here. Every pull request body this repository has
opened was read for the shape, and each merge carrying one was compared against
the moment the issue it named was closed:

    gh pr list --repo Flowfin/jellyfin-plugin-watch-sync --state all --limit 500 --json number,body,mergedAt
    gh api "repos/Flowfin/jellyfin-plugin-watch-sync/issues/events?per_page=100" --paginate --jq '.[] | select(.event=="closed") | "issue \(.issue.number) closed \(.created_at)"'

Thirteen bodies carry it, and twelve of them ended the issue they named, one or
two seconds after the merge. Six were noticed and reopened, the slowest after
sixty-nine minutes. Two are closed today although the body that closed them says
in so many words that it does not close them. The other four ended an issue the
author did mean to end, in a spelling that happened to work.

So there is one place a closing link may stand, and it is the start of a line, at
column zero, where writing one is a decision rather than a turn of phrase:

    Closes #29.

Anywhere else in the body, `Deterministic PR-hygiene checks` refuses it by name
and quotes the line back:

    FAIL a-closing-verb-opens-its-own-line: ...

Write anything else with the number out of the verb's reach. The two spellings
are different for a writer and identical to the forge, and this reads the
forge's.

**It fails rather than warns.** Whether a line begins with a closing link is
decidable by reading text, which is the whole test the three tiers here turn on:
a rule that needs a judgement warns and can never fail, and a rule that needs
none fails. The harm is the wrong shape for a warning as well. A warning is read
by whoever is looking at the run; a wrongly closed issue is read by nobody,
because it has left the list where somebody would have looked, and reopening it
is somebody happening to notice.

**What it does not reach**, so that a green run is not read as more than it is.
It reads the pull request body and not the commit messages, which the forge also
acts on when they land on the mainline. It reads `#1`, the reference this project
writes, and not `owner/repo#1` or a full issue URL. And it reads every line the
same way: a closing link inside a fenced block or an indented quotation is
refused with the rest, because nothing here has measured whether the forge skips
those, and refusing a line somebody could safely have written costs a rewrite
while passing one the forge acts on costs an issue nobody notices is gone.

## Every test is headless

Every test this plugin ships runs without a display, without elevation, without a
machine-wide trust store, without the network, without a real wait and without the
machine clock. It exists because a suite that needs any of those runs on one
machine and quietly stops running everywhere else, and the first anybody hears of
it is when a regression ships.

The rule, what each refusal replaces, and what the scan cannot see are in
[the headless test rule](Jellyfin.Plugin.WatchSync.Tests/headless-rule.md). It is
not restated here, because a second copy of a rule drifts from the one that is
enforced and a reader cannot tell which of the two they are holding.

## What this note does not carry yet

The one local command that runs the legs the gate runs, in the same order, so that
a contributor who runs it green is not surprised. It is not here because two of
the four contexts the mainline requires are produced by a workflow in another
repository, so what they run is not in this tree to reproduce:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/rulesets/20464507 --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]'
    ["call / build","call / test","Reject Trojan Source Unicode","Audit workflows (zizmor)"]

Building here and naming the contexts this repository produces is #90, and which
names those contexts take is #105. Until then a contributor runs `dotnet test` and
the zizmor invocation above, and the gate can still surprise them. That is the
honest state of it rather than a command that covers less than it claims to.
