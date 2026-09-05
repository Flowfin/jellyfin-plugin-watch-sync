# Fuzzing the inbound envelope reader

An envelope arrives from another server. Even a paired one is a machine this operator
does not administer, so the bytes that reach `Envelope.Read` are the only bytes in this
plugin chosen by somebody else. That is the surface worth spending a run on, and this
document says what the run is, what it cannot see, and what happens when it finds
something.

`docs/parity.md` carries the row this adopts and the one line saying why it is a
deviation upward from the gate this board takes its target from.

## The one command

    dotnet run --project Jellyfin.Plugin.WatchSync.Tests --framework net10.0 \
      -- fuzz <iterations> <seed> <output directory>

The same line runs locally and in `.github/workflows/fuzz.yaml`. It writes two files
into the output directory, `findings.txt` and `corpus.txt`, prints a summary, and exits
non-zero where anything was found.

A run is bounded by iterations and not by elapsed time, and every input is derived from
the seed number. So a finding reproduces from two numbers on any machine, and the
harness reads no clock: the suite refuses a clock in a tracked test source, and a
harness that needed one would be asking for the departure that rule exists to make
unnecessary.

## What it judges

Not the absence of a crash on its own. #18 and #19 decided that this surface refuses
rather than truncates and that every refusal is its own answer, so an input can break
the contract while the process stays up. The rules, one per way that happens:

| rule | what an input made the reader do |
| --- | --- |
| `reader-threw` | it threw instead of answering |
| `reader-answered-nothing` | it came back with no reading at all |
| `reading-names-no-supported-set` | a reading naming no version it was made against |
| `refused-carries-an-envelope` | a refusal carrying an envelope a caller can read |
| `readable-carries-no-envelope` | a reading that is not refused carrying nothing |
| `version-not-supported-names-no-version` | that refusal naming no version |
| `member-missing-names-no-member` | that refusal naming no member |
| `member-carried-twice-names-no-member` | that refusal naming no member |
| `not-an-envelope-names-a-version` | bytes that are not an envelope carrying a version |
| `readable-version-is-not-spoken` | a readable envelope of a version nobody speaks |
| `readable-keeps-the-version-member` | the version left among the members beside it |
| `readable-misses-a-required-member` | a readable envelope missing what its version requires |
| `bounds-threw` | the bounds threw on quantities counted off the bytes |
| `bounds-answer-disagrees-with-its-own-bounds` | an envelope allowed past a bound it exceeds |

The rules above are about text that is already in memory. The body reader is the layer in
front of them, and it decides how much of what a peer is sending this side takes at all,
which is the second condition of #19. Its rules are asked of the same input, and the
fifth condition of that issue is why they are here rather than only beside the reader:

| rule | what an input made the body reader do |
| --- | --- |
| `body-reader-threw` | it threw instead of answering |
| `body-reader-answered-nothing` | it came back with no reading at all |
| `refused-body-carries-text` | a refused body carrying text a caller can parse |
| `read-body-carries-no-text` | a body that was not refused carrying nothing |
| `the-declaration-was-not-carried` | a reading naming a length the peer did not declare |
| `too-many-bytes-names-no-bound` | a refusal by length naming no bound |
| `bound-named-where-no-bound-refused` | a bound named where no bound refused anything |
| `declared-past-the-bound-was-read-anyway` | bytes taken off a body already refused on its declaration |
| `a-body-inside-the-bound-was-refused-for-its-length` | a body inside the bound refused for its length |
| `the-text-is-not-the-bytes-that-arrived` | text that re-encodes to bytes the peer did not send |
| `body-read-past-the-bound` | more than one byte past the bound taken off the stream |
| `an-endless-body-was-not-refused` | a peer that never stops sending answered as read |

Five declarations per input, at the length of the input rather than the length of the
bound: none, the honest one, one below the body, one past the bound, and the same bytes
with a lead byte on the end that no continuation follows. The fourth is the case the rule
exists for, because nothing is read off the stream at all; the fifth is the one shape a
body derived from text cannot otherwise reach, since everything the mutations produce is
a string and a string encodes to UTF-8 that decodes again.

The last two rules are asked once per sweep rather than once per input, against a peer
that never stops sending. Reading a quarter of a mebibyte per input would spend a run's
whole budget re-answering a question that does not depend on the input, and a reader
whose stopping condition is the end of the stream rather than the bound passes every
input that has an end.

Each of those is proven by a reader that breaks exactly that rule, in
`EnvelopeFuzzTests`. That leg is what makes the run worth reading: an oracle is only
ever exercised by inputs that satisfy it, so one that has quietly stopped asking looks
exactly like a surface with no defects, and ten million inputs report the same clean
sheet either way.

## The corpus

The seeds are in `Jellyfin.Plugin.WatchSync.Tests/Envelope/corpus.txt`, one body per
line, and they are not written for the harness. Every one of them is a body the
envelope cases already hand the reader, and the suite refuses the file and those call
sites disagreeing in either direction, so there is one set of bytes rather than two
that drift apart.

What that guard cannot see is a body assembled at run time. Three call sites in
`EnvelopeVersionTests` build their argument rather than writing it, and they are
outside the corpus rather than exempted by name: what makes a body seedable is that it
is bytes rather than an expression.

## What a run cannot see, stated rather than left to be inferred

**It is not coverage guided.** There is no instrumented build here and no runtime that
would provide one. An input is kept when it produces an answer the run has not seen, so
the corpus a run evolves is a corpus of answers rather than of paths: two inputs
reaching different code and coming back with the same answer are one entry, and a path
no mutation reached is a path the run says nothing about. The run prints that sentence
on every execution, including a green one.

**A clean run is not an absence of defects.** It is the absence of a defect this
harness's mutations reached with a rule this harness carries.

**It judges the readers and the bounds and nothing beyond them.** The bounds are asked
with the three quantities counted off the bytes; what a real caller does with a refusal
is the transfer plane in #47 and the apply path in #54, and neither exists.

**The body leg hands the bytes over rather than receiving them.** No transport in this
plugin produces a body, so the stream is one the harness builds out of the input and the
declaration is a number it chooses. What a real transport declares, and whether it
declares anything, is #40's adapter and is not measured here.

**The suite does not sweep.** `EnvelopeFuzzTests` proves the machinery on fixed inputs
and runs the oracle over the seeds only. A sweep inside the suite would be a fuzz run
gating every merge, which is what #102's second condition refuses, and it would redden
a pull request that touched none of this. So a crasher is found by the scheduled run
and never by the suite.

## What happens when it finds something

A crasher is a security finding. It gets its own issue and its own fix, and the finding
is recorded rather than folded into unrelated work. It is never repaired by narrowing a
mutation, by removing a rule, or by catching the exception inside the harness: all
three turn a defect into a green run without changing what a peer can do.

The run that landed this harness found one, so the rule above is not hypothetical here.
`{"version":1,"changes":[],"changes":[]}` and `{"version":1,"version":2,"changes":[]}`
make the reader throw `System.ArgumentException` rather than answer, which is a duplicate
member in an envelope from a peer. It is #253, and it is deliberately not fixed in the
change that brought the harness.
