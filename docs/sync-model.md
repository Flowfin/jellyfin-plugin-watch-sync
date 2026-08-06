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

`moved` means this plugin carries the field between servers. `refused` means it
never leaves the server it is on, and the reason is in the row. `held` means the
question is an open decision on this board, named in the row, and until it is
answered the field is not carried.

| property | disposition | why |
| --- | --- | --- |
| `Played` | moved | Whether the person watched the work. It is watch history by any reading, and it is the field the plugin exists for. #31 refuses regressing it to a partial position. |
| `PlayCount` | moved | How often the person watched the work. #33 reconciles it against the agreed record so that a sync never invents a play. |
| `PlaybackPositionTicks` | moved | Where the person stopped. It is the field a person notices immediately when it is wrong, and it is subject to the thresholds #17 sets, so a playback produces a bounded number of changes rather than one per progress report. |
| `LastPlayedDate` | moved | When the person last watched the work. It is what lets a disagreement about position be settled by recency rather than by whichever server spoke last, bounded by the tolerated clock skew in #32. |
| `Rating` | held, decision 1 in #1 | The 0 to 10 rating is an opinion rather than history. Whether an opinion is in scope is decision 1, and it carries a cost the history fields do not: the record holds no timestamp for it, so a two sided disagreement cannot be settled by recency without this plugin keeping a stamp of its own. #12 defines the moved set around it and #35 holds the conflict rule. |
| `IsFavorite` | held, decision 1 in #1 | An opinion rather than history, and the same missing timestamp. It rides with `Rating` because one answer settles both. |
| `AudioStreamIndex` | refused | It indexes the streams of one file as one server muxed it. The peer's copy of the same work can carry its streams in a different order, so moving the value across lands a correct number on the wrong stream, which is worse than carrying nothing. |
| `SubtitleStreamIndex` | refused | The same reason, and the visible failure is louder: subtitles in the wrong language, turned on by a sync the person did not ask for. |
| `Key` | refused | It is the server's own addressing for the item. Matching an item across two servers is what M3 does, from provider identity, and carrying this field would be storage identity arriving by another route. `docs/matching.md` refuses that class and says why. |
| `Likes` | refused | The server's own documentation says the field may not leave the server: "Gets or sets a value indicating whether the item is liked or not. This should never be serialized." It is marked to be left out of the server's own responses, so a plugin carrying it between servers would be building a channel for a value the server declines to publish. |

The reason a refusal is written here rather than only in a comment is that a
refusal without a reason reads as an oversight, and the next person to look at the
gap fills it.

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
exchange. What a first exchange does is decision 4 in #1, it is open, and #37
carries it. Nothing in this document decides it.

The record is bounded by the number of matched items and not by the number of
playback events. Watching one item a hundred times adds no rows.

## Direction

Which side moves the data is decision 3 in #1 and it is open.

A puller asks its peer what changed and applies it locally, so a server only ever
writes its own users' data. A pusher sends what changed and asks the peer to write,
which means a server accepting writes about its own users from outside. Both is the
most useful answer and twice the surface.

This document does not choose. #47 defines the transfer plane once the decision
lands, and the rest of M6 is written to be direction-agnostic, so the answer changes
what one exchange looks like and not what a change is, what the agreed record holds,
or which fields move. Nothing above this section depends on the answer.

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

- The direction, decision 3 in #1, open, landing in #47.
- Rating and favourite, decision 1 in #1, open, landing in #12 and #35.
- What a first exchange does, decision 4 in #1, open, landing in #37.
- The treatment of each reason the server gives when it saves user data, which
  #15 adds to this document.
- The position thresholds, their defaults and the reason for each default, which
  #17 adds to this document.
- The envelope version and the bounds on what one may carry, which are #18 and
  #19.

## How this document is held true

By a reading, at the review of the change that touches it.

Nothing in the tree compares the table above against the server's own record. The
suite does that for `docs/matching.md`, where `MatchingDocumentTests` refuses the
table and the server's enumeration disagreeing, and the same shape is owed here.
#12 is where it lands, because the check it asks for is the same check: every
property of the server's record is either mapped into this plugin's own type or
listed with its reason, so a property added upstream reddens the suite rather than
being dropped in silence.

Until that lands, a property added to the record on a future server line will not
be noticed by anything here. Re-run the command at the top of the field section
rather than trusting the ten rows below it.
