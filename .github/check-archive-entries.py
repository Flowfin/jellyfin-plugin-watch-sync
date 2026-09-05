#!/usr/bin/env python3
"""Refuse a plugin archive that carries anything but the plugin's own files.

The archive an operator installs is produced by the packager and, until this, was
looked into by nobody. Both packaging routes check that the files were produced,
that the assembly's stamp equals the manifest's version, and that the two
packager calls share a pin and a framework; none of that reads a byte of what is
inside the zip.

The failure this refuses is a build that packages more than the plugin. A test
assembly, a test project's dependency, or a stray file from the output directory
shipped inside a plugin archive is loaded onto somebody's server, and the first
anybody hears of it is a server that behaves differently from the one it was
tested on. That is not hypothetical for a repository that multi-targets and
whose test project sits beside the plugin in one solution.

## What the plugin's own files are

The manifest says. `build.yaml` declares `artifacts`, one relative path per
file the packager copies out of the publish directory, and the packager adds
two things of its own: `meta.json`, which it writes into every archive, and the
image where the manifest declares one, under its base name. Nothing else may be
in the archive, and a directory entry is allowed only where it is a parent of a
declared artifact, because the packager writes directory entries for the
folders it had to create and for nothing else.

The list is read out of the manifest rather than carried here, so an artifact
added to `build.yaml` is allowed the same day and a file that is not in that
list is refused whatever it is called.

## What is refused, and what is asserted

An entry that is not the plugin's own is refused by name, every one of them,
so a run that packaged three stray files says three names rather than one.

The archive is asserted to carry every declared artifact, by name, so an empty
or wrong-shaped archive fails rather than passing a refusal that found nothing
to refuse. An archive with no entries at all is refused for the same reason.

## What this cannot see

Whether an entry's bytes are what its name says. A file called by the plugin
assembly's name that holds something else passes here; that is what the
assembly version check and the provenance attestation on the release route are
for, and neither is repeated by this.

Exit 0 when every entry is the plugin's own and every declared artifact is
there. Exit 1 otherwise, with each refused entry and each missing artifact
named, and exit 1 when the archive or the manifest cannot be read, because an
unreadable archive would otherwise pass as one with nothing to refuse.
"""

import argparse
import pathlib
import sys
import zipfile

# The file the packager writes into every archive, read from its source at the
# commit both workflows pin: JSON_METADATA_FILE in jprm/__init__.py.
METADATA = "meta.json"


def refuse(message):
    """Print a refusal and exit non-zero."""
    print("check-archive-entries: " + message)
    sys.exit(1)


def unquoted(value):
    """Return a YAML scalar without its surrounding quotes, if any."""
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in ('"', "'"):
        return value[1:-1]
    return value


def read_manifest(path):
    """Return (artifacts, image) as the manifest declares them, or refuse.

    Anchored on column zero rather than parsed, which is what every other read
    of the manifest in this repository does and for the same reason: one
    dependency for one list, in a hand-maintained file its readers read by eye.
    The artifacts list is the sequence of `- ` lines directly under the
    `artifacts:` key, and it ends at the first line that is neither one of
    those nor blank.
    """
    if not path.exists():
        refuse(
            "%s does not exist. The artifacts list is what says which files are "
            "the plugin's own, and there is no second place to read it from." % path
        )

    artifacts = []
    image = None
    in_artifacts = False

    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.rstrip("\r")
        if in_artifacts:
            stripped = line.strip()
            if stripped.startswith("- "):
                artifacts.append(unquoted(stripped[2:]).replace("\\", "/"))
                continue
            if stripped == "":
                continue
            in_artifacts = False
        if line.startswith("artifacts:"):
            in_artifacts = True
            continue
        if line.startswith("image:"):
            image = pathlib.PurePosixPath(unquoted(line[len("image:"):]).replace("\\", "/")).name

    if not artifacts:
        refuse(
            "%s declares no artifact under a top-level `artifacts:` key, so "
            "nothing here could say which files are the plugin's own." % path
        )

    return artifacts, image


def parent_directories(artifacts):
    """Return every directory entry the packager writes for the given paths."""
    directories = set()
    for artifact in artifacts:
        parts = artifact.split("/")[:-1]
        for depth in range(1, len(parts) + 1):
            directories.add("/".join(parts[:depth]) + "/")
    return directories


def read_entries(path):
    """Return the archive's entry names with forward slashes, or refuse."""
    if not path.exists():
        refuse("%s does not exist, so there is no archive to look into." % path)
    try:
        with zipfile.ZipFile(path) as archive:
            names = archive.namelist()
    except zipfile.BadZipFile:
        refuse("%s is not a zip archive this can read." % path)
    return [name.replace("\\", "/") for name in names]


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--archive", required=True, help="the archive the packager produced")
    parser.add_argument(
        "--manifest",
        default="build.yaml",
        help="the manifest whose artifacts list says which files are the plugin's own",
    )
    arguments = parser.parse_args()

    artifacts, image = read_manifest(pathlib.Path(arguments.manifest))
    entries = read_entries(pathlib.Path(arguments.archive))

    allowed = set(artifacts) | {METADATA}
    if image is not None:
        allowed.add(image)
    directories = parent_directories(artifacts)

    print("check-archive-entries: %s carries %d entr%s:" % (arguments.archive, len(entries), "y" if len(entries) == 1 else "ies"))
    for entry in entries:
        print("  " + entry)

    if not entries:
        refuse("the archive is empty, so it carries no plugin at all.")

    strays = [entry for entry in entries if entry not in allowed and entry not in directories]
    missing = [artifact for artifact in artifacts if artifact not in entries]

    problems = []
    for entry in strays:
        problems.append(
            "  %s is not the plugin's own: it is neither an artifact %s "
            "declares, nor %s, nor the declared image, nor a folder one of those "
            "sits in." % (entry, arguments.manifest, METADATA)
        )
    for artifact in missing:
        problems.append(
            "  the archive does not carry %s, which %s declares as an "
            "artifact, so this is not the plugin's package." % (artifact, arguments.manifest)
        )

    if problems:
        refuse(
            "%d problem(s) with what the archive carries:\n%s"
            % (len(problems), "\n".join(problems))
        )

    print(
        "check-archive-entries: every entry is the plugin's own and every "
        "declared artifact is there."
    )


if __name__ == "__main__":
    main()
