# The sync model

What moves between two paired servers, at what granularity, and what never moves.

The model is written down before it is built, so that the tests come from this
document rather than from the implementation, and so that somebody can disagree
with the design without reading code.

## The permanent non-goal

This plugin moves state about items. It never moves items.

Nothing in this family replicates files. A transfer carries no part of a media
file, no library path, and no credential. That is not a limit of the current
version and it is not a feature that arrives later; it is what the plugin is, and
every later issue references this paragraph rather than restating it.

The consequence is worth stating plainly, because it is the first thing an
operator assumes otherwise. Two paired servers do not end up holding the same
library. They end up agreeing about the items they both already hold, and about
nothing else.

## Vocabulary

These five words appear throughout the plan and mean one thing each.

A **peer** is another server this one has an active pairing with. A peer is a
server, never a user and never a library.

A **mapping** is the statement that a user on this server and a user on the peer
are the same person. This plugin consumes mappings and never infers one. The
pairing plugin owns them, and #42 holds the consumption rule.

A **change** is one field, on one item, for one mapped user, with the value and
the time this server observed it. A change is the smallest thing that moves.

An **envelope** is one transfer between two servers. It carries a version, a set
of changes, and nothing else. #18 versions it and #19 bounds what one may carry.

A **conflict** is two sides holding different values for one field of one item
for one mapped user, where neither value is the value the two sides last agreed.
M4 resolves conflicts, one rule per field, and #36 records every one of them with
its loser.

## The record the server holds, field by field

The server's per-user record is `UserItemData`. It carries ten properties, and
the set is the same on both server lines this plugin supports, so what moves is
one decision and not two.

Derived from the documentation file shipped inside the referenced package, so it
is read from the assembly this plugin actually compiles against rather than from a
source tree nobody has to have:

    GP="$(dotnet nuget locals global-packages --list | sed 's/^.*: //' | tr -d '\r')"
    grep -o 'P:MediaBrowser\.Controller\.Entities\.UserItemData\.[A-Za-z]*' \
      "$GP/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.xml" | sort -u
    P:MediaBrowser.Controller.Entities.UserItemData.AudioStreamIndex
    P:MediaBrowser.Controller.Entities.UserItemData.IsFavorite
    P:MediaBrowser.Controller.Entities.UserItemData.Key
    P:MediaBrowser.Controller.Entities.UserItemData.LastPlayedDate
    P:MediaBrowser.Controller.Entities.UserItemData.Likes
    P:MediaBrowser.Controller.Entities.UserItemData.PlaybackPositionTicks
    P:MediaBrowser.Controller.Entities.UserItemData.PlayCount
    P:MediaBrowser.Controller.Entities.UserItemData.Played
    P:MediaBrowser.Controller.Entities.UserItemData.Rating
    P:MediaBrowser.Controller.Entities.UserItemData.SubtitleStreamIndex

The two lines carry the same ten. The comparison is between the sorted sets rather
than between the files, because the rest of each file differs for reasons that have
nothing to do with this type:

    diff <(grep -o 'P:MediaBrowser\.Controller\.Entities\.UserItemData\.[A-Za-z]*' \
             "$GP/jellyfin.controller/10.11.11/lib/net9.0/MediaBrowser.Controller.xml" | sort -u) \
         <(grep -o 'P:MediaBrowser\.Controller\.Entities\.UserItemData\.[A-Za-z]*' \
             "$GP/jellyfin.controller/12.0.0-rc4/lib/net10.0/MediaBrowser.Controller.xml" | sort -u)
    exit=0

Ten is worth pausing on, because the count in the issue that opened this question
was nine. The tenth is `Likes`, and it is missing from a reading of the source
rather than from the record. A pattern anchored on a `public` declaration at a
fixed indentation does not report a property whose line is not shaped that way, and
the assembly is what settles it. This is the reason the table below is derived from
the package rather than from a grep over a checkout of the server.

Every property above appears exactly once in the table below.

Two dispositions, and the table admits no third:

- `moved`, which this plugin carries between servers, and which is a member of the
  moved set in `Jellyfin.Plugin.WatchSync/Model/SyncedState.cs`;
- `refused`, which never leaves the server it is on, and the reason is in the row.

There was a third, `held`, for a field whose disposition was an open decision. The
decision it waited on was taken on 2026-08-08 and the answer is that only history
moves, so the two fields carrying it are `refused` below with the reason recorded on
their rows. The disposition is gone rather than left unused, because a word declared
here and used by no row reads as a rule somebody removed from the table and forgot
in the prose. A field whose disposition is genuinely undecided is a field this
document is not ready to have a row for.

