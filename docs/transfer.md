# The transfer plane

What one exchange between two servers is, who starts one, what it may cover, and
what it leaves behind when it fails.

It is fixed here once so that the rest of M6 references it instead of arguing it
again per issue. What moves, at what granularity, and what never moves is
`docs/sync-model.md`, and this document does not restate any of it: a change, an
envelope, a peer, a mapping and a conflict mean here exactly what that document
says they mean.

## Who starts one

The server that wants the data. Data is pulled, which is decision 3 in #1,
answered on 2026-08-08, and the reason is in `docs/sync-model.md` under
`## Direction` rather than repeated here.

So an exchange has an asking side and an answering side, and the two roles are
about one exchange rather than about a server. Both servers of a pairing ask on
their own schedules, and a pairing where only one side ever asks is a working
pairing in which data moves one way, not a broken one.

The answering side never decides anything about the asking side's users. It
reads its own state, applies the bounds below, and answers. Every decision about
what to write is taken by the asking side, on its own users, against its own
agreed record.

## What one exchange consists of

Six steps, in this order, and the order is the point rather than the count.

The asking side decides to start one. That is either the scheduled sweep in #55,
or an event that put a change where the next pull will find it, or an operator
pressing the button in #64. Nothing else starts one.

It asks the adapter for the pairing state, and refuses on anything that is not a
live confirmed pairing. #41 holds that refusal and the states it fails closed on.
This step is first because every later step is a reason to skip it.

It reads its watermark for that pairing and that mapped user, which is #51, and
asks the peer for what changed since it. A watermark the peer does not recognise
is not an error and produces the full reconciliation in #52.

The answering side replies with an envelope. The envelope carries a version, #18,
and no more than the bounds in #19 allow. The answering side applies the same
pairing check before it answers anything.

The asking side matches each change to a local item, resolves it against its own
value and the agreed record, and applies what the conflict table says, item by
item. An item that fails is recorded and the rest continue, which is #54, and an
applied change is idempotent so a repeat changes nothing, which is #50. Nothing
in this step reaches the network.

The asking side writes the agreed record for every item it decided, #14, stamps
provenance for every value it wrote, #44, and advances its watermark to the point
the answer named. The watermark advances here and nowhere else.

The exchange is over when the agreed record and the watermark are written. Not
when the envelope arrived, and not when the last item was applied.

## What one exchange may cover

One pairing and one mapped user. An exchange that covered two mappings would have
one watermark for two agreements, and a failure in the middle would leave neither
of them describable.

A bounded number of changes. The bound on the envelope is #19, which is what the
answering side may put in one reply, and the bound on what one run may write is
#38, which is what the asking side may apply before it stops. They are two
different bounds and both are refusals rather than truncations: an exchange that
reaches either stops, records that it stopped and why, and leaves a watermark the
next exchange resumes from.

A run that covered less than everything is never reported as one that covered it
all. The record an exchange writes says what it examined as well as what it
changed, which is the same rule #55 puts on the sweep.

## Whether two exchanges may overlap

Not on one pairing and one mapped user. One at a time, and the second is refused
rather than queued.

The pair is what the exclusion is over rather than the pairing alone, because two
mapped users of one pairing share no agreed record and no watermark, so nothing
they write can collide.

The refusal is the answer to the sweep starting on top of an event-driven
exchange. A manual run while one is in progress is refused and says so, which is
#64's rule for a repeatable action, and the sweep's own version of it is in #55.
A refusal here costs one interval and nothing else, because the next exchange
starts from the same watermark and reaches the same state.

Holding is not an alternative to refusing. A held start is a start whose
conditions were read at one time and acted on at another, and the pairing state
is exactly the thing that can have changed in between, which is #45.

## What a failed exchange leaves behind

Every way an exchange can end, and the next step for each. The rule the table
follows is that a failure leaves a state the next exchange starts from without
special handling, so no row's next step is a repair.

