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

Nothing here writes a plugin catalog. A GitHub release is the whole output. If this
repository previously published through the Jellyfin meta plugins workflow, that path
is gone and no catalog is fed until a manifest generator is added.

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

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` and `*-prerelease` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.
