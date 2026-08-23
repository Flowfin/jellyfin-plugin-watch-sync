#!/usr/bin/env python3
"""Reads a coverage run and decides whether it cleared the floors it declares.

The percentage is not the artefact. What this prints for the critical areas is
the list of lines and branches nothing executed, because that list is what
somebody acts on, and a number is what somebody argues with.

The floors are in Jellyfin.Plugin.WatchSync.Tests/Coverage/floors.txt rather than
here, so a clone gets the same verdict this gets and the register is the one
place a floor is written. This file decides; it declares nothing.

It refuses in both directions. A component the register does not name is refused,
because a component nobody entered is one the floors say nothing about while the
run reports a number that reads as if they did. A component entered as measured
and carrying no executable line in the report is refused, because a measurement
that stopped looks exactly like one that passed. A component entered as awaited
and present in the report is refused, because the row is the claim that nothing
is there to measure.

    python3 .github/check-coverage.py REGISTER COVERAGE-ROOT TARGET [TARGET ...]
"""

import glob
import os
import sys
import xml.etree.ElementTree as ElementTree

STATES = ("critical", "ordinary", "awaited", "empty")


def fail(message):
    print(f"::error::{message}")
    return True


def register(path):
    """The floors, read as data.

    A row this cannot read fails rather than being skipped, which is the
    difference between a register and a comment.

    The row is split before it is trimmed. A row whose reason is deleted keeps the
    separator in front of the space that is left, so a line trimmed first arrives
    as four fields and is refused as malformed rather than as a row with no
    reason, and the refusal names the wrong fault.
    """
    rows = {}

    try:
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
    except FileNotFoundError:
        print(f"::error::No floor register at {path}, so there is nothing to judge against.")
        sys.exit(1)

    for row in lines:
        row = row.rstrip(chr(13))

        if not row.strip() or row.strip().startswith("#"):
            continue

        fields = row.split(" :: ")

        if len(fields) != 5:
            print(f"::error::A row of {path} has {len(fields)} fields rather than five: {row.strip()}")
            sys.exit(1)

        component, state, line_floor, branch_floor, reason = (field.strip() for field in fields)

        if state not in STATES:
            print(f"::error::{component} is entered as {state}, which is not a state this register has.")
            sys.exit(1)

        if not reason:
            print(f"::error::{component} is entered as {state} and gives no reason.")
            sys.exit(1)

        rows[component] = (state, int(line_floor), int(branch_floor))

    if not rows:
        print(f"::error::{path} carries no row, so nothing declares a floor.")
        sys.exit(1)

    return rows


def report(root, target):
    """The one cobertura report a target produced, or nothing where the run made none."""
    pattern = os.path.join(root, target, "**", "coverage.cobertura.xml")
    found = sorted(glob.glob(pattern, recursive=True))

    if len(found) != 1:
        return None, f"{target} produced {len(found)} coverage reports under {root} rather than one."

    try:
        return ElementTree.parse(found[0]).getroot(), found[0]
    except ElementTree.ParseError as error:
        return None, f"The report for {target} does not parse: {error}."


def measure(document):
    """Per component: lines covered and valid, branches covered and valid, and what missed.

    The lines are taken off the class rather than off everything under it. A
    cobertura report writes each line twice, once inside the method that holds it
    and once in the block the class carries, so a walk that descends into the
    methods counts every line and every branch of this project twice and reports a
    percentage that looks exactly right while every count behind it is doubled.
    """
    tally = {}

    for element in document.iter("class"):
        filename = element.get("filename", "").replace(chr(92), "/")
        component = filename.split("/")[0] if "/" in filename else "."
        counts = tally.setdefault(component, {"lines": [0, 0], "branches": [0, 0], "missed": []})
        block = element.find("lines")

        for line in [] if block is None else block.findall("line"):
            number = line.get("number", "0")
            hits = int(line.get("hits", "0"))
            counts["lines"][1] += 1

            if hits > 0:
                counts["lines"][0] += 1
            else:
                counts["missed"].append(f"{filename}:{number} no fact executes this line")

            coverage = line.get("condition-coverage")

            if not coverage or "(" not in coverage:
                continue

            taken, total = coverage.split("(")[1].rstrip(")").split("/")
            counts["branches"][0] += int(taken)
            counts["branches"][1] += int(total)

            if taken != total:
                counts["missed"].append(f"{filename}:{number} {taken} of {total} branches taken")

    return tally


