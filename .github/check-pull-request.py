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
    releases         how many releases the repository has published, or null

The caller supplies the commits rather than this file deriving them, for the
same reason the branch guard beside it takes its branch list from the API: a
shallow clone would otherwise shrink the set and pass the check by accident.
`releases` arrives the same way and for the same reason, and it is a count in
the document rather than a call from here, which is what keeps the verdict
provable on a document written by hand and offline.

Three tiers rather than two. A blocking rule fails. An advisory rule warns and
never fails. A note records that a blocking rule was considered and did not
apply, so an excused pull request cannot be read as one nothing looked at. Only
the failing tier reaches the exit code.

Exit 0 when every blocking rule holds, 1 otherwise, and 1 again when the
document carries no commits at all, because a pull request always has one and
an empty list would otherwise pass every rule that reads them.
"""

import json
import re
import sys

# Where a changelog entry lives. One fragment file per change, assembled at
# release, because a single shared file is where parallel changes collide. #116
# lands the directory and the assembly; this rule looks for a path under it, and
# until the directory exists a bump on a repository that has published finds
# nothing there and is refused, which is what that issue asks for.
#
# Any changed path under it counts. The document carries paths and not statuses,
# and a fragment touched by a change is an entry belonging to that change either
# way, so the distinction would cost a schema and buy nothing here.
CHANGELOG_DIRECTORY = "changelog.d/"

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


def is_the_zero_version(version):
    """Whether a version is zero in every component it has.

    `0.0.0` is what a manifest carries before anybody has decided a number, and
    it is never a version somebody installed. The manifest here writes four
    components and the issue that asked for this wrote three, so the test is
    over the components rather than over one spelling: every one of them parses
    as a number and every one of them is zero.
    """
    if not version:
        return False
    parts = version.split(".")
    return all(part.isdigit() and int(part) == 0 for part in parts)


def has_published(document):
    """Whether the repository this document is about has published a release.

    A missing count is treated as published. The rule this feeds excuses a
    version change, so the direction that is safe when nothing is known is the
    one that keeps the rule biting.
    """
    count = document.get("releases")
    if count is None:
        return True
    return count > 0


def changelog_fragments(files):
    """The changed paths that are changelog fragments."""
    return [path for path in files if path.startswith(CHANGELOG_DIRECTORY)]


def version_change_is_a_release(document, before, after):
    """Why a version change is not a release, or None when it is one.

    A released number a caller cannot look up is a number they cannot act on,
    and that is the whole reason the changelog rule exists. It does not reach a
    number nobody could have looked up.
    """
    if not has_published(document):
        return (
            "the repository has published no release, so the number that "
            "moves from {} to {} was never one anybody could look up"
        ).format(before, after)
    for end, version in (("before", before), ("after", after)):
        if is_the_zero_version(version):
            return (
                "the version {} the change is {}, which is what a manifest "
                "carries before a number is decided rather than a release"
            ).format(end, version)
    return None


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
    if (
        before != after
        and version_change_is_a_release(document, before, after) is None
        and not changelog_fragments(files)
    ):
        yield (
            "a-version-bump-carries-a-changelog-entry: the manifest version "
            "moves from {} to {} and no changed path is under {}. An operator "
            "deciding whether to upgrade a plugin that writes into their "
            "users' data reads those entries and nothing else.".format(
                before, after, CHANGELOG_DIRECTORY
            )
        )


def notes(document):
    """Yield one line per blocking rule that was considered and did not apply.

    The rule this reports on is the only one that can decline to fire, and a
    decline that printed nothing would leave a version bump passing for a
    reason the reader has to guess at, which is the same silence the rule was
    written against.
    """
    before = version_of(document.get("manifest_before"))
    after = version_of(document.get("manifest_after"))
    if before == after:
        return
    excuse = version_change_is_a_release(document, before, after)
    if excuse is None:
        return
    yield (
        "a-version-bump-carries-a-changelog-entry: not applied, because {}. "
        "The rule asks for an entry an operator can read against a release "
        "they can install, and there is none to read it against.".format(excuse)
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
    remarks = list(notes(document))

    for remark in remarks:
        print("NOTE {}".format(remark))
    for warning in warnings:
        print("WARN {}".format(warning))
    for failure in failures:
        print("FAIL {}".format(failure))
    print(
        "read {} commit(s) and {} changed file(s) against a repository with "
        "{} published release(s): {} blocking failure(s), {} advisory "
        "warning(s), {} rule(s) not applied".format(
            len(document["commits"]),
            len(document.get("files") or []),
            "an unstated number of" if document.get("releases") is None
            else document["releases"],
            len(failures),
            len(warnings),
            len(remarks),
        )
    )
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
