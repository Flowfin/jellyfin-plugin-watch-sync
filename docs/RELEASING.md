# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable` for the stable channel, and
`X.Y.Z-prerelease` or `X.Y.Z.W-prerelease` for the pre-release one, for example
`1.4.0-stable` or `0.1.0.0-prerelease`. The numeric part is the plugin version that
Jellyfin installs, and it must be exactly the `version` in `build.yaml`, written the
same way, with the same number of parts. The suffix lives only in the tag and in the
release name.

## The two channels

The suffix chooses the channel and nothing else does. Both channels run the same
build, the same checks and produce the same assets; the difference is that a
`-prerelease` tag publishes a release marked as a pre-release, which is what the
second manifest address serves.

An operator who wants to try a version subscribes to the pre-release address as well,
rather than repointing a running install at something else. That is also what replaces
a trial release that gets deleted afterwards: deleting a release spends its tag
permanently, and a pre-release does the same job without that price because it is a
real version that stays.

A version number is spent once across both channels. `1.4.0-prerelease` and
`1.4.0-stable` would be two releases claiming one version, built from two commits, and
a catalog reading one of them offers bytes the other channel's operators never saw. So
promoting a pre-release means raising the version in `build.yaml` and tagging the new
number, not retagging the old one. The release job asks for both suffixes before it
writes anything and stops on either.

## The two-server harness gates a release

The harness of #88 runs this plugin's whole loop between two real servers, which is
the only thing the unit suite cannot do and is the entire claim a sync plugin makes.
So it gates: a release is not cut unless it has run and is green.

    Harness gate: required
    No container runtime: the release waits

The third answer, gating only where a container runtime happens to be present, is
refused rather than merely not chosen. A gate that disables itself when its runtime is
missing produces a green result that means either "passed" or "never ran", with nothing
in the result to tell them apart, and the reader who most needs the difference is the
one cutting the release.

So if the machine cutting a release has no container runtime, the release waits until
one is available. It does not ship unproven, and it does not ship with the gate
skipped. That is written here so it is met before a tag is pushed rather than
discovered at the moment somebody wants to ship.

**The harness does not exist yet.** #88 has neither a container runtime to run on nor
this plugin's own behaviour to exercise, so nothing runs this gate today and any
release cut before it lands is one no harness has proven. The rule above is what
applies from the moment it does; it is not a description of what runs now, and a
reader who takes it for one has been told the opposite here.

## Cutting a release

1. Update `version` in `build.yaml` on the release branch and merge it.
2. Check that the commit you want to release is on that branch.
3. Push the tag for that commit:

    ```
    git tag 1.4.0-stable <commit>
    git push origin 1.4.0-stable
    ```

The `Publish Release` workflow takes it from there.

Push one tag at a time and wait for its run to finish. GitHub keeps at most one
queued run per concurrency group, and although the group here is keyed on the tag,
serialising them by hand is what keeps the release order readable.

## What the run produces

The workflow builds the plugin from the tagged commit, creates the GitHub release
for the tag, and attaches five files:

- the plugin archive
- the packaging metadata written beside it, `<archive>.zip.meta.json`
- `build.yaml`, the manifest the package was built from
- one `.md5` file, the checksum of the archive
- one `.sha256` file for the same archive

The body of the release is assembled from the changelog fragments under
`changelog.d/`, by `.github/assemble-release-notes.py`, in the gate job. The entries
marked as reaching watch state that has already been synced come first, under their
own heading, and a release carrying none of them says so rather than leaving the
heading out. GitHub's generated list of merged pull requests follows underneath.
Which fragments a release carries is worked out from the previous release tag
contained in the tagged commit, so nothing has to be deleted after a release and no
entry goes out twice. `docs/changelog.md` is where the format and the assembler are
argued.

The `.md5` is the value a Jellyfin catalog serves as the plugin checksum. There is
exactly one per release so that no generator can pair a checksum with the wrong
file. The archive is the only file with a checksum beside it; the metadata and the
manifest are read rather than installed, and adding a second sidecar is what the
single `.md5` above exists to prevent.

The manifest is attached because a catalog entry for this release, and any repair of
one, is written from the version, the ABI and the framework the package was built
with. Read back out of the tree later those are the values of a different commit.
The three inputs are checked for existence by name before the release job runs, and
the manifest is asked for again after the download, so a release short of one of them
is not a state this route can reach.

The run also signs a build provenance statement for the archive, in a separate job
that downloads the archive and runs no build tooling. A downloaded archive can be
checked against it:

```
gh attestation verify <archive>.zip --repo <owner>/<repository>
```

## The manifest the run generates

THIS SECTION SAID NOTHING HERE WROTE A PLUGIN CATALOG. A run generates one now, and
what has not moved is where it is served from.

After the release exists, a further job reads the whole release history and writes one
manifest per channel with `.github/generate-manifest.py`, then regenerates each of them
from the same history and refuses a difference. Both are attached to the run, together
with the history they were generated from.

Nothing about that manifest comes from the checkout. The plugin's identity in it is read
out of the packaging metadata the newest release in that channel published, and each
version entry out of that release's metadata, its archive's own download address and its
`.md5`. So the manifest is a function of the releases that exist, and regenerating it
from a later commit reproduces it byte for byte rather than quietly carrying a
description that has moved since. That is what makes a repair of a lost index
comparable to what is being served, which is what `docs/publication-route.md` is about.

The run refuses rather than writing an index that is wrong in a way an operator finds by
failing to install: a history that answered nothing, a tag whose channel is a guess, a
release whose pre-release flag disagrees with its own tag, a release with no archive or
with more than one, an archive with no checksum, a checksum that is the digest of
another file, and two releases claiming one version. A channel with no release in it is
not one of those: it produces an index with no versions rather than a failed run.