| how it ended | what is left | the next step |
| --- | --- | --- |
| Completed | The agreed record is written for every item decided, and the watermark is at the point the answer named. | The next exchange asks from the new watermark. |
| Refused, no live pairing | Nothing read, nothing written, the watermark unmoved. A refusal is on the status page with the state that caused it, #41. | Nothing, until the pairing is live again. The next exchange asks the same question and gets the same answer, cheaply. |
| Refused, already running for this pairing and user | Nothing. | Nothing. The exchange in progress reaches the state this one would have. |
| The peer did not answer, or timed out | The watermark unmoved. The peer's failure count advances, and a peer failing for the configured period is shown as unreachable, #53. | The next exchange asks from the same watermark, after the backoff. |
| The layer below refused the request | The watermark unmoved and nothing read. The refusal is recorded with the peer and the code the plane answered with, and the peer is **not** counted as unreachable, because it answered. | It depends on the code, and the plane is the authority for them, so this row names the shape rather than copying the list. A refusal the same request succeeds under later is one wait and one retry. A refusal about the two clocks is not retried at any interval and an operator is shown which two instants disagreed. A refusal about a repeated request is a fresh request rather than a repeat. A refusal carrying no cause is backed off, #53. |
| The envelope was refused for its version | The watermark unmoved, the refusal recorded with both versions, #18. | Nothing until one side is upgraded. Retrying changes nothing and the status page is where an operator sees why. |
| The envelope was refused for a member carried twice | The watermark unmoved, the refusal recorded with the peer and the member that arrived twice, #253. | Nothing until the peer's serializer is repaired. Retrying changes nothing, because the same peer builds the same body, and the status page is where an operator sees which member it was. |
| The envelope was refused for a bound | The watermark unmoved, the refusal recorded with the peer, the bound and the count, #19. | The next exchange asks from the same watermark. A peer that keeps exceeding a bound is a peer to look at rather than a state to recover from. |
| Some items applied, then the run cap was reached | The agreed record written for the items decided, the watermark advanced to the last point both sides agreed, and the stop recorded with what was examined, #38. | The next exchange asks from that watermark and continues. |
| Some items applied, then the failures stopped being about the items | The agreed record written for the items that were written, the ones that failed named with their reasons and still outstanding, and the stop carried on the answer rather than left to be inferred from the counts, #54. | The next exchange asks from the same watermark and meets the same side. Nothing here is a repair, and repeating the exchange is not one either: what stopped the run is this server, and an operator is the thing that has to look at it. |
| Some items applied, then the process stopped | The agreed record written for the items decided. The watermark is wherever it was last written, which is at or behind those items. | The next exchange asks from that watermark. Items decided but behind the watermark are offered again and change nothing, because applying a change twice is #50. |
| An item could not be matched or was ambiguous | Nothing written for that item, the reason recorded against it, #26 and #27. The rest of the exchange continued. | Nothing automatic. The operator fixes the library metadata and the next exchange matches it. |
| The pairing was revoked mid-exchange | Whatever was applied before the revocation was learned, its provenance stamped with the pairing, #44. The queue for that peer is dropped and nothing further is sent or applied, #45. | Nothing on that pairing, ever. Undoing what was written is the provenance route in #44 and it is an operator action. |

### The row above is the one this table did not have

It is written out here rather than left in the cell, because what it costs is
invisible from the cell. Every other refusal in the table is this plugin refusing
something, and every one of those has a cause this plugin decided. This one is the
pairing plane refusing the request before anything of this plugin's is involved,
and the cause reaches this side as an answer it gives rather than as anything
readable from here.

Collapsing it into the row above it is the mistake worth naming, and it is the one
this table made until now. A peer that never answers and a peer that answers
instantly with a refusal are opposite observations about a link. Under the row
above, the second advances a failure count and ends with an operator being shown a
peer that is down, while the peer is up, is answering in milliseconds, and has said
which of several different things is wrong. That is the failure this whole table
exists to refuse, arriving through it rather than around it.

The next step is left as a shape rather than as a list of codes, and that is
deliberate. The vocabulary belongs to `docs/protocol.md` on the pairing board, and a
copy of it here would be a list that drifts against the thing it describes. What
this document fixes is that those refusals have different next steps and that none
of them is the unreachable-peer route; the adapter in #40 is where a code becomes
one of them.

