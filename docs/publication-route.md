# When the publication address stops working

An operator installs this plugin by adding a manifest address to their server. That
address is a single point of failure, this repository owns it, and the people who
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

## What is not built, and what that costs

Nothing in this repository generates a manifest. The runbook says so about itself:

    git grep -n 'Nothing here writes a plugin catalog' -- docs/RELEASING.md
    docs/RELEASING.md:83:Nothing here writes a plugin catalog. A GitHub release is the whole output. If this

There is also no release history to regenerate one from:

    gh release list --repo Flowfin/jellyfin-plugin-watch-sync --json tagName --jq 'length'
    0

So the regeneration named above is a plan rather than a command anybody can run
today, and until it exists a lost address costs a manifest assembled by hand out of
the release list. That is #119, whose fourth condition is exactly that a regeneration
reproduces the manifest rather than approximating it.

No address has been chosen either. This document fixes what happens rather than
recording a route that has run, and the day the first one is published is the day
each claim above becomes checkable against something.

Nothing points at this file yet. The readme, the runbook and the manifest all naming
one place is the second condition of #123, and the runbook it needs is #108.

## What holds this document true

`PublicationFallbackTests` holds the checksum list above to the publish route, in
both directions. It reads the route this repository ships rather than a copy of it,
and it fails loudly on finding no sidecar at all, so a read that stopped matching
cannot leave the comparison green over nothing.

Nothing holds the rest. The two quoted readings above were run at the commit that
landed this file and go stale the moment either becomes untrue, which for both of
them is the moment somebody does the work they name. A reading at review is what
stands against that.
