# Configuration

Settings arrived in this plan one at a time. This document is where they are collected
before the page that carries them is built, and it answers two questions that every one of
those issues would otherwise answer for itself, differently: where a setting lives, and
which numbers are deliberately not settings at all.

Nothing in this plugin is a setting today. The configuration type is empty, every number
below is declared on the type that uses it, and no operator can change any of them:

    git grep -c 'public' Jellyfin.Plugin.WatchSync/Configuration/PluginConfiguration.cs
    1

That one declaration is the class. This document is therefore a rule the settings are
written against rather than a description of settings that exist, and it says so here so
that a reader does not take the table below for a page they can open.

## Where a setting lives

Three homes, and which one a setting takes follows from what the setting is about rather
than from where it is convenient to put it.

**Server-wide, in the plugin configuration.** A setting describing what this server does,
independent of any peer and of any person. The thresholds that decide what counts as a
meaningful position are this kind: they are about this server's own chattiness, and a
server with two peers wants one answer rather than two.

**Per pairing, with that pairing's state.** A setting that names, describes or is a
judgement about one peer. The tolerated clock skew is this kind: it is a statement about
how far one particular machine's clock can be trusted.

**Per user, with that user's record.** A setting a person makes about their own data. The
opt-out in `docs/opt-out.md` is this kind.

The reason the split matters more than it looks is the plugin configuration file. An
operator can copy it between servers, and people do, because it is the fastest way to stand
up a second server that behaves like the first. A per-pairing setting copied that way
arrives on a server where that pairing does not exist, or worse, where a pairing with the
same identity points at a different peer, and the operator has then configured a machine
they were not thinking about. A per-user setting copied that way is one person's decision
about their own history applied to somebody else's.

So the test is not "is this a small number an operator might want to change". It is "what
does this setting mean on a server it was not written for". A setting that means nothing
there, or means the wrong thing, does not belong in a file that travels.

## Every number this plugin declares

One row per public static number in the plugin's own sources, and the closure runs in both
directions: a number with no row fails, and a row naming no number fails. That is what
stops this table from being a list somebody wrote once, and it is why the table holds the
numbers that are not settings as well as the ones that will be.

`Home` is where the setting will live once it is one, or why it will never be one.

| Number | Type | Value | Range | Home | Why this value |
| --- | --- | --- | --- | --- | --- |
| `PositionThresholds.DefaultMove` | `TimeSpan` | 5 minutes | up to `MaximumMove` | plugin configuration | the count of changes follows the length of the work rather than the chattiness of the player |
| `PositionThresholds.DefaultFinish` | `TimeSpan` | 2 minutes | up to `MaximumFinish` | plugin configuration | the length of what sits after the last thing anybody watches |
| `PositionThresholds.DefaultShortestItem` | `TimeSpan` | 5 minutes | up to `MaximumShortestItem` | plugin configuration | below it a resume point is not a thing anybody uses |
| `PositionThresholds.MaximumMove` | `TimeSpan` | 30 minutes | none | bound on a setting | above it the threshold stops being coarse and becomes a different rule |
| `PositionThresholds.MaximumFinish` | `TimeSpan` | 15 minutes | none | bound on a setting | beyond it the distance covers a real part of the work |
| `PositionThresholds.MaximumShortestItem` | `TimeSpan` | 1 hour | none | bound on a setting | above it the ordinary television episode is on the wrong side of the line |
| `PositionRecency.DefaultToleratedSkew` | `TimeSpan` | 1 minute | up to `MaximumToleratedSkew` | pairing state | a minute never swallows a genuine ordering on a pair of working machines |
| `PositionRecency.MaximumToleratedSkew` | `TimeSpan` | 15 minutes | none | bound on a setting | wider and the tie rule becomes the rule and recency the exception |
| `ConflictRecords.MaximumEntries` | `int` | 200 | none | plugin configuration | a day of real disagreement is whole in it and the document stays a file somebody can read |
| `ConflictRecords.DefaultRetention` | `TimeSpan` | 14 days | up to `MaximumRetention` | plugin configuration | the span in which the question is actually asked |
| `ConflictRecords.MaximumRetention` | `TimeSpan` | 90 days | none | bound on a setting | past it what is kept is a history of what a household watched |
| `RunCap.DefaultMaximumChanges` | `int` | 100 | up to `MaximumConfigurableChanges` | pairing state | a busy evening and a day of catching up fit under it, and a mass-mark is nowhere near it |
| `RunCap.DefaultMaximumShare` | `double` | 0.1 | up to `MaximumConfigurableShare` | pairing state | a tenth of a small library is a change nobody makes by watching things |
| `RunCap.MaximumConfigurableChanges` | `int` | 10000 | none | bound on a setting | above it the count reads as a cap while letting a mass-mark through |
| `RunCap.MaximumConfigurableShare` | `double` | 0.5 | none | bound on a setting | past half of one person's matched items the cap has already allowed what it exists against |
| `PeerText.DefaultLimit` | `int` | 200 | none | plugin configuration | a server name, a user name and a refusal reason arrive whole, and a page of a hundred is still a page |
| `EnvelopeBounds.MaximumChanges` | `int` | 1000 | none | deliberately absent | what the answering side may put in one reply |
| `EnvelopeBounds.MaximumBytes` | `int` | 262144 | none | deliberately absent | well below the transport ceiling, so a refusal here is always a peer doing something wrong |
| `EnvelopeBounds.LongestStringLength` | `int` | 512 | none | deliberately absent | an order of magnitude above any legitimate match key |
| `EnvelopeBounds.MaximumEnvelopesInAWindow` | `int` | 64 | none | deliberately absent | a peer reaching it is looping rather than syncing |
| `EnvelopeBounds.Window` | `TimeSpan` | 10 minutes | none | deliberately absent | an evening's burst sits inside one window and a refused peer answers again the same evening |
| `VersionLanding.WidestRuntimeDifference` | `TimeSpan` | 1 minute | none | deliberately absent | under it the difference is packaging, over it it is an edit or a speed conversion |
| `MatchIndex.PageSize` | `int` | 500 | none | deliberately absent | it changes how much memory a rebuild takes and nothing an operator can observe |
| `EnvelopeBounds.TransportBodyCeilingBytes` | `int` | 1048576 | none | another tree | read from the pairing plugin's protocol document |
| `EnvelopeBounds.FreshnessBudgetPerPairing` | `int` | 4096 | none | another tree | read from the pairing plugin's freshness window |
| `DocumentVersions.Current` | `int` | 1 | none | derived | the newest version in `DocumentVersions.Shipped` |
| `EnvelopeVersions.Current` | `int` | 1 | none | derived | the newest version in `EnvelopeVersions.Supported` |

