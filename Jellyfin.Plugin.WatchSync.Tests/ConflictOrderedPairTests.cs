using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Conflict;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Walks every ordered pair of states the two sides can hold, per row of the conflict table,
/// with the expected answer in <c>Conflict/ordered-pairs.txt</c> rather than in this file.
/// That is the third condition of #81, and the mirror leg is the fourth.
///
/// <see cref="ConflictRowCoverageTests"/> holds every row of the table to the facts that drive
/// it, and says in its own words what it cannot see: which pairs of states those facts reach.
/// A rule tested by the pairs somebody thought of is a rule whose interesting pairs are the
/// ones nobody thought of, and the pair that was never driven is the one a later change to make
/// a resolver simpler quietly answers differently. So the pairs are enumerated rather than
/// chosen: the register declares the states, this walks the whole cross product, and a pair
/// missing from the register is refused rather than passing as a table that looks complete.
///
/// The register is data rather than source for the reason the vocabularies already in this
/// project are, and for one more that is specific to it: an expected winner written in a switch
/// beside the rule it judges is a second implementation of that rule, and two implementations of
/// one rule agree with each other rather than with the table.
///
/// What this cannot judge is whether a row's rule is the right rule, which is the bound every
/// check over the conflict table carries and is a reading at review. What it does judge is that
/// the rule answers the same thing every time, that it answers the same thing whichever side is
/// asked first except where the register declares otherwise and that declaration is executed,
/// and that a value the server cannot produce is refused rather than answered.
/// </summary>
public class ConflictOrderedPairTests
{
    /// <summary>
    /// The fields this table walks are the rows of the conflict table that have a rule, read out
    /// of the coverage register rather than listed here.
    ///
    /// This is what makes the pair table fail closed the day the last row gains a rule. That row
    /// moves from awaited to covered in the coverage register, this file is then missing a field,
    /// and the walk is owed for it before anything is green again.
    /// </summary>
    [Fact]
    public void TheTableWalksEveryRowThatHasARule()
    {
        var covered = ConflictRowCoverageTests.Register()
            .Where(entry => entry.IsCovered)
            .Select(entry => entry.Field)
            .ToList();

        Assert.NotEmpty(covered);

        var walked = Register().States.Select(state => state.Field).Distinct(StringComparer.Ordinal).ToList();

        Assert.Empty(covered
            .Where(field => !walked.Contains(field, StringComparer.Ordinal))
            .Select(field => $"{field} has a rule in the resolver and no states in the ordered pair table, so no pair of states is walked for it."));

        Assert.Empty(walked
            .Where(field => !covered.Contains(field, StringComparer.Ordinal))
            .Select(field => $"{field} has states in the ordered pair table and no rule the coverage register declares covered."));
    }

    /// <summary>
    /// The whole point. Every ordered pair of the states declared for a field is in the table
    /// exactly once, both directions of a pair and a state against itself included.
    /// </summary>
    [Fact]
    public void EveryOrderedPairOfStatesIsInTheTableExactlyOnce()
    {
        var table = Register();

        Assert.NotEmpty(table.Pairs);

        var report = Compare(table.States, table.Pairs);

        Assert.Empty(report.Missing
            .Select(key => $"{key} is an ordered pair of declared states and the table has no entry for it."));

        Assert.Empty(report.Unknown
            .Select(key => $"{key} is an entry of the table and names a state the field does not declare."));

        Assert.Empty(report.Repeated
            .Select(key => $"{key} has more than one entry, so which answer is expected is undefined."));
    }

    /// <summary>
    /// Each pair is driven through the rule its row names and the answer is compared against the
    /// register, column by column, so a rule that answered with the right member and the wrong
    /// loser is refused rather than passing on the member alone.
    /// </summary>
    [Fact]
    public void EveryPairAnswersWhatTheTableSaysItAnswers()
    {
        var table = Register();

        foreach (var pair in table.Pairs)
        {
            Assert.Equal(Expected(pair), Resolve(table, pair.Field, pair.Here, pair.Peer));
        }
    }

