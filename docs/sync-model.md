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

## One work held in several versions

A leaf item can be one work the server holds as several files: two cuts of a film,
two encodes of one cut, a copy somebody kept at a lower resolution. The server
presents those as one item, so a change arrives about the work rather than about a
file. Three of the four moved fields are about the work and one of them is not.

Whether the person watched it, how often, and when they last did are properties of
the work. Somebody who watched the extended cut watched the film. Those three are
applied to the item, and the question of which version does not arise.

The position is not a property of the work. A tick counts from the start of one
particular file, and the same number names a different moment in a version of
another length. So the position is applied to the version this server would resume,
and only where that version's runtime and the runtime the peer sent for its own are
within one minute of each other. Where they differ by more, or where the peer sent
no runtime at all, the position is dropped, the other three fields are applied, and
the drop is recorded against the item with both runtimes.

The same absence on this side is the same case. An item this server has not analysed
yet carries no runtime either, and without both numbers the displacement cannot be
bounded, so the position is dropped for that reason as well. The sentence above names
the peer's side because that is the side whose number travels, and a reader should not
take the silence about this one for a decision to apply a position on the strength of
a number that is not there.

### Why one minute

The displacement a position can carry is the difference between the two runtimes. A
version four minutes longer puts the same tick up to four minutes from the moment
the person stopped at, and where the extra length sits at the head, it puts it there
from the first frame rather than only at the end.

Under a minute, the difference is packaging. A distributor logo, a few seconds of
black, a container that padded the end. The tick lands in the same scene, and the
person resumes a little early or a little late, which is what they do by hand
anyway. Over a minute, the difference is an edit or a speed conversion, and both
move the whole timeline: a theatrical cut against an extended one, a recap or a
title sequence one version carries and the other does not, a frame rate conversion
that takes three and a half minutes off a ninety minute work.

The boundary is not sharp and the number sits at the small end of it deliberately,
because the two mistakes do not cost the same. A position refused where it would
have been fine costs the person the few seconds it takes to find their place, and it
is recorded where they can see why. A position applied where it should not have been
drops them into a scene they had not reached, which is the one failure here nobody
can take back.

It is fixed rather than offered as a setting. An operator cannot see the two
runtimes side by side at the moment the question is asked, so it is not a number
they are in a position to judge, and a setting that is always left at its default is
a default with a support burden attached. #58 is where that would be revisited if a
real library argues against it.

### Why the drop is not silent

A dropped position that nothing records is indistinguishable from a position that
never moved, and the second is what an operator assumes. The drop is recorded
against the item with the two runtimes that produced it, and #62 is the surface it
is read from.

Dropping the position never drops the rest of the change. The three fields about the
work are applied whatever happens to the position. An implementation that treats one
item's change as one unit fails exactly here, because the position and the played
state arrive together, and refusing the pair is how a watched film comes out
unwatched on the other server.

### What of this has a rule in the sources today

The comparison and the answer, and nothing on either side of them. The listing is taken
at the commit being read rather than at a remote reference, so it answers for the tree in
front of the reader:

    git ls-tree -r --name-only HEAD -- Jellyfin.Plugin.WatchSync/Versions/
    Jellyfin.Plugin.WatchSync/Versions/VersionLanding.cs
    Jellyfin.Plugin.WatchSync/Versions/VersionLandingAnswer.cs

The tolerance is a number that type declares rather than one this page holds, so the two
cannot drift apart:

    git grep -n 'WidestRuntimeDifference =>' -- Jellyfin.Plugin.WatchSync/Versions/VersionLanding.cs
    Jellyfin.Plugin.WatchSync/Versions/VersionLanding.cs:66:    public static TimeSpan WidestRuntimeDifference => TimeSpan.FromMinutes(1);

