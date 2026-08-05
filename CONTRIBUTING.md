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

## The rest

This file covers sign-off only. The wider note, covering the checks a change has
to pass and the shape an issue is expected to take, is #114.
