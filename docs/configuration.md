# Configuration

Settings arrived in this plan one at a time. This document is where they are collected
before the page that carries them is built, and it answers two questions that every one of
those issues would otherwise answer for itself, differently: where a setting lives, and
which numbers are deliberately not settings at all.

Six of the numbers below are settings an operator can change, and the rest are not. The
table under `## The settings the configuration document carries` is the six, and the table
under `## Every number this plugin declares` is all of them with the reason each of the
others is not one. Both are closed against the sources in both directions, so neither can
quietly stop describing this plugin.

Where a number is not a setting, this document says which of five things it is instead
rather than leaving a reader to infer it from an absence.

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

## The settings the configuration document carries

Six, and every one of them is server-wide by the rule above. Each is stored as a whole
number of the unit its name ends in, and the reason is the control rather than the format.
What an operator meets on the page is a number box, so a duration held as a `TimeSpan` would
be formatted into one and parsed back out of it, and those two conversions are where a value
stops being the one that was typed. As counts, the number on the page, the number in the
document and the number the rule is handed are one number.

The serializer is not the reason, and this paragraph said it was: it said the server writes a
`TimeSpan` as an empty element and reads it back as zero. The fact written to execute that
failed, because it round-trips one. The claim was made before the run rather than after it,
`PluginConfigurationTests` keeps the run so the next person to propose spans meets the
measurement rather than the guess, and the reason above is what the choice actually rests on.

`Default` is what the document carries where nobody has chosen anything, and it is not typed
into the configuration type: each member reads it from the rule that consumes it, so the page
and the rule cannot come to disagree about what this plugin does out of the box. `Bound` is
the widest value the rule accepts, declared on the same type.

| Setting | Unit | Default | Carries | Bound |
| --- | --- | --- | --- | --- |
| `PositionMoveSeconds` | seconds | 300 | `PositionThresholds.DefaultMove` | `PositionThresholds.MaximumMove` |
| `PositionFinishSeconds` | seconds | 120 | `PositionThresholds.DefaultFinish` | `PositionThresholds.MaximumFinish` |
| `PositionShortestItemSeconds` | seconds | 300 | `PositionThresholds.DefaultShortestItem` | `PositionThresholds.MaximumShortestItem` |
| `EchoWindowSeconds` | seconds | 30 | `EchoWindow.DefaultWindow` | `EchoWindow.MaximumWindow` |
| `ConflictRetentionDays` | days | 14 | `ConflictRecords.DefaultRetention` | `ConflictRecords.MaximumRetention` |
| `ProvenanceRetentionDays` | days | 90 | `ProvenanceRecords.DefaultRetention` | `ProvenanceRecords.MaximumRetention` |

`ServerWideSettings` is the one place this document becomes the values the rules take, and it
refuses rather than repairs. A value outside its bound is not clamped and not replaced by the
default, because both of those leave a server running a rule the operator did not choose while
the page goes on showing the number they typed. Every refused setting is named at once rather
than the first one, because the other end of this is a person with a form open.

Zero is refused along with everything below it. Each of these six is a distance or a window,
and zero switches its rule off through the setting: no move threshold, no echo suppression, no
retention. Switching a rule off is a decision of its own rather than a boundary value of a
number that means something else.

One relation is refused that no single setting can be judged against: the finish distance has
to stay below the shortest item length. Above it, every position on the shortest item this
plugin carries is a finish, and the rule silently stops being two rules.

## What is not a setting yet, and is not refused as one either

Four of the numbers below are homed `plugin configuration` and are not in the table above:
`ConflictRecords.MaximumEntries`, `ProvenanceRecords.MaximumEntries`,
`UnmatchedRecords.MaximumEntries` and `PeerText.DefaultLimit`. Their home is where each will
live once it is a setting, and none of them is one today.