One end of what the rule is between has arrived since. Which version this server would
resume, and therefore whose runtime is handed in, is answered by the adapter #20 asks for,
and it is answered in a different place on each line:

    git ls-tree -r --name-only HEAD -- Jellyfin.Plugin.WatchSync/UserData/
    Jellyfin.Plugin.WatchSync/UserData/IUserDataGateway.cs
    Jellyfin.Plugin.WatchSync/UserData/NewerLineUserData.cs
    Jellyfin.Plugin.WatchSync/UserData/OlderLineUserData.cs
    Jellyfin.Plugin.WatchSync/UserData/ServerUserData.cs
    Jellyfin.Plugin.WatchSync/UserData/UserDataReading.cs

A read answers with the moved set and that runtime together, so the number this rule is
handed is the length of the version this server would resume rather than the length of the
item. The two are the same on the older line and are not on the newer one, which is the
whole of the difference.

What is still not there is the other end and the surfaces. Nothing calls the adapter, so
the rule decides and nothing yet asks it. Where a dropped position is recorded is #26 and
the surface it is read from is #62, so a row on a status page saying how many positions
were dropped is a thing this page describes rather than a thing a server does.

### What the two lines answer differently

Both lines carry a runtime on the item, so the comparison above is available on
each. With `GP` set as the section above sets it:

    for v in 10.11.11/lib/net9.0 12.0.0-rc4/lib/net10.0; do
      printf '%s ' "${v%%/*}"
      grep -c 'P:MediaBrowser\.Controller\.Entities\.BaseItem\.RunTimeTicks' \
        "$GP/jellyfin.controller/$v/MediaBrowser.Controller.xml"
    done
    10.11.11 1
    12.0.0-rc4 1

What the two lines do not share is any notion of which version drives the resume
point. It is in the reference assembly of one and absent from the other:

    for v in 10.11.11/lib/net9.0 12.0.0-rc4/lib/net10.0; do
      printf '%s: ' "${v%%/*}"
      grep -oE 'VersionResumeData|GetResumeUserDataBatch|GetResumeUserData' \
        "$GP/jellyfin.controller/$v/MediaBrowser.Controller.xml" | sort -u | tr '\n' ' '
      echo
    done
    10.11.11:
    12.0.0-rc4: GetResumeUserData GetResumeUserDataBatch VersionResumeData

That is a match on names in the documentation shipped with each package, so it says
the names are present in one reference set and absent from the other. It does not
say what the members do.

The newer line's answer names a version and carries no runtime with it:

    grep -oE 'MediaBrowser\.Controller\.Library\.VersionResumeData\.[A-Za-z]+' \
      "$GP/jellyfin.controller/12.0.0-rc4/lib/net10.0/MediaBrowser.Controller.xml" | sort -u
    MediaBrowser.Controller.Library.VersionResumeData.ApplyTo
    MediaBrowser.Controller.Library.VersionResumeData.UserData
    MediaBrowser.Controller.Library.VersionResumeData.VersionId

So the same question is answered in two places. On the newer line the server names
the version and this plugin reads the runtime of the item that identifier names. On
the older line there is no version to name and the item's own runtime is the answer.
The caller asks one question either way, which is the whole reason the difference
sits behind the adapter in #20: two lines that answer a question differently is a
place where they quietly behave differently, and the promise that they do not is
kept in the adapter or nowhere.

That adapter exists and the promise is kept by facts written against its interface rather
than against either implementation, so each of them runs on both legs of the suite and a
difference that showed on one line only would redden the leg it showed on. What the two
implementations do underneath is covered separately, in a file compiled only into the
target its line is built for, because a fact about a batch read cannot be written on a line
that has no batch read.

ONE THING THIS ADAPTER DOES NOT ANSWER, FOUND WHILE WRITING IT AND WRITTEN HERE RATHER THAN
LEFT TO BE MET. The newer line does not only name a version: it merges that version's state
into what the server shows for the work. The merge is three lines and it is not a
replacement.

    git -C jellyfin show v12.0-rc4:MediaBrowser.Controller/Library/VersionResumeData.cs | sed -n '25,38p'
                dto.Played = dto.Played || UserData.Played;

                if ((UserData.LastPlayedDate ?? DateTime.MinValue) > (dto.LastPlayedDate ?? DateTime.MinValue))
                {
                    dto.LastPlayedDate = UserData.LastPlayedDate;
                }

                // A different version was finished (played, no resume position of its own) and is the most
                // recently played: the whole movie is watched.
                if (!VersionId.Equals(dto.ItemId) && UserData.Played && UserData.PlaybackPositionTicks <= 0)
                {
                    dto.PlaybackPositionTicks = 0;
                    dto.PlayedPercentage = null;
                }

