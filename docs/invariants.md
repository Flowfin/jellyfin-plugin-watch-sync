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
| `user-data-behind-the-adapter` | #20 | the plugin's own sources | `InvariantGuardTests` |
| `pairing-behind-the-adapter` | #40 | the plugin's own sources | `InvariantGuardTests` |
| `endpoint-authorised-by-the-server` | #66 | the plugin's own sources | `InvariantGuardTests` |
| `no-second-chance-match` | #26 | the plugin's own sources | `InvariantGuardTests` |

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

**`user-data-behind-the-adapter`.** No source names the server's user data manager. The
interface this plugin reads and writes user data through is not the same on the two
supported lines: the newer one carries a batch read and a notion of which version drives
the resume point, and the older one carries neither. #20 holds that difference in one
adapter of this plugin's own, and the bullet with teeth there is that no type outside the
adapter references the manager.

IT LANDED BEFORE THE ADAPTER AND THAT IS WHAT IT WAS FOR. A scan asserting this over
sources that could not violate it keeps passing while the first call site is written
somewhere else, and by then the boundary is a refactor across every caller rather than a
decision taken once. The adapter arrived afterwards, into a rule that was already there, so
the first call this plugin makes to the server's user data is inside it:

    git grep -l 'IUserDataManager' -- 'Jellyfin.Plugin.WatchSync/**/*.cs'
    Jellyfin.Plugin.WatchSync/UserData/NewerLineUserData.cs
    Jellyfin.Plugin.WatchSync/UserData/OlderLineUserData.cs
    Jellyfin.Plugin.WatchSync/UserData/ServerUserData.cs

The interface itself is not among them, which is the point of it: it carries this plugin's
own types and the server's entities, and the manager whose shape differs between the lines
is named only by the three files that hold one.

**`pairing-behind-the-adapter`.** No source names the pairing plugin's namespace, its
interfaces, its protocol types or its key material. This plugin holds no pairing, no key
and no user mapping, and all three come from a plugin whose contract another board
decides. #40 puts that contract behind one interface of this plugin's own, so that a
record gaining a member, a state gaining a value or a namespace moving reaches one type
here rather than every caller.

The last of the four rules is not about the adapter at all. Key material is something the
contract never offers a consumer, so there is no place in this plugin where it may appear
and no departure that would be legitimate. A consumer that never holds a key cannot leak
one, and the rule is what keeps that true rather than a sentence saying it is.

It is here before the adapter for the reason `user-data-behind-the-adapter` is: the first
type that needs a pairing is the one that reaches for the nearest thing that already
offers it, and by the time the adapter is written the reach is spread across every caller.

**`endpoint-authorised-by-the-server`.** No source opens an endpoint to a caller who has
not signed in, takes the user an action is about out of the request, names one of the
server's policies as a string, or decides authorisation with a role comparison of this
plugin's own. #66 takes the authorisation model from the server rather than inventing one
here, and these four are the ways the first controller avoids doing that.

The four are not equally expensive. Opening an endpoint and comparing a role by hand are
visible in review. Naming a policy as a string is not: it compiles, it authorises
correctly today, and it stops matching on the day the server renames the policy, which
leaves an endpoint whose attribute refuses nobody. The identifier rule is the one with the
worst outcome, and it is #66's own last condition seen from the source rather than from a
test: an endpoint that reads whose history to show out of the query string answers
correctly for the person who wrote it and answers for everybody else too.

There is no controller in this plugin, so all four are green over a subject that could not
violate them. That is the order the rest of this register was built in and the argument is
the same one: a scan written after the first endpoint meets a shape that is a change across
every route rather than a decision taken once, and the endpoint that forgot its attribute
is indistinguishable from one that has it until somebody calls it.

**`no-second-chance-match`.** No source asks which of the two non-matching answers a lookup
came back with. An item that produced no key, and a key no local item carries, are terminal
answers for that item in that run: [matching.md](matching.md) fixes three answers and says
in the same section that there is no second pass at a weaker comparison, and #26 asks for
the absence of one to be asserted rather than written down. A fallback cannot be written
without first asking whether the first attempt failed, and the only place this plugin
spells that failure is `MatchAnswer`, so the line that names `NoMatch` or `Ambiguous` is
the line the second chance is written under.

