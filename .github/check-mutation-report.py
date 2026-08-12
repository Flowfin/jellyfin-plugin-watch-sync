#!/usr/bin/env python3
"""Reads a Stryker report and decides whether the run happened at all.

The score is not the verdict. A threshold on a merge is a number people tune
rather than a fault they fix, so this exits 0 on a low score and says what the
score was. What it refuses is the instrument having stopped: a missing report, a
report that does not parse, and a report carrying no mutant. Those three look
identical to a green run from the outside, which is how the same instrument
stopped working unnoticed on the board this gate comes from.

It also prints the survivors, because the list is the artefact somebody acts on
and the percentage is not.

    python3 .github/check-mutation-report.py StrykerOutput/*/reports/mutation-report.json
"""

import json
import sys

RUN_STATUSES = ("Killed", "Survived", "Timeout", "NoCoverage", "RuntimeError")
UNKILLED_STATUSES = ("Survived", "NoCoverage")


def fail(message):
    print(f"::error::{message}")
    return 1


def main(argv):
    if len(argv) != 2:
        return fail(
            "Expected exactly one argument, the path of the mutation report. "
            f"Got {len(argv) - 1}."
        )

    path = argv[1]

    try:
        with open(path, encoding="utf-8") as handle:
            report = json.load(handle)
    except FileNotFoundError:
        return fail(
            f"No mutation report at {path}. The run produced nothing, which is "
            "not the same as a run that found nothing."
        )
    except json.JSONDecodeError as error:
        return fail(f"The mutation report at {path} does not parse: {error}.")

    files = report.get("files")

    if not isinstance(files, dict):
        return fail(
            f"The report at {path} carries no file map, so it is not a report "
            "this can read."
        )

    mutants = [
        (path_in_report, mutant)
        for path_in_report, entry in files.items()
        for mutant in entry.get("mutants", [])
    ]

    tested = [pair for pair in mutants if pair[1].get("status") in RUN_STATUSES]

    if not tested:
        return fail(
            f"The report at {path} carries no tested mutant. Every mutant was "
            "skipped, or none was created, and either way nothing was measured."
        )

    unkilled = [pair for pair in tested if pair[1].get("status") in UNKILLED_STATUSES]
    killed = len(tested) - len(unkilled)
    score = 100.0 * killed / len(tested)

    print(f"{len(tested)} mutants tested, {killed} killed, {len(unkilled)} left alive.")
    print(f"Mutation score {score:.2f} per cent. The score is reported and gates nothing.")

    if unkilled:
        print()
        print("Left alive, which is the list to triage:")

        for path_in_report, mutant in sorted(
            unkilled,
            key=lambda pair: (pair[0], pair[1].get("location", {}).get("start", {}).get("line", 0)),
        ):
            line = mutant.get("location", {}).get("start", {}).get("line", 0)
            print(
                f"  {mutant.get('status')} {path_in_report}:{line} "
                f"{mutant.get('mutatorName')}"
            )

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