def percentage(covered, valid):
    """A component with nothing to cover has met its floor rather than missed it."""
    return 100.0 if valid == 0 else 100.0 * covered / valid


def judge(target, path, tally, rows):
    """Prints what one target measured and answers whether anything in it fails the run."""
    print(f"## {target}, read from {path}")

    red = False

    for component in sorted(set(tally) | set(rows)):
        entry = rows.get(component)
        counts = tally.get(component)

        if entry is None:
            red = fail(
                f"{target}: {component} is in the coverage report and in no row of the register, "
                "so no floor is declared for it while the run reports a number that reads as if "
                "one were."
            )
            continue

        state, line_floor, branch_floor = entry

        if state == "awaited":
            if counts is not None:
                red = fail(
                    f"{target}: {component} is entered as awaited and the run measured it. Move the "
                    "row, which is the moment somebody checks the area landed under the name the "
                    "register was already pointing at."
                )

            continue

        if counts is None or counts["lines"][1] == 0:
            if state == "empty":
                print(f"  {component:14s} entered empty, and the run found nothing executable in it.")
            else:
                red = fail(
                    f"{target}: {component} is entered as {state} and the run measured no executable "
                    "line in it. A measurement that stopped looks exactly like one that passed."
                )

            continue

        if state == "empty":
            red = fail(
                f"{target}: {component} is entered as empty and the run measured "
                f"{counts['lines'][1]} executable lines in it. The row has to say ordinary or "
                "critical, and whoever moves it decides which floor the new code is held to."
            )
            continue

        lines = percentage(*counts["lines"])
        branches = percentage(*counts["branches"])

        print(
            f"  {component:14s} {state:8s} "
            f"lines {lines:6.2f} per cent ({counts['lines'][0]}/{counts['lines'][1]}, floor {line_floor}) "
            f"branches {branches:6.2f} per cent ({counts['branches'][0]}/{counts['branches'][1]}, floor {branch_floor})"
        )

        if lines + 1e-9 < line_floor:
            red = fail(
                f"{target}: {component} covers {lines:.2f} per cent of its lines and its floor is "
                f"{line_floor}."
            )

        if branches + 1e-9 < branch_floor:
            red = fail(
                f"{target}: {component} covers {branches:.2f} per cent of its branches and its floor "
                f"is {branch_floor}."
            )

    critical = [name for name, entry in rows.items() if entry[0] == "critical"]
    findings = [
        line
        for name in sorted(critical)
        for line in tally.get(name, {}).get("missed", [])
    ]

    print()

    if findings:
        print(f"Nothing executes these, in the areas the register calls critical. {len(findings)} of them:")

        for line in findings:
            print(f"  {line}")
    else:
        print("Every line and every branch of the critical areas was executed.")

    print()

    return red


def main(argv):
    if len(argv) < 4:
        print(
            "::error::Expected the register, the coverage root and at least one target. "
            f"Got {len(argv) - 1} arguments."
        )
        return 1

    rows = register(argv[1])
    root = argv[2]
    red = False

    for target in argv[3:]:
        document, path = report(root, target)

        if document is None:
            red = fail(path)
            continue

        red = judge(target, path, measure(document), rows) or red

    if red:
        print("::error::The coverage run did not clear the floors this repository declares.")
        return 1

    print("Every component cleared the floor its row declares, on every target.")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
