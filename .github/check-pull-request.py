#!/usr/bin/env python3
"""Refuse a pull request that does not say what it is for.

Every other check here reasons about the code. This one reasons about the pull
request: whether it names an issue, whether its commits do, and whether a
version bump carries the changelog entry that goes with it. Nothing the
compiler, the analyzers, the sign-off gate or the workflow audit reads can
answer any of those, because none of them sees the pull request at all.

The three rules above are decidable by reading text, so they fail. Anything
that needs a judgement warns and never fails, because a gate that blocks on a
judgement is one people learn to work around, and a warning that can block is
the same defect wearing a different label. The two tiers are printed with
different prefixes and the exit code follows the failing tier alone.

The pull request arrives as one JSON document on standard input, so the same
file runs in a workflow and against a checkout by hand. Its keys:

    title            the pull request title
    body             the pull request body, or null
    commits          [{"sha": ..., "message": ...}], non-merge commits only
    files            the paths the pull request changes
    manifest_before  the text of build.yaml at the base, or null
    manifest_after   the text of build.yaml at the head, or null

The caller supplies the commits rather than this file deriving them, for the
same reason the branch guard beside it takes its branch list from the API: a
shallow clone would otherwise shrink the set and pass the check by accident.

Exit 0 when every blocking rule holds, 1 otherwise, and 1 again when the
document carries no commits at all, because a pull request always has one and
an empty list would otherwise pass every rule that reads them.
"""

import json
import re
import sys

# The file a version bump has to touch. It does not exist yet; #116 lands it,
# and until then this rule reddens a bump rather than passing it, which is what
# that issue asks for.
CHANGELOG = "CHANGELOG.md"

# An issue reference as this project writes one.
ISSUE = re.compile(r"#(\d+)")

# The version, read as one anchored line rather than parsed. build.yaml is a
# hand-maintained manifest with the key at column zero, and Directory.Build.props
# reads it the same way; a version moved into a nested mapping is not found by
# either, which is a refused build there and no comparison here.
VERSION = re.compile(r'(?m)^version:[ \t]*"([^"]*)"')


def issues_in(text):
    """The set of issue numbers a piece of text names."""
    return set(ISSUE.findall(text or ""))


def version_of(manifest):
    """The version an anchored line of the manifest declares, or None."""
    if manifest is None:
        return None
    found = VERSION.search(manifest)
    return found.group(1) if found else None


def blocking(document):
    """Yield one failure line per blocking rule the document breaks."""
    title = document.get("title") or ""
    body = document.get("body") or ""
    commits = document.get("commits") or []
    files = document.get("files") or []

    if not issues_in(title + "\n" + body):
        yield (
            "pull-request-names-an-issue: neither the title nor the body names "
            "an issue. Work here starts as an issue, and a pull request that "
            "names none cannot be read against what it was for."
        )

    for commit in commits:
        if not issues_in(commit.get("message")):
            yield (
                "every-commit-names-an-issue: {} names no issue. A reader "
                "arriving at one commit through git blame has only its message, "
                "and the pull request it came through is not in it.".format(
                    (commit.get("sha") or "?")[:8]
                )
            )

    before = version_of(document.get("manifest_before"))
    after = version_of(document.get("manifest_after"))
    if before != after and CHANGELOG not in files:
        yield (
            "a-version-bump-carries-a-changelog-entry: the manifest version "
            "moves from {} to {} and {} is not among the changed files. An "
            "operator deciding whether to upgrade a plugin that writes into "
            "their users' data reads that file and nothing else.".format(
                before, after, CHANGELOG
            )
        )


def advisory(document):
    """Yield one warning line per advisory rule the document breaks."""
    named_by_request = issues_in(
        (document.get("title") or "") + "\n" + (document.get("body") or "")
    )
    for commit in document.get("commits") or []:
        stray = issues_in(commit.get("message")) - named_by_request
        if stray:
            yield (
                "commits-and-request-name-the-same-issues: {} names {} which "
                "the pull request does not. Often deliberate, sometimes a "
                "commit left on the wrong branch, so this warns and never "
                "fails.".format(
                    (commit.get("sha") or "?")[:8],
                    ", ".join("#" + number for number in sorted(stray)),
                )
            )


def main():
    try:
        document = json.load(sys.stdin)
    except ValueError as bad:
        sys.exit("check-pull-request: standard input is not JSON: {}".format(bad))
    if not isinstance(document, dict):
        sys.exit("check-pull-request: standard input is not a JSON object.")
    if not document.get("commits"):
        sys.exit("check-pull-request: the document carries no commits.")

    failures = list(blocking(document))
    warnings = list(advisory(document))

    for warning in warnings:
        print("WARN {}".format(warning))
    for failure in failures:
        print("FAIL {}".format(failure))
    print(
        "read {} commit(s) and {} changed file(s): {} blocking failure(s), "
        "{} advisory warning(s)".format(
            len(document["commits"]),
            len(document.get("files") or []),
            len(failures),
            len(warnings),
        )
    )
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
