#!/usr/bin/env python3
"""Generate the plugin manifest for one channel out of the release history.

A manifest is the index a server reads to find versions, checksums, the ABI each
archive was built for, and where the archive lives. It is the single point where a
mistake means nobody can install or update, and it is the artefact most likely to be
edited by hand at three in the morning. This exists so that it never is: the release
run produces it, and what it produces is a function of the releases that exist rather
than of anything anybody typed.

That property is what makes a repair possible without guesswork. An address that has
gone takes the index and leaves the artifacts, and the recovery is a republished index
rather than a reconstruction of anything, which is `docs/publication-route.md`'s whole
argument. A regeneration that reproduced the manifest only approximately would leave an
operator unable to tell a repair from a rewrite.

## Where every field comes from

Nothing here reads the checkout. The identity of the plugin - the guid, the name, the
description, the overview, the owner, the category and the image - is read out of the
packaging metadata the NEWEST release in the channel published, not out of `build.yaml`
as it stands now. A generator reading the working tree would produce a different
manifest from a later commit over the same history, which is the fourth condition of
issue #119 failing quietly rather than loudly.

Per version:

- `version`, `changelog`, `targetAbi` and `timestamp` come from the `.zip.meta.json`
  the packaging tool wrote beside the archive and the run attached to the release;
- `sourceUrl` is the archive asset's own download address, taken from the release
  rather than composed here, so a manifest cannot name a URL nothing serves;
- `checksum` is the MD5 the run wrote into the `.md5` sidecar, which is the value a
  Jellyfin catalog serves as the plugin checksum.

## What it reads

The release history arrives as one JSON document on standard input, in the shape the
releases API answers with: a list of objects carrying `tag_name`, `prerelease` and
`assets`, each asset carrying `name` and `browser_download_url`. It arrives as a
document rather than being fetched here for the same reason the pull-request check
beside this file takes its commits from its caller: a verdict that can be reproduced
against a document written by hand and offline is one somebody can argue with.

The bytes of the small assets - the packaging metadata and the checksum sidecar - are
read from `--assets <directory>`, where the run has put each release's files under a
subdirectory named for its tag.

## What it refuses, and why an empty answer is not one of them

Every refusal below is a state in which a manifest could still be written and would be
wrong in a way an operator finds by failing to install:

- a history document that is not a list, or that holds no release at all, because an
  API call that failed and answered nothing would otherwise render a valid, empty
  catalog that looks exactly like a project that has not shipped yet;
- a tag carrying neither channel suffix, because which channel it belongs to is then a
  guess;
- a release whose `prerelease` flag disagrees with its tag suffix, because the two are
  the same fact written twice and a catalog reading one of them offers bytes the other
  channel's operators were never shown;
- a release in the channel with no archive, or with more than one;
- a release whose packaging metadata or checksum sidecar is absent, unreadable, or
  short of a field a version entry needs;
- a checksum sidecar whose digest is not an MD5, or that names a file other than the
  archive it sits beside;
- two releases in the channel claiming one version.

A channel with no releases in it is NOT a refusal. A project with a stable release and
no pre-release is the ordinary state on the day it first ships, and its pre-release
address serves an index with no versions in it rather than nothing at all.

## The one shape it will not guess at

More than one archive on a release is refused rather than rendered. The route publishes
one archive and one `.md5`, because a catalog that picks a checksum by file name picks
the wrong one the moment a second `.md5` exists. One artifact per server line, which is
issue #117, therefore collides with that constraint, and which of the three shapes
settles it is an open decision on that issue. Pairing a lone checksum with one of two
archives here would take that decision by writing it into a generator.

Exit 0 when a manifest was written. Exit 1 on any refusal above.
"""

import argparse
import json
import pathlib
import re
import sys

# The suffix a tag carries to say which channel its release belongs to. The suffix is
# the only thing that chooses a channel, which docs/RELEASING.md states and
# ReleaseChannelTests holds the publish route to, so this reads the same fact rather
# than a second one.
CHANNELS = ("stable", "prerelease")

