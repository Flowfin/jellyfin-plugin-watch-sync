# What moves, to where, and on whose authority

What somebody watched, when, and how far they got is personal data about them.
This plugin moves it between two machines, so what moves is written here rather
than left to be inferred from a configuration page. It is a page an operator can
hand to the people whose data it is, and every claim in it names the setting or
the code path that makes it true.

This is the note [#107](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/107)
asks for. It does not restate the arguments behind the answers: what moves and why
is [sync-model.md](sync-model.md), what a person is told when they stop their own
history moving is [opt-out.md](opt-out.md), and what reaches a log is
[logging.md](logging.md). Those three are the authority and this one is the summary
a person is handed, so where they disagree the other three are right.

## What is true of this plugin today, before anything below is read

Nothing in this plugin runs on its own. There is no scheduled task, no background
service and no event handler in it, so nothing transfers anything on a schedule or
off a playback, and nothing writes a log line:

    grep -rlnE "IScheduledTask|IServerEntryPoint|IHostedService" \
      --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

    grep -rln "ILogger" --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

CORRECTED BY RE-RUNNING IT. THIS PARAGRAPH ASKED FOR `IPluginServiceRegistrator` IN
THE SAME COMMAND AND PASTED `exit=1` UNDER IT. That reading stopped reproducing when
the registration in [#8](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/8)
landed, and the paste went on saying the opposite:

    grep -rln "IPluginServiceRegistrator" --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/ServiceRegistrator.cs

That file is the list of services the server hands to whatever asks for one. It starts
nothing, it is not called on a schedule and it is not a route into this plugin from
outside, so what the sentence above says about this plugin running is unchanged. The
term is split out rather than dropped, because somebody checking whether this plugin
has a way in is owed the answer that it has a registrar and what a registrar is.

The two endpoints described below are the one thing here that runs at all, and they run
when an administrator calls them rather than on their own.

Everything below is therefore a rule that exists as a value or a function a caller
hands its inputs to, and the caller is what has not been built. Where a rule is
written and nothing calls it, this document says so on that rule rather than in a
disclaimer at the end, because a reader stops at the row they came for.

That is what makes this document safe to hand to somebody before the plugin works.
It is not an early draft that becomes true later: what is stated as configured is
configured, and what is stated as not running is not running.

## What moves, field by field

The server keeps one record per person per item. It carries ten properties, and
four of them move. The full argument for each disposition, including the reason a
refused field is refused rather than forgotten, is in
[sync-model.md](sync-model.md); the rows below are the same set in the words
somebody asks the question in.

| property | disposition | what it says about a person |
| --- | --- | --- |
| `Played` | moves | Whether they watched the work. |
| `PlayCount` | moves | How often they watched it. |
| `PlaybackPositionTicks` | moves | Where they stopped, which is what a resume offers them. |
| `LastPlayedDate` | moves | When they last watched it. |
| `Rating` | does not move | The score they gave the work. It is an opinion rather than history. |
| `IsFavorite` | does not move | Whether they marked the work a favourite. An opinion rather than history. |
| `Likes` | does not move | Whether they liked or disliked the work. An opinion rather than history. |
| `AudioStreamIndex` | does not move | Which audio track they chose, numbered against one server's own copy of the file. |
| `SubtitleStreamIndex` | does not move | Which subtitle track they chose, numbered the same way. |
| `Key` | does not move | The server's own addressing for the item, which is not about the person at all. |

The four that move are a closed set in the source rather than a list in a document:
`Jellyfin.Plugin.WatchSync/Model/SyncedField.cs` declares one member per moved
field, and a property that is not a member of it has no route into a transfer.
`PrivacyNoteTests` refuses this table and the field table in
[sync-model.md](sync-model.md) disagreeing in either direction, so a field whose
disposition changes cannot change in one document and not in the other.

## What a transfer never contains

No part of a media file, in any encoding, at any size. No library path, no file
name and no storage location of any kind. No credential, no token and no secret of
a server or of a person.

A change carries a field, a value, the item identified the way this plugin
identifies items, the mapped person and a time.
`Jellyfin.Plugin.WatchSync/Model/TransferSubject.cs` is the unit, and it carries a
mapped user, an item and the kind of item, which is the whole of what one transfer
is about. How much one envelope may carry is bounded in
`Jellyfin.Plugin.WatchSync/Model/EnvelopeBounds.cs`.

## On whose authority it moves

Only between two servers one operator paired and confirmed by hand.

The pairing, the trust between the two servers and the mapping between their user
accounts belong to the Server Pairing plugin and not to this one. This plugin reads
that mapping and never invents one, so somebody whose account the operator has not
mapped to an account on the other server has nothing move at all.

The rule this plugin is built to is that it refuses rather than continues: no
pairing, a pairing not yet confirmed, a revoked pairing, or a pairing plugin that
cannot be asked, each stop a transfer instead of allowing one.

**Nothing enforces that in code yet, because nothing transfers yet.** Nothing in
this plugin reaches the pairing plugin at all:

    grep -rn "ServerPairing" Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

The adapter is [#40](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/40)
and the refusals are
[#41](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/41). Until both
exist the sentence above is a rule this plugin is built to and not a rule a machine
keeps, and it is written that way round on purpose.

## What a person can stop, and what stopping does not undo

Somebody can stop their own watch history moving. The wording that choice is
offered in is fixed in [opt-out.md](opt-out.md), along with the two things it may
not claim: it takes effect in both directions, and it deletes nothing that has
already moved.

Nothing in this plugin reads such a choice yet. There is no per-user setting and no
run for one to stop, which [opt-out.md](opt-out.md) states on its own page, and
[#60](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/60) is where the
behaviour lands.

## What this plugin stores about a person, beyond the server's own record

Five kinds of document, in this plugin's own store, which is a folder the server
hands it rather than the server's database.
`Jellyfin.Plugin.WatchSync/Storage/StoredKinds.cs` is the one declaration of what
the store holds, and `StoredKindsTests` closes it against the tree in both
directions, so a sixth kind cannot arrive without a row here.

Every document is filed under one pairing and one person, so what is held about
somebody is answerable without opening a document to find out what it is about.

| document | what it holds about a person | how long it is kept | the setting |
| --- | --- | --- | --- |
| `agreed-` | The last state the two servers agreed for one item: the four moved fields, when it was agreed, and under which envelope version. | As long as the pairing and the mapping exist. Nothing expires it, because it is what stops the next exchange inventing a play. The document holds at most 20000 items, and an item past that is refused rather than an older one dropped. | none |
| `conflicts-` | One row per disagreement resolved: the item, the field, the rule that decided, the two values, and which side lost. | 14 days by default, and at most 90. | `ConflictRetentionDays` |
| `provenance-` | One row per value this plugin wrote into the server: the item, the field, what was there before, what was written, and which pairing and which account on the peer it came from. | 90 days by default, and at most 365. | `ProvenanceRetentionDays` |
| `unmatched-` | One row per item that produced no match key: the item, its kind, and the reason. It names items rather than what anybody watched. | No time limit. The document holds at most 1000 rows and the oldest go first. | none |
| `stopped-` | The plan of one run the cap stopped, waiting for an operator to approve it: one row per item the run was about to write, holding the state the conflict table decided and the state this server held at that moment, which bound stopped the run, and which account on the peer the values came from. | Until the next run for the same pairing and person stops, which replaces it, or until the records are removed. Nothing expires it, because an operator who has not approved it yet is still owed the reading of what stopped. Its size is the size of the run that was stopped, and nothing here bounds that further. | none |

The two retentions are settings an operator sets, bounded by
`ConflictRecords.MaximumRetention` and `ProvenanceRecords.MaximumRetention`, and
[configuration.md](configuration.md) carries the reason for each default.
`PrivacyNoteTests` holds the numbers above to the ones the sources declare, so a
default that moves in the source and not here reddens the suite.

**What applies a retention is not built.** `ConflictRecords.Retaining` and
`ProvenanceRecords.Retaining` drop what is older than a moment handed to them, and
nothing calls either:

    grep -rn "Retaining(" --include=*.cs Jellyfin.Plugin.WatchSync/ | grep -v "public " ; echo "exit=$?"
    exit=1

So the number an operator sets is the number the rule takes, and the sweep that
would run that rule on a schedule is
[#55](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/55). The entry
caps are a different case and do hold today, because each document trims itself as
rows are added to it rather than waiting for a sweep.

Two of the five hold something about a person on the other server. `provenance-`
carries the account a value came from, which is somebody on a machine the same
operator owns, and it is there so that a revoked pairing can be undone. Shortening
`ProvenanceRetentionDays` shortens how far back that undo reaches. `stopped-` carries
the same account for the same reason one step earlier: an approved plan stamps what
it writes with the account the values came from, and the plan is where that has to
be read from days after the run that knew it.

## What is in a log, and what is never in one

Counts, durations, pairing identities, refusal reasons, this plugin's own match key
for an item, and the rule that decided a conflict.

Never, at any level: the title of a work next to a person, an identifier that
resolves to the work in one search, anything from the key material the pairing
plugin holds, or any part of a peer's payload verbatim.

The rules, which two of them a scan refuses, and which two nothing scans for, are in
[logging.md](logging.md). That document is the authority and this section is a
summary of it.

## How a person asks what is held about them, and how it is removed

`Jellyfin.Plugin.WatchSync/Storage/HeldAboutOnePerson.cs` answers both, across every
pairing at once, because somebody asks about themselves rather than about a pairing
they have never heard of. `Report` returns every document the store holds about
them; `Remove` deletes them. Both walk `StoredKinds.All` rather than a list of their
own, so a kind added later cannot be missed by either.

Neither touches the server's own user data. What a person watched belongs to the
server and stays there.

There is a second removal, of a different size:
`Jellyfin.Plugin.WatchSync/Storage/StoreRemoval.cs` removes this plugin's whole
store, which is the action an operator takes before uninstalling if they want the
records of the syncing gone. It removes the one folder the store lives in and
nothing the server owns.

**Two of the three are reachable and the third is not.** The report and the removal
about one person are behind endpoints of this plugin's own, and an operator with an
administrator account reaches both from the configuration page:

    grep -rn "HeldAboutOnePerson\.\|StoreRemoval\." --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Api/HeldAboutOnePersonController.cs:67:        var held = HeldAboutOnePerson.Report(_store, mappedUserId);
    Jellyfin.Plugin.WatchSync/Api/HeldAboutOnePersonController.cs:101:        return new RecordsRemoved(mappedUserId, HeldAboutOnePerson.Remove(_store, mappedUserId));
    Jellyfin.Plugin.WatchSync/Api/RecordsRemoved.cs:9:/// <see cref="Jellyfin.Plugin.WatchSync.Storage.HeldAboutOnePerson.Remove"/>'s own distinction

Two of those three lines are calls and the third is a comment naming the rule it
describes, which is what a grep over sources returns and is left here rather than
filtered out.

The server's own elevation policy is what decides who may ask, on both, and the
routes are in [the endpoint document](endpoints.md). Neither endpoint separates a
person this plugin holds nothing about from a person this server has never had: both
answer the same way, deliberately, so that the answer cannot be used to find out
which accounts exist.

`StoreRemoval` is the third and nothing reaches it. That is the whole-store removal
an operator takes before an uninstall, it is
[#73](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/73)'s question
rather than this one's, and the surface for it does not exist. The status surface is
[#62](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/62) and the other
manual actions are
[#64](https://github.com/Flowfin/jellyfin-plugin-watch-sync/issues/64).

## What this does not do

**Removing this plugin's records does not remove the server's own watch history.**
What this plugin wrote into the server is the server's data afterwards, in the same
place and the same shape as anything watched here. Removing this plugin's store
removes the record of what was agreed, what was in conflict, where a value came from
and what did not match, and it removes nothing a person can see in their own
account. Undoing what a pairing wrote is a different action, driven by the
provenance record, and it happens when a pairing is revoked.

**It is not a backup and it restores nothing.** Two servers agreeing about what was
watched is not an archive of it.

**It does not reach a server the operator does not hold.** Both ends of a pairing
are machines one operator paired deliberately, and there is no route to anything
else.

## What holds this document true

`PrivacyNoteTests`, and what it does and does not judge is worth having written
beside the claim that it holds anything.

It refuses the field table above disagreeing with the one in
[sync-model.md](sync-model.md), in either direction. It refuses a kind in
`StoredKinds.All` with no row in the store table, and a row naming no kind. It
refuses a retention or a setting name here disagreeing with the source that
declares it. And it refuses the three references this document is a summary of
being deleted, which is how a summary quietly becomes the authority.

What nothing judges is whether the sentences say what they mean. That is a
judgement about meaning, and no reading of this tree makes one. The review is where
a wording that has drifted is caught.
