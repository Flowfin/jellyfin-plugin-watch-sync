# The invariants this repository refuses in its own sources

A rule this project has learned is a sentence in a document until something refuses
the violation. This is the register of the ones that have been turned into checks,
what each one scans, and what each one cannot see.

The register is data, at
`Jellyfin.Plugin.WatchSync.Tests/Invariants/register.txt`, and a test holds this
document and that file to the same set. Neither is derived from the other, so an
invariant added to one and not the other is a red run rather than a drift somebody
finds later.

## What an invariant is here

An invariant is a property of this plugin's own sources that no change may break, and
that a reader cannot check by reading, because the mistake looks reasonable at the
moment somebody makes it. Each one was argued in an issue before it was a pattern, and
the entry names that issue so the argument is one link away rather than lost behind the
regular expression that came out of it.

An invariant is carried by one or more rules. Storage identity is five, one for each
way a key can be derived from where a file is kept. A separate identifier per way is
what lets a finding say which mistake was made, and lets a departure cover one call
without covering the rest.

## The register

| invariant | argued in | what is scanned | refused by |
| --- | --- | --- | --- |
| `storage-identity` | #25 | the plugin's own sources | `StorageIdentityGuardTests` |
| `mapping-not-inferred` | #42 | the plugin's own sources | `InvariantGuardTests` |
| `injected-clock` | #32 | the plugin's own sources | `InvariantGuardTests` |
| `log-holds-no-viewing` | #67 | the plugin's own sources | `InvariantGuardTests` |
| `static-instance-not-read` | #8 | the plugin's own sources | `InvariantGuardTests` |
| `applied-change-is-assigned` | #50 | the plugin's own sources | `InvariantGuardTests` |
| `store-path-from-the-server` | #68 | the plugin's own sources | `InvariantGuardTests` |
| `waiting-is-on-the-injected-clock` | #16 | the plugin's own sources | `InvariantGuardTests` |

## What each one is for

**`storage-identity`.** No path, file name, container, size or hash is used in a match
key. The two servers are not required to hold the same files, so a key over any of them
works only where they happen to and fails silently for the case this plugin exists to
serve. The refusals are argued in [matching.md](matching.md), which is also where that
guard's rules are held against the document that declares them.

**`mapping-not-inferred`.** No user name is compared to decide who a change belongs to.
The mapping between two accounts is an operator decision the pairing plugin owns, and a
name comparison is an inference on top of it. The failure it produces is the worst one
available here: one person's watch history appearing in another person's account, on the
first server where two people happen to share a name.

**`injected-clock`.** No wall clock is read outside the injected one. A position is
resolved by the newer play, bounded by a tolerated skew, and a resolver that reads the
machine clock cannot be tested at that boundary at all. The suite half of this rule is
already refused by the headless vocabulary; these rules are the plugin half, which is
where the clock decides an outcome rather than a test.

**`log-holds-no-viewing`.** No log statement carries the title of a work or a provider
identifier for it. A log is a file that gets copied into a support thread and shipped to
a collector, and what somebody watched belongs in neither. What may be logged, what may
never be, and which half of that a machine refuses are in
[logging.md](logging.md), which is also where these two rules are held against the
document that declares them.

**`static-instance-not-read`.** No type reaches the plugin's static instance. The template
this repository started from keeps the plugin in a static and reads it from anywhere, and
a type written that way stops being constructible in a test, because the static holds
nothing until a server has loaded the plugin. The queue, the matcher, the store and the
resolver are all wanted behind constructors for that reason, and #8 is where the
registration that supplies them is argued. The static itself is still there and is that
issue's subject; this rule holds the reach for it, which is the half that gets written
without anybody deciding to.

**`applied-change-is-assigned`.** No applied change adds to the value it finds. Whether
a send that timed out is ever repeated is not this plugin's to decide, so an envelope
may arrive twice and applying it twice has to be indistinguishable from applying it
once. A write that assigns is that whatever arrives; a write that adds is not, and the
set of recently seen envelopes #50 also asks for only bounds the window in which the
addition is invisible. The failure it produces is one watch counted as two, with
nothing on either server saying which of the two was invented, and it is the same
failure the prior art in that issue produces from the other direction. The plugin's own
record is immutable, so the mistake has nowhere to be made except on the server's
record at the moment of the write, which is exactly where the apply path will be.

