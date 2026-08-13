# Mutation testing

Coverage says which lines a test executed. This changes a line and asks whether
anything went red, which is the question that matters for a matcher whose failure
is somebody's watch history landing on the wrong item.

It reports and gates nothing. What fails is the instrument having stopped.

## Running it

    dotnet tool install --global dotnet-stryker --version 4.16.0
    dotnet stryker
    python3 .github/check-mutation-report.py StrykerOutput/<run>/reports/mutation-report.json

The same configuration and the same guard run in `.github/workflows/mutation.yaml`,
weekly and on demand. There is no pull-request trigger: a run takes minutes per
component and a check that slow on every change is one people learn to ignore.

## What is in scope

`stryker-config.json` carries the scope, and
`Jellyfin.Plugin.WatchSync.Tests/Mutation/scope.txt` carries one row per component
of the plugin project saying whether the run reaches it and why.
`MutationScopeTests` holds the two to each other in both directions and holds each
row to the tree, so a component added to the plugin and named in neither file
fails rather than being silently unmeasured.

The conflict resolver is in the scope and is not in the tree. Its row says
`awaited` for that reason, and the day a `Conflict` directory appears the row is a
red test until somebody moves it to `mutated`. That is the moment the scope gets
checked against the name the resolver actually landed under.

## The run this document was written from

Before the tests below existed:

    dotnet stryker
    Killed:   147
    Survived:  15
    Timeout:    5
    The final mutation score is 88.37 %

Ten mutants did not compile, five were not covered by any test, and fifty three
were skipped by the block filter or by the mutate filter. A timeout counts as
killed: the mutant changed the code enough that a test stopped rather than
finished, which is a test noticing.

After them:

    dotnet stryker
    Killed:   165
    Survived:   5
    Timeout:    2
    The final mutation score is 97.09 %

    python3 .github/check-mutation-report.py StrykerOutput/2026-08-12.03-22-35/reports/mutation-report.json
    172 mutants tested, 167 killed, 5 left alive.
    Mutation score 97.09 per cent. The score is reported and gates nothing.

The five left alive are the five reasoned through below and no others.

## The triage

Twenty mutants were left alive by the first run, fifteen survived and five
uncovered. Fifteen of them are killed by the eight tests in
`MutationSurvivorTests`, which is what that file is for. The other five are
recorded here as reasons.

### Killed by a test that was owed

| where | what the mutation did | the test |
| --- | --- | --- |
| `ProviderIdentifier.cs:161` | read an empty run of characters as a run of digits | `TheImdbPrefixWithNoNumberIsRefusedForItsShapeAndNotForItsLength` |
| `EpisodeMatchKey.cs:162` | turned the null check in front of equality into an or | `AKeyIsNotEqualToTheAbsenceOfOne` |
| `MatchKey.cs:79` | the same, in the other key | `AKeyIsNotEqualToTheAbsenceOfOne` |
| `MatchKey.cs:81` | made two keys of different kinds compare equal | `AKeyIsNotEqualToTheAbsenceOfOne` |
| `MatchKey.cs:50` (three) | wrote a film key in the episode form and the reverse | `TheWrittenFormOfAKeyIsTheFormOfItsKind` |
| `MatchKey.cs:59`, `:71` | removed the refusal of an absent argument where a key is made | `AKeyIsNotMadeFromNothing` |
| `KeyedItem.cs:26` | let an item be held with no key at all | `AKeyedItemWithoutItsKeyIsRefusedWhereItIsMade` |
| `MatchIndex.cs:69`, `:157`, `:174` | removed the same refusal at three of the index's entry points | `TheIndexRefusesAnAbsentArgumentAtTheDoor` |
| `MatchIndex.cs:131` | removed it at the fourth, the lookup | `ALookupOfNothingIsRefusedBeforeTheLibraryIsRead` |
| `MatchIndex.cs:242`, `:244` | turned the end-of-walk test into one that reaches into a page that is not there | `ALibraryThatAnswersWithNothingEndsTheWalkRatherThanThrowing` |

Two of those are worth naming rather than leaving in a table.

The lookup's refusal has a separate test because the obvious one does not kill the
mutant, and that is the useful part. A dictionary asked for an absent key raises
the same kind of failure the refusal does, so a test asserting only the type passes
with the refusal deleted. What the refusal buys is the order: the argument is
refused before the index decides it needs to walk the library. The first attempt
here asserted the type, the mutant survived it, and the test was rewritten to
measure whether the library was read.

`IMatchIndexSource.ReadPage` returns a list rather than a list or nothing, so a
null page is an implementation breaking its own contract. It is held anyway,
because the implementations of that interface are a fake in the suite and, once
#29's adapter lands, a read of the server's library manager, and an index that
threw on the first page would take out every lookup on the server rather than the
one item it could not read.

### Left alive with a reason

Five mutants change nothing an outside caller can observe. Each was reasoned
through against the source rather than assumed, and the reasoning is the part that
is worth reading if one of these lines is ever changed.

`MatchIndex.cs:279`, the test that a finished walk produced both maps, read as an
or rather than an and. `Build` is the only caller of `Adopt` and it assigns the
pair together: both are the maps a finished walk produced, or both are null. So no
reachable call tells the two operators apart. What the line is really doing is
refusing a future caller that hands over one map and not the other, and there is no
such caller to write a test around.

`MatchIndex.cs:288`, the early return for a walk that held nothing. `Adopt` runs
only from `Build`'s `finally`, and `Build` sets the journal before entering the
`try`, so the held list is never absent by the time it is read. The line is a
defence against a caller that does not exist. It is uncovered for the same reason
it is unkillable.

`MatchIndex.cs:310`, the return after a change is journalled. Without it the change
is journalled and also applied to the map that is live during the walk. That map is
then either replaced wholesale by the one the walk built, which discards the extra
application, or kept because the walk failed, in which case the journal is replayed
onto it and `Apply` is idempotent: applying one change twice removes the item from
the key it is already under and puts it back. Either way the state afterwards is the
same state.

`MatchIndex.cs:341`, dropping a key whose last holder left. `Lookup` answers no
match on a key with no holders as well as on a key that is absent, so leaving the
empty entry behind changes no answer. It leaks one dictionary entry per key that
every item has left, which is a growth over a long-running server rather than a
wrong answer, and holding it would mean exposing the map to a test that has no
other reason to see it.

`MatchIndex.cs:345`, dropping an item from the reverse map. Every path that reaches
it either writes the item's new key over the entry two lines later, or is a removal
whose entry no lookup reads, because `Lookup` reads only the forward map. The
reverse map exists so that an item whose metadata was repaired leaves the key it no
longer produces, and that path is covered by
`AnItemWhoseKeyChangedLeavesTheKeyItNoLongerProduces` in `MatchIndexTests`.

## What this does not cover

The timeouts are counted as killed and were not read one by one. A timeout is
a test that stopped rather than one that failed for the reason the mutant names, so
the count is a floor on what the suite noticed rather than a statement about which
assertion did it.

The run covers one target framework, `net10.0`. The suite runs on both, and a
mutant that only survives on the other line would not be found here. Nothing in the
matcher is written per line, which is a reading of the sources rather than something
a check refuses.

Nothing compares this document against a report. The score and the counts above are
the run named at the top of this section and are not a standing fact about the tree:
a change to the suite moves them and nothing here goes red. What is machine-held is
the scope and the never-gating threshold, in `MutationScopeTests`, and the instrument
having produced a report at all, in `.github/check-mutation-report.py`.