So on that line a person who finished the extended cut sees the work as watched, and the
item's own record may still say otherwise. The adapter reads the item's own record on both
lines, which is one behaviour rather than two, and it is the behaviour that reports the
smaller of the two states for a work held in several versions. Whether what this plugin
offers a peer should be the merged view is a question about what leaves a server rather
than about where an arriving position lands, so it belongs with the moved set in #12 and
the handler in #15. Nothing reads either record yet, so nothing is wrong today, and this
paragraph is here so that the day something does, the question has already been asked.

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

## The reason the server gives when it saves

The server raises one event whenever it saves what a user's record holds for an
item, and the event carries the reason it saved. That reason is the first thing the
handler in #15 reads, before the item and before the user, because one of the seven
arrives several times a minute while something is playing and one of them can change
nothing this plugin carries.

The seven are the same set on both server lines, so the table below is one decision
and not two:

    GP="$(dotnet nuget locals global-packages --list | sed 's/^.*: //' | tr -d '\r')"
    for v in 10.11.11/lib/net9.0 12.0.0-rc4/lib/net10.0; do
      printf '%s: ' "${v%%/*}"
      grep -o 'F:MediaBrowser\.Model\.Entities\.UserDataSaveReason\.[A-Za-z]*' \
        "$GP/jellyfin.model/$v/MediaBrowser.Model.xml" | sed 's/.*\.//' | sort | tr '\n' ' '
      echo
    done
    10.11.11: Import PlaybackFinished PlaybackProgress PlaybackStart TogglePlayed UpdateUserData UpdateUserRating
    12.0-rc4: Import PlaybackFinished PlaybackProgress PlaybackStart TogglePlayed UpdateUserData UpdateUserRating

Three treatments, and every reason carries exactly one of them.

- The treatment `enqueued` reads the event and turns every moved field whose value
  differs from the record of what the two sides last agreed into one change. Nothing
  else is carried, and a value equal to the agreed one is not a change. Direction is
  pull, so the change waits for the peer to fetch it, which is the queue in #48.
- The treatment `thresholded` is the same as `enqueued`, behind the position
  thresholds #17 fixes. A position that has not moved past the threshold is counted
  and carried no further, and the position that does leave is the one the playback
  stopped at rather than one report in the middle of it.
- The treatment `dropped` is for a reason under which no moved field can change. The
  event is counted at the handler and carried no further. Counted rather than
  ignored, because a reason that has stopped arriving is a change this plugin has
  stopped noticing, and #62 is the surface a count is read from.

| the reason | treatment | what the server writes under it | why that treatment |
| --- | --- | --- | --- |
| `PlaybackStart` | enqueued | `PlayCount`, `LastPlayedDate`, and `Played`, which it sets false on an item that supports resuming | Starting something is a play the server has already counted, and on a resumable item it also writes `Played` false. Both are the server's own state about what was watched, so both move, and what the receiving side does with them is #33 and #31 rather than a decision here. |
| `PlaybackProgress` | thresholded | `PlaybackPositionTicks`, and `Played` where the position reaches the end of the item | One save per progress report, several a minute for as long as something plays, and every one of them but the last is a position nobody will ever resume from. |
| `PlaybackFinished` | enqueued | `PlaybackPositionTicks`, and `PlayCount`, `Played` and a zeroed position where the client reported no position at all | The stop is the moment the position is worth carrying, which is what lets the threshold above drop every report before it without losing where somebody got to. |
| `TogglePlayed` | enqueued | `Played`, `PlayCount`, `PlaybackPositionTicks` and `LastPlayedDate` | Somebody said watched or unwatched by hand. It is the reason a deliberate unplayed arrives under, which #34 holds against the ratchet undoing it. |
| `UpdateUserRating` | dropped | `IsFavorite`, or `Likes` | Both are refused by the field table above, so no moved field can change under this reason. |
| `Import` | enqueued | `Played`, `PlayCount` or `LastPlayedDate`, one save per element of the metadata file | A metadata file writes the same fields a person watching writes, and a scan writes them for a whole library at once, which is the run the cap in #38 stands against rather than a reason to stop reading them. |
| `UpdateUserData` | enqueued | every moved field, as one document | The route the server's own interface writes through, so anything with an access token can produce it, and so can another plugin. |

