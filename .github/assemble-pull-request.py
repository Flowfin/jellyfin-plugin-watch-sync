#!/usr/bin/env python3
"""Write the document that describes one pull request, for the checker beside it.

Splitting the reading from the verdict is what lets the verdict be proven on a
fixture: check-pull-request.py never talks to a network and never runs a
command, so a document written by hand is indistinguishable to it from one
written here. This file is the half that talks to the server, and it has no
opinions.

    python3 .github/assemble-pull-request.py OWNER/REPO NUMBER > document.json

Everything comes from the API rather than from a checkout, so no clone depth
can shrink what is read, and the title and the body are never expanded into a
shell by a template engine on the way. `gh` supplies the authentication.
"""

import base64
import json
import subprocess
import sys


def gh(*arguments):
    """Run gh and return its standard output, or None when it refuses."""
    finished = subprocess.run(
        ("gh",) + arguments,
        capture_output=True,
        text=True,
        check=False,
    )
    if finished.returncode != 0:
        return None
    return finished.stdout


def gh_or_die(*arguments):
    """Run gh and stop when it refuses, because a partial document is worse."""
    output = gh(*arguments)
    if output is None:
        sys.exit(
            "assemble-pull-request: 'gh {}' failed, and a document assembled "
            "from what did answer would be judged as if it were whole.".format(
                " ".join(arguments)
            )
        )
    return output


def objects(text):
    """Parse gh's one-JSON-value-per-line output into a list."""
    return [json.loads(line) for line in text.splitlines() if line.strip()]


def manifest_at(repository, ref):
    """The text of build.yaml at one commit, or None when it is not there."""
    encoded = gh("api", "repos/{}/contents/build.yaml?ref={}".format(repository, ref),
                 "--jq", ".content")
    if encoded is None:
        return None
    return base64.b64decode("".join(encoded.split())).decode("utf-8")


def main(argv):
    if len(argv) != 3:
        sys.exit("usage: assemble-pull-request.py OWNER/REPO NUMBER")
    repository, number = argv[1], argv[2]

    request = json.loads(
        gh_or_die("api", "repos/{}/pulls/{}".format(repository, number))
    )

    # Non-merge commits only, which is the set the sign-off gate reads as well.
    # A merge commit's message is written by the server and says nothing about
    # what anybody intended.
    commits = [
        {"sha": commit["sha"], "message": commit["message"]}
        for commit in objects(
            gh_or_die(
                "api",
                "repos/{}/pulls/{}/commits".format(repository, number),
                "--paginate",
                "--jq",
                ".[] | {sha: .sha, message: .commit.message, parents: (.parents | length)}",
            )
        )
        if commit["parents"] < 2
    ]

    files = [
        line
        for line in gh_or_die(
            "api",
            "repos/{}/pulls/{}/files".format(repository, number),
            "--paginate",
            "--jq",
            ".[].filename",
        ).splitlines()
        if line.strip()
    ]

    json.dump(
        {
            "title": request.get("title"),
            "body": request.get("body"),
            "commits": commits,
            "files": files,
            "manifest_before": manifest_at(repository, request["base"]["sha"]),
            "manifest_after": manifest_at(repository, request["head"]["sha"]),
        },
        sys.stdout,
        indent=2,
    )
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