# A tag as this project writes one: the plugin version, then the channel. The numeric
# part is what a server installs and the suffix lives only in the tag and the release
# name.
TAG = re.compile(r"^(?P<version>[0-9]+(?:\.[0-9]+){2,3})-(?P<channel>[a-z]+)$")

# What `md5sum` writes: the digest, whitespace, then the name of the file it was taken
# over, which the binary-mode spelling prefixes with an asterisk. Both halves are read.
# A sidecar naming another file is a checksum that belongs to something else, which is
# exactly the pairing the single-`.md5` rule in the publish route exists to make
# impossible, and reading only the digest is how that pairing would pass.
DIGEST = re.compile(r"^(?P<digest>[0-9a-f]{32})[ \t]+\*?(?P<name>.+)$")

# The packaging metadata the tool writes beside the archive. Named by suffix rather
# than by composing the archive's name, so a run that renames the archive is read
# correctly and a metadata file belonging to nothing is found as an extra rather than
# silently matched.
METADATA_SUFFIX = ".zip.meta.json"

# What a version entry owes, in the order a manifest writes it. Read as a list rather
# than as a set so the rendered document has one member order and two runs cannot
# differ by it.
VERSION_FIELDS = ("version", "changelog", "targetAbi", "sourceUrl", "checksum", "timestamp")

# What the metadata has to carry for a version entry to be assembled at all. The two
# entries this does not name, `sourceUrl` and `checksum`, are the two the metadata
# deliberately does not hold: the packaging tool cannot know where the archive will be
# published or what the run will write beside it.
METADATA_FIELDS = ("version", "changelog", "targetAbi", "timestamp")

# What the plugin's own identity is, in the order a manifest writes it. `imageUrl` and
# `image` are not here because either may be absent, and a manifest carrying an empty
# one is a catalog entry pointing at nothing.
IDENTITY_FIELDS = ("guid", "name", "description", "overview", "owner", "category")

# The identity members that are carried when the newest release published them.
OPTIONAL_IDENTITY_FIELDS = ("imageUrl", "image")


def refuse(message):
    """Print a refusal and stop. Nothing is written when one is reached."""
    print(f"error: {message}", file=sys.stderr)
    raise SystemExit(1)


def history_from(text):
    """The releases a history document holds, or a refusal."""
    try:
        document = json.loads(text)
    except json.JSONDecodeError as broken:
        refuse(f"the release history is not JSON: {broken}")

    if not isinstance(document, list):
        refuse("the release history is not a list of releases")

    if not document:
        refuse(
            "the release history holds no release at all. A call that failed and "
            "answered nothing looks the same as a project that has not shipped, and "
            "the manifest it would produce is a catalog that installs nothing."
        )

    return document


def tag_of(release):
    """The tag a release carries, or a refusal."""
    tag = release.get("tag_name")
    if not isinstance(tag, str) or not tag:
        refuse("a release in the history carries no tag, so nothing here can place it")
    return tag


def channel_of(tag):
    """The channel a tag names, or a refusal."""
    found = TAG.match(tag)
    if found is None:
        refuse(
            f"the tag {tag} carries neither channel suffix, so which channel its "
            "release belongs to is a guess. docs/RELEASING.md fixes the two forms."
        )

    channel = found.group("channel")
    if channel not in CHANNELS:
        refuse(f"the tag {tag} names the channel {channel!r}, which is not one this project publishes")

    return channel


def agreed_channel(release):
    """The channel a release belongs to, refusing where its two statements disagree."""
    tag = tag_of(release)
    channel = channel_of(tag)
    marked = release.get("prerelease")

    if not isinstance(marked, bool):
        refuse(f"the release {tag} does not say whether it is a pre-release")

    if marked != (channel == "prerelease"):
        refuse(
            f"the release {tag} is tagged for the {channel} channel and is marked "
            f"prerelease={str(marked).lower()}. The suffix and the flag are one fact "
            "written twice, and a catalog reading either would offer bytes the other "
            "channel's operators were never shown."
        )

    return channel


