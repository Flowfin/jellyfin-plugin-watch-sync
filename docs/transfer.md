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

Nothing calls this yet, for the reason the rest of the walk is not called: there is no
exchange, and #47 is where one is defined. The share in force is a parameter of the walk
rather than a number it reads, so where the value comes from is the caller's question.

The answer to that question is a setting an operator chooses, on the configuration page and
in the plugin configuration document. It was going to be per pairing and it is server-wide,
and the argument for the move is in `docs/configuration.md` rather than restated here: every
cause this rule exists to catch is on the side doing the writing, so the same number means
the same thing on a server it was not written for. `ServerWideSettings` reads it and refuses
a value outside what the rule accepts rather than repairing one, which is the only place
between the document and this rule.

## How this document is held true

By a reading, at the review of the change that touches it.

Nothing in the tree compares this document against anything. The sentence here
used to give the reason as every issue this document points at being unbuilt, and
that has stopped being the reason: the walk in #54 is in the tree and the unwind
rule above is asserted by facts over it. What is still true is the smaller half.
There is no table here that names a member of a type, so there is nothing for a
guard to compare, which is what `docs/sync-model.md` and `docs/matching.md` each
have and this document does not. When the exchange exists, the end states above
are the list a guard would read, and a row with an empty next step is what it
would refuse.

That is a gap and it is named here rather than left for somebody to discover.