### Where each reason is written

Read at the two tags the referenced packages are built from, so the table above is a
statement about both lines rather than about the newer one:

    for tag in v10.11.11 v12.0-rc4; do
      printf '%s\n' "$tag"
      for r in PlaybackStart PlaybackProgress PlaybackFinished TogglePlayed \
               UpdateUserRating Import UpdateUserData; do
        printf '  %-17s ' "$r"
        git -C jellyfin grep -l "UserDataSaveReason\.$r" "$tag" -- '*.cs' \
          | grep -v 'UserDataSaveReason.cs' | sed "s/^$tag://" | tr '\n' ' '
        echo
      done
    done
    v10.11.11
      PlaybackStart     Emby.Server.Implementations/Session/SessionManager.cs
      PlaybackProgress  Emby.Server.Implementations/EntryPoints/UserDataChangeNotifier.cs Emby.Server.Implementations/Session/SessionManager.cs
      PlaybackFinished  Emby.Server.Implementations/Session/SessionManager.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      TogglePlayed      MediaBrowser.Controller/Entities/BaseItem.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      UpdateUserRating  Jellyfin.Api/Controllers/UserLibraryController.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      Import            MediaBrowser.XbmcMetadata/Parsers/BaseNfoParser.cs
      UpdateUserData    Jellyfin.Api/Controllers/ItemsController.cs
    v12.0-rc4
      PlaybackStart     Emby.Server.Implementations/Session/SessionManager.cs
      PlaybackProgress  Emby.Server.Implementations/EntryPoints/UserDataChangeNotifier.cs Emby.Server.Implementations/Session/SessionManager.cs
      PlaybackFinished  Emby.Server.Implementations/Session/SessionManager.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      TogglePlayed      MediaBrowser.Controller/Entities/BaseItem.cs MediaBrowser.Controller/Entities/Video.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      UpdateUserRating  Jellyfin.Api/Controllers/UserLibraryController.cs MediaBrowser.XbmcMetadata/NfoUserDataSaver.cs
      Import            MediaBrowser.XbmcMetadata/Parsers/BaseNfoParser.cs
      UpdateUserData    Jellyfin.Api/Controllers/ItemsController.cs

`NfoUserDataSaver` is in that listing because it reads the reason off the event and
not because it writes one, which is worth separating: it is another consumer of the
same event, filtering on three of the seven.

The one difference between the two lines is `Video.cs` under `TogglePlayed` on the
newer one, where marking a video played writes a document carrying `Played` and, on
a reset, a zeroed position. It is the same reason with the same fields, reached from
one more place.

The write columns above were read out of those two trees. Nothing in this repository
compares them against anything, so a line that starts writing a different field
under a reason it already raises leaves this table saying what used to be true. A
reason added or removed upstream is the half a machine does hold, and it is under
`## How this document is held true`.

### The echo is not a reason

Two of these reasons look like the place to recognise this plugin's own write coming
back, and neither of them is, because the reason is chosen by whoever calls the
save. It is a parameter on the interface, on both lines:

    GP="$(dotnet nuget locals global-packages --list | sed 's/^.*: //' | tr -d '\r')"
    for v in 10.11.11/lib/net9.0 12.0.0-rc4/lib/net10.0; do
      printf '%s: ' "${v%%/*}"
      grep -c 'M:MediaBrowser\.Controller\.Library\.IUserDataManager\.SaveUserData([^)]*UserDataSaveReason' \
        "$GP/jellyfin.controller/$v/MediaBrowser.Controller.xml"
    done
    10.11.11: 2
    12.0-rc4: 2