| property | disposition | why |
| --- | --- | --- |
| `Played` | moved | Whether the person watched the work. It is watch history by any reading, and it is the field the plugin exists for. #31 refuses regressing it to a partial position. |
| `PlayCount` | moved | How often the person watched the work. #33 reconciles it against the agreed record so that a sync never invents a play. |
| `PlaybackPositionTicks` | moved | Where the person stopped. It is the field a person notices immediately when it is wrong, and it is subject to the thresholds #17 sets, so a playback produces a bounded number of changes rather than one per progress report. |
| `LastPlayedDate` | moved | When the person last watched the work. It is what lets a disagreement about position be settled by recency rather than by whichever server spoke last, bounded by the tolerated clock skew in #32. |
| `Rating` | refused | The 0 to 10 rating is an opinion rather than history, and decision 1 in #1 answered on 2026-08-08 that only history moves. It also carries a cost the history fields do not: the record holds no timestamp for it, so a two sided disagreement cannot be settled by recency without this plugin keeping a stamp the server never writes, and two such stamps are equally old in a first exchange and come from two clocks afterwards, which #32 can only compare with a tolerance. |
| `IsFavorite` | refused | An opinion rather than history, and the same missing timestamp. It rode with `Rating` because one answer settled both, and the answer was the same. |
| `AudioStreamIndex` | refused | It indexes the streams of one file as one server muxed it. The peer's copy of the same work can carry its streams in a different order, so moving the value across lands a correct number on the wrong stream, which is worse than carrying nothing. |
| `SubtitleStreamIndex` | refused | The same reason, and the visible failure is louder: subtitles in the wrong language, turned on by a sync the person did not ask for. |
| `Key` | refused | It is the server's own addressing for the item. Matching an item across two servers is what M3 does, from provider identity, and carrying this field would be storage identity arriving by another route. `docs/matching.md` refuses that class and says why. |
| `Likes` | refused | A nullable boolean expressing like or dislike, beside the 0 to 10 `Rating`. The property on the record is marked not to be serialized and its own documentation says so: "Gets or sets a value indicating whether the item is liked or not. This should never be serialized." The transfer object the server's API returns carries the field anyway and is not marked, so the record and the API disagree about it. This plugin refuses it rather than picking a side in a disagreement it did not create. Refusing costs little: it is an opinion rather than history, which is the class decision 1 refused, so it would not have moved even if the record and the API agreed about it. |

The reason a refusal is written here rather than only in a comment is that a
refusal without a reason reads as an oversight, and the next person to look at the
gap fills it.

### The moved set as a type

The four `moved` rows are the whole of what this plugin carries, and they are a type
of its own: `SyncedState`, in `Jellyfin.Plugin.WatchSync/Model/SyncedState.cs`, with
one member per row and nothing else.

The server's record is not that type and is not used as one. A wire type that was
the server's record would carry every property a future server line adds to it into
the transfer on the day it is added, which is a decision nobody took, and it would
tie what two servers exchange to a type whose owner has no reason to keep it stable
for this plugin's sake. Refusing a field is then structural rather than careful: a
property that is not a member of `SyncedState` has no route into an envelope.

The cost of the closed set is worth writing down beside it. Widening it later is not
an addendum, it is a schema change after a release has shipped: the agreed record in
#14 gains members, the conflict table in #30 gains rows, and the store in #68 gains a
migration with a fixture per shipped version, which is #71. That cost is why the
table above is closed rather than left permissive.

## The unit a transfer is about

One mapped user and one leaf item. Nothing smaller is meaningful, and nothing
larger is carried.

A leaf item is one a person watches: a film, an episode. An aggregate is a
container of those: a series, a season, a collection, a playlist, a folder.
`docs/matching.md` fixes which kinds are which, and refuses aggregates as transfer
subjects by name.

Aggregates are never transferred, and the reason is not tidiness. The server does
not store the played state of an aggregate; it derives it from the leaf items under
it at the moment it is asked. So a transferred series-played has no place to land.
Applying it means marking every episode the peer holds, including the episodes the
peer has and the sender does not, which turns one watched series into a library of
false history. This is the failure the prior art keeps producing, and #13 refuses
it by construction rather than by care.

The receiving side leaves aggregate state to the server to derive. Applying a set
of episode changes writes to those episodes and to nothing above them.

## The record of what two sides last agreed

This plugin keeps its own record, per peer, per mapped user, per matched item, of
the state as both sides last agreed it, when that agreement was reached by this
server's clock, and the version of the envelope that carried it. #14 defines it and
M8's store holds it.

