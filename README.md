# Watch Sync

A Jellyfin plugin that moves watch history between two Jellyfin servers the same
operator has paired.

If the same person watches on more than one server, each server keeps its own
answer to what has been played, how far into it, and when. Watch Sync carries
that answer across, for the users the operator has mapped to each other, so a
film finished on one server does not ask to be resumed on the other.

## What it never does

No media file is ever copied. Only the state a server holds about an item moves,
never a byte of the item itself. This is a permanent property of the plugin and
not a setting, and it does not change in a later version.

Nothing moves except between servers the same operator has paired. A pairing that
is absent, disabled, suspended or revoked stops the transfer. There is no
fallback that keeps sending when the pairing cannot be confirmed.

Nothing is matched by a file name or a path. An item that cannot be matched on
the identifiers the servers already hold is recorded as unmatched and left alone,
because a wrong match writes one person's history onto somebody else's film.

## What it needs

A second Jellyfin server, and a pairing plugin installed on both. The pairing
plugin owns the relationship between the two servers and the mapping between
their user accounts. Watch Sync consumes that mapping and never invents one, so
on its own it does nothing. With no pairing plugin present it loads, syncs
nothing, and says which state it is in on its configuration page.

## Which servers it runs on

Two server lines are intended, and they sit on different frameworks, which is why
the artifact will be built once per line rather than once. Nothing here names a
line that has been run and checked. The build in this tree does not yet declare
either intended line, and #4 is where that lands. Until the compatibility matrix
in #113 exists, read every version as not evaluated rather than as supported.

## State of this repository

Early. The plan is in the issues and the milestones. What is in the tree is the
plugin skeleton and the checks around it, and nothing here syncs anything yet.
This file says so rather than describing a plugin that does not exist. The full
front door, with installation and the operator runbook, is #106.

## Licence

GNU General Public License, version 3. See [LICENSE](LICENSE).