Both overloads take a `UserDataSaveReason` from the caller, so a write this plugin
makes carries whichever reason this plugin passes, and a write somebody else makes
can carry the same one. `Import` and `UpdateUserData` are the two an applied change
would plausibly arrive under, and both of them are also produced without this plugin
being involved: `Import` by a metadata scan and `UpdateUserData` by anything holding
an access token.

So no treatment in the table above is `dropped` on the ground that the event is this
plugin's own. The echo is stopped by the agreed record and by the suppression in
#16, which compare the value against what this plugin just applied, and the reason
is not evidence either way. What the apply path passes is decided where that path is
built, in #54, and it changes nothing here.

### What the treatment does not decide

The treatment is what the reason decides and it is not the whole gate. An event
about an item this plugin does not sync, or about a user with no mapping, is dropped
at the handler whatever its reason, and the mapping is consumed rather than inferred
under #42. #15 carries both.

## The suppression window

The plan takes two mechanisms against the echo and they are not equals. The first is
the record of what two sides last agreed: an echo is a value already equal to what was
agreed, so it leaves nothing outstanding and never becomes a change. That covers every
echo the server hands back unchanged, which is nearly all of them.

The second covers the one case the first cannot see. Where the server normalises a
value on the way in, what this server holds afterwards is this plugin's own write as
the server stored it, and it is not the value that arrived. The comparison against the
agreement finds a difference and the difference is real, so without a second mechanism
it leaves as a local change, the peer applies it, its own server normalises something,
and one watched episode becomes an exchange with no end.

`EchoWindow` is that second mechanism. It is asked about one field of one mapped user
and one leaf item, and it is told three things: whether the field is outstanding
against the agreement, when this plugin last wrote that field itself, and when the
reading was observed. Where the field is outstanding and this plugin's own write
stands inside the window, the answer is that the difference is the server's
normalisation of it, and what the caller does is agree what is stored rather than send
it back. Agreeing is what ends the exchange at one write on each side; leaving the
difference outstanding is the same exchange one round slower.

### Why the order of the two questions is the rule

Whether anything is outstanding is asked first, and where nothing is, the window is
not consulted at all. The rule carries that out rather than leaving it to be inferred,
because it is the property that keeps the second mechanism second.

A window asked before the record would answer a question the record has already
answered. Worse, it would suppress echoes on values the server never normalised, which
is a defect in the agreed record rather than a case for a window, and nothing would
say so: the sync would work, and what carried it would be the mechanism that exists to
cover a gap in the other one.

### What the window cannot see

It is told that this plugin wrote the field and when, and never what was written. So a
person who changes the same field of the same item inside the window has their change
read as the server's normalisation, and it does not leave this server. Nothing is
lost - the person's value stands here - and what converges it is the full
reconciliation in #52 rather than anything in this rule.

That residual is why the window is short and why it is bounded rather than left to an
operator. Past the bound it stops covering a server normalising a value and starts
covering a person acting, and somebody who un-marks a work a few minutes after a sync
applied it is making the deliberate change #34 exists to carry. Both numbers and where
each of them lives are in `docs/configuration.md` rather than repeated here.

A window of nothing is legal and switches the second mechanism off, leaving the agreed
record carrying the rule alone. That is the state the plugin is in whenever the record
is enough, so it is a setting an operator may choose rather than a configuration the
rule refuses.

### What of this has a rule in the sources today

The rule and its two numbers, in `EchoWindow`, held by `EchoWindowTests`. Nothing
calls it. The event it would be asked from is #15 and the walk whose writes it would
be told about is the apply path in #54, which records nothing about when it wrote, so
what a caller reads for the second parameter does not exist yet. The three assertions
#16 asks for are each over an apply and an event together, and they arrive with that
caller rather than with this rule.