Which states answer an exchange at all is the neighbouring question and is not this
row's. It belongs to the `Refused, no live pairing` row above, and #41 carries the
reading it rests on.

There is no row whose next step is a repair, and that is a property of the design
rather than of the table. The agreed record and the watermark are the only two
things an exchange writes about itself, they are written in that order, and both
are behind or level with the work that was done. A reader who finds them behind
the truth loses an exchange's worth of time and nothing else.

### Nothing already applied is unwound

The table above says what is left behind on every route out of an exchange, and
what it never says is that something was taken back. That is the rule rather than
an omission, so it is written here: no code path undoes an item this exchange
already applied, for any reason, including the seventh item of ten failing.

There is no transaction across two servers, so an unwind is not a rollback in the
sense a database offers one. It is a second pass of writes, made at the moment
something is already going wrong, against a server that has just refused a write.
It can fail halfway itself, and what it leaves then is a third state nobody
planned and no row above describes: some items at the peer's value, some at the
value they held before this exchange, and an agreed record that matches neither.
The failure the per-item rule exists to prevent is reached by the mechanism meant
to prevent it.

What stands in its place is the order the agreed record is written in. It is
written for the items that were decided and for no others, so an item that failed
keeps the record it had, and the next exchange offers exactly that item again. A
partial exchange is a smaller exchange rather than a damaged one, and repeating an
applied change is #50, which is what makes offering an item again cheap.

Undoing a write is not refused everywhere, and the difference is worth keeping
sharp. What a revoked pairing wrote can be undone, by the provenance stamped on
each value, #44, and that is an operator action taken against a pairing after the
fact. It is not an exchange reversing itself while it runs, which is what this
rule refuses.

Something in the tree holds this now. The walk that writes a decided set of items
is `Jellyfin.Plugin.WatchSync/Apply/ItemByItemApply.cs`, and the rule is asserted
by a fact that reads the order of the writes rather than the state they left,
because a walk that put an item back leaves the same state as one that never
touched it, and the order is the only place the difference shows.

The reason this paragraph gave for the rule was that the walk keeps no record of
what an item held before it wrote, so it has nothing to put back. That reason has
gone: the walk reads what this server held immediately before each write and puts
it in the record of provenance, because #44 asks for exactly that value. What is
left is the rule itself and it is stronger for being stated on its own terms. An
unwind is a second pass of writes made at the moment the server has already
refused one, and it can fail halfway itself; what it leaves then is a third state
nobody planned. Provenance is read by an operator acting on a revoked pairing,
days or months later, against a server that is answering. The two are different
operations and the record being available to one of them is not a reason to give
it to the other.

What is not held is the rest of the exchange around it. The walk is handed items
something else decided about and answers a record something else writes, so the
order this document fixes for the whole exchange, the agreed record and then the
watermark, is still a sentence a reading enforces. Which of the end states above
a run reaches is decided by nothing here yet, because there is no exchange, and
#47 is where one is defined.

### A walk that is failing stops, and a walk with failures in it does not

The row above is the one route out of an exchange where this side rather than an item is
what went wrong, and the two are worth separating because the same walk produces both.
Items disappearing from a library between an exchange deciding and a walk writing is
ordinary, and stopping for it would be the all-or-nothing outcome the per-item rule exists
to refuse. A database that is down, an account that may not write, or a mapping naming
somebody else's record fails nearly every item it is handed, and working through the rest
of the envelope records hundreds of refusals about a fault none of them is about, then does
it again on the next exchange.

The rule is a share of what the walk has attempted, with a floor beneath which it declines
to judge, and it is
`Jellyfin.Plugin.WatchSync/Apply/FailureShare.cs`. The floor is what keeps the rule off the
smallest envelope there is: one attempted item that was refused is a share of one, which is
above every share the rule accepts, so without it a walk over a single deleted film would
stop and report a systematic failure. The share is taken over everything attempted rather
than over a run of consecutive failures, because what it is looking for is a side that has
stopped accepting writes and not a stretch of bad luck.

