> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Watch Sync

Moves watch history between Jellyfin servers that the same operator has paired.

If the same person watches on more than one server, each server keeps its own
answer to what has been played, how far into it, and when. Watch Sync carries
that answer across, for the users the operator has mapped to each other, so a
film finished on one server does not ask to be resumed on the other.

That is the whole of it. Watch state moves between two servers one operator has
paired, and nothing else moves at all.

## What it needs next to it

A second Jellyfin server, and a pairing plugin installed on both.

The pairing plugin owns the relationship between the two servers and the mapping
between their user accounts. Watch Sync reads that mapping and never invents one,
so on its own it does nothing at all. Installed without it, the plugin still
loads, syncs nothing, and says on its configuration page which of those states it
is in and what to do about it.

## Which servers it runs on

Two server lines, which sit on different frameworks, which is why the artifact is
built once per line rather than once.

Which lines those are, what has been checked on each and what has not, is in
[docs/compatibility.md](docs/compatibility.md). The versions are not repeated
here, because two lists of one thing drift and the one in this file is the one a
visitor would believe.

What that file says today, and what this one will not soften: no line has been
run on a server, so neither is supported. Both are compiled against and nothing
more.

## What it will never do

No media file is ever copied. Only the state a server holds about an item
moves, never a byte of the item itself. This is a permanent property of the
plugin rather than a setting, it does not change in a later version, and it holds
for every plugin in this family.

Nothing moves without a pairing. A pairing that is absent, disabled,
suspended or revoked stops the transfer. There is no fallback that keeps sending
while the pairing cannot be confirmed.

Nothing is matched by a file name or a path. Items are matched on the
metadata identifiers the servers already hold, the ones a scraper wrote when it
identified the film or the episode. An item carrying none of them is recorded as
not matched and is left alone. A library of home video has no such identifiers,
so it will not sync, and no setting turns that into a guess. A wrong match writes
one person's history onto somebody else's film, which is worse than an item that
did not move.

It is not a backup. It holds no copy of anything and restores nothing. Two
servers agreeing about what was watched is not an archive of it.

It is not a migration tool. It does not move a library, a user account or a
configuration from one server to another.

It is not a way to share a library with somebody else. Both servers belong to
one operator, who paired them deliberately. Nothing here reaches a server that
operator does not hold.

## Where the detail is

- [docs/compatibility.md](docs/compatibility.md), what each server line has been
  checked against, and what nothing has checked.
- [docs/matching.md](docs/matching.md), which kinds of item carry watch state
  across and by which key.
- [docs/parity.md](docs/parity.md), the checks this repository runs and the ones
  it has decided not to.
- [CONTRIBUTING.md](CONTRIBUTING.md), how to sign off a change.
- [LICENSE](LICENSE), the licence in full.

## State of this repository

Early. The plan is in the issues and the milestones. What is in the tree is the
plugin skeleton and the checks around it, and nothing here syncs anything yet.
This file describes what the plugin is for rather than a working plugin, and says
so here rather than leaving a reader to find out. The operator runbook, which is
the document to follow the first time, is #108 and does not exist.

## Licence

GNU General Public License, version 3. See [LICENSE](LICENSE).
