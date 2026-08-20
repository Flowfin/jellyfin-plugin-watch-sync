# Conflict resolution

A conflict is a moment where two paired servers hold different values for one
mapped user, one leaf item and one moved field, and this plugin has to answer with
one of them. Answering means discarding something a person did on one of the two
servers, which is defensible when the rule is right and indefensible when nobody
can find out that it happened.

Latest wins is a decision rather than a default, and this plan does not take it
wholesale. One field ratchets, one is computed rather than chosen, one is settled
by recency inside a tolerance, and one is the evidence the others are settled by.
The first exchange between two servers is the same table rather than a special
case, which is a section below rather than a column here.

The reason a table exists rather than one sentence is visible in the prior art. A
tool that resolves everything by recency turns a stale partial position into the
winner over a completed watch and reverts a finished episode every hour
(https://github.com/luigi311/JellyPlex-Watched/issues/322). A tool that resolves
everything by overwrite does the same thing more quickly and in one direction
(https://github.com/GermanCoding/jellyfin-server-sync).

## How to read a row

One row per field this plugin syncs. `docs/sync-model.md` is where the moved set
is decided and this file does not restate it: a field is here because it is moved
there, and the suite refuses the two disagreeing.

The rule column is a closed set, and every word in it is declared here:

- `ratchet`, where one state is stronger than the other and wins whatever the two
  clocks say. The rule reads no time at all, which is the property that separates
  it from a recency rule with a wide tolerance.
- `reckon`, where the answer is computed from what the two sides last agreed
  rather than chosen between the two readings. Neither reading is a winner, and a
  side that is behind is not lowered.
- `recency`, where the later reading wins, bounded by the tolerated clock skew,
  with the tie rule written down rather than left to whichever side is asked
  first.
- `maximum`, where the answer is the greater of the two values because the field
  is a high-water mark of something that already happened, so neither reading is
  discarded and there is nothing to record as a loser.

The evidence column says what the rule reads. The loser column says what happens
to the value that did not win, because a value discarded in silence is the failure
this document exists against and #36 is the record it is written into. The last
column says which failure the row prevents, and where a prior art failure is the
reason it is linked.

| field | rule | the evidence the rule uses | what happens to the value that lost | the failure it prevents |
| --- | --- | --- | --- | --- |
| `Played` | ratchet | The played state on both sides, and the agreed record where one side turned it off since the last agreement. The two last played dates reach the rule and are never read by it. | The position offered against a completion is discarded, and it is carried out of the rule rather than dropped inside it, so the record of the conflict names what was lost. | A stale partial position beating a finished watch, which is what a pure recency rule produces: the person watches the last twenty minutes again after every run, because nothing about the pair has changed (https://github.com/luigi311/JellyPlex-Watched/issues/322). |
| `PlayCount` | reckon | Both counts and the count the two sides last agreed. Each side's plays since that agreement are what it holds above the agreement. | Nothing is discarded. A side below the agreement is carried up rather than the other side being lowered, and the shortfall is recorded, because an operator is the only one who can tell a restore from a defect. | Adding the two counts re-counts every play that already moved, so two servers with nothing new to say still climb by the whole history on every run. Taking the newer count overwrites a rewatch the other side recorded, and the field carries no per-play timestamp for anything to notice that with. |
| `PlaybackPositionTicks` | recency | Both positions and both last played dates, where neither side holds the work played. A difference smaller than the tolerated skew is not a comparison at all and the tie rule applies instead, which is the greater position. | The older position is discarded and recorded. A peer whose clock is outside the tolerated skew produces a refusal that names the clock and is distinct from every other refusal, because a clock failure that reads as anything else costs an evening. | Two home servers disagreeing by seconds deciding which of two positions a person is further into, and a rule that asks whichever side it happens to hold first answering differently in the two directions. |
| `LastPlayedDate` | maximum | Both dates. Nothing else, because this field is what the other rules read rather than a field they decide. | Nothing. The earlier date is not a loss: it is a moment that happened and that the later one already accounts for. | A sync moving somebody's last played date backwards, which is the field the position rule reads, so an answer that lowers it decides the next exchange as well as this one. |

## The first exchange is this table and nothing else

Two servers that have never exchanged anything for one user and one item have no
agreed record for it, and that is a defined state rather than a missing one.

What the first run does was decided on #37: it applies the same conflict table as
every later exchange, seeds no side, overwrites nothing, and records what it
cannot decide
(https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/37#issuecomment-5228337224).

That is worth writing down because a reader who is not told it will look for a
special rule per field on a first run, and because the alternative is the failure
the whole plan is written against: a fresh or restored server meeting an
established one and being treated as an authority over it.

Two of the rows behave differently without an agreement, and neither of them is a
different rule. `Played` ratchets on the two states alone, so a first exchange
answers it exactly as the hundredth does, and the deliberate unplayed that #34
holds against the ratchet is the case that needs an agreement and therefore cannot
arise on a first run. `PlayCount` cannot be reckoned without an agreement, because
two sides each holding one play may be one play that already moved or two
watchings that never met, and no reading of the two numbers separates those. That
case is answered by #37 rather than guessed at by the rule.

## The pair that makes either rule safe

Marking something unwatched is a real thing people do, before a rewatch or after
somebody else used their account. The `Played` row says the played state never
regresses to a position, and read on its own that rule makes an unmark impossible
to carry: the peer holds the work played, the person turns it off here, and the
ratchet hands the win back to the peer on the next exchange, for as long as both
servers are running. So the two rules contradict each other on their face, and
this section is where they are reconciled rather than left to be discovered by
somebody reading one row.

They are reconciled by the agreed record and by nothing else. A local transition
from played to unplayed since the last agreement is a change with an intent behind
it, and it wins over a played state the peer has held unchanged since before that
agreement. A peer that never agreed to the played state in the first place is not
offering an intent, it is offering an old value, and the ratchet answers it.

The failure in the other direction is the more dangerous one and is why this is
not simply softened into letting any unplayed beat any played. A fresh server with
an empty library, or one restored from a backup taken before any of this history
existed, holds every item unplayed and has agreed nothing. Under a rule that reads
unplayed as intent without asking whether an agreement stands behind it, first
contact between that server and an established one wipes the established one's
history, and what was overwritten is the part nobody can reconstruct.

So neither rule is safe on its own. The ratchet without the agreed record loses
every deliberate unmark, and the intent rule without the ratchet is the wipe. #34
holds the pair and #31 holds the ratchet, and the pair is the reason a later
reader should not remove either half as redundant.

What the tree holds today is the ratchet and not the pair. `PlayedRatchet` decides
on the two played states alone, and it reads no agreed record because there is no
agreed record: that is #14, and it is what the second half of this section waits
for. Until then a deliberate unmark is carried by nothing, which is a gap rather
than a rule, and this document says so here rather than describing a reconciliation
that no code performs.

The save reason the unmark arrives under is `TogglePlayed`, which
`docs/sync-model.md` classifies with the rest, so the event reaches the plugin and
the question this section answers is what is done with it rather than whether it is
seen.

## Which rows have a rule in the sources today

Three of the four. The document is ahead of the code deliberately, and this
section is here so that a reader cannot mistake a row for something running. The
listing is taken at the commit being read rather than at a remote reference, so it
answers for the tree in front of the reader:

    git ls-tree -r --name-only HEAD -- Jellyfin.Plugin.WatchSync/Conflict/
    Jellyfin.Plugin.WatchSync/Conflict/PlayCountAnswer.cs
    Jellyfin.Plugin.WatchSync/Conflict/PlayCountReconciliation.cs
    Jellyfin.Plugin.WatchSync/Conflict/PlayedRatchet.cs
    Jellyfin.Plugin.WatchSync/Conflict/PositionAnswer.cs
    Jellyfin.Plugin.WatchSync/Conflict/PositionRecency.cs
    Jellyfin.Plugin.WatchSync/Conflict/RatchetAnswer.cs

`PlayedRatchet` is the `Played` row and is #31. `PlayCountReconciliation` is the
`PlayCount` row and is #33. `PositionRecency` is the `PlaybackPositionTicks` row
and is #32: the tie rule is a branch rather than a sentence, and the two
boundaries the tolerance draws are opposite, because a difference of exactly the
tolerance is a comparison while a peer date exactly the tolerance ahead of this
server's present moment is not yet outside it. The `LastPlayedDate` row is decided
nowhere in the sources yet.

The tolerance is a number that type declares rather than one this page holds, so
the two cannot drift apart:

    git grep -n 'ToleratedSkew =>' -- Jellyfin.Plugin.WatchSync/Conflict/PositionRecency.cs
    Jellyfin.Plugin.WatchSync/Conflict/PositionRecency.cs:55:    public static TimeSpan DefaultToleratedSkew => TimeSpan.FromMinutes(1);
    Jellyfin.Plugin.WatchSync/Conflict/PositionRecency.cs:70:    public static TimeSpan MaximumToleratedSkew => TimeSpan.FromMinutes(15);

The reason for each is beside it in that file. The default is where a server with
a time source and a server without one fall on opposite sides. The maximum is a
refusal rather than advice, because everything inside the tolerance is answered by
the tie rule, so a tolerance wide enough to hold a viewing session would make the
tie rule the rule and recency the exception.

## What this document does not fix

Named so that a gap is readable as a gap rather than as an answer nobody wrote
down.

- The setting an operator changes the tolerated clock skew with, which is #58.
  The two numbers are fixed by the rule in #32, which refuses a tolerance outside
  them, and nothing reads either at run time, because no run resolves anything
  yet.
- Where a resolved conflict is recorded, what the record holds and how long it is
  kept, which is #36. This file fixes only that a loser exists and what it is.
- What a run does when the number of changes it is about to make is large, which
  is the cap in #38 and is a rule about a run rather than about a field.
- Whether an applied answer is idempotent, which is #50 and is a property of the
  apply path rather than of a row here.

## How this document is held true

By the suite, for the field column, and by a reading at review for everything
else.

`ConflictTableTests` reads the members of `SyncedState` by reflection and the rows
of the table out of this file, and refuses the two disagreeing in either
direction: a moved field with no row, a row naming something that is not a moved
field, and a field named twice. So a field added to the moved set reddens the
suite rather than crossing between two servers under no rule, and a row for a
field that stopped moving is refused rather than left standing.

The rule column is closed against the rules declared above, in both directions: a
row carrying a word the document never declared is refused, and a rule declared in
the prose that no row uses is refused as well. The evidence, loser and failure
columns are refused when empty, because a row asserting a rule and giving no
reason for it is the shape that reads as an oversight and gets filled in by
whoever notices it next.

What the suite does not judge is whether a row's rule is the right rule, or
whether the failure it names is the failure it prevents. That is a reading at
review, and it is the same bound `docs/sync-model.md` and `docs/matching.md`
carry.

The third condition of #30 asks for a test per row that fails when that row's rule
is removed, and the fourth asks that this table be the input to the resolver
rather than a description of it. Neither is met here and neither can be met by a
document: there are two rules in the sources and no resolver for the table to be
the input of. What is held today is the field column and the closure of the rule
column, which is the half that stops the table and the moved set drifting apart
while the rest is built.