A stop is the walk declining to attempt what is left. It is not an unwind and the section
above is not weakened by it: what was written stays written, the items that failed keep the
agreement they had, and the ones never reached keep theirs, so the next exchange offers
exactly what is still outstanding. The answer says the walk stopped, because a walk of ten
that stopped after the eighth and a walk of eight that finished leave the same two lists,
and this document's rule that a run covering less than everything is never reported as one
that covered it all has nowhere else to live.

What calls the walk is the cap, and only the cap: `CappedApply` judges `RunCap` before a
decided set reaches the walk, and it is the one route a decided set takes to a write, which
is the section below. Nothing calls the cap in turn, for the reason the rest of the plane is
not called: there is no exchange, and #47 is where one is defined. The share in force is a
parameter of the walk rather than a number it reads, so where the value comes from is the
caller's question.

The answer to that question is a setting an operator chooses, on the configuration page and
in the plugin configuration document. It was going to be per pairing and it is server-wide,
and the argument for the move is in `docs/configuration.md` rather than restated here: every
cause this rule exists to catch is on the side doing the writing, so the same number means
the same thing on a server it was not written for. `ServerWideSettings` reads it and refuses
a value outside what the rule accepts rather than repairing one, which is the only place
between the document and this rule.

## The cap is judged before anything is written

One run has a cap, which is #38, and it is judged before the first write rather than while
the walk is running. `RunCap` is the rule and carries both bounds with the reason for each;
`CappedApply` is where it is asked, and a decided set reaches the walk through it and
through nothing else. So the ordering rule #38 carries, that nothing which applies changes
to a server lands ahead of the cap, is kept by there being one route rather than by care.

A run within the cap walks exactly as it would have without one, and pays nothing visible
for having been judged: the same writes, the same reads, no document. A cap that cost an
ordinary evening anything is the cap an operator turns off.

A run the cap stops writes nothing and records what it would have done, item by item, in a
`stopped-` document under the pairing and the person. Every item carries the state the
table decided and what this server held at that moment, and the second half is what makes
an approval safe. An operator approves the plan, and the approval writes exactly what the
plan recorded: for every item it reads what this server holds now and sets the item aside
where that is not what the plan recorded, where the library no longer holds the item, and
where the plan had no baseline for it because the read was refused when the run stopped.
What is set aside is named with its reason and is offered again by the next run, which
judges it afresh. What is not set aside is handed to the walk as the plan wrote it. The
approval does not ask the cap again, because the operator's approval is the answer to the
cap's question, and it does not recompute the plan, because a plan recomputed at approval is
a second run the operator never read.

What this does not do, said rather than left to be found. Nothing shows a stopped run to an
operator and nothing takes an approval from one; the status page is #62 and the manual
action is #64. The bounds and the matched count arrive at `CappedApply` as parameters,
because the bounds are per pairing and nothing holds a pairing yet to keep them beside.

## What the sweep does today

The scheduled sweep in #55 is a task the server runs, once at server start and then
at the interval `docs/configuration.md` carries, and an operator sees it, runs it and
sees when it last ran in the dashboard's task list. What one run converges is what
can be converged without a peer: no exchange starts, because the pairing adapter in
#40 is not in this tree, and no watch state moves. A run rebuilds the match index
from the library first, one page at a time with the finished map swapped in whole,
which is how the index in #29 is built on start and how an item the library gained
that no event carried is in the index by the next run. It then walks every record of
conflicts and of provenance the store holds, trims each to the retention its setting
carries, and records the run the way `SweepRun` fixes it, over the set declared
before the walk, one result per record, and covered or stopped short derived from
the counts when it ends. The rebuild is not a subject of that record: it is a cache
being refreshed rather than a record being changed, and a run over an empty store is
still a run over nothing however large the library it walked. A cancellation from
the dashboard ends the run where it stands and it is recorded as one that stopped
short; a rebuild already under way finishes its walk first, because the index takes
no token and a map half built is a map not adopted.

A configuration the rules refuse runs nothing: the task fails naming the setting, the
dashboard shows the failure, and no record is trimmed against a retention nobody
chose. A write the store refuses fails the run the same way rather than reporting the
record as examined, because the run record has one word for a record examined with no
change and no word for one whose trim was refused, and reporting the second as the
first is the reading that record exists to refuse.