The mistake it refuses is not a careless one, which is why it is worth a machine. The line
gets added on the day the identifiers turn out to be absent, in a pass that is already
correct about everything else, and in the branch it opens the two works usually are the
same one. What it costs is invisible in the run that adds it: a weaker comparison matches
works that are not the same one, and what moves is somebody's watch history landing on the
wrong film, on a run that reported nothing unusual. The second answer is the same mistake
with a worse case behind it, because an ambiguity is two local items claiming one key, so
taking the first of them is choosing at random and choosing differently next run.

`Matched` is deliberately not refused. A caller has to know that it matched, and
`MatchLookup` offers that as `IsMatched` rather than as a comparison, for the reason its
own comment gives: the two answers that are not a match differ in what is recorded and
never in what is done, so a caller that branches on one of them and not the other writes to
a competing item. That property is what the repaired fixture beside the near-miss uses, and
it is what a source refused by this rule is pointed at.

It is here before the pass that would call a lookup, for the reason
`user-data-behind-the-adapter` and `pairing-behind-the-adapter` are: the fallback is the
line somebody writes at the moment a key comes back with nothing, which is exactly when it
looks reasonable, and a guard arriving after that line does not prevent it.

## What these patterns cannot see

A pattern reads one line of text. That is enough for the mistakes above as they are
actually written, and it is not the same as the property being held. Where the two come
apart, it is written here rather than left for somebody to discover after trusting a
green run.

**A scan is only as wide as its subject.** These rules read the plugin's own sources, so
what a green run is worth depends on what those sources hold, and that moves under this
paragraph without anybody editing it. Read at `f9ec69a`, which is what the mainline is on.
Four of the eleven invariants that guard carries scan sources holding nothing of the kind
the rule is about, and seven scan sources that hold the surface a violation would be
written on. The eleven are counted from the register rather than from this sentence:

    grep -c ':: InvariantGuardTests$' Jellyfin.Plugin.WatchSync.Tests/Invariants/register.txt
    11

