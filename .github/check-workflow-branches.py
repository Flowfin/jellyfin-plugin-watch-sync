#!/usr/bin/env python3
"""Refuse a workflow trigger that names a branch this repository does not have.

A push or pull_request trigger naming a branch that does not exist never fires.
Nothing reports that: the workflow is present, its runs list is empty, and an
empty runs list is what a workflow that has never found anything to complain
about also looks like. Two of the workflows here were carried from a repository
whose default branch is `main` and sat silent for exactly that reason.

The set of existing branches is read from standard input, one name per line, so
the same file runs against the remote in CI and against a clone locally. A
pattern containing a glob character is skipped: `**` is not a claim that a
branch exists.

Exit 0 when every literal branch named by a trigger exists, 1 otherwise, and 1
again if the branch list on standard input is empty, because an empty set would
otherwise pass everything that names nothing and fail everything else for the
wrong reason.
"""

import sys
import pathlib

try:
    import yaml
except ImportError:
    sys.exit(
        "check-workflow-branches: PyYAML is not importable. This guard parses "
        "the workflow YAML rather than grepping it, so it fails closed here "
        "instead of scanning with a weaker reader."
    )

GLOB = set("*?[]!+")


def branches_of(trigger):
    """Yield the branch patterns a single trigger declares."""
    if not isinstance(trigger, dict):
        return
    for key in ("branches", "branches-ignore"):
        value = trigger.get(key)
        if isinstance(value, str):
            yield value
        elif isinstance(value, list):
            for item in value:
                if isinstance(item, str):
                    yield item


def main():
    existing = {line.strip() for line in sys.stdin if line.strip()}
    if not existing:
        sys.exit("check-workflow-branches: no branches on standard input.")

    root = pathlib.Path(".github/workflows")
    paths = sorted(p for p in root.iterdir() if p.suffix in (".yml", ".yaml"))
    if not paths:
        sys.exit("check-workflow-branches: no workflow files found.")

    failures = []
    checked = 0
    for path in paths:
        with path.open(encoding="utf-8") as handle:
            document = yaml.safe_load(handle)
        if not isinstance(document, dict):
            failures.append("{}: does not parse as a mapping".format(path))
            continue
        # `on` is the YAML 1.1 boolean True once PyYAML has read it.
        triggers = document.get("on", document.get(True))
        if not isinstance(triggers, dict):
            continue
        for event, trigger in triggers.items():
            for pattern in branches_of(trigger):
                if GLOB & set(pattern):
                    continue
                checked += 1
                if pattern not in existing:
                    failures.append(
                        "{}: {} names branch '{}', which does not exist".format(
                            path, event, pattern
                        )
                    )

    for failure in failures:
        print("FAIL {}".format(failure))
    print(
        "checked {} literal branch name(s) in {} workflow file(s) against "
        "{} existing branch(es)".format(checked, len(paths), len(existing))
    )
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