The last run is kept in memory and the status in #62 reads it: its two moments, what
it was over, what it examined, what it changed, and whether it covered its set or
stopped short, with a run that stopped short shown above the rest of the status. It
is the server's run rather than any pairing's, because the walk is over records
rather than pairs. A restart loses it, and the status then says no sweep has ended
since the server started; the server's own history keeps when the task last ran and
whether it failed. When the exchange arrives it takes its place in the same walk under the same
record, one subject per pairing and mapped user, which is the set `SweepRun` was
written for.

## The wait after a failure

An attempt on a peer that failed is retried, and the interval between attempts
doubles from a first wait until it reaches a ceiling and stays there. Both numbers
and the reason for each:

| what | its default | the reason |
| --- | --- | --- |
| the first wait | 30 seconds | It is chosen against what the layer below admits rather than against how slow a peer is. At thirty seconds one pairing spends two arrivals inside the plane's published sixty second window, against sixty it admits there, so a peer that is down cannot make this plugin the reason its other traffic is refused. The short direction is the one that costs something quietly: a wait of a second or two looks attentive and is where a failing pairing spends its own allowance, and the refusal an operator then meets is the plane's undistinguished one, which says nothing about a peer being down. |
| the ceiling | 30 minutes | It is reached at the seventh consecutive failure, which is a little over half an hour of a peer being unreachable, and it is the interval a peer that stays down is asked at afterwards. What the number is chosen against is how long a peer that came back stays unnoticed, and half an hour is the worst case for that. It is not shorter because the scheduled sweep asks anyway, so a peer that came back is picked up by whichever of the two happens first, and a ceiling under the sweep's own interval buys nothing and asks more often. |

The ceiling is REACHED and not merely never exceeded, and the difference is the
whole of what the second row buys. A rule that abandons the doubling as soon as the
next one would pass the ceiling never exceeds it and never arrives at it, so it
settles at whatever interval happened to be the last one underneath - sixteen
minutes rather than thirty, at the numbers above, and a failing peer asked nearly
twice as often as anybody chose. `BoundedBackoffTests` refuses both directions.

Which failures are retried at all is not this rule's and is not decided here. The
row above for a refusal from the layer below says why: the codes belong to
`docs/protocol.md` on the pairing board, they are not interchangeable for anything
retrying, and the adapter in #40 is where a code becomes one of the next steps in
that table. What the wait answers is the interval for a caller that has already
decided this failure is one to retry.

The wait is not jittered. Jitter is worth its cost where many callers back off
against one server at once, and one pairing is one caller against one peer, so what
it would buy here is nothing and what it would cost is a rule whose answer cannot
be stated. A second pairing to the same peer is the case that would change that,
and it is not one this plan has.

Neither number is a setting, and `docs/configuration.md` gives both the home
`deliberately absent` with the reason. What decides that is where the numbers come
from: the first wait is picked against an allowance another server applies rather
than against anything an operator here can observe, and a shorter one is not a
preference but the value at which a failing pairing spends that allowance. The
first rule of #53 asks for a timeout that is a setting, and the timeout is a
different number from either of these: it bounds one attempt and these bound the
gap between two.

## How this document is held true

By a reading, at the review of the change that touches it.

ONE TABLE HERE IS COMPARED AGAINST THE TREE NOW, AND THIS PARAGRAPH SAID NONE WAS.
What stood here said there is no table naming a member of a type, so there is
nothing for a guard to compare, and that stopped being true when the backoff
section landed with two numbers a type declares. `BoundedBackoffTests` reads that
section, refuses the two numbers disagreeing with `BoundedBackoff` in either
direction, and refuses the section being absent.

Everything else here is held by a reading, at the review of the change that touches
it. The end states above are the table that matters most and are the one a guard
cannot read yet: they name issues rather than members, so there is nothing to
resolve them against until the exchange exists. When it does, that table is the list
a guard would walk, and a row with an empty next step is what it would refuse.

That is a gap and it is named here rather than left for somebody to discover.
