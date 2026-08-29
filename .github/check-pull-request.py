#!/usr/bin/env python3
"""Refuse a pull request that does not say what it is for.

Every other check here reasons about the code. This one reasons about the pull
request: whether it names an issue, whether its commits do, whether a version
bump carries the changelog entry that goes with it, whether a change that drops
support for something carries one, and whether one that raises the floor of a
line it keeps carries one. Nothing the compiler, the analyzers,
the sign-off gate or the workflow audit reads can answer any of those, because
none of them sees the pull request at all.

Each of those is decidable by reading text, so they fail. Anything that needs a
judgement warns and never fails, because a gate that blocks on a judgement is
one people learn to work around, and a warning that can block is the same
defect wearing a different label. The two tiers are printed with different
prefixes and the exit code follows the failing tier alone.

The pull request arrives as one JSON document on standard input, so the same
file runs in a workflow and against a checkout by hand. Its keys:

    title            the pull request title
    body             the pull request body, or null
    commits          [{"sha": ..., "message": ...}], non-merge commits only
    files            [{"path": ..., "status": ...}], one per changed file, with
                     the status GitHub gives it, so that a file a change REMOVES
                     can be told from one it writes
    manifest_before  the text of build.yaml at the base, or null
    manifest_after   the text of build.yaml at the head, or null
    envelope_versions_before  the text of EnvelopeVersions.cs at the base, or null
    envelope_versions_after   the text of EnvelopeVersions.cs at the head, or null
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
# A path under it that the change WRITES counts, and one it REMOVES does not.
# THIS PARAGRAPH SAID ANY CHANGED PATH COUNTED, on the reasoning that a fragment
# touched by a change is an entry belonging to that change either way, and that
# the distinction would cost a schema and buy nothing. The second half of that
# is what failed: a fragment a change DELETES is touched by it and is the
# opposite of an entry it wrote, so a version bump that tidied one stale
# fragment out of the directory and wrote none satisfied this rule, and so did a
# change dropping a server line or raising a floor beside such a deletion. #296
# is where that was found and what carries the demonstration. The schema it
# costs is one field on a list the caller already had the answer for.
CHANGELOG_DIRECTORY = "changelog.d/"

# The status of a file the change took away. Named as the one exclusion rather
# than the permitted statuses being listed, because the list of statuses is the
# API's and a member added to it would silently stop counting as an entry if this
# were written the other way round. A rename arrives as one entry carrying the
# NEW path, so it is an entry the change wrote and is not this.
REMOVED = "removed"

# An issue reference as this project writes one.
ISSUE = re.compile(r"#(\d+)")

# The version, read as one anchored line rather than parsed. build.yaml is a
# hand-maintained manifest with the key at column zero, and Directory.Build.props
# reads it the same way; a version moved into a nested mapping is not found by
# either, which is a refused build there and no comparison here.
VERSION = re.compile(r'(?m)^version:[ \t]*"([^"]*)"')

# Where a server line is declared. build.yaml carries one entry per supported
# line under `targets:`, and that list is the declaration rather than a copy of
# one: the packaging pair above it is one of those entries and never a third
# answer, which BuildTargetsTests refuses. An entry is found by its `framework`,
# because that is what a target is here, and an ABI moving under a framework
# that stays is a package bump rather than a line dropped. This reading does not
# reach that case; the reading below it does, and the two are separate because
# what they strand is not the same thing.
TARGETS = re.compile(r"(?m)^targets:[ \t]*$")
TARGET_ENTRY = re.compile(r'(?m)^-[ \t]+framework:[ \t]*"([^"]*)"')

# Where a server line's floor is declared, read as the pair rather than as the
# framework alone. `targetAbi` is not a description of the build: it is the
# oldest server this plugin claims on that line, and a server compares itself
# against it before it will install. A number that rises under a framework that
# stays therefore leaves every install below the new one holding the version it
# has and never offered another. That is the same harm as a line disappearing,
# and it arrives without the list losing a member and without the version
# moving, so neither of the two rules beside this one sees it.
#
# The pair is required in the order the manifest writes it, and a reordered
# entry is unread rather than empty. That is the safe direction and it is
# reported by the advisory over an unreadable declaration rather than passing in
# silence.
TARGET_FLOOR = re.compile(
    r'(?m)^-[ \t]+framework:[ \t]*"([^"]*)"[ \t]*\r?\n[ \t]+targetAbi:[ \t]*"([^"]*)"'
)

# Where the envelope versions are declared. EnvelopeVersions.Supported is the
# one place that set exists, which is the fifth rule in #18, so the set a peer
# can be refused against is read from the same line the plugin refuses by.
SUPPORTED = re.compile(r"Supported\s*\{\s*get;\s*\}\s*=\s*new\[\]\s*\{([^}]*)\}")


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


def changed_files(document):
    """The files the pull request changes, as (path, status) pairs.

    A file the document states without a path or without a status stops the run
    rather than being read as one of either. The whole point of the field is the
    distinction between a fragment written and a fragment taken away, and a
    default for a missing status would be the permissive one every time, which is
    the answer #296 was about.
    """
    pairs = []

    for entry in document.get("files") or []:
        if not isinstance(entry, dict):
            sys.exit(
                "check-pull-request: a changed file is {} rather than an object "
                "carrying a path and a status.".format(type(entry).__name__)
            )
        path, status = entry.get("path"), entry.get("status")
        if not path or not status:
            sys.exit(
                "check-pull-request: the changed file {!r} carries no {}, and a "
                "rule that counts what a change WROTE cannot read it.".format(
                    entry, "path" if not path else "status"
                )
            )
        pairs.append((path, status))

    return pairs


def changelog_fragments(files):
    """The changelog fragments the change WROTE.

    A path the change removed is not one of them. It is touched by the change and
    is the opposite of an entry belonging to it, and counting it is what let a
    version bump satisfy this rule by deleting a stale fragment.
    """
    return [
        path
        for path, status in files
        if path.startswith(CHANGELOG_DIRECTORY) and status != REMOVED
    ]


def server_lines(manifest):
    """The server lines a manifest declares, or None where none is readable.

    None and the empty set are different answers, and the difference is the
    safety of the rule that reads this. A manifest this cannot parse is one
    where nothing may be concluded about what was dropped, and a list somebody
    reindented would otherwise read as every line disappearing at once.
    """
    if manifest is None:
        return None
    if not TARGETS.search(manifest):
        return None
    found = set(TARGET_ENTRY.findall(manifest))
    return found or None


def target_floors(manifest):
    """The ABI floor each server line declares, or None where none is readable.

    The same None-or-a-reading rule as the two beside it, for a sharper reason.
    A manifest whose entries this cannot parse would answer the empty mapping,
    the comparison would find no framework at both ends, and a change that
    raised a floor would pass because the reading failed rather than because the
    floor held.

    It does not repeat the `targets:` guard `server_lines` carries above it. The
    pair regex already requires an entry, so the guard changed no answer on any
    document here and could be deleted without a single one going red, which
    makes it a line claiming a check nothing proves.
    """
    if manifest is None:
        return None
    found = TARGET_FLOOR.findall(manifest)
    if not found:
        return None
    floors = {framework: abi for framework, abi in found}
    if len(floors) != len(found):
        return None
    return floors


def is_a_version_number(value):
    """Whether a value is a dotted number this can order against another."""
    parts = (value or "").split(".")
    return bool(value) and all(part.isdigit() for part in parts)


def as_version_number(value):
    """A dotted number as a tuple, padded so two of unequal length compare."""
    return tuple(int(part) for part in value.split("."))


def rose(was, now):
    """Whether a floor moved upward, or None where the two cannot be ordered."""
    if not (is_a_version_number(was) and is_a_version_number(now)):
        return None
    first, second = as_version_number(was), as_version_number(now)
    width = max(len(first), len(second))
    first += (0,) * (width - len(first))
    second += (0,) * (width - len(second))
    return second > first


def raised_floors(document):
    """Yield the framework and the two floors, per line whose floor rose.

    Only upward. A floor that falls widens what the plugin installs on and
    strands nobody, and a rule refusing it would ask for an entry about a
    server that just gained the plugin rather than lost it.
    """
    before = target_floors(document.get("manifest_before"))
    after = target_floors(document.get("manifest_after"))
    if before is None or after is None:
        return
    for framework in sorted(set(before) & set(after)):
        was, now = before[framework], after[framework]
        if was == now:
            continue
        if rose(was, now):
            yield framework, was, now


def unorderable_floors(document):
    """Yield the framework and its two floors, per line this cannot order.

    A floor that moved to or from something that is not a dotted number is a
    change nothing here may conclude about, and it is a different state from a
    floor that held. Saying so is what keeps the rule's silence readable.
    """
    before = target_floors(document.get("manifest_before"))
    after = target_floors(document.get("manifest_after"))
    if before is None or after is None:
        return
    for framework in sorted(set(before) & set(after)):
        was, now = before[framework], after[framework]
        if was != now and rose(was, now) is None:
            yield framework, was, now


def envelope_versions(source):
    """The envelope versions a source declares, or None where none is readable.

    The same shape as the reading above and for the same reason. A declaration
    whose initialiser is written in another form is unread rather than empty,
    because an unread declaration answering the empty set would refuse a change
    that dropped nothing.
    """
    if source is None:
        return None
    found = SUPPORTED.search(source)
    if not found:
        return None
    numbers = {part.strip() for part in found.group(1).split(",") if part.strip()}
    if not numbers or not all(number.isdigit() for number in numbers):
        return None
    return numbers


# What dropping support is read out of, one entry per thing a drop strands. The
# pairing contract version is the third member of that sentence in
# docs/changelog.md and is deliberately absent here: nothing in this tree
# declares one, so there is no before and no after to compare. An entry reading
# a declaration that does not exist would answer None on every pull request and
# refuse nothing, while making the rule read as though it covered all three.
SUPPORT = (
    (
        "envelope version",
        "envelope_versions_before",
        "envelope_versions_after",
        envelope_versions,
    ),
    ("server line", "manifest_before", "manifest_after", server_lines),
)

# Every declaration a rule here reads out of one end and compares against the
# other. The drop rule walks SUPPORT alone, because a floor is not a member that
# can go missing; the advisory over an unreadable end walks all of them, because
# an end nothing could parse is worth saying whichever rule was going to read
# it.
DECLARATIONS = SUPPORT + (
    ("ABI floor", "manifest_before", "manifest_after", target_floors),
)


def dropped_support(document):
    """Yield the declaration and what it lost, per declaration this change cuts."""
    for what, before_key, after_key, read in SUPPORT:
        before = read(document.get(before_key))
        after = read(document.get(after_key))
        if before is None or after is None:
            continue
        gone = sorted(before - after)
        if gone:
            yield what, gone


def unreadable_support(document):
    """Yield the declaration and the end that did not parse, where the other did.

    One end readable and the other not is the case worth a word: something was
    there to compare and the comparison could not be made, which is what a list
    somebody reindented looks like from here. Neither end readable is a document
    that never carried the declaration at all, and there is nothing to say about
    a change that did not touch one.
    """
    for what, before_key, after_key, read in DECLARATIONS:
        ends = [(key, read(document.get(key))) for key in (before_key, after_key)]
        if len([end for key, end in ends if end is not None]) != 1:
            continue
        for key, end in ends:
            if end is None:
                yield what, key


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
    files = changed_files(document)

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
            "moves from {} to {} and this change writes no path under {}. An "
            "operator "
            "deciding whether to upgrade a plugin that writes into their "
            "users' data reads those entries and nothing else.".format(
                before, after, CHANGELOG_DIRECTORY
            )
        )

    if has_published(document) and not changelog_fragments(files):
        for what, gone in dropped_support(document):
            yield (
                "dropping-support-carries-a-changelog-entry: the {} declaration "
                "loses {} and this change writes no path under {}. Dropping one "
                "strands "
                "a peer or a server that was working yesterday, and the number "
                "in the manifest does not have to move for that to happen, so "
                "the rule above sees none of it.".format(
                    what,
                    ", ".join(gone),
                    CHANGELOG_DIRECTORY,
                )
            )

    if has_published(document) and not changelog_fragments(files):
        for framework, was, now in raised_floors(document):
            yield (
                "raising-an-abi-floor-carries-a-changelog-entry: the {} line "
                "declares {} where it declared {}, and this change writes no path "
                "under {}. A server below the new number is not offered this version "
                "at all, so the install that was working yesterday stops being "
                "updated and is told nothing. The line is still declared and the "
                "manifest version need not move, so neither rule above sees "
                "it.".format(framework, now, was, CHANGELOG_DIRECTORY)
            )


def notes(document):
    """Yield one line per blocking rule that was considered and did not apply.

    Both rules that can decline read the same count, and a decline that printed
    nothing would leave the change passing for a reason the reader has to guess
    at, which is the same silence the rules were written against.
    """
    before = version_of(document.get("manifest_before"))
    after = version_of(document.get("manifest_after"))
    if before != after:
        excuse = version_change_is_a_release(document, before, after)
        if excuse is not None:
            yield (
                "a-version-bump-carries-a-changelog-entry: not applied, because "
                "{}. The rule asks for an entry an operator can read against a "
                "release they can install, and there is none to read it "
                "against.".format(excuse)
            )

    if not has_published(document):
        for what, gone in dropped_support(document):
            yield (
                "dropping-support-carries-a-changelog-entry: not applied, "
                "although the {} declaration loses {}, because the repository "
                "has published no release. What a drop strands is a peer or a "
                "server running a build somebody installed, and there is no "
                "such build.".format(what, ", ".join(gone))
            )

        for framework, was, now in raised_floors(document):
            yield (
                "raising-an-abi-floor-carries-a-changelog-entry: not applied, "
                "although the {} line moves its floor from {} to {}, because "
                "the repository has published no release. What a raised floor "
                "strands is an install somebody made, and there is "
                "none.".format(framework, was, now)
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

    for what, key in unreadable_support(document):
        yield (
            "a-support-declaration-is-read-or-nothing-is-concluded: the {} "
            "declaration parses at one end of this change and not in `{}`, so "
            "what that end declares is unknown and the rule over it is not run "
            "here. An end nothing could read is not an end that declares "
            "nothing, and answering it as one would refuse a change that took "
            "nothing away or pass one the rule exists for, so this warns and "
            "never fails, and somebody reads the two ends.".format(what, key)
        )

    for framework, was, now in unorderable_floors(document):
        yield (
            "an-abi-floor-is-ordered-or-nothing-is-concluded: the {} line moves "
            "its floor from {} to {}, and at least one of those is not a dotted "
            "number, so whether the floor rose is unknown and the rule over it "
            "is not run here. Answering that an unordered pair did not rise "
            "would pass a raised floor for the reading having failed, so this "
            "warns and never fails, and somebody reads the two "
            "numbers.".format(framework, was, now)
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
            len(changed_files(document)),
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