def assets_of(release):
    """The assets a release published, by name, or a refusal."""
    assets = release.get("assets")
    tag = tag_of(release)

    if not isinstance(assets, list):
        refuse(f"the release {tag} carries no asset list")

    published = {}
    for asset in assets:
        name = asset.get("name") if isinstance(asset, dict) else None
        if not isinstance(name, str) or not name:
            refuse(f"an asset of the release {tag} has no name")
        if name in published:
            refuse(f"the release {tag} carries two assets named {name}")
        published[name] = asset

    return published


def archive_of(tag, assets):
    """The one archive a release published, or a refusal."""
    archives = sorted(name for name in assets if name.endswith(".zip"))

    if not archives:
        refuse(f"the release {tag} published no archive, so there is nothing for a version entry to point at")

    if len(archives) > 1:
        refuse(
            f"the release {tag} published {len(archives)} archives and one checksum "
            "sidecar can only belong to one of them. One artifact per server line is "
            "issue #117 and it collides with the single-`.md5` rule this route "
            "carries; which of the three shapes settles it is an open decision there, "
            "and pairing a lone checksum with one of these archives would take it here."
        )

    return archives[0]


def source_url_of(tag, asset):
    """Where the archive is served from, taken from the release rather than composed."""
    url = asset.get("browser_download_url")
    if not isinstance(url, str) or not url:
        refuse(f"the archive of the release {tag} carries no download address, so the manifest would name a URL nothing serves")
    return url


def read(directory, tag, name):
    """The bytes of one asset the run downloaded, or a refusal."""
    path = pathlib.Path(directory) / tag / name

    if not path.is_file():
        refuse(f"{path} is not there, so the release {tag} cannot be described without guessing at it")

    try:
        return path.read_text(encoding="utf-8")
    except (OSError, UnicodeDecodeError) as unreadable:
        refuse(f"{path} could not be read: {unreadable}")


def metadata_of(directory, tag, assets, archive):
    """The packaging metadata a release published beside its archive, or a refusal."""
    named = sorted(name for name in assets if name.endswith(METADATA_SUFFIX))

    if len(named) != 1:
        refuse(
            f"the release {tag} published {len(named)} packaging metadata files and "
            "exactly one is what a version entry is assembled from"
        )

    expected = archive + ".meta.json"
    if named[0] != expected:
        refuse(f"the release {tag} published {named[0]} beside the archive {archive}, so the metadata describes something else")

    try:
        metadata = json.loads(read(directory, tag, named[0]))
    except json.JSONDecodeError as broken:
        refuse(f"the packaging metadata of the release {tag} is not JSON: {broken}")

    if not isinstance(metadata, dict):
        refuse(f"the packaging metadata of the release {tag} is not one object")

    missing = [field for field in METADATA_FIELDS if not metadata.get(field)]
    if missing:
        refuse(f"the packaging metadata of the release {tag} carries no {', '.join(missing)}")

    return metadata


def checksum_of(directory, tag, assets, archive):
    """The MD5 the run wrote beside the archive, or a refusal."""
    sidecars = sorted(name for name in assets if name.endswith(".md5"))

    if len(sidecars) != 1:
        refuse(
            f"the release {tag} published {len(sidecars)} MD5 sidecars. A catalog "
            "serves that value as the plugin checksum, so there is exactly one per "
            "release and a version entry has nowhere else to read one from."
        )

    # The first line and only the first. A sidecar carrying a second line is a file
    # covering two archives, which is the state the single-`.md5` rule refuses, and
    # reading past the first would pick one of them silently.
    lines = [line for line in read(directory, tag, sidecars[0]).splitlines() if line.strip()]

    if len(lines) != 1:
        refuse(f"the checksum sidecar of the release {tag} holds {len(lines)} checksum lines and exactly one archive is what it is written beside")

    found = DIGEST.match(lines[0].rstrip())
    if found is None:
        refuse(f"the checksum sidecar of the release {tag} does not hold an MD5 digest followed by the name of the file it was taken over")

    named = found.group("name").strip()
    if named != archive:
        refuse(
            f"the checksum sidecar of the release {tag} is the digest of {named} and "
            f"the archive published there is {archive}. A manifest pairing those would "
            "refuse every install of a file that is not corrupt."
        )

    return found.group("digest")


