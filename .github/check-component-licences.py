#!/usr/bin/env python3
"""Refuse a component this repository may not distribute under its own licence.

The component inventory records what is in the build. It does not ask what any of
it is licensed under, so a package arriving under terms this repository cannot
ship under lands in the graph, is written into the inventory, and is published
beside the archive as a description of a distribution nobody was allowed to make.
That is found by a person reading a bill of materials afterwards, or not at all.

It reads the CycloneDX inventory on standard input, the one
`.github/workflows/package.yaml` writes off the locked restore, so what it judges
is the graph `packages.lock.json` holds rather than whatever a feed served.

## What the outbound licence is, and why it is read rather than typed

The set of licences that may come in is a function of the one that goes out, so
this reads `LICENSE` instead of carrying an answer. The file says which licence
this is; a repository relicensed to something else gets a refusal here rather
than the compatibility set of a licence it no longer ships under.

## What a component's licence is

The inventory states an SPDX identifier for most components and nothing at all
for some, because a NuGet package may carry its licence as a file inside the
archive or as a link, and neither reaches the identifier field. An unstated
licence is not read as permission. `.github/component-licences.txt` is where a
licence somebody went and read is declared, keyed on the exact name and version,
with the place it was read from beside it.

That register fails closed in both directions. A component with no stated
identifier and no declaration is refused, and a declaration covering nothing in
the inventory is refused as dangling, so a dependency bump takes the reading with
it instead of leaving a two-year-old answer standing over bytes nobody looked at.

A declaration is not an acceptance. An incompatible licence cannot be declared
away here, because the repair for one is removing the component rather than
recording that it is there.

## What this cannot see

The inventory is the build closure and the archive is one assembly, so a package
that only ever supplied a reference assembly or a compile-time analyser is
judged here as though it shipped. That is the safe direction and it is not the
same question as what the archive contains; attaching the inventory to a release
so the two can be compared is the rest of #118.

Whether a licence identifier is the one the component is really under is a
reading of a licence file, which is what the register records and no run makes.

Exit 0 when every component resolves to a licence this repository may ship under
and every declaration covers something. Exit 1 otherwise, and exit 1 when
standard input is not the inventory, or names no component at all, because an
empty inventory would otherwise pass as a clean graph.
"""

import argparse
import json
import pathlib
import sys

SEPARATOR = " :: "
FIELDS = 3

# What may come in, given what goes out, with the reason each one may. The set is
# a property of the outbound licence rather than a preference, which is why the
# outbound licence is read before it is used and a file that is not the one below
# is refused instead of falling through to this table.
GPL3_INBOUND = {
    "GPL-3.0-only": "the licence this repository ships under",
    "GPL-3.0-or-later": "the licence this repository ships under, at a later version",
    "LGPL-3.0-only": "carries its own permission to be taken up under the GPL, version 3",
    "LGPL-3.0-or-later": "carries its own permission to be taken up under the GPL, version 3",
    "MIT": "imposes nothing the GPL, version 3, does not already impose",
    "ISC": "imposes nothing the GPL, version 3, does not already impose",
    "0BSD": "imposes nothing the GPL, version 3, does not already impose",
    "BSD-2-Clause": "imposes nothing the GPL, version 3, does not already impose",
    "BSD-3-Clause": "imposes nothing the GPL, version 3, does not already impose",
    "Apache-2.0": "its patent and notice terms are ones the GPL, version 3, accommodates",
}

# The two lines that say which licence a file is. Both are required, because the
# first alone is carried by every version of this licence and the second is what
# separates version 3 from the ones whose inbound set differs.
GPL3_HEADING = "GNU GENERAL PUBLIC LICENSE"
GPL3_VERSION = "Version 3, 29 June 2007"
SHIPS_UNDER = "GPL-3.0-only"


def refuse(message):
    """Print a refusal and exit non-zero."""
    print("check-component-licences: " + message)
    sys.exit(1)


def read_outbound_licence(path):
    """Return the SPDX identifier the licence file carries, or refuse it."""
    if not path.exists():
        refuse(
            "%s does not exist. The set of licences a component may carry is a "
            "function of the one this repository ships under, so there is "
            "nothing to judge a component against without it." % path
        )

    text = path.read_text(encoding="utf-8", errors="replace")
    if GPL3_HEADING not in text or GPL3_VERSION not in text:
        refuse(
            "%s is not the licence this check knows the inbound set for. It "
            "expects the text of the GNU General Public License, version 3, "
            "naming '%s' and '%s'. A repository that has been relicensed needs "
            "the inbound set decided again rather than inherited."
            % (path, GPL3_HEADING, GPL3_VERSION)
        )

    return SHIPS_UNDER