**`store-path-from-the-server`.** No path this plugin stores anything under is taken from
anywhere but the application paths the server hands over. #68 asks for that in one
sentence, and the sentence has a trap in it: it says the store lives in the plugin's own
data folder, and the server offers a property with that name. On both supported lines
`BasePlugin.DataFolderPath` is the plugin's install directory with the version appended,
and the server deletes and re-extracts that directory when it installs a new version. So
the reading the wording invites produces a store that works, keeps its documents, and
empties itself on the one day the agreed record and the document upgrade are what
everything depends on. The other rules are the roots somebody reaches for when that one is
refused: the environment, the account the server runs as, a temporary directory, the
directory the assembly was loaded from, and an absolute path typed into the source. What
the plugin does instead is `StoreFolder`, which composes one name under
`IApplicationPaths.DataPath` and nothing else.

**`waiting-is-on-the-injected-clock`.** No source waits on real elapsed time. #16
suppresses the echo with a window around this plugin's own write, so that a change this
plugin applied is not handed straight back to the peer that sent it, and a window held
open by waiting closes when the machine says so rather than when a test says so. That
issue's third condition runs ten exchanges after one change and counts the writes on each
side; against a window that waits, those ten exchanges take ten real windows. The rule
this carries is the same one `injected-clock` carries one step over: reading a clock and
waiting on one are different mistakes, and the second has its own names.

## What these patterns cannot see

A pattern reads one line of text. That is enough for the mistakes above as they are
actually written, and it is not the same as the property being held. Where the two come
apart, it is written here rather than left for somebody to discover after trusting a
green run.

