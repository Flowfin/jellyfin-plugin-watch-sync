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

## The rest

This file covers sign-off and the rule for a workflow change. The wider note,
covering the checks a change has to pass and the shape an issue is expected to
take, is #114.