def read_declarations(path):
    """Return {(name, version): (identifier, source)} from the register."""
    declared = {}
    if not path.exists():
        return declared

    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not raw.strip() or raw.strip().startswith("#"):
            continue

        # Split as written rather than after stripping, for the reason the
        # acceptance register beside this one gives: an entry whose last field
        # was never typed ends in a separator, and stripping first turns that
        # into a short line refused for the wrong reason.
        parts = raw.split(SEPARATOR)
        if len(parts) != FIELDS:
            refuse(
                "%s line %d declares %d field(s) and a declaration takes %d, "
                "separated by '%s': name@version, SPDX identifier, where the "
                "licence was read." % (path, number, len(parts), FIELDS, SEPARATOR)
            )

        component, identifier, source = (part.strip() for part in parts)
        if "@" not in component:
            refuse(
                "%s line %d names '%s', and a declaration is keyed on the exact "
                "name and version as name@version. A key without a version "
                "would carry a reading of one release onto every later one."
                % (path, number, component)
            )
        if not identifier:
            refuse("%s line %d names no SPDX identifier." % (path, number))
        if not source:
            refuse(
                "%s line %d says nowhere the licence was read. A declaration "
                "without one cannot be checked by anybody but its author."
                % (path, number)
            )
        if identifier not in GPL3_INBOUND:
            refuse(
                "%s line %d declares %s under %s, which is not a licence this "
                "repository may ship under. A declaration records a reading; it "
                "is not a way to accept an incompatible licence."
                % (path, number, component, identifier)
            )

        name, _, version = component.rpartition("@")
        declared[(name.casefold(), version.casefold())] = (identifier, source)

    return declared


def stated_identifier(component):
    """Return (identifier, reason it is absent) for one inventory component."""
    entries = component.get("licenses") or []
    identifiers = set()
    unstated = "the inventory states no SPDX identifier for it"

    for entry in entries:
        if not isinstance(entry, dict):
            continue
        if "expression" in entry:
            expression = str(entry["expression"]).strip()
            # A bare identifier is an expression of one term. Anything carrying
            # an operator is a choice or a combination, and which arm applies is
            # a decision rather than a reading, so it is sent to the register.
            if expression and all(
                character.isalnum() or character in ".+-" for character in expression
            ):
                identifiers.add(expression)
            else:
                unstated = "the inventory carries the expression %r" % expression
            continue

        licence = entry.get("license")
        if not isinstance(licence, dict):
            continue
        if licence.get("id"):
            identifiers.add(str(licence["id"]).strip())
        elif licence.get("url"):
            unstated = "the inventory carries only the address %s" % licence["url"]
        elif licence.get("name"):
            unstated = "the inventory carries only the name %r" % licence["name"]

    if len(identifiers) == 1:
        return identifiers.pop(), None
    if len(identifiers) > 1:
        return None, "the inventory states %d identifiers for it (%s)" % (
            len(identifiers),
            ", ".join(sorted(identifiers)),
        )
    return None, unstated


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--declarations",
        default=".github/component-licences.txt",
        help="the register of licences read out of a component itself",
    )
    parser.add_argument(
        "--licence",
        default="LICENSE",
        help="the licence this repository ships under, read rather than assumed",
    )
    arguments = parser.parse_args()

    ships_under = read_outbound_licence(pathlib.Path(arguments.licence))
    declared = read_declarations(pathlib.Path(arguments.declarations))

    text = sys.stdin.read()
    if not text.strip():
        refuse(
            "standard input was empty. The packaging run writes an inventory "
            "whatever the graph holds, so no inventory means it did not run."
        )

    try:
        inventory = json.loads(text)
    except json.JSONDecodeError as error:
        refuse(
            "standard input is not the CycloneDX inventory this reads (%s). It "
            "is the file the packaging run writes with --output-format Json."
            % error
        )

    components = inventory.get("components")
    if not isinstance(components, list) or not components:
        refuse(
            "the inventory names no component. An empty list passes every "
            "graph, including one nothing looked at, so it is refused rather "
            "than read as a graph with nothing in it."
        )

    covered = set()
    refused = []
    for component in components:
        name = str(component.get("name", "<unnamed component>"))
        version = str(component.get("version", "<unstated version>"))
        identifier, absent = stated_identifier(component)
        key = (name.casefold(), version.casefold())

        if identifier is None:
            if key in declared:
                covered.add(key)
                identifier, source = declared[key]
                print(
                    "%s %s: %s, declared, read from %s" % (name, version, identifier, source)
                )
            else:
                print("%s %s: unresolved" % (name, version))
                refused.append(
                    "%s %s carries no licence this run could read: %s, and "
                    "nothing declares it. Read the licence out of the package "
                    "and add a line to %s."
                    % (name, version, absent, arguments.declarations)
                )
                continue
        else:
            print("%s %s: %s, stated by the inventory" % (name, version, identifier))

        if identifier not in GPL3_INBOUND:
            refused.append(
                "%s %s is under %s, and this repository ships under %s. There "
                "is no acceptance for this: the repair is removing the "
                "component." % (name, version, identifier, ships_under)
            )

    dangling = sorted(set(declared) - covered)

    print(
        "examined %d component(s) against %s, %d declaration(s) in the register."
        % (len(components), ships_under, len(declared))
    )

    if dangling:
        refuse(
            "the register declares a licence for what the inventory no longer "
            "carries: %s. A reading belongs to the exact version it was taken "
            "at, so it is removed by the change that raises the dependency."
            % ", ".join("%s@%s" % entry for entry in dangling)
        )

    if refused:
        refuse(
            "%d component(s) this repository may not ship as it stands:\n%s"
            % (len(refused), "\n".join(refused))
        )

    print(
        "check-component-licences: every component is under a licence "
        "%s may carry." % ships_under
    )


if __name__ == "__main__":
    main()