The four are `mapping-not-inferred`, `log-holds-no-viewing`,
`waiting-is-on-the-injected-clock` and `pairing-behind-the-adapter`. Nothing in this plugin
names a user, logs at all, waits on anything, or names anything of the pairing plugin's:

    grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)\s*\(|\bILogger\b|\bUsername\b|Thread\.Sleep|\bSpinWait\b|Task\.Delay|new\s+(System\.Threading\.)?(Periodic)?Timer\s*\(|\bJellyfin\.Plugin\.ServerPairing\b|\b(IPairedPeers|IPairingKeySource|IPairingKeyStore|IPairingRecordStore|PairingRecord|PairingState|PairingMessage|PeerChannel|PeerReply|KeyMaterial|PairingKeys|OfferedKey)\b' --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

CORRECTED BY RE-RUNNING IT, FOR THE SECOND TIME AND FOR THE SAME REASON. THIS PARAGRAPH
SAID SEVEN OF THE ELEVEN WERE IN THAT POSITION AND PASTED A COMMAND UNDER `exit=1`. Seven
and four have swapped ends since, and the command it rested on no longer returns nothing:

    grep -rnE 'Log(Trace|Debug|Information|Warning|Error|Critical)|Username|DateTime\.(Now|UtcNow)|\.(PlayCount|PlaybackPositionTicks)\s*(\+\+|\+=)|Thread\.Sleep|Task\.Delay|SpinWait|new (System\.Threading\.)?(Periodic)?Timer\(|Jellyfin\.Plugin\.ServerPairing|IPair(edPeers|ingKeySource|ingKeyStore|ingRecordStore)|Pairing(Record|State|Message)|Peer(Channel|Reply)|KeyMaterial|PairingKeys|OfferedKey|\[AllowAnonymous\]|\[From(Query|Route|Form|Header)\].*[Uu]serId|Policy\s*=\s*"|\.IsInRole\s*\(' --include=*.cs Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    Jellyfin.Plugin.WatchSync/Api/HeldAboutOnePersonController.cs:60:    public ActionResult<HeldRecordsReport> Report([FromRoute] Guid mappedUserId)
    Jellyfin.Plugin.WatchSync/Api/HeldAboutOnePersonController.cs:94:    public ActionResult<RecordsRemoved> Remove([FromRoute] Guid mappedUserId)
    exit=0

WHICH CLAUSE MOVED MATTERS MORE THAN THE COUNT. It is "nor exposes an endpoint", and the
invariant it made vacuous is `endpoint-authorised-by-the-server`, which exists for the
surface this plugin now has. A reader was told that rule scanned sources that could not
have violated it at the moment they first could. The endpoints landed with #74 on
2026-09-01, so the sentence was true when it was last corrected and stopped being true
without anything reading this file: what a guard carries and what it scans are both derived
on every run, and a sentence typed into a document is neither. #320 is where this
correction was argued; the earlier one is below and stands unchanged.

THE TWO HITS ARE NOT A VIOLATION AND ARE NOT A HOLE. Both are the same declared departure,
and it is where the guard reads it rather than in prose here, with the reason beside it:

    grep -n 'HeldAboutOnePersonController' Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt

A departure whose file stops carrying a hit for its rule is refused as dangling, so that
entry goes with the call rather than outliving it. What the entry says is that both
endpoints are deliberately about another person, which is the case that rule names
elevation for, and that the server's own elevation policy is on the controller. Whether
that is the right reading of those two endpoints is #66's subject and not this document's.

WHAT MAKES THIS PARAGRAPH GO STALE IS ANY CHANGE THAT GIVES THIS PLUGIN A SURFACE. A first
log call, a first user name, a first wait, a first call into the pairing plugin: each moves
one invariant out of the four and into the seven, and none of them is a change anybody
would open this file to make. The command under the four is what to re-run, and what to
read out of it is which of those names has stopped returning nothing, not whether the
number is still four.

CORRECTED BY RE-RUNNING IT. THIS PARAGRAPH SAID SEVEN OF NINE AND ITS COMMAND CARRIED THE
USER DATA NAMES. Two things had moved under it. The guard carries eleven invariants rather
than nine, and the adapter #20 asks for has landed, so the manager's names are in this
plugin's sources and that command returned seven hits under a pasted `exit=1`. It was found
while the entry above was being added rather than by anything that reads this file: what a
guard carries is derived from the register on every run, and a count typed into a document
is not. The names that now return hits are out of the command, the endpoint names are in it
because that invariant arrived after this paragraph was written and never joined its count,
and the seven the sentence was about are a different seven from the ones it named.

The seven that are not vacuous. The static that `static-instance-not-read` refuses reaching
for is in `Plugin.cs`, and every source in the project can see it, so that rule has had
something to be true of since the day the project was created. `store-path-from-the-server`
arrived with the one type that composes a path:

    grep -rn 'DataPath' --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Storage/StoreFolder.cs:15:/// The root is <see cref="IApplicationPaths.DataPath"/>, which the server creates and keeps across
    Jellyfin.Plugin.WatchSync/Storage/StoreFolder.cs:48:    public string FullPath => Path.Combine(_applicationPaths.DataPath, FolderName);

The first of the two is the comment saying so and the second is the line. One type composes
a path and the rules above are about the roots it did not take.

`user-data-behind-the-adapter`, `no-second-chance-match` and
`endpoint-authorised-by-the-server` are each about a surface that arrived with a declared
departure, which is what the exceptions file beside the register is a list of rather than a
set of holes. `applied-change-is-assigned` is about the two fields an apply writes, and
there is an apply that writes them:

    grep -rn 'gateway.Write' --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Apply/ItemByItemApply.cs:280:            gateway.Write(

`injected-clock` is the seventh and is the one to read carefully, because no source here
reads a machine clock and the rule is still not vacuous. What makes it live is that the
decisions it exists for are in the tree and take their moment as a parameter, so the line
that reads a clock instead is one somebody can now write inside a file that already
compiles:

    grep -rnE '^\s*(DateTime|DateTimeOffset)\??\s+(now|appliedAt|writtenAt)\b' --include=*.cs Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Apply/ItemByItemApply.cs:149:        DateTimeOffset appliedAt,
    Jellyfin.Plugin.WatchSync/Apply/ItemByItemApply.cs:325:        DateTimeOffset writtenAt)
    Jellyfin.Plugin.WatchSync/Conflict/PositionRecency.cs:155:        DateTime now)
    Jellyfin.Plugin.WatchSync/Exchange/FirstExchange.cs:144:        DateTime now,
    Jellyfin.Plugin.WatchSync/Exchange/FirstExchange.cs:239:        DateTime now)
    Jellyfin.Plugin.WatchSync/Records/ProvenanceRecord.cs:95:        DateTimeOffset writtenAt)

