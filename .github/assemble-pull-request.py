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


# The tracked file each declaration the checker compares is read out of. The
# manifest carries the version and the server lines, and the envelope versions
# are declared in one place in the plugin's own sources. Both ends of every one
# of them is fetched, because a rule about what a change dropped cannot be
# answered from the head alone.
ENVELOPE_VERSIONS = "Jellyfin.Plugin.WatchSync/Model/EnvelopeVersions.cs"


def file_at(repository, path, ref):
    """The text of a tracked file at one commit, or None when it is not there."""
    encoded = gh(
        "api",
        "repos/{}/contents/{}?ref={}".format(repository, path, ref),
        "--jq",
        ".content",
    )
    if encoded is None:
        return None
    return base64.b64decode("".join(encoded.split())).decode("utf-8")


def published_releases(repository):
    """How many releases the repository has published.

    Drafts are dropped, because a draft is a release nobody can install and the
    question the checker asks is whether a number was ever one somebody could
    look up. A pre-release is counted: it is published, it has a tag, and an
    operator who added the pre-release address can install it.
    """
    tags = gh_or_die(
        "api",
        "repos/{}/releases".format(repository),
        "--paginate",
        "--jq",
        ".[] | select(.draft | not) | .tag_name",
    )
    return len([line for line in tags.splitlines() if line.strip()])


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

    # The status travels with the path. A rule that counts what a change WROTE
    # cannot be written over names alone: a fragment a change deletes is a
    # changed path under `changelog.d/` and is the opposite of an entry the
    # change wrote, which is how a version bump satisfied that rule by tidying
    # the directory. #296 is where that was found. Taking the field here is what
    # makes the distinction available, because this is the one place the answer
    # exists.
    files = objects(
        gh_or_die(
            "api",
            "repos/{}/pulls/{}/files".format(repository, number),
            "--paginate",
            "--jq",
            ".[] | {path: .filename, status: .status}",
        )
    )

    base, head = request["base"]["sha"], request["head"]["sha"]

    json.dump(
        {
            "title": request.get("title"),
            "body": request.get("body"),
            "commits": commits,
            "files": files,
            "manifest_before": file_at(repository, "build.yaml", base),
            "manifest_after": file_at(repository, "build.yaml", head),
            "envelope_versions_before": file_at(repository, ENVELOPE_VERSIONS, base),
            "envelope_versions_after": file_at(repository, ENVELOPE_VERSIONS, head),
            "releases": published_releases(repository),
        },
        sys.stdout,
        indent=2,
    )
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
