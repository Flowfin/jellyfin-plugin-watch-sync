#!/usr/bin/env python3
"""Assemble the release notes an operator reads from the fragments under `changelog.d/`.

The changelog in this repository is a directory of fragments rather than one prose
file somebody edits at the end, and until this existed nothing read them: the publish
route asked GitHub to generate notes from the merged pull requests, so an entry
written for an operator reached the directory and stopped there.

This is the other end of that format. It takes the fragments a release carries, refuses
one it cannot read rather than dropping it, and writes the notes with the entries that
change what happens to already-synced watch state FIRST, under their own heading. That
order is the whole reason the marking exists: the reader this changelog is written for
is deciding whether to upgrade a plugin that writes into their users' data, and the
entry they need is the one that says their existing history will be treated
differently. An assembler that emitted the fragments in file order would put that entry
wherever its ordinal happened to fall.

What it refuses is what would make the notes wrong, and no more. Whether a fragment is
well formed in every respect is held by `ChangelogFragmentTests` in the suite, against
the same document; a second full copy of those rules here would be a format with two
definitions. The three below are the ones this file cannot proceed past:

  no-marking          `Existing-Data` is absent or is not one of the two words, so
                      which section the entry leads in is not decidable.
  change-without-an-effect
                      the entry says it reaches existing data and does not say what it
                      does to it, which is the sentence the leading section exists for.
  no-entry-text       there is nothing under the header, so the release note would be a
                      heading with no entry beneath it.

The field names are read out of the table in `docs/changelog.md` rather than written
here, for the same reason the guard in the suite reads them there.

Usage:

    assemble-release-notes.py [FRAGMENT ...]

With no arguments it takes every file in `changelog.d/`. Notes go to standard output.
Exit 0 when the notes were written, 1 when a fragment was refused or there was nothing
to assemble.
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DIRECTORY = ROOT / "changelog.d"
DOCUMENT = ROOT / "docs" / "changelog.md"
FIELD_SECTION = "## The fields"

# The two values `Existing-Data` takes. `docs/changelog.md` states them and the guard in
# the suite carries them as constants; a fact in that suite holds these two lines to it.
CHANGED = "changed"
UNCHANGED = "unchanged"

HEADER_LINE = re.compile(r"^(?P<name>[A-Za-z][A-Za-z-]*):[ \t]*(?P<value>.*)$")
FRAGMENT_NAME = re.compile(r"^(?P<ordinal>[0-9]{4})-(?P<slug>[a-z0-9]+(?:-[a-z0-9]+)*)\.md$")


def fields_the_document_names():
    """Read the field names out of the table in `docs/changelog.md`.

    Returns each field name against whether the document says it is required on every
    entry. A table this file could not find is a refusal rather than an empty set: an
    empty field set would make every fragment read as carrying nothing.
    """
    text = DOCUMENT.read_text(encoding="utf-8")
    start = text.find(FIELD_SECTION)
    if start < 0:
        sys.exit(
            "assemble-release-notes: {} carries no section headed \"{}\", so there is "
            "nothing to read the field set out of.".format(DOCUMENT, FIELD_SECTION)
        )
    rest = text[start + len(FIELD_SECTION):]
    end = rest.find("\n## ")
    section = rest if end < 0 else rest[:end]

    fields = {}
    for line in section.splitlines():
        line = line.strip()
        if not line.startswith("|") or "---" in line:
            continue
        cells = [cell.strip() for cell in line.strip("|").split("|")]
        if len(cells) < 3 or cells[0] == "field":
            continue
        name = re.search(r"`([A-Za-z][A-Za-z-]*)`", cells[0])
        if name:
            fields[name.group(1)] = cells[1] == "always"

    if not fields:
        sys.exit(
            "assemble-release-notes: the field table under \"{}\" in {} has no rows "
            "this reader recognised.".format(FIELD_SECTION, DOCUMENT)
        )
    return fields


def read_fragment(path, known):
    """Split one fragment into its header and its entry text."""
    lines = path.read_text(encoding="utf-8").replace("\r\n", "\n").split("\n")
    header = {}
    last = None
    index = 0

    for index, line in enumerate(lines):
        if line == "":
            break
        if last is not None and (line.startswith(" ") or line.startswith("\t")):
            header[last] = header[last] + " " + line.strip()
            continue
        match = HEADER_LINE.match(line)
        if not match:
            # Not a field. The guard in the suite refuses it; here it is passed over so
            # that a header this reader cannot parse never becomes an entry line.
            last = None
            continue
        name = match.group("name")
        if name in header or name not in known:
            continue
        header[name] = match.group("value").strip()
        last = name

    return header, "\n".join(lines[index:]).strip("\n")


def title_of(path):
    """The heading for an entry, derived from its own file name.

    A fragment carries no title field and one is not owed: the slug in the name is
    already the entry's short description, and it is the half of the name a writer
    chooses. Deriving the heading from it means the heading and the file cannot come
    apart.
    """
    name = FRAGMENT_NAME.match(path.name)
    if not name:
        return path.name
    words = name.group("slug").replace("-", " ")
    return words[:1].upper() + words[1:]


def ordinal_of(path):
    """The ordinal the fragment sorts on inside its own section."""
    name = FRAGMENT_NAME.match(path.name)
    return (name.group("ordinal"), path.name) if name else ("9999", path.name)


def refusals(path, header, entry):
    """What about one fragment stops it from becoming a release note."""
    found = []
    marking = header.get("Existing-Data")

    if marking not in (CHANGED, UNCHANGED):
        found.append((
            "no-marking",
            "Existing-Data reads \"{}\", and the two values are {} and {}, so which "
            "section this entry leads in is undecided".format(marking, UNCHANGED, CHANGED),
        ))
    elif marking == CHANGED and not header.get("Effect"):
        found.append((
            "change-without-an-effect",
            "the entry is marked as reaching data that has already been synced and says "
            "nothing about what it does to it, which is the sentence the leading section "
            "of the notes exists to carry",
        ))

    if not entry.strip():
        found.append((
            "no-entry-text",
            "there is nothing under the header, so the note would be a heading with "
            "nothing beneath it",
        ))

    return [(path, rule, detail) for rule, detail in found]


def render(entries):
    """Write the notes, the entries that reach existing data first."""
    changed = [item for item in entries if item[1].get("Existing-Data") == CHANGED]
    unchanged = [item for item in entries if item[1].get("Existing-Data") == UNCHANGED]
    out = []

    out.append("## Before you upgrade")
    out.append("")
    if changed:
        out.append(
            "These changes reach watch state that has already been synced between "
            "servers. Each one says what it means for the history that is already there."
        )
        out.append("")
        for path, header, entry in changed:
            out.append("### " + title_of(path))
            out.append("")
            # Read with a default rather than by subscript on purpose. A missing
            # `Effect` is refused above, and the refusal is what has to be the thing
            # that stops it: a subscript here would crash instead, so deleting the
            # refusal would still redden a run and the proof would not be measuring
            # the refusal at all.
            out.append("**What it means for data already synced.** " + header.get("Effect", ""))
            out.append("")
            out.append(entry)
            out.append("")
            out.append("(" + header.get("Issue", "no issue named") + ")")
            out.append("")
    else:
        out.append(
            "Nothing in this release changes what happens to watch state that has "
            "already been synced between servers."
        )
        out.append("")

    if unchanged:
        out.append("## Everything else")
        out.append("")
        for path, header, entry in unchanged:
            out.append("### " + title_of(path))
            out.append("")
            out.append(entry)
            out.append("")
            out.append("(" + header.get("Issue", "no issue named") + ")")
            out.append("")

    return "\n".join(out).rstrip("\n") + "\n"


def main():
    """Assemble the notes for the fragments named on the command line."""
    known = fields_the_document_names()

    if len(sys.argv) > 1:
        paths = [pathlib.Path(argument) for argument in sys.argv[1:]]
    elif DIRECTORY.is_dir():
        paths = sorted(DIRECTORY.iterdir())
    else:
        paths = []

    paths = [path for path in paths if path.is_file()]

    if not paths:
        print(
            "assemble-release-notes: there is no changelog fragment to assemble, so "
            "this release would be published with nothing written for the operator "
            "deciding whether to take it. Write one under changelog.d/.",
            file=sys.stderr,
        )
        return 1

    entries = []
    found = []
    for path in sorted(paths, key=ordinal_of):
        header, entry = read_fragment(path, known)
        found.extend(refusals(path, header, entry))
        entries.append((path, header, entry))

    if found:
        for path, rule, detail in found:
            print("FAIL {}: {}: {}".format(rule, path, detail), file=sys.stderr)
        return 1

    sys.stdout.write(render(entries))
    return 0


if __name__ == "__main__":
    sys.exit(main())
