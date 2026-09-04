# When the publication address stops working

An operator installs this plugin by adding a manifest address to their server. That
address is a single point of failure, this project owns it, and the people who
need an answer for the day it stops answering are exactly the people who cannot
reach the place an answer would normally be posted. So the answer is written here,
before it is needed.

## What the address holds, and what it does not

A manifest is an index. It names versions, the checksum of each archive, the ABI each
was built for, and where the archive lives. It holds none of those bytes itself.
Every artifact is an asset of a release for its tag, and
[`RELEASING.md`](RELEASING.md) describes what a run attaches to one.

An address that has gone therefore takes the index and leaves the artifacts. That is
the whole reason this document is short. Nothing published is lost with the address,
and the recovery is a republished index rather than a reconstruction of anything.

## Installing while it is gone

A release carries the archive and a checksum file for it, and those are what make an
install without the index verifiable rather than trusting:

- `.md5`, which is the value a catalog serves as the plugin checksum
- `.sha256`, for the same archive

Check the archive against the sidecar before unpacking it, and against the build
provenance statement the same run signs; [`RELEASING.md`](RELEASING.md) carries that
command. The two names above are held to the route that writes them by
`PublicationFallbackTests`, in both directions, so a route that stops writing one of
them reddens the suite rather than leaving this list pointing at a file nobody can
download, and a name added here that no run produces reddens it too.

Nothing about this path is faster than the index and nothing about it is meant to be.
It exists so that a server whose repository list has gone quiet is a server somebody
can still put a known version onto.

## What this repository does on the day it happens

Publish the new address. Regenerate the manifest from the release history, which is
what makes the new address serve the same versions rather than a fresh history
starting from whatever is current. Then say so where the old address was written
down, because an operator is not migrated by anything here and has to add the new
address by hand.

The order matters in one direction only. The address is published before it is
announced, because an announcement pointing at an address that is not serving yet
spends the one attempt an operator makes.

## What is built, what is not, and what that costs

THIS SECTION SAID NOTHING IN THIS REPOSITORY GENERATED A MANIFEST. Something does. The
regeneration this document leans on is a command rather than a plan, and what is left is
the address it would be published to.

    grep -n 'generate-manifest.py' .github/workflows/publish.yaml
    670:          generator=.github/generate-manifest.py
    730:            python3 .github/generate-manifest.py \
    748:            python3 .github/generate-manifest.py \

Three lines and not one. The first is the step that proves the generator refuses what
it is written for before the run trusts it; the second writes each channel's manifest
and the third regenerates it to be compared.

The release run reads the whole release history, writes one manifest per channel out of
it, regenerates each from the same history and refuses a difference. `RELEASING.md`
carries what it reads each field from and what it refuses. The manifest is a function of
the releases that exist rather than of the checkout, which is what makes the recovery
above a comparison instead of a reconstruction: a republished index can be held against
what is being served, byte for byte.

There is still no release history to regenerate one from:

    gh release list --repo Flowfin/jellyfin-plugin-watch-sync --json tagName --jq 'length'
    0

So the generator has never run over anything but its own fixtures, and the first tag
pushed is the first exercise of it. That is the same position every other part of this
route is in.

THIS SECTION SAID NO ADDRESS HAD BEEN CHOSEN AND THAT NOTHING SERVED EITHER
MANIFEST. One address is chosen, it answers, and this repository is declared behind
it, so a lost address is a state this repository can be in from today rather than a
later one and what this document fixes is live rather than hypothetical. The address
is the one the readme prints:

    curl -sS -o /dev/null -w "%{http_code}\n" https://flowfin.dev/manifest.json
    200

    curl -sS https://flowfin.dev/manifest.json \
      | jq -r '.[] | "\(.name)\tversions=\(.versions|length)"'
    Requests	versions=2
    Playback Statistics	versions=1

Run 2026-09-04. Two plugins and neither of them is this one, which is the paragraph
above arriving rather than a second absence: the catalogue is generated from the
releases each declared source has published, and this repository has published none.
The declaration is already there and switched on, so nothing at that end has to change
on the day the first tag is pushed:

    gh api 'repos/Flowfin/hub/contents/sources/watch-sync.json?ref=b091d072827b20fceb7057df22839053a2c3100e' \
      --jq '.content' | base64 -d | jq '{repository, slug, enabled}'
    {
      "repository": "jellyfin-plugin-watch-sync",
      "slug": "watch-sync",
      "enabled": true
    }

What is still unexercised is the recovery itself. No address of this project has ever
stopped answering, so every step under the two headings above is a plan rather than a
procedure anybody has run, and the first tag pushed is still the first exercise of the
generator.

The readme prints the address and points at this file, which is as far as the second
condition of #123 reaches inside this repository. The runbook that has to name the
same address is #108 and does not exist.

## What holds this document true

`PublicationFallbackTests` holds the checksum list above to the publish route, in
both directions. It reads the route this repository ships rather than a copy of it,
and it fails loudly on finding no sidecar at all, so a read that stopped matching
cannot leave the comparison green over nothing.

`InstallAddressTests` refuses ONE address being two, in both directions. The files it
compares are derived rather than listed: the readme and every document under `docs/`
that prints an address at all, so a second address arriving in a file written tomorrow
is inside the comparison without anybody adding it. Beside that it requires the readme
and this file to each print one, because a value that survives in one of them only
leaves the reader of the other without the thing they came for.

What it cannot ask is whether the address is the right one. Nothing in this tree
declares it - the name belongs to the project that serves the catalogue - so every
file agreeing on a wrong address passes here. That half is a reading at review, and
the request that decides it is the one pasted above.

Nothing holds the rest. The two quoted readings above were run at the commit that
landed this file and go stale the moment either becomes untrue, which for both of
them is the moment somebody does the work they name. A reading at review is what
stands against that.

The sentence in the readme saying nothing installs from the address yet is refused by
nobody and stops being true on the day the first tag is pushed. Nothing in this tree
can see a release, so no check here could judge it. Removing it is part of #159.