    /// <summary>
    /// A pure function of its inputs, in the half that is about repetition. Each pair is driven
    /// twice from two states built afresh, and the two answers are compared.
    ///
    /// What this refuses is a rule that carries something between calls: a cached answer keyed on
    /// too little, a static holding the last reading, a value read once and reused. None of the
    /// three rules does today, and the failure is invisible in a suite that drives each pair once
    /// because the first call is always right.
    /// </summary>
    [Fact]
    public void EveryPairAnswersTheSameThingTwice()
    {
        var table = Register();

        foreach (var pair in table.Pairs)
        {
            Assert.Equal(
                Resolve(table, pair.Field, pair.Here, pair.Peer),
                Resolve(table, pair.Field, pair.Here, pair.Peer));
        }
    }

    /// <summary>
    /// A pure function of its inputs, in the half that is about order. Every pair the register
    /// declares mirrored is driven with the two sides exchanged, and the answer has to be the
    /// same answer with the two loses columns exchanged.
    ///
    /// This is the failure the conflict table names for the position row: a rule that asks
    /// whichever side it happens to hold first answers differently in the two directions, so two
    /// paired servers disagree about the same pair of readings and each one overwrites the other
    /// on every exchange.
    /// </summary>
    [Fact]
    public void EveryMirroredPairAnswersTheMirrorWithTheSidesExchanged()
    {
        var table = Register();
        var mirrored = table.Pairs.Where(pair => pair.Mirrored).ToList();

        Assert.NotEmpty(mirrored);

        foreach (var pair in mirrored)
        {
            Assert.Equal(
                Mirror(Resolve(table, pair.Field, pair.Here, pair.Peer)),
                Resolve(table, pair.Field, pair.Peer, pair.Here));
        }
    }

    /// <summary>
    /// The other direction of the same declaration, so the column fails closed.
    ///
    /// A pair declared asymmetric has to actually be asymmetric. Without this leg the word would
    /// be a way of excusing a pair from the mirror check, and a rule that stopped reading the
    /// peer's date against this server's present moment would leave every asymmetric entry
    /// mirroring and nothing would say so.
    /// </summary>
    [Fact]
    public void EveryAsymmetricPairIsRefusedTheMirror()
    {
        var table = Register();
        var asymmetric = table.Pairs.Where(pair => !pair.Mirrored).ToList();

        Assert.NotEmpty(asymmetric);

        foreach (var pair in asymmetric)
        {
            Assert.NotEqual(
                Mirror(Resolve(table, pair.Field, pair.Here, pair.Peer)),
                Resolve(table, pair.Field, pair.Peer, pair.Here));
        }
    }