The reason is the same for all four and it is mechanical rather than a judgement about whether
an operator wants them. Each is read inside the type that declares it, as the cap a trim is
performed against, rather than taken as a parameter the way the six above are. So making one a
setting is a change to that type's own surface and to every caller of it, and it needs a bound
of its own that nothing declares yet. Doing it in passing here would have been four types
changed for a table's sake.

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
| `EchoWindow.DefaultWindow` | `TimeSpan` | 30 seconds | up to `MaximumWindow` | plugin configuration | an order of magnitude above the time a server takes to raise the event its own write caused |
| `EchoWindow.MaximumWindow` | `TimeSpan` | 5 minutes | none | bound on a setting | past it the window stops covering a server normalising a value and starts covering a person acting |
| `PositionRecency.DefaultToleratedSkew` | `TimeSpan` | 1 minute | up to `MaximumToleratedSkew` | pairing state | a minute never swallows a genuine ordering on a pair of working machines |
| `PositionRecency.MaximumToleratedSkew` | `TimeSpan` | 15 minutes | none | bound on a setting | wider and the tie rule becomes the rule and recency the exception |
| `ConflictRecords.MaximumEntries` | `int` | 200 | none | plugin configuration | a day of real disagreement is whole in it and the document stays a file somebody can read |
| `ConflictRecords.DefaultRetention` | `TimeSpan` | 14 days | up to `MaximumRetention` | plugin configuration | the span in which the question is actually asked |
| `ConflictRecords.MaximumRetention` | `TimeSpan` | 90 days | none | bound on a setting | past it what is kept is a history of what a household watched |
| `ProvenanceRecords.MaximumEntries` | `int` | 2000 | none | plugin configuration | twenty runs at the default change cap fit under it whole, and reaching it means one person's record was changed two thousand times |
| `ProvenanceRecords.DefaultRetention` | `TimeSpan` | 90 days | up to `MaximumRetention` | plugin configuration | the undo is bounded by when a pairing is revoked rather than by how long a diagnostic stays interesting |
| `ProvenanceRecords.MaximumRetention` | `TimeSpan` | 365 days | none | bound on a setting | past a year the record is kept for a revocation nobody expects and what is held is a year of somebody's viewing |
| `UnmatchedRecords.MaximumEntries` | `int` | 1000 | none | plugin configuration | a list somebody can work through, and a library that reaches it has a systematic problem rather than rows to repair |
| `RunCap.DefaultMaximumChanges` | `int` | 100 | up to `MaximumConfigurableChanges` | pairing state | a busy evening and a day of catching up fit under it, and a mass-mark is nowhere near it |
| `RunCap.DefaultMaximumShare` | `double` | 0.1 | up to `MaximumConfigurableShare` | pairing state | a tenth of a small library is a change nobody makes by watching things |
| `RunCap.MaximumConfigurableChanges` | `int` | 10000 | none | bound on a setting | above it the count reads as a cap while letting a mass-mark through |
| `RunCap.MaximumConfigurableShare` | `double` | 0.5 | none | bound on a setting | past half of one person's matched items the cap has already allowed what it exists against |
| `FailureShare.DefaultMaximumShare` | `double` | 0.5 | from `SmallestConfigurableShare` to `LargestConfigurableShare` | pairing state | every second write refused is a side that has stopped accepting them, and no library reaches it by having items missing |
| `FailureShare.SmallestConfigurableShare` | `double` | 0.25 | none | bound on a setting | below it one deleted film stops an exchange, which is the all-or-nothing outcome the rule sits inside the refusal of |
| `FailureShare.LargestConfigurableShare` | `double` | 0.9 | none | bound on a setting | above it the rule fires only once essentially everything has failed, which is the rule switched off from the page |
| `FailureShare.SmallestJudgeableAttempts` | `int` | 8 | none | deliberately absent | below it a share is arithmetic on too few points, and one refused item is a share of one |
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

**A number that stops a guard becoming the failure it bounds is not a setting.**
`FailureShare.SmallestJudgeableAttempts` is how many items a walk has to have attempted
before a share of them is read as anything. It is not a preference about how strict that
rule is: the share beside it is that, and the share is a setting. This one is what keeps
the rule from answering confidently about one item, because a walk over a single deleted
film is a share of one and that is above every share the rule accepts. A setting for it
would let an operator set it to one and get an exchange that stops at the first missing
item, which is the all-or-nothing outcome the rule was added to bound, arrived at through
the rule.

**A number read from another tree is not this plugin's to set.**
`TransportBodyCeilingBytes` and `FreshnessBudgetPerPairing` are readings of the pairing
plugin, kept here so the bounds beside them can be held below them by the suite rather than
by somebody remembering the relation. A setting would let an operator declare a ceiling the
layer below does not have.

**A derived number is not a setting either.** `DocumentVersions.Current` and
`EnvelopeVersions.Current` are the newest entry of a shipped list. A setting for one would
let an operator claim this plugin writes a version it does not write.

## What this document does not yet do

Nothing consumes the settings. `ServerWideSettings` turns the document into the values the
rules take and no caller asks it to, because the things that would - the event this plugin
classifies, the walk that decides what to send, the sweep that trims a record - are #15, the
transfer plane in #47 and #55, and none of them exists. So an operator can change these six
and save them, and what a changed value alters today is what the next caller is handed rather
than any behaviour that runs. That is stated here rather than left to be discovered from a
page that saves.

The per-pairing and per-user settings have no home to be stored in. Both of those homes are a
record beside a pairing or a person and neither is written by anything yet, so the tolerated
skew, the run caps, the failure share and the opt-out stay numbers on their own types with a
row saying where they will live.

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