## The position thresholds

The treatment `thresholded` above is one reason and three numbers. The reason
arrives several times a minute for as long as something is playing, and all but a
handful of the positions it carries say something the next one contradicts. These
are the numbers that decide which handful.

Each is a setting an operator of this server changes, and the table below names the
one that carries it. Where each lives and what bounds it is `docs/configuration.md`;
what is fixed here is the value each setting defaults to and the reason for that
value, which is the half of a setting no page can state.

A setting stores a whole number of seconds and the rule takes a span, and the number
in this table is the same number in both. `PluginConfiguration` derives each default
from the rule rather than repeating it, so a default moved on the rule moves on the
page too, and neither this table nor the setting can go on stating the number it used
to be.

| the threshold | default | the setting | why that default |
| --- | --- | --- | --- |
| `move` | 5 minutes | `PositionMoveSeconds` | How far a position moves while something is still playing before the move is worth carrying. At five minutes a two hour work produces at most twenty four changes however many reports the player sent, so the count follows the length of the work rather than the chattiness of the client. |
| `finish` | 2 minutes | `PositionFinishSeconds` | How close to the end a position has to be to be a finish rather than a place to resume from. It is the length of what sits after the last thing anybody watches: credits, a distributor card, the black at the end of a container. |
| `shortestItem` | 5 minutes | `PositionShortestItemSeconds` | The length below which no position is carried at all. Below it a resume point is not a thing anybody uses, and a position on a trailer or a clip fills the record of what two sides last agreed on the part of a library that has the most items in it. |

Three of the four rules refuse and one converts, which is the shape to read them in.
The order they are asked in is a decision rather than a convenience. The length of
the item is asked first, because an item nobody resumes carries no position whatever
the report says, a stop included. The finish is asked next, because a stop at the end
of a work is a finish and not a resume point, and asking the stop first would carry
the end of every film as a position. The stop is asked before the move, because the
only thing that lets the move threshold be as coarse as it is that the stop always
survives it.

The two boundaries are drawn in opposite directions on purpose. A position exactly
the finish distance from the end is a finish, because the distance is the widest gap
that still counts as the end. A move of exactly the threshold is not yet a change,
because the threshold is the largest move that is still too small to carry.

### Why a finish is carried as watched and not as a number

A tick counts from the start of one file and the peer holds its own, which is the
same fact `## One work held in several versions` above is about. Two servers with
different runtimes for one work disagree about where the end is, so a position near
it, carried as a position, becomes an offer to resume a few minutes from the end of
something the person has finished. Carried as watched it is the same statement on
both sides whatever either runtime says.

The finish carries no position beside the watched state. A resolution carrying both
would hand the receiving side the pair the ratchet in #31 exists to settle, invented
on this side for no reason.

### What the thresholds cost

The move threshold is the one with a residual and it is stated rather than left to be
discovered. A playback that ends without the server saving a stop, which is a client
killed or a server restarted part way through, loses up to the move threshold on the
peer's side, so the person resumes at most five minutes early. That is recoverable by
the person in seconds. The failure at the other end of the trade is a peer sent a
position every few seconds for as long as anybody in the household is watching
anything, which is what turns a paired server into a load problem for its neighbour.

The two distances also sit at the small end of their ranges deliberately, because the
two mistakes each of them can make do not cost the same. A finish read as a position
costs one click. A position read as a finish marks something watched that the person
had not finished, which is a claim about them they did not make and which the ratchet
in #31 then holds against the other server correcting it.

### An item with no runtime

Two of the three rules are about the length of the work, and a server that has not
analysed an item yet holds no runtime for it. Neither question can be asked without
that number, so neither is asked, and the report is judged by the move and the stop
alone. The absence is carried out of the rule rather than folded into its answer, so
that #62 can show it.

What that leaves open is named rather than softened: a position near the end of a
long item this server has not analysed is carried as a position. It is still bounded
on the receiving side, where the version rule refuses a position whose two runtimes
are not within a minute of each other, and where there is no runtime there is nothing
for that rule to compare either.

