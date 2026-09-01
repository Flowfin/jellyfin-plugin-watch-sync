# The opt-out, and what it does not do

Watch history is personal data about the person who watched, and the operator of
a server is not always that person. So the person whose history it is can stop it
moving. This document is the wording that choice is offered in, which is the
fourth condition of [#60](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/60):
what a person is told has to say exactly what the choice does and exactly what it
does not, and it lives here rather than only in a control on a page, because a
sentence that exists only in a page is one the next person to edit that page
rewrites for length.

Nothing in this plugin reads any of this yet. There is no setting, no user record
to keep one in, and no run for it to stop, and this document says so on its own
page rather than leaving a reader to discover it. What is fixed here is the
wording and the two claims it may not make, so that the control, when #59 and #68
make one possible, is written against a sentence that was decided rather than
against whatever fitted the space.

## The wording

> Stop my watch history moving between paired servers.
>
> Nothing about what you have watched will be sent to a paired server, and
> nothing a paired server holds about you will be applied here. This takes
> effect immediately, on every pairing, in both directions.
>
> This does not delete anything. What has already moved stays where it is,
> here and on the paired server, and this server keeps showing you what it
> shows you now. Removing what a pairing brought over is a separate action
> your administrator takes, and it happens on its own when a pairing is
> revoked.

Three sentences and each carries one of the three claims the rules below fix. A
wording that keeps the first two and loses the third is the version this document
exists against.

## What the wording may not say

**It may not read as an undo.** A person who is told their history will stop
moving, and who understands that as their history being taken back, has been
misled by a control that looked like one. What already moved is a write this
plugin already made, and the record of where each of those came from is #44. So
the wording names the thing that does delete rather than leaving the reader to
assume this is it: on revocation, what came from the peer is deleted, which is
decision 5 on the pairing board taken on 2026-08-08 and is a different action
taken by a different person for a different reason.

**It may not soften "both directions" into "sending".** An opt-out honoured on
the way out and not on the way in is the failure that reads as working: the
person sees nothing of theirs arriving anywhere, and a peer's reading of what
they watched keeps landing in their account here. Both halves are one choice and
the wording states both.

**It may not describe the choice as a preference the operator can weigh.** The
opt-out is the stronger setting, and where it and the operator's per-pairing
selection disagree the opt-out wins. A wording that reads as a request leaves
somebody expecting an operator to honour it.

## What it stops, field by field

The choice is about the whole of what moves rather than a part of it, so the
fields it stops are exactly the ones `docs/sync-model.md` declares as moved:

| field | what a person is choosing to stop |
| --- | --- |
| `Played` | whether this server tells a paired server you watched something, and whether it applies that from one. |
| `PlayCount` | how often you watched it. |
| `PlaybackPositionTicks` | where you stopped, which is what a resume offers you. |
| `LastPlayedDate` | when you last watched it. |

`OptOutDocumentTests` refuses this table and the moved set disagreeing in either
direction. That is the drift this table would otherwise have: a field added to
what moves and not named here is a field a person was not told about when they
were asked to decide, and a row here for a field that does not move is a promise
to stop something that was never happening.

The rest of what a server holds about an item never moves at all, opt-out or not,
and `docs/sync-model.md` is where that set and the reason for each member are
argued. This document does not restate it.

## What holds this

The table above, by the check named at it. Nothing holds the wording itself, and
that is stated rather than left to be assumed: whether three sentences say what
this document says they say is a judgement about meaning, and no reading of this
tree makes one. The review is where a wording that has drifted is caught, and
the rules above are what it is read against.

## What is not here

The setting. THIS PARAGRAPH SAID WHERE IT IS STORED IS #58. That issue is closed
and the distinction it draws is drawn, in [configuration.md](configuration.md):

    grep -n 'opt-out in `docs/opt-out.md` is this kind' docs/configuration.md
    33:opt-out in `docs/opt-out.md` is this kind.

The rule there is that a per-user choice belongs with that user's record rather
than in the plugin configuration, because the configuration file is one an
operator copies between servers and a per-user choice copied to another server is
a choice somebody made about a different machine. So what is missing is not the
rule but the record: nothing in this plugin writes one per person, and until
something does there is nowhere this choice may be kept.

The surface. THIS PARAGRAPH NAMED #57 AS ONE OF THE TWO PLACES IT WOULD BE PLACED
IN, AND THAT ISSUE HAD ALREADY CLOSED WHEN THIS DOCUMENT LANDED. The page it asked
for is in the tree, and it is not a page this wording can go on, because it is
server-wide by construction and says so about itself:

    grep -n 'Every setting on this page is server-wide' Jellyfin.Plugin.WatchSync/Configuration/configPage.html
    23:                    Every setting on this page is server-wide. A setting belonging to one pairing is

A choice one person makes about their own history is neither server-wide nor an
operator's, so it does not belong on that document any more than a per-pairing
value does. The per-pairing selection this choice has to beat is #59. Nor does
this plugin serve a route a person could set it through: both of the endpoints it
has are elevated, so each is an operator acting about somebody else rather than a
person acting about themselves.

    grep -vE '^#|^$' Jellyfin.Plugin.WatchSync.Tests/Endpoints/policies.txt
    HeldAboutOnePersonController.Report :: GET :: Plugins/WatchSync/Persons/{mappedUserId}/Records :: RequiresElevation
    HeldAboutOnePersonController.Remove :: DELETE :: Plugins/WatchSync/Persons/{mappedUserId}/Records :: RequiresElevation

The wording above is written to be placed in a surface rather than to be one, and
that is unchanged.

The behaviour. That an opted-out person is skipped everywhere, that the opt-out
beats the pairing selection, and that opting out drops what was outstanding for
them are the first three conditions of #60, and each needs a run to be true of.