def version_entry(directory, release, tag, assets):
    """One version of the manifest, assembled out of what a release published."""
    archive = archive_of(tag, assets)
    metadata = metadata_of(directory, tag, assets, archive)

    return {
        "version": metadata["version"],
        "changelog": metadata["changelog"],
        "targetAbi": metadata["targetAbi"],
        "sourceUrl": source_url_of(tag, assets[archive]),
        "checksum": checksum_of(directory, tag, assets, archive),
        "timestamp": metadata["timestamp"],
    }, metadata


def ordered(version):
    """A version number as a tuple, so two of them order by number and not by text."""
    parts = version.split(".")
    if not all(part.isdigit() for part in parts):
        refuse(f"the version {version} is not a dotted number, so it cannot be ordered against another")
    return tuple(int(part) for part in parts)


def manifest_of(directory, releases, channel):
    """The manifest for one channel, or a refusal."""
    entries = []
    identities = []

    for release in releases:
        if agreed_channel(release) != channel:
            continue

        tag = tag_of(release)
        entry, metadata = version_entry(directory, release, tag, assets_of(release))
        entries.append(entry)
        identities.append((ordered(entry["version"]), tag, metadata))

    claimed = {}
    for entry in entries:
        version = entry["version"]
        if version in claimed:
            refuse(
                f"two releases in the {channel} channel claim the version {version}. "
                "A server offered two sets of bytes under one number installs whichever "
                "the catalog happens to list first."
            )
        claimed[version] = True

    if not entries:
        # Not a refusal. A project with a stable release and no pre-release is the
        # ordinary state on the day it first ships, and the address for the empty
        # channel serves an index with no versions rather than nothing at all.
        return []

    entries.sort(key=lambda entry: ordered(entry["version"]), reverse=True)
    identities.sort(key=lambda identity: identity[0], reverse=True)

    newest = identities[0][2]

    missing = [field for field in IDENTITY_FIELDS if not newest.get(field)]
    if missing:
        refuse(
            f"the packaging metadata of the newest release in the {channel} channel "
            f"carries no {', '.join(missing)}, and the plugin's identity in the "
            "manifest is read from it rather than from the checkout"
        )

    plugin = {field: newest[field] for field in IDENTITY_FIELDS}

    for field in OPTIONAL_IDENTITY_FIELDS:
        if newest.get(field):
            plugin[field] = newest[field]

    plugin["versions"] = entries

    return [plugin]


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument(
        "--channel",
        required=True,
        choices=CHANNELS,
        help="which channel's index to write",
    )
    parser.add_argument(
        "--assets",
        required=True,
        help="the directory holding each release's downloaded assets, under a subdirectory named for its tag",
    )
    parser.add_argument(
        "--output",
        help="where to write the manifest; standard output when absent",
    )

    arguments = parser.parse_args()

    releases = history_from(sys.stdin.read())
    manifest = manifest_of(arguments.assets, releases, arguments.channel)

    # Four spaces and a closing newline, with the member order fixed above rather than
    # sorted, so that two runs over one history produce identical bytes and a
    # regeneration can be compared to what is being served rather than read.
    rendered = json.dumps(manifest, indent=4, ensure_ascii=False) + "\n"

    if arguments.output:
        pathlib.Path(arguments.output).write_text(rendered, encoding="utf-8", newline="\n")
    else:
        sys.stdout.write(rendered)

    return 0


if __name__ == "__main__":
    sys.exit(main())
