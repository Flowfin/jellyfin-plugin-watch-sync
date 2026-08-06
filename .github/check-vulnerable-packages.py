#!/usr/bin/env python3
"""Refuse a vulnerable package in the resolved dependency graph.

The dependency review workflow reads what a pull request changes. An advisory
published overnight against a dependency nobody touched changes nothing, so it
reports nothing, and the graph stays vulnerable with every check green. This
guard reads the whole resolved graph instead of a diff, which is why it runs on
a schedule rather than on a pull request.

It reads the output of

    dotnet list <solution> package --vulnerable --include-transitive --format json

on standard input. Two things about that command decide the shape of this file.

The verdict is not the exit code. That command reports a high severity advisory
and still exits 0, so a step that ran it and trusted its status would be green on
a vulnerable graph, which is the failure this guard exists to prevent and is
worse than not running it at all.

The verdict is not the console text either. `dotnet` writes that text in the
operating system's display language, so a scan that grepped it for an English
phrase would pass on any machine not set to English, silently and for a reason
nothing on the run would show. The JSON is the same on every machine, and it is
what this reads.

A project with no vulnerable package carries only its path. A project with one
carries a `frameworks` list, and each entry carries `topLevelPackages` and
`transitivePackages` whose members carry a `vulnerabilities` list. Both package
lists are read, because a transitive advisory is the case a pull request review
is least likely to have seen.

`.github/dependency-acceptances.txt` is where a vulnerability that is known and
being lived with is declared. An acceptance carries a reason and a date it stops
working, so it is a decision with an end rather than a permanent hole. The
register fails closed in both directions: an acceptance past its date is refused
like the vulnerability it covers, and an acceptance covering nothing in the
current graph is refused as dangling, so it is removed by the change that
retires it rather than left to rot.

Exit 0 when the graph carries no vulnerability that is not covered by a live
acceptance and every acceptance covers something. Exit 1 otherwise, and exit 1
again when standard input is not readable as the expected JSON or names no
project at all, because an empty project list would otherwise pass a graph
nothing had actually looked at.
"""

import argparse
import datetime
import json
import pathlib
import sys

SEPARATOR = " :: "
FIELDS = 3


def refuse(message):
    """Print a refusal and exit non-zero."""
    print("check-vulnerable-packages: " + message)
    sys.exit(1)


def read_acceptances(path, today):
    """Return {package id: (expiry, reason)} from the register, or refuse it."""
    accepted = {}
    if not path.exists():
        return accepted

    for number, raw in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not raw.strip() or raw.strip().startswith("#"):
            continue

        # Split the line as written rather than after stripping it. An entry
        # whose reason was never typed ends in a separator and a space, and
        # stripping first turns that into a short line, so it would be refused
        # for the wrong reason and the message would name the wrong repair.
        parts = raw.split(SEPARATOR)
        if len(parts) != FIELDS:
            refuse(
                "%s line %d declares %d field(s) and an acceptance takes %d, "
                "separated by '%s': package id, expiry as YYYY-MM-DD, reason."
                % (path, number, len(parts), FIELDS, SEPARATOR)
            )

        package, expiry, reason = (part.strip() for part in parts)
        if not package:
            refuse("%s line %d names no package." % (path, number))
        if not reason:
            refuse(
                "%s line %d carries no reason. An acceptance without one is "
                "indistinguishable from an oversight." % (path, number)
            )

        try:
            stops = datetime.date.fromisoformat(expiry)
        except ValueError:
            refuse(
                "%s line %d carries '%s' where an expiry as YYYY-MM-DD belongs. "
                "An acceptance with no end is a permanent hole."
                % (path, number, expiry)
            )

        if stops < today:
            refuse(
                "%s line %d accepts %s until %s, and that date has passed. "
                "Raise the dependency or take the decision again with a new "
                "date and a reason that is still true."
                % (path, number, package, expiry)
            )

        accepted[package.casefold()] = (expiry, reason)

    return accepted


def vulnerable_packages(report):
    """Yield (project path, package id, resolved version, severity, advisory)."""
    projects = report.get("projects")
    if not isinstance(projects, list) or not projects:
        refuse(
            "the report names no project. An empty project list passes every "
            "graph, including one nothing looked at, so it is refused rather "
            "than read as a clean scan."
        )

    for project in projects:
        path = project.get("path", "<unnamed project>")
        for framework in project.get("frameworks") or []:
            for key in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(key) or []:
                    for finding in package.get("vulnerabilities") or []:
                        yield (
                            path,
                            package.get("id", "<unnamed package>"),
                            package.get("resolvedVersion", "<unknown version>"),
                            finding.get("severity", "<unstated severity>"),
                            finding.get("advisoryurl", "<no advisory address>"),
                        )


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--acceptances",
        default=".github/dependency-acceptances.txt",
        help="the register of accepted vulnerabilities",
    )
    parser.add_argument(
        "--today",
        default=None,
        help=(
            "the date an acceptance is measured against, as YYYY-MM-DD. "
            "Defaults to the day the scan runs; it is settable so that a "
            "demonstration of this guard fixes a date instead of depending on "
            "when somebody runs it."
        ),
    )
    arguments = parser.parse_args()

    if arguments.today is None:
        today = datetime.date.today()
    else:
        try:
            today = datetime.date.fromisoformat(arguments.today)
        except ValueError:
            refuse("--today takes a date as YYYY-MM-DD, not '%s'." % arguments.today)

    text = sys.stdin.read()
    if not text.strip():
        refuse(
            "standard input was empty. The scan writes a report even when it "
            "finds nothing, so no report means the scan did not run."
        )

    try:
        report = json.loads(text)
    except json.JSONDecodeError as error:
        refuse(
            "standard input is not the JSON report this reads (%s). The scan "
            "is run with --format json; its console text is written in the "
            "display language of the machine and is not read here." % error
        )

    accepted = read_acceptances(pathlib.Path(arguments.acceptances), today)

    findings = list(vulnerable_packages(report))
    covered = set()
    refused = []
    for path, package, version, severity, advisory in findings:
        key = package.casefold()
        if key in accepted:
            covered.add(key)
            print(
                "accepted until %s: %s %s, %s severity, %s\n    %s\n    in %s"
                % (
                    accepted[key][0],
                    package,
                    version,
                    severity,
                    advisory,
                    accepted[key][1],
                    path,
                )
            )
        else:
            refused.append(
                "%s %s, %s severity, %s\n    in %s"
                % (package, version, severity, advisory, path)
            )

    dangling = sorted(set(accepted) - covered)

    projects = len(report.get("projects", []))
    print(
        "examined %d project(s), %d vulnerable package finding(s), "
        "%d acceptance(s) in the register."
        % (projects, len(findings), len(accepted))
    )

    if dangling:
        refuse(
            "the register accepts what the graph no longer carries: %s. An "
            "acceptance is removed by the change that retires it, so a "
            "dangling one is refused rather than left to rot."
            % ", ".join(dangling)
        )

    if refused:
        refuse(
            "%d vulnerable package(s) with no acceptance:\n%s"
            % (len(refused), "\n".join(refused))
        )

    print("check-vulnerable-packages: no unaccepted vulnerability in the graph.")


if __name__ == "__main__":
    main()
