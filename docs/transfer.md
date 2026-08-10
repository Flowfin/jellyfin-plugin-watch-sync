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
| The envelope was refused for its version | The watermark unmoved, the refusal recorded with both versions, #18. | Nothing until one side is upgraded. Retrying changes nothing and the status page is where an operator sees why. |
| The envelope was refused for a bound | The watermark unmoved, the refusal recorded with the peer, the bound and the count, #19. | The next exchange asks from the same watermark. A peer that keeps exceeding a bound is a peer to look at rather than a state to recover from. |
| Some items applied, then the run cap was reached | The agreed record written for the items decided, the watermark advanced to the last point both sides agreed, and the stop recorded with what was examined, #38. | The next exchange asks from that watermark and continues. |
| Some items applied, then the process stopped | The agreed record written for the items decided. The watermark is wherever it was last written, which is at or behind those items. | The next exchange asks from that watermark. Items decided but behind the watermark are offered again and change nothing, because applying a change twice is #50. |
| An item could not be matched or was ambiguous | Nothing written for that item, the reason recorded against it, #26 and #27. The rest of the exchange continued. | Nothing automatic. The operator fixes the library metadata and the next exchange matches it. |
| The pairing was revoked mid-exchange | Whatever was applied before the revocation was learned, its provenance stamped with the pairing, #44. The queue for that peer is dropped and nothing further is sent or applied, #45. | Nothing on that pairing, ever. Undoing what was written is the provenance route in #44 and it is an operator action. |

There is no row whose next step is a repair, and that is a property of the design
rather than of the table. The agreed record and the watermark are the only two
things an exchange writes about itself, they are written in that order, and both
are behind or level with the work that was done. A reader who finds them behind
the truth loses an exchange's worth of time and nothing else.

## How this document is held true

By a reading, at the review of the change that touches it.

Nothing in the tree compares this document against anything, and nothing could
today: every issue it points at is unbuilt, so there is no code for a check to
read and no table here that names a member of a type. `docs/sync-model.md` and
`docs/matching.md` each carry a guard because each has a list the tree also holds;
this document has none yet. When the exchange exists, the end states above are the
list a guard would read, and a row with an empty next step is what it would refuse.

That is a gap and it is named here rather than left for somebody to discover.