The `Why this value` column is a clause and not the argument. Every one of these numbers
carries its reason on its own declaration, at length, with what it costs and what it was
weighed against, and that is the authority. Nothing holds the clause here to the paragraph
there, because whether a summary still says what it summarises is a judgement about
meaning; a reader deciding anything reads the declaration.

## What is deliberately not a setting, and why

Four of the six homes above are refusals to make something configurable, and each is a
different refusal.

**A bound on a setting is not itself a setting.** `MaximumMove`, `MaximumFinish`,
`MaximumShortestItem`, `MaximumToleratedSkew` and `MaximumRetention` exist to refuse a
value an operator may otherwise choose. A setting for one of them is that bound removed
with an extra step in front of it, and the step is one an operator takes once and never
sees again.

**A refusal does not become a guess.** The envelope bounds and the runtime difference are
what this plugin refuses a peer for. A setting raising `MaximumBytes` or
`MaximumEnvelopesInAWindow` asks an operator to decide how much a peer that is already
misbehaving may send, which is a question nobody has the information to answer, and the
answer is only ever discovered to have been wrong. `WidestRuntimeDifference` is the same
shape pointed at a person rather than at a peer: raising it drops somebody into a scene
they had not reached, and that is the one failure here that cannot be taken back.

**A number nobody can observe is not worth configuring.** `MatchIndex.PageSize` changes
how much memory a rebuild holds at once and changes nothing an operator can see. A setting
for it is one more thing to get wrong with no way of telling that it was.

That row is the one place this table disagrees with an issue rather than collecting one.
#56 states as a rule that every enumeration is paged and that the page size is a setting,
and the match index walk is the only enumeration in the tree today. Its own declaration
says the opposite, in the same words as the paragraph above. The table takes the source's
side, because the argument there is the specific one and #56's is the general one, and
because the number bounds a rebuild's memory rather than anything a run produces. Which of
the two governs is #56's to settle, and a reader who finds it settled the other way should
expect this row to move rather than the rule above it.

**A number read from another tree is not this plugin's to set.**
`TransportBodyCeilingBytes` and `FreshnessBudgetPerPairing` are readings of the pairing
plugin, kept here so the bounds beside them can be held below them by the suite rather than
by somebody remembering the relation. A setting would let an operator declare a ceiling the
layer below does not have.

**A derived number is not a setting either.** `DocumentVersions.Current` and
`EnvelopeVersions.Current` are the newest entry of a shipped list. A setting for one would
let an operator claim this plugin writes a version it does not write.

## What this document does not yet do

The table lists every number this plugin declares and none of them is a setting, so the
part of #58 that fixes defaults for settings that exist is not met here and is not claimed
to be. What is met is the rule about where a setting lives, the closure that stops the
table and the sources drifting, and the list of what is deliberately absent.

Two settings this plan names have no number in the sources yet and therefore no row: the
queue depth and age, which is #48, and the sweep schedule, which is #55. Each arrives with
a row, because the closure refuses the number without one. #48's own premise was overtaken
by the decision that the transfer pulls rather than pushes, so whether it contributes a
setting at all is a scope question taken there.

The cap on what one run may change was the third of those and has its numbers now, in
`RunCap`. Both of its settings are per pairing rather than server-wide, and that is the
storage rule applied rather than a convenience: a cap is a judgement about how much one
particular peer may be allowed to change, and the same number carried to another server
would be that judgement applied to a peer nobody made it about.