### What of this has a rule in the sources today

The judgement and the three numbers, and nothing on either side of them. The listing
is taken at the commit being read rather than at a remote reference, so it answers for
the tree in front of the reader:

    git ls-tree -r --name-only HEAD -- Jellyfin.Plugin.WatchSync/Model/ | grep PositionThreshold
    Jellyfin.Plugin.WatchSync/Model/PositionThreshold.cs
    Jellyfin.Plugin.WatchSync/Model/PositionThresholdAnswer.cs
    Jellyfin.Plugin.WatchSync/Model/PositionThresholds.cs

What is not there is what the rule is between. The handler that reads the event and
hands the report over is #15, the position last carried comes out of the record of
what two sides last agreed in #14, and the list a peer reads a carried change from is
#48. So the rule decides and nothing yet asks it.

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
- The envelope version and the bounds on what one may carry, which are #18 and
  #19. What the reader answers for an envelope carrying one member twice is
  decided with them rather than here, and `docs/transfer.md` carries what that
  refusal leaves behind beside the other two.

## How this document is held true

By the suite, for the field table, the save reason table, the unit a transfer is
about and the position thresholds, and by a reading at review for everything else.

`SyncModelDocumentTests` reads the properties of the server's record off the
referenced assembly by reflection, reads the rows of the field table out of this
file, and refuses the two disagreeing in either direction: a property with no row,
a row naming no property, and a property named twice. It reads the members of
`SyncedState` the same way and refuses a `moved` row that is not a member and a
member that is not a `moved` row. So a property added on a future server line
reddens the suite rather than being dropped in silence, and a field moved into or
out of the moved set has to move in the table and in the type together.

The save reason table is held the same way, against the members of the server's
`UserDataSaveReason` rather than against a list kept here: a reason with no row, a
row naming no reason, and a reason named twice are each refused, and so is a row
whose treatment is not one of the three the section declares. The treatments are
read out of the prose that declares them rather than restated in the test, so a
treatment removed from the document and left in a row is refused as well. A reason
added upstream therefore reddens the suite instead of arriving as an event nothing
has a treatment for, which is the failure that matters: an unclassified reason is
either carried as a change nobody decided to carry or dropped in silence, and
nothing in the middle.

The unit is held by the type rather than by a rule anybody has to remember.
`TransferSubject` has no public constructor, so the only route to one is a reading
that refuses every kind `docs/matching.md` gives no key rule to, and a caller that
wanted to carry a series has nothing to put a series into. `TransferSubjectTests`
drives every member of the server's own kind enumeration through that reading and
refuses an answer the disposition column of that table disagrees with, so a kind
moved between two dispositions there, and a kind added upstream, both redden the
suite instead of arriving as a subject nothing classified.

The position threshold table is held against the type that declares the numbers.
`PositionThresholdDocumentTests` reads the rows out of this file and the defaults
off `PositionThresholds` by reflection, and refuses the two disagreeing in either
direction: a threshold the type declares with no row, a row naming no threshold, a
row naming one twice, and a row whose default is not the number the type would use.
So a default changed in one of the two places is a red suite rather than a document
describing a rule the code stopped following, which is the direction that costs
most, because the number a person reads before deciding whether to change a setting
is the one in the document.

The reflection is over the assembly this project compiles against, which is a
different one per target, and the suite runs once per target. So the table is
judged against both server lines rather than against whichever one happened to
build.

What the suite does not judge is whether a row's reason is the right reason. That
is a reading at review, and it is the same bound `docs/matching.md` carries.

The one minute above is in the same position, one step further out. Nothing in the
tree reads it, because there is no apply path for it to be part of, so today it is a
number in prose and a run that was green says nothing about it. When the apply path
arrives it takes the number from here or the two drift, and the tests #28 asks for
are what hold them together: one version, several versions with close runtimes,
several with runtimes far apart, and a peer whose runtime is unknown.