That is the line the four and the seven are drawn on, and it is not the same line as
whether a rule has ever refused anything. Vacuous here means the sources hold nothing of
the kind the rule reads, so a green run over them says nothing at all. A run that finds
nothing is not evidence that the rule holds over code that has not been written. The
near-miss fixtures are what make each rule a guard rather than a green tick, and each one
is refused and its repair passes on every run.

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

**`user-data-behind-the-adapter` refuses the adapter too, and that is the design rather
than an oversight.** The rules read the plugin's own sources and nothing scopes them to a
directory, so the one type that is allowed to name the manager is refused along with every
type that is not. The adapter has landed, and each of its calls is a declared departure
with the reason beside it. That is the same shape `static-instance-not-read` takes over the
accessor #8 names, and it is worth more here than a carve-out would be: the exception list
IS the list of places this plugin touches the server's user data at all, in one file, read
by anybody asking how wide the surface is.

    git grep -c 'user-data-manager-interface\|user-data-batch-read\|user-data-resume-version' -- Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt
    Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt:5

Deleting any one of the five turns `NoPluginSourceViolatesAnInvariantThisGuardCarries` red
on the call it covered, and declaring a sixth for a call no file makes turns
`NoDeclaredDepartureHasOutlivedWhatItWasWrittenFor` red instead. Both directions were run.

THE COMMAND COUNTED EVERY LINE OF THAT FILE UNTIL THIS EDIT, AND THE FILE IS NO LONGER ONE
INVARIANT'S. `no-second-chance-match` declares one of its own, so a count of the whole file
would answer six and the sentence above it would be about five of them. The count is scoped
to this invariant's three rules instead, which keeps the claim true and keeps it from moving
the next time another invariant declares a departure. What the sentence claims is unchanged:
these five are the whole of what this plugin touches of the server's user data.

**It does not refuse the record.** `UserItemData` is the thing the manager reads and
writes, and a type outside the adapter holding one has reached past the boundary just as
surely. There is no rule for it, because the plugin already names it in prose, in the
comment saying why the wire type is not that record:

    git grep -n 'UserItemData' -- 'Jellyfin.Plugin.WatchSync/**/*.cs'
    Jellyfin.Plugin.WatchSync/Model/SyncedState.cs:13:/// It is deliberately not the server's <c>UserItemData</c>. Using that record as the wire type
    Jellyfin.Plugin.WatchSync/UserData/ServerUserData.cs:69:        var record = Server.GetUserData(user, item) ?? new UserItemData { Key = string.Empty };
    Jellyfin.Plugin.WatchSync/UserData/ServerUserData.cs:96:    protected static SyncedState? MovedSetOf(UserItemData? record) =>

A rule over that name would refuse that comment on the day it landed, and a departure
covering a sentence is a debt nothing retires. So the record is held by the review and by
the type that exists instead of it, not here.

THE OTHER TWO HITS ARE THE ADAPTER AND ARE WHERE THE RECORD IS MEANT TO BE. They are the
two lines where the server's record is turned into this plugin's moved set and back, which
is the whole of what the adapter is for. They are not departures, because no rule names
that type, and the reading above is the reason there is none. What that leaves is what the
sentence has always left: a type somewhere else holding one would pass, and nothing but a
reader would notice.

**And it sees names rather than reaches.** The manager obtained through a variable typed
somewhere else, handed in as an interface of another name, or reached through a wrapper
passes all four rules. What they hold is that the server's own names for it do not appear
in this plugin's sources, which is the mistake made while writing the first call.