**A scan is only as wide as its subject.** These rules read the plugin's own sources.
The plugin is small today. Nothing in it logs, names a user, reads a machine clock, waits
on one or writes a change, so five of the seven invariants carried by that guard scan
sources that could not have violated them:

    grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)|Username|DateTime\.(Now|UtcNow)|\.(PlayCount|PlaybackPositionTicks)\s*(\+\+|\+=)|Thread\.Sleep|Task\.Delay|SpinWait|new (System\.Threading\.)?(Periodic)?Timer\(' --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

The other two are not in that position. The static that `static-instance-not-read` refuses
reaching for is in `Plugin.cs`, and every source in the project can see it, so that rule
has had something to be true of since the day the project was created. And
`store-path-from-the-server` arrived with the one type that composes a path, so it has
been about a real line from the change that added it:

    grep -rn 'DataPath' --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Storage/StoreFolder.cs:15:/// The root is <see cref="IApplicationPaths.DataPath"/>, which the server creates and keeps across
    Jellyfin.Plugin.WatchSync/Storage/StoreFolder.cs:48:    public string FullPath => Path.Combine(_applicationPaths.DataPath, FolderName);

The first of the two is the comment saying so and the second is the line. One type composes
a path and the rules above are about the roots it did not take.

A run that finds nothing is not evidence that the rule holds over code that has not been
written. The near-miss fixtures are what make each rule a guard rather than a green tick,
and each one is refused and its repair passes on every run.

**`mapping-not-inferred` sees comparisons, not every inference.** The rules match a name
compared with an operator, compared through an equality call, or searched for in a
collection. A lookup keyed by name in a dictionary reaches the same wrong user without
comparing anything on a line a pattern can read, and so does a name carried into a query
and matched somewhere else. Those are caught by review and by the tests #42 asks for, not
here.

**`injected-clock` sees the ways of reading a clock that have names.** A clock reached
through a variable, a wrapper written over one of the refused calls, or a value handed in
from a caller that read the machine clock itself all pass. What the rules hold is that
the plugin's sources do not reach for the machine clock by any of the names the runtime
offers for it, which is the mistake somebody makes while writing a comparison.

**`log-holds-no-viewing` is stronger than the invariant it carries, deliberately.** The
invariant is that a title never appears next to a user. A pattern reading one line cannot
decide whether a user is named on the same statement, and a log call split across lines
would defeat one that tried. So the rules refuse a title reaching a log call at all.
That is stronger, and it refuses nothing #67 permits: that issue allows an item
identifier at the ordinary level and allows a title at no level. A property named `Name`
on something that is not a work, a pairing or a scheduled task, is the case where these
rules refuse the wrong thing, and that is what a departure is for.

**`static-instance-not-read` sees the reach, not the static.** The rule reads a line that
names the holder, so it refuses the read and says nothing about the property being there.
A service parked in a static of its own under any other name passes, and so does one
reached through a local assigned somewhere else, or through a wrapper written over the
holder. Removing the static is #8 and is not what this rule does; what it holds is that
the first type to reach for the one that exists is refused rather than merged.

The exception #8 names is the configuration accessor, and this rule refuses that too. It
is stronger than the invariant deliberately, for the same reason the logging rules are:
there is no accessor in the plugin today, so a rule written around one would carve out a
shape nobody has decided yet. When it arrives it is a declared departure, which puts the
exception where a reader meets it and takes it away again the day the call goes.

**`applied-change-is-assigned` sees the operators, not every addition.** The rules
refuse an increment written as `++` or `+=` on the two fields that could carry one. The
same non-idempotent write spelled out, a value read from the record and put back with
one added to it, reaches the wrong count without either operator on the line, and so
does a count assembled somewhere else and assigned here. What the rules hold is the
one-character version, which is the one that gets written while the apply path is being
written, and the reconciliation that decides the count is #33 rather than this guard.
They also name two fields by name, so they say nothing at all about a field this plugin
does not carry.

**`store-path-from-the-server` sees the roots that have names, and it does not read the
combine.** The rules refuse the calls and the property that produce a root, so a root
obtained through a variable assigned somewhere else, handed in by a caller, or read out of
a setting reaches the same wrong directory with none of those names on the line. The one
name this plugin composes under the server's root is a constant in `StoreFolder`, and no
rule here refuses a constant, because a leaf name has to be something: what the rules hold
is that the part above it was not chosen here. Which folder the store actually sits in is
asserted by `StoreFolderTests` rather than by this guard, and the two are about different
halves of the same sentence.

**`waiting-is-on-the-injected-clock` is stronger than the invariant it carries, and it
sees only the names.** Some of what it refuses has a spelling that takes a time source, so
the correct way to wait, once this plugin has an injected clock, may well be one of the
very lines these rules refuse. These three, with `p` a `TimeProvider`, compile against
both of the target frameworks this project builds:

    Task.Delay(TimeSpan.FromSeconds(1), p);
    new PeriodicTimer(TimeSpan.FromSeconds(1), p);
    p.CreateTimer(_ => { }, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

That is a library compile of those three statements at `<TargetFrameworks>net9.0;net10.0`
under SDK 10.0.301, and it says the overloads exist rather than that this plugin should
use them. `wait-task-delay` refuses the first and `wait-timer` refuses the second; nothing
refuses the third, because its rule reads a `new`. They are refused anyway, because what
this plugin's injected clock will be is #86 and is undecided: a rule written to permit an
overload carrying a provider would settle there and then that the clock is a
`TimeProvider`, which is not this guard's to settle. When the clock arrives, a place that
legitimately waits on it is a declared departure, which puts the exception where a reader
meets it and takes it away again with the line. That is the same shape
`static-instance-not-read` takes over the configuration accessor #8 names.

What the rules do not see is the waiting that has no name on the line. A wrapper written
over one of them, a task that waits inside something else and is blocked on here with
`.Result` or `.Wait()`, a cancellation token given a timeout, and a span waited out by a
caller and handed in already elapsed all reach real time with none of these names in view.
What the rules hold is the spellings somebody reaches for while writing a window, which is
the moment #16 is about.

## Departures

A departure is declared at
`Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt`, with the file, the rule and
the reason. It is a debt with the thing that retires it written next to it rather than a
dispensation: an entry whose file no longer carries the call it was written for is
refused as dangling, so it leaves with the line it covered.

The plugin declares none today.