It exists because without it there are only two current values and no history, and
the only rule available to two current values is to overwrite. Overwriting picks a
winner by which server spoke last rather than by what happened, and it cannot tell
these three apart:

- a value that arrived from the peer moments ago, which must not be sent back;
- an old value that never moved, which is not news;
- a value the person deliberately changed back, which is news and must move.

The agreed record separates them. A local value equal to the agreed value is not a
change. A local value different from it is exactly one change. That is also the
first of the two mechanisms that stop an applied change leaving again as a local
one, which #16 holds.

Its absence is a defined state rather than an error. An item with no agreed record
has never been exchanged with that peer for that user, which makes it a first
exchange. Decision 4 in #1 was answered on 2026-08-08 and a first exchange merges
by the conflict table rather than seeding from one side: it applies the same rules
every later exchange applies, and an item it cannot decide stays undecided instead
of falling to a weaker rule. #37 holds the mode itself, which is a named one rather
than the ordinary path with an empty record, and the shape of what it records.

The record is bounded by the number of matched items and not by the number of
playback events. Watching one item a hundred times adds no rows.

## Direction

Data is pulled. Decision 3 in #1 was answered on 2026-08-08.

Each server asks its peer what changed and writes the result into the records of
its own users. No server accepts a write about its own users from outside, so the
only code that ever touches a person's history is the code running on the server
that holds their account.

The reason is the worst failure this plugin can have, which is one person's history
landing in another person's account. Pulling bounds that failure to the server that
holds the user: a wrong mapping there damages that server and reaches no further.
Pushing would make every server depend on the care of every peer it is paired with,
and #42 is where the mapping rule that failure turns on is consumed.

The second reason is the pairing plugin. Pulling works under both answers its own
decision 2 still has open. Pushing works only under the symmetric one, and if that
board settles on an initiator and a responder instead, a pushing plugin needs one
pairing per direction.

What it costs is stated with it. There is no immediate sync on an event. A peer
learns about a change when it next pulls, so the delay is the sweep interval in #55
rather than the time it takes to send one envelope. An event still matters, because
it is what puts a change where the next pull will find it, and #15 is the handler.

Two things follow that are not this document's to settle. #47 fixes the transfer
plane, meaning what one exchange is and what a failed one leaves behind. And M6 was
planned as direction-agnostic and is not: #48, #49, #50 and #54 read as pushing and
#51 reads as pulling, so the outbound queue in those four becomes a list of changes
the peer reads on request. Idempotence is still owed, on the answer rather than on
the envelope.

## What a transfer never contains

Stated separately from the field table because it is about what is absent from the
envelope rather than about a field's disposition.

No part of a media file, in any encoding, at any size. No library path, no file
name, no folder layout, and no storage location of any kind; `docs/matching.md`
refuses those as match inputs and this refuses them as cargo. No credential, no
token, and no server or user secret.

An envelope carries changes, and a change carries a field, a value, an item
identified the way M3 identifies items, a mapped user, and a time. #19 bounds how
many of those one envelope may hold.

## What this document does not fix

Named so that a gap is readable as a gap rather than as an answer nobody wrote
down.

- What one exchange is, who starts one, whether two may overlap on one pairing and
  what a failed one leaves behind, which is `docs/transfer.md` and #47. The
  direction that document is written against is the section above.
- What a first exchange records and how it is distinguishable from an ordinary run,
  which is #37. The rule it applies is the section above.
- The treatment of each reason the server gives when it saves user data, which
  #15 adds to this document.
- The position thresholds, their defaults and the reason for each default, which
  #17 adds to this document.
- The envelope version and the bounds on what one may carry, which are #18 and
  #19.

## How this document is held true

By the suite, for the field table, and by a reading at review for everything else.

`SyncModelDocumentTests` reads the properties of the server's record off the
referenced assembly by reflection, reads the rows of the field table out of this
file, and refuses the two disagreeing in either direction: a property with no row,
a row naming no property, and a property named twice. It reads the members of
`SyncedState` the same way and refuses a `moved` row that is not a member and a
member that is not a `moved` row. So a property added on a future server line
reddens the suite rather than being dropped in silence, and a field moved into or
out of the moved set has to move in the table and in the type together.

The reflection is over the assembly this project compiles against, which is a
different one per target, and the suite runs once per target. So the table is
judged against both server lines rather than against whichever one happened to
build.

What the suite does not judge is whether a row's reason is the right reason. That
is a reading at review, and it is the same bound `docs/matching.md` carries.