**`pairing-behind-the-adapter` names four interfaces rather than a shape.** The rule that
reads the pairing plugin's own interfaces lists the four that board declares:

    gh api 'repos/Flowfin/jellyfin-plugin-server-pairing/git/trees/96561b6d60b12f131d009a1749685219dbbc0df3?recursive=1' --jq '[.tree[].path | select(test("/IPair[A-Za-z]+[.]cs$"))] | sort | .[]'
    Jellyfin.Plugin.ServerPairing/KeyStore/IPairingKeyStore.cs
    Jellyfin.Plugin.ServerPairing/Protocol/IPairedPeers.cs
    Jellyfin.Plugin.ServerPairing/Protocol/IPairingKeySource.cs
    Jellyfin.Plugin.ServerPairing/Protocol/IPairingRecordStore.cs

That is read at one commit of that board and named rather than at its moving head, so the
quotation reproduces for a reader tomorrow. The four names are what the rule lists.

A fifth one added there is not in that list until somebody adds it here, and this is the
one place in this file where a list of another repository's names lives. The alternative
was a pattern over every name that begins the way those four do, and that pattern refuses
the adapter #40 asks this board to write, because an interface of this plugin's own in
front of a pairing contract is called something that begins the same way. What holds the
fifth name meanwhile is the namespace rule, which sees the using directive or the
qualified name any use of it needs.

**It sees the protocol vocabulary as the other board spells it.** `PairingRecord`,
`PairingState`, `PairingMessage`, `PeerChannel` and `PeerReply` are refused by name. Two
of the five are ordinary enough that this plugin might want one of them for a type of its
own, and if that day comes the answer is a different name here rather than a carve-out,
because the whole point of the boundary is that the two vocabularies stay apart.

**And it sees names rather than reaches, like its neighbour.** A pairing contract obtained
through a variable typed somewhere else, handed in under an interface of another name, or
reached through a wrapper written once, passes all four rules. What they hold is that the
other plugin's own names for its own things do not appear in this plugin's sources.

**It cannot see an endpoint that carries no attribute at all.** This is the largest thing
the endpoint rules do not do, and it is the condition #66 names second. A pattern reads one
line, and whether a method has an authorisation attribute is a fact about the lines above
it, so a route added with nothing declared on it passes every rule here. What answers that
condition is the reflection over the controllers #66 asks for, which reads assemblies
rather than text, and that issue's own reading says what such a test has to do to be worth
anything: refuse an empty comparison as well as a disagreeing one, because a reflection
over zero controllers is green and reads exactly like coverage.

**A route parameter naming a user is refused whether or not it is trusted.** The rule sees
the reach and not what is done with it, so an elevated endpoint that deliberately names
another user, an operator clearing one person's queue, trips
`endpoint-user-from-the-request` and is a declared departure rather than a carve-out in the
pattern. The reason on such an entry has to say which policy makes the naming legitimate,
because that is the whole question the rule is about. Narrowing the pattern to spare that
case would spare the user-scoped endpoint it exists for as well, since the two are the same
line of source.

**It does not see the policy that is wrong rather than absent.** An endpoint declaring the
server's constant for a weaker policy than the action needs passes every rule here, because
the constant is referenced correctly and only a reader knows what the action does. That is
the table in #66's first condition, and no pattern over one line replaces it.

## Departures

A departure is declared at
`Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt`, with the file, the rule and
the reason. It is a debt with the thing that retires it written next to it rather than a
dispensation: an entry whose file no longer carries the call it was written for is
refused as dangling, so it leaves with the line it covered.

THE PLUGIN DECLARED NONE WHEN THIS PAGE WAS WRITTEN, AND THIS SENTENCE WENT ON SAYING SO
AFTER THAT STOPPED BEING TRUE. It declares six. The number is not written here, because a
count in a document is the thing this page has already been wrong about once:

    git grep -c '^Jellyfin' -- Jellyfin.Plugin.WatchSync.Tests/Invariants/exceptions.txt

Five are the adapter #20 asks for and are argued above; the other is the type that declares
the three match answers. Each entry carries its reason on its own line, and that file is the
list to read rather than a number in this one.