    /// <summary>
    /// A state no answer exists for is refused, against every state its field declares, in both
    /// positions.
    ///
    /// The cross product above holds the states the server produces. A position below zero and a
    /// count below zero are not among them: they reach a rule only out of an envelope, and a rule
    /// that answered one would discard it as a position somebody reached or read it as a
    /// shortfall it is not. So they are asserted impossible here rather than left undefined, and
    /// they are held against every ordinary state rather than against one, because a check made
    /// before the pair is looked at and a check made inside one branch of it are the same
    /// refusal until a branch stops making it.
    /// </summary>
    [Fact]
    public void AStateNoAnswerExistsForIsRefusedAgainstEveryOtherState()
    {
        var table = Register();

        Assert.NotEmpty(table.Refused);

        foreach (var refused in table.Refused)
        {
            var others = table.States.Where(state => string.Equals(state.Field, refused.Field, StringComparison.Ordinal)).ToList();

            Assert.NotEmpty(others);

            foreach (var other in others)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(table, refused.Field, refused, other));
                Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(table, refused.Field, other, refused));
            }
        }
    }

    /// <summary>
    /// The guard proven by deleting it, on the mistake a hand-written cross product actually
    /// makes. The near-miss declares every state, uses every state, and leaves out one direction
    /// of one pair, so it reads as complete to anybody counting states. The repair is that one
    /// line and nothing else.
    ///
    /// The fixture carries its own vocabulary, because a fixture judged against the real register
    /// would prove the state of that file on the day it ran rather than proving the guard.
    /// </summary>
    [Fact]
    public void TheGuardRefusesTheNearMissAndPassesItsRepair()
    {
        var refused = Parse(Fixture("ordered-pairs-near-miss.txt"));

        Assert.Equal("Fixture: two against one", Assert.Single(Compare(refused.States, refused.Pairs).Missing));

        var repaired = Parse(Fixture("ordered-pairs-near-miss-repaired.txt"));
        var report = Compare(repaired.States, repaired.Pairs);

        Assert.Empty(report.Missing);
        Assert.Empty(report.Unknown);
        Assert.Empty(report.Repeated);
    }

    /// <summary>
    /// The other way a hand-written cross product goes wrong, which is a pair written twice while
    /// its reverse is the one that is missing. This leg is driven off the fixture, because the
    /// register has no repeat and a leg exercised only by the tree stops being exercised the
    /// moment the tree is right.
    /// </summary>
    [Fact]
    public void APairEnteredTwiceIsRefused()
    {
        var repaired = Parse(Fixture("ordered-pairs-near-miss-repaired.txt"));

        Assert.Empty(Compare(repaired.States, repaired.Pairs).Repeated);

        var doubled = repaired.Pairs.Append(repaired.Pairs[0]).ToList();

        Assert.Equal("Fixture: one against one", Assert.Single(Compare(repaired.States, doubled).Repeated));
    }

    /// <summary>
    /// A setting is context the driver asks for by name, so a line nobody reads is refused rather
    /// than sitting in the register looking like configuration.
    /// </summary>
    [Fact]
    public void EverySettingIsOneTheDriverAsksFor()
    {
        var table = Register();

        foreach (var field in table.States.Select(state => state.Field).Distinct(StringComparer.Ordinal))
        {
            Assert.Equal(
                SettingsWanted(field).OrderBy(key => key, StringComparer.Ordinal),
                table.Settings.Keys
                    .Where(key => string.Equals(key.Field, field, StringComparison.Ordinal))
                    .Select(key => key.Key)
                    .OrderBy(key => key, StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// The keys the driver reads for one field. A field the driver does not know reaches this and
    /// fails, which is the same refusal <see cref="Resolve"/> makes and is why neither is a
    /// silent pass.
    /// </summary>
    /// <param name="field">The row of the conflict table.</param>
    /// <returns>The setting keys that field is driven with.</returns>
    internal static IReadOnlyList<string> SettingsWanted(string field) => field switch
    {
        "Played" => Array.Empty<string>(),
        "PlaybackPositionTicks" => new[] { "now", "toleratedSkew" },
        "PlayCount" => new[] { "agreed" },
        _ => throw new InvalidOperationException($"{field} is walked by the ordered pair table and this suite has no driver for it."),
    };

    /// <summary>
    /// Drives one ordered pair through the rule that decides its row.
    /// </summary>
    /// <param name="table">The register.</param>
    /// <param name="field">The row of the conflict table.</param>
    /// <param name="here">The name of the state this server holds.</param>
    /// <param name="peer">The name of the state the peer holds.</param>
    /// <returns>What the rule answered, in the register's own columns.</returns>
    internal static Resolution Resolve(Table table, string field, string here, string peer) =>
        Resolve(table, field, table.State(field, here), table.State(field, peer));

    /// <summary>
    /// Drives two states through the rule that decides one row, and reports the answer in the
    /// four columns the register carries.
    ///
    /// The switch is the wiring rather than the expectation: it names which rule answers for a
    /// row and nothing about what the answer should be. A row with no case reaches the last arm
    /// and fails, so a rule that lands for a row nobody wired up is refused rather than walked
    /// past.
    /// </summary>
    /// <param name="table">The register.</param>
    /// <param name="field">The row of the conflict table.</param>
    /// <param name="here">The state this server holds.</param>
    /// <param name="peer">The state the peer holds.</param>
    /// <returns>What the rule answered.</returns>
    internal static Resolution Resolve(Table table, string field, Side here, Side peer)
    {
        switch (field)
        {
            case "Played":
                var ratchet = PlayedRatchet.Hold(here.AsState(), peer.AsState());

                return new Resolution(
                    ratchet.Answer.ToString(),
                    null,
                    ratchet.PositionDiscardedHere,
                    ratchet.PositionDiscardedAtThePeer);

            case "PlaybackPositionTicks":
                var recency = PositionRecency.Settle(
                    here.AsState(),
                    peer.AsState(),
                    TimeSpan.ParseExact(table.Setting(field, "toleratedSkew"), "c", CultureInfo.InvariantCulture),
                    Moment(table.Setting(field, "now")));

                return new Resolution(
                    recency.Answer.ToString(),
                    recency.Position,
                    recency.PositionDiscardedHere,
                    recency.PositionDiscardedAtThePeer);

            case "PlayCount":
                var reconciliation = PlayCountReconciliation.Reconcile(
                    int.Parse(table.Setting(field, "agreed"), CultureInfo.InvariantCulture),
                    here.PlayCount,
                    peer.PlayCount);

                return new Resolution(
                    reconciliation.Answer.ToString(),
                    reconciliation.Count,
                    Loss(reconciliation.ShortfallHere),
                    Loss(reconciliation.ShortfallAtThePeer));

            default:
                throw new InvalidOperationException($"{field} is walked by the ordered pair table and this suite has no driver for it.");
        }
    }

    /// <summary>
    /// What a register entry says the answer is.
    /// </summary>
    /// <param name="pair">The entry.</param>
    /// <returns>The answer it declares.</returns>
    internal static Resolution Expected(Pair pair) =>
        new Resolution(pair.Answer, pair.Value, pair.HereLoses, pair.PeerLoses);

    /// <summary>
    /// One answer with the two sides exchanged. The answer and the value are properties of the
    /// pair rather than of a side, so only the two loses columns move.
    /// </summary>
    /// <param name="resolution">The answer.</param>
    /// <returns>Its mirror.</returns>
    internal static Resolution Mirror(Resolution resolution) =>
        new Resolution(resolution.Answer, resolution.Value, resolution.PeerLoses, resolution.HereLoses);

    /// <summary>
    /// A shortfall of nothing is nothing lost, which is what the register's empty column means
    /// for every row rather than for two of them.
    /// </summary>
    /// <param name="shortfall">What the side fell short of the agreement by.</param>
    /// <returns>The loss, or null where there was none.</returns>
    internal static long? Loss(int shortfall) => shortfall == 0 ? null : shortfall;

    /// <summary>
    /// An instant out of the register. It carries its zone, and a value that lost it is refused
    /// here rather than reaching a rule that says in its own words why it will not take one.
    /// </summary>
    /// <param name="value">The written form.</param>
    /// <returns>The instant.</returns>
    internal static DateTime Moment(string value)
    {
        var moment = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        Assert.Equal(DateTimeKind.Utc, moment.Kind);

        return moment;
    }

    /// <summary>
    /// What a set of declared states and a set of entries disagree about. Pure, so the fixtures
    /// run through the same code the register does rather than through a second implementation
    /// of it.
    /// </summary>
    /// <param name="states">The declared states.</param>
    /// <param name="pairs">The entries.</param>
    /// <returns>What the two disagree about.</returns>
    internal static Disagreement Compare(IReadOnlyList<Side> states, IReadOnlyList<Pair> pairs)
    {
        var declared = states
            .GroupBy(state => state.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(state => state.Name).ToList(), StringComparer.Ordinal);

        var expected = declared
            .SelectMany(field => field.Value.SelectMany(here => field.Value.Select(peer => Key(field.Key, here, peer))))
            .ToList();

        var entered = pairs.Select(pair => Key(pair.Field, pair.Here, pair.Peer)).ToList();
        var present = new HashSet<string>(entered, StringComparer.Ordinal);

        return new Disagreement(
            expected.Where(key => !present.Contains(key)).ToList(),
            entered.Where(key => !expected.Contains(key, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToList(),
            entered.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList());
    }

    /// <summary>
    /// How one ordered pair is named, in both the expected set and the entered one, so the two
    /// are compared as one vocabulary rather than as two shapes that have to be kept in step.
    /// </summary>
    /// <param name="field">The row of the conflict table.</param>
    /// <param name="here">The name of the state this server holds.</param>
    /// <param name="peer">The name of the state the peer holds.</param>
    /// <returns>The name.</returns>
    internal static string Key(string field, string here, string peer) =>
        $"{field}: {here} against {peer}";

    /// <summary>
    /// The register, read as data.
    /// </summary>
    /// <returns>The table.</returns>
    internal static Table Register() => Parse(Lines("ordered-pairs.txt"));

    /// <summary>
    /// Reads one of the two fixtures.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>Its lines.</returns>
    internal static IReadOnlyList<string> Fixture(string name) => Lines(name);

    /// <summary>
    /// Reads a file out of the register's own directory.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>Its lines.</returns>
    internal static IReadOnlyList<string> Lines(string name) =>
        File.ReadAllLines(Path.Join(
            HeadlessGuardTests.HeadlessGuard.RepositoryRoot(),
            "Jellyfin.Plugin.WatchSync.Tests",
            "Conflict",
            name));

    /// <summary>
    /// Reads the register out of lines. Pure, so the fixtures run through the same code the
    /// register does. A line the parser cannot read fails rather than being skipped, which is the
    /// difference between a register and a comment.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The table.</returns>
    internal static Table Parse(IEnumerable<string> lines)
    {
        var settings = new Dictionary<SettingKey, string>();
        var states = new List<Side>();
        var refused = new List<Side>();
        var pairs = new List<Pair>();

        foreach (var line in lines)
        {
            var text = line.Trim();

            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            // Split the line rather than the trimmed text, for the reason the coverage register's
            // parser gives: a column that was emptied and left its separator behind still arrives
            // as a column, so the refusal names the thing that is missing rather than the shape of
            // the line.
            var fields = line.Split(" :: ").Select(field => field.Trim()).ToList();

            switch (fields[0])
            {
                case "setting":
                    Assert.True(fields.Count == 4, $"a setting of the ordered pair register has {fields.Count} fields rather than four: {text}");
                    settings.Add(new SettingKey(fields[1], fields[2]), fields[3]);
                    break;

                case "state":
                    states.Add(ReadSide(fields, text));
                    break;

                case "refused":
                    refused.Add(ReadSide(fields, text));
                    break;

                case "pair":
                    Assert.True(fields.Count == 9, $"a pair of the ordered pair register has {fields.Count} fields rather than nine: {text}");
                    Assert.Contains(fields[8], new[] { "mirrored", "asymmetric" });
                    pairs.Add(new Pair(
                        fields[1],
                        fields[2],
                        fields[3],
                        fields[4],
                        Number(fields[5]),
                        Number(fields[6]),
                        Number(fields[7]),
                        string.Equals(fields[8], "mirrored", StringComparison.Ordinal)));
                    break;

                default:
                    throw new InvalidOperationException($"a line of the ordered pair register begins with {fields[0]}, which is not one of setting, state, refused or pair: {text}");
            }
        }

        Assert.NotEmpty(states);
        Assert.NotEmpty(pairs);

        return new Table(settings, states, refused, pairs);
    }

    /// <summary>
    /// One side's state out of a line.
    /// </summary>
    /// <param name="fields">The columns.</param>
    /// <param name="text">The line, for a refusal that shows it.</param>
    /// <returns>The state.</returns>
    private static Side ReadSide(IReadOnlyList<string> fields, string text)
    {
        Assert.True(fields.Count == 7, $"a state of the ordered pair register has {fields.Count} fields rather than seven: {text}");

        return new Side(
            fields[1],
            fields[2],
            bool.Parse(fields[3]),
            int.Parse(fields[4], CultureInfo.InvariantCulture),
            long.Parse(fields[5], CultureInfo.InvariantCulture),
            string.Equals(fields[6], "-", StringComparison.Ordinal) ? null : Moment(fields[6]));
    }

    /// <summary>
    /// A number out of a column, where the empty column is written as a dash.
    /// </summary>
    /// <param name="value">The written form.</param>
    /// <returns>The number, or null.</returns>
    private static long? Number(string value) =>
        string.Equals(value, "-", StringComparison.Ordinal)
            ? null
            : long.Parse(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// Which field of which row a setting is about.
    /// </summary>
    /// <param name="Field">The row of the conflict table.</param>
    /// <param name="Key">The setting name.</param>
    internal sealed record SettingKey(string Field, string Key);

    /// <summary>
    /// One side's state, named, with every member of <see cref="SyncedState"/> filled in.
    /// </summary>
    /// <param name="Field">The row of the conflict table it is declared for.</param>
    /// <param name="Name">Its name in the register.</param>
    /// <param name="Played">Whether the person watched the work.</param>
    /// <param name="PlayCount">How often the person watched the work.</param>
    /// <param name="PositionTicks">Where the person stopped.</param>
    /// <param name="LastPlayedDate">When the person last watched the work, or null.</param>
    internal sealed record Side(
        string Field,
        string Name,
        bool Played,
        int PlayCount,
        long PositionTicks,
        DateTime? LastPlayedDate)
    {
        /// <summary>
        /// The state as the rules take it. Built afresh on every call, so the repetition leg
        /// drives two states rather than one held between two calls.
        /// </summary>
        /// <returns>The state.</returns>
        internal SyncedState AsState() =>
            new SyncedState(Played, PlayCount, PositionTicks, LastPlayedDate);
    }

    /// <summary>
    /// One ordered pair and the answer the register declares for it.
    /// </summary>
    /// <param name="Field">The row of the conflict table.</param>
    /// <param name="Here">The name of the state this server holds.</param>
    /// <param name="Peer">The name of the state the peer holds.</param>
    /// <param name="Answer">The member the rule answers with.</param>
    /// <param name="Value">The value it answers with, or null where it answers with none.</param>
    /// <param name="HereLoses">What this server lost, or null where it lost nothing.</param>
    /// <param name="PeerLoses">What the peer lost, or null where it lost nothing.</param>
    /// <param name="Mirrored">Whether exchanging the two sides exchanges the answer.</param>
    internal sealed record Pair(
        string Field,
        string Here,
        string Peer,
        string Answer,
        long? Value,
        long? HereLoses,
        long? PeerLoses,
        bool Mirrored);

    /// <summary>
    /// What a rule answered, in the register's own columns.
    /// </summary>
    /// <param name="Answer">The member the rule answered with.</param>
    /// <param name="Value">The value it answered with, or null.</param>
    /// <param name="HereLoses">What this server lost, or null.</param>
    /// <param name="PeerLoses">What the peer lost, or null.</param>
    internal sealed record Resolution(string Answer, long? Value, long? HereLoses, long? PeerLoses);

    /// <summary>
    /// What a set of declared states and a set of entries disagree about.
    /// </summary>
    /// <param name="Missing">Ordered pairs of declared states with no entry.</param>
    /// <param name="Unknown">Entries naming a state the field does not declare.</param>
    /// <param name="Repeated">Ordered pairs with more than one entry.</param>
    internal sealed record Disagreement(
        IReadOnlyList<string> Missing,
        IReadOnlyList<string> Unknown,
        IReadOnlyList<string> Repeated);

    /// <summary>
    /// The register, read as data.
    /// </summary>
    /// <param name="Settings">The context each field is driven in.</param>
    /// <param name="States">The states each field declares.</param>
    /// <param name="Refused">The states each field has no answer for.</param>
    /// <param name="Pairs">The ordered pairs and their answers.</param>
    internal sealed record Table(
        IReadOnlyDictionary<SettingKey, string> Settings,
        IReadOnlyList<Side> States,
        IReadOnlyList<Side> Refused,
        IReadOnlyList<Pair> Pairs)
    {
        /// <summary>
        /// One declared state by name. A name no state carries fails here rather than producing a
        /// default nobody wrote.
        /// </summary>
        /// <param name="field">The row of the conflict table.</param>
        /// <param name="name">The state name.</param>
        /// <returns>The state.</returns>
        internal Side State(string field, string name)
        {
            var state = States.SingleOrDefault(candidate =>
                string.Equals(candidate.Field, field, StringComparison.Ordinal)
                && string.Equals(candidate.Name, name, StringComparison.Ordinal));

            Assert.True(state is not null, $"{field} has no state named {name}.");

            return state!;
        }

        /// <summary>
        /// One setting by name.
        /// </summary>
        /// <param name="field">The row of the conflict table.</param>
        /// <param name="key">The setting name.</param>
        /// <returns>Its value.</returns>
        internal string Setting(string field, string key)
        {
            Assert.True(
                Settings.TryGetValue(new SettingKey(field, key), out var value),
                $"{field} is driven with {key} and the register declares no such setting.");

            return value!;
        }
    }
}