**No address is chosen and nothing is served.** The manifests are attached to the run
and go nowhere else, so an operator cannot subscribe to either channel today. Choosing
the two addresses and publishing to them is #123, and this section is what a run
produces rather than a description of a catalog anybody can reach.

## What fails the run

- The tag does not end in a channel suffix, or the workflow was started from something
  other than a tag.
- The numeric part of the tag differs from `version` in `build.yaml`.
- `build.yaml` is missing a required field, or `version`, `targetAbi`, `framework`
  or `guid` has the wrong shape.
- `framework` in `build.yaml` names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- `build.yaml` declares an `image` file that is not in the repository.
- The tagged commit is not contained in a release branch, or the tag was moved after
  the run started.
- There is no `packages.lock.json` next to the plugin project, so the release build
  cannot restore against a reviewed dependency graph. Create one with
  `dotnet restore <project> -p:RestorePackagesWithLockFile=true` and commit it.
- The version stamped into the assembly is not the version in `build.yaml`.
- The build produced no archive, or more than one, or no packaging metadata.
- No changelog fragment belongs to the release, or one of them cannot be assembled
  into a note: it carries no `Existing-Data` marking, or it is marked as reaching
  already-synced watch state and says nothing about what it does to it, or there is
  nothing under its header. This fails in the gate job, before anything is built.
- A release already exists for the tag, or the same version was already published on
  the other channel.

All of these fail before anything is published.

## What the run notes without failing

The packaging tool warns when `build.yaml` declares neither `image` nor `imageUrl`.
The plugin then shows without a logo in a catalog. That is a warning on every run
until a logo exists, and it is not a reason to hold a release.

## Re-running

A release that exists is not touched again. The release job asks whether a release
exists for the tag before it writes anything and stops if one does, and the upload
step is configured not to replace an asset of the same name. Replacing the bytes of a
version people have already installed is the failure this prevents, and it is worth
more than the convenience of a re-run.

So: if a release went out with the wrong contents, fix the problem, raise the version
in `build.yaml`, and push a new tag.

If a run failed **before** the release was created, the tag is still clean. Fix the
cause and re-run the workflow from the Actions page, or delete and re-push the tag.

If a run failed **after** the release was created but before every asset was attached,
the release is incomplete and a re-run will refuse it. What is possible then depends
on the repository settings below. Without immutable releases you can delete the
incomplete release, delete the tag, and push it again. With immutable releases you
cannot, and the version has to be raised.

## When the watcher files an issue

A run that is not a required pull-request check is shown to nobody. The publish runs on a
tag push, a scan runs on a schedule, and the mainline's own runs report to no pull
request, so any of them can go red and stay red while every pull request on the board is
green. The failure #121 exists for has happened on a neighbouring board: a release was
created, the step after it failed, and a green looking publish shipped nothing anybody
could install.

`.github/workflows/publish-failure-alert.yml` sweeps every run on the default branch and
every run on a tag, once every half hour, and files one issue labelled `release-alert`
when any of them concluded other than success. What it sweeps is derived from the runs
rather than from a list of workflow names, so a workflow added later is watched the day
it lands. The body names the workflow and the step that failed and carries the end of
that step's log; it is rewritten on every tick to the current state, a comment marks a
change in the set of red workflows and nothing else, and the watcher closes the issue
itself once nothing is red. It can be started from the Actions page with `dry_run` left
on, which writes what it would file into the run log and files nothing.

What decides that a workflow is red is that workflow's own latest concluded run, and never
how long ago it ran. So an alert stands until the workflow produces a better run: a weekly
scan that goes red stays reported for the week rather than for a day, and a publish that
failed on a tag stays reported until a publish succeeds or somebody starts one from the
Actions page. A cancelled run is read as somebody's own act and decides nothing, so the run
before it is the one judged. This is not what shipped first: a 24 hour window stood here,
and against six scheduled workflows that run weekly or monthly it filed the alert and
closed it again the next day with the failure still standing.

Each workflow is asked for its own runs rather than one listing of the repository's most
recent ones, because a single listing is capped and a busy board fills that cap with
pull-request runs. Measured here, the 300 most recent runs reached back 33 hours and three
of the scheduled workflows had no run inside them at all, so those three were not judged
late, they were not judged. The workflow list is read from the API on every tick, so one
added later is asked about without anybody registering it, and one the repository has
disabled is left out.

Which repair applies is decided by which workflow the body names, because the two
failures this is about look alike from the release page and are not:

- **The publish route is named.** The publish failed. Whether the release exists decides
  everything, and `## Re-running` above is the whole of the repair: a run that failed
  before the release was created leaves the tag clean, and one that failed after leaves
  an incomplete release that a re-run refuses.
- **The freshness check is named.** The publish succeeded and what is served is stale: a
  release exists whose version the published manifest does not list. The repair is to
  regenerate the manifest from the release history and publish it again, which
  `docs/publication-route.md` fixes. THE FRESHNESS CHECK IS NOT IN THIS TREE. It is #120,
  and until it lands nothing produces this second failure, so the body can name it only
  on the day it exists.

What the watcher cannot see is written here so that its silence is not read as a clean
publish. A run that created a release, attached one archive and wrote the manifest has
shipped one server line where `build.yaml` declares two; that run is green, and the gap
is #117's. And the watcher has not been exercised against a real failure: the rehearsal
is a publish deliberately failed on the pre-release channel, which spends a version and
a tag and is not a step to take on the way to a first release. Until that rehearsal is
linked from #121, what holds the watcher is the file, `PublishFailureAlertTests`, the
workflow audit and a dry run.

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` and `*-prerelease` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.
