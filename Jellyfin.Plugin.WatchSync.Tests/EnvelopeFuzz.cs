using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The fuzz harness against the inbound envelope reader and the bounds beside it, which is #102.
///
/// An envelope is the one surface a peer controls. Everything else this plugin reads is its own
/// store or its own server, so this is where bytes chosen by a machine this operator does not
/// administer meet code, and it is the surface worth spending a run on rather than a review.
///
/// <para>Nothing here is coverage guided. The mutations are chosen from the seeds and from a
/// fixed set of shapes rather than from what the last input executed, so a path this harness
/// never reaches is a path it cannot know it never reached. That bound is the honest half of
/// what a run reports, and <c>docs/fuzz.md</c> states it beside the run rather than leaving it
/// to be inferred from a green job.</para>
///
/// <para>A run is bounded by iterations rather than by elapsed time, and every input is derived
/// from the number it was started with. So a finding reproduces from two numbers, on any
/// machine, and the harness needs no clock: the suite refuses one in a tracked test source, and
/// a harness reading the machine clock would ask for the departure that rule exists to keep
/// unnecessary.</para>
/// </summary>
internal static class EnvelopeFuzz
{
    /// <summary>
    /// How many envelopes this peer is treated as having already sent inside the window while
    /// the bounds are asked. Zero, because the rate is a fact the caller keeps rather than one
    /// the bytes carry, and a harness that varied it would be fuzzing its own bookkeeping.
    /// </summary>
    private const int NoEnvelopesInTheWindowYet = 0;

    /// <summary>
    /// The member the change list sits under. The reader declares it internally to the plugin,
    /// so this is a second spelling; the readable seeds in the suite are what would fail if the
    /// two came apart, rather than a run nobody would read for a week.
    /// </summary>
    private const string ChangesMember = "changes";

    /// <summary>
    /// The member the version sits under, for the same reason.
    /// </summary>
    private const string VersionMember = "version";

    /// <summary>
    /// The characters a mutation puts where one is changed or inserted. They are the ones this
    /// surface is written against, because a uniform byte spends a run in the first branch of
    /// the parse, which every seed already reaches.
    /// </summary>
    private const string InterestingCharacters =
        "{}[]\",:0123456789.-+eEtrufalsn \t\r\n\\/\u0000\uFFFD\u00E9\u4E2D";

    /// <summary>
    /// What one reading looked like from outside the reader.
    ///
    /// The oracle judges this rather than the reading itself, so a reader that breaks one rule
    /// on purpose can be handed to it. The plugin's own reading cannot be built from here at
    /// all: its constructor is private and its factories are internal to the plugin, which is
    /// the property #18 wanted and is not one to undo for a harness.
    /// </summary>
    /// <param name="Answer">The answer, by name.</param>
    /// <param name="IsRefused">Whether the reading refuses the envelope.</param>
    /// <param name="HasEnvelope">Whether it carries an envelope to read.</param>
    /// <param name="FoundVersion">The version it names, or null.</param>
    /// <param name="MissingMember">The member it names as absent, or null.</param>
    /// <param name="DuplicateMember">The member it names as carried twice, or null.</param>
    /// <param name="SupportedVersions">The set it was made against.</param>
    /// <param name="EnvelopeVersion">The version of the envelope it carries, or null.</param>
    /// <param name="Members">The members beside the version, or none.</param>
    internal sealed record Observation(
        string Answer,
        bool IsRefused,
        bool HasEnvelope,
        int? FoundVersion,
        string? MissingMember,
        string? DuplicateMember,
        IReadOnlyList<int> SupportedVersions,
        int? EnvelopeVersion,
        IReadOnlyList<string> Members);

    /// <summary>
    /// One thing an input made the reader do that the reader's own contract says it may not.
    /// </summary>
    /// <param name="Rule">Which rule the input broke.</param>
    /// <param name="Body">The bytes that broke it.</param>
    /// <param name="Detail">What was seen, so a finding names the state and not only the rule.</param>
    internal sealed record Finding(string Rule, string Body, string Detail);

    /// <summary>
    /// What one bounded run came back with.
    /// </summary>
    /// <param name="Inputs">How many inputs were judged, the seeds included.</param>
    /// <param name="Findings">Everything the oracle refused.</param>
    /// <param name="Corpus">The inputs kept, one per answer the run had not seen before.</param>
    internal sealed record Sweep(int Inputs, IReadOnlyList<Finding> Findings, IReadOnlyList<string> Corpus);

    /// <summary>
    /// The reader under test, taken as a delegate so the oracle can be proven against readers
    /// that break each rule on purpose.
    ///
    /// The proof is the reason for the indirection rather than configurability. An oracle whose
    /// only subject is a reader that satisfies it is an oracle nobody has watched refuse
    /// anything, and it passes identically on the day it has stopped asking.
    /// </summary>
    /// <param name="body">The bytes of the envelope, as text.</param>
    /// <param name="supportedVersions">The versions the caller speaks.</param>
    /// <returns>What the reader answered.</returns>
    internal delegate Observation Reader(string body, IReadOnlyList<int> supportedVersions);

    /// <summary>
    /// The reader this plugin ships, seen from outside.
    /// </summary>
    /// <returns>The reader.</returns>
    internal static Reader TheRealReader() => (body, versions) => Observe(Envelope.Read(body, versions));

    /// <summary>
    /// What one reading looks like from outside the reader.
    /// </summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The observation.</returns>
    /// <exception cref="ArgumentNullException">The reading is null.</exception>
    internal static Observation Observe(EnvelopeReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new Observation(
            reading.Answer.ToString(),
            reading.IsRefused,
            reading.Envelope is not null,
            reading.FoundVersion,
            reading.MissingMember,
            reading.DuplicateMember,
            reading.SupportedVersions,
            reading.Envelope?.Version,
            reading.Envelope is null
                ? Array.Empty<string>()
                : reading.Envelope.Members.Select(pair => pair.Key).ToList());
    }

    /// <summary>
    /// Runs one bounded sweep: every seed first, then that many mutations of them.
    ///
    /// The seeds are judged before anything is mutated, because a seed the reader already
    /// answers wrongly is a finding about the corpus rather than about the run, and a sweep that
    /// met it among ten thousand mutations would report it as one of them.
    /// </summary>
    /// <param name="seeds">The corpus to start from.</param>
    /// <param name="iterations">How many mutated inputs to judge after the seeds.</param>
    /// <param name="seed">The number every input of this run is derived from.</param>
    /// <param name="read">The reader to judge.</param>
    /// <returns>What the run found and what it kept.</returns>
    /// <exception cref="ArgumentNullException">The seeds or the reader are null.</exception>
    /// <exception cref="ArgumentException">
    /// An empty corpus. It would judge nothing and report no finding, which reads exactly like a
    /// run that judged everything and found none.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A negative number of iterations.</exception>
    internal static Sweep Run(IReadOnlyList<string> seeds, int iterations, int seed, Reader read)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfNegative(iterations, nameof(iterations));

        if (seeds.Count == 0)
        {
            throw new ArgumentException(
                "An empty corpus judges nothing and reports no finding, which is indistinguishable from a run that found none.",
                nameof(seeds));
        }

        var findings = new List<Finding>();
        var kept = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var random = new Random(seed);
        var inputs = 0;

        foreach (var body in seeds)
        {
            inputs++;
            JudgeAndKeep(body, read, findings, kept, seen);
        }

        for (var index = 0; index < iterations; index++)
        {
            inputs++;
            JudgeAndKeep(Mutate(seeds, random), read, findings, kept, seen);
        }

        return new Sweep(inputs, findings, kept);
    }

    /// <summary>
    /// Judges one input against what the reader's own contract promises, and says what it broke.
    ///
    /// The absence of a crash is the weakest thing a harness can assert and it is not what this
    /// asserts. #18 and #19 decided that this surface refuses rather than truncates and that
    /// every refusal is its own answer, so each of those is a property an input can break while
    /// the process stays up: a refusal carrying an envelope to read, a readable envelope of a
    /// version nobody speaks, a missing member named by nothing.
    /// </summary>
    /// <param name="body">The bytes to judge.</param>
    /// <param name="read">The reader to judge them with.</param>
    /// <returns>Everything this input broke, and the answer it produced.</returns>
    /// <exception cref="ArgumentNullException">The body or the reader are null.</exception>
    internal static (IReadOnlyList<Finding> Findings, string Answer) Judge(string body, Reader read)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(read);

        var findings = new List<Finding>();
        Observation? seen;

        try
        {
            seen = read(body, EnvelopeVersions.Supported);
        }
        catch (Exception thrown)
        {
            findings.Add(new Finding(
                "reader-threw",
                body,
                (thrown.GetType().FullName ?? "an exception") + ": " + thrown.Message));

            return (findings, "threw");
        }

        if (seen is null)
        {
            findings.Add(new Finding("reader-answered-nothing", body, "the reader came back with no reading at all"));

            return (findings, "nothing");
        }

        JudgeTheReading(body, seen, findings);

        var bounds = JudgeTheBounds(body, findings);

        return (findings, AnswerKey(seen, bounds));
    }

    /// <summary>
    /// One mutated input, derived from the corpus and from the run's own source of choices.
    ///
    /// The shapes are the ones this surface is written against: a version that is not a whole
    /// number, a change list large enough to reach its bound, a string past its own, a body past
    /// the byte bound, nesting deep enough to matter to a parser, and two seeds spliced together.
    /// </summary>
    /// <param name="seeds">The corpus to derive from.</param>
    /// <param name="random">The run's own source of choices.</param>
    /// <returns>The input.</returns>
    /// <exception cref="ArgumentNullException">The seeds or the source of choices are null.</exception>
    /// <exception cref="ArgumentException">An empty corpus, which nothing can be derived from.</exception>
    internal static string Mutate(IReadOnlyList<string> seeds, Random random)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        ArgumentNullException.ThrowIfNull(random);

        if (seeds.Count == 0)
        {
            throw new ArgumentException("There is nothing to mutate.", nameof(seeds));
        }

        var body = seeds[random.Next(seeds.Count)];

        return random.Next(11) switch
        {
            0 => Truncated(body, random),
            1 => WithACharacterChanged(body, random),
            2 => WithACharacterInserted(body, random),
            3 => body + seeds[random.Next(seeds.Count)],
            4 => Spliced(body, seeds[random.Next(seeds.Count)], random),
            5 => WithAVersionOf(body, VersionLiteral(random)),
            6 => WithAChangeListOf(random.Next(1, 4096)),
            7 => WithALongStringOf(random.Next(1, 4) * EnvelopeBounds.LongestStringLength),
            8 => PastTheByteBound(random),
            9 => Nested(random.Next(1, 64)),
            _ => Repeated(body, random.Next(2, 8)),
        };
    }

    /// <summary>
    /// The rules over one reading, each of them a sentence the reader's own documentation makes.
    /// </summary>
    /// <param name="body">The bytes that produced the reading.</param>
    /// <param name="seen">What the reader answered.</param>
    /// <param name="findings">Where a broken rule is written.</param>
    private static void JudgeTheReading(string body, Observation seen, List<Finding> findings)
    {
        if (seen.SupportedVersions is null || seen.SupportedVersions.Count == 0)
        {
            findings.Add(new Finding(
                "reading-names-no-supported-set",
                body,
                "the reading names no version it was made against, so a refusal tells an operator nothing"));
        }

        if (seen.IsRefused && seen.HasEnvelope)
        {
            findings.Add(new Finding(
                "refused-carries-an-envelope",
                body,
                "a refused reading carries an envelope a caller can read"));
        }

        if (!seen.IsRefused && !seen.HasEnvelope)
        {
            findings.Add(new Finding(
                "readable-carries-no-envelope",
                body,
                "a reading that is not refused carries nothing"));
        }

        switch (seen.Answer)
        {
            case nameof(EnvelopeAnswer.Readable) when seen.HasEnvelope:
                JudgeTheEnvelope(body, seen, findings);
                break;

            case nameof(EnvelopeAnswer.VersionNotSupported) when seen.FoundVersion is null:
                findings.Add(new Finding(
                    "version-not-supported-names-no-version",
                    body,
                    "the refusal names no version, so neither of the two servers can be told to move"));
                break;

            case nameof(EnvelopeAnswer.MemberMissing) when string.IsNullOrEmpty(seen.MissingMember):
                findings.Add(new Finding(
                    "member-missing-names-no-member",
                    body,
                    "the refusal names no member"));
                break;

            case nameof(EnvelopeAnswer.MemberCarriedTwice) when string.IsNullOrEmpty(seen.DuplicateMember):
                findings.Add(new Finding(
                    "member-carried-twice-names-no-member",
                    body,
                    "the refusal names no member, so whoever operates the peer has the whole body to search"));
                break;

            case nameof(EnvelopeAnswer.NotAnEnvelope) when seen.FoundVersion is not null:
                findings.Add(new Finding(
                    "not-an-envelope-names-a-version",
                    body,
                    string.Create(CultureInfo.InvariantCulture, $"bytes that are not an envelope carry version {seen.FoundVersion}")));
                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The rules over an envelope the reader let through.
    /// </summary>
    /// <param name="body">The bytes that produced it.</param>
    /// <param name="seen">What the reader answered.</param>
    /// <param name="findings">Where a broken rule is written.</param>
    private static void JudgeTheEnvelope(string body, Observation seen, List<Finding> findings)
    {
        var version = seen.EnvelopeVersion ?? 0;
        var spoken = EnvelopeVersions.Supported.Contains(version);

        if (!spoken)
        {
            findings.Add(new Finding(
                "readable-version-is-not-spoken",
                body,
                string.Create(CultureInfo.InvariantCulture, $"version {version} was read and is not in the supported set")));
        }

        if (seen.Members.Contains(VersionMember, StringComparer.Ordinal))
        {
            findings.Add(new Finding(
                "readable-keeps-the-version-member",
                body,
                "the version is among the members beside it"));
        }

        if (!spoken)
        {
            return;
        }

        foreach (var required in EnvelopeVersions.Requires(version))
        {
            if (!seen.Members.Contains(required, StringComparer.Ordinal))
            {
                findings.Add(new Finding(
                    "readable-misses-a-required-member",
                    body,
                    required + " is required by the version that was read and is not there"));
            }
        }
    }

    /// <summary>
    /// Asks the bounds what they make of the same bytes, with the three quantities counted the
    /// way a reader in front of them counts them.
    ///
    /// What is checked is membership rather than the order the refusals are tried in. Which
    /// refusal a body past two bounds gets is #19's decision and its own tests' subject; what an
    /// input can break here is the bounds answering that an envelope may be read while one of
    /// the three quantities is past its limit.
    /// </summary>
    /// <param name="body">The bytes to measure.</param>
    /// <param name="findings">Where a broken rule is written.</param>
    /// <returns>The answer the bounds gave, or what stopped them being asked.</returns>
    private static string JudgeTheBounds(string body, List<Finding> findings)
    {
        var declaredBytes = Encoding.UTF8.GetByteCount(body);
        var changes = CountedChanges(body);
        var longest = LongestString(body);

        EnvelopeBounds judged;

        try
        {
            judged = EnvelopeBounds.Judge(NoEnvelopesInTheWindowYet, declaredBytes, changes, longest);
        }
        catch (Exception thrown)
        {
            findings.Add(new Finding("bounds-threw", body, thrown.GetType().FullName ?? "an exception"));

            return "threw";
        }

        var within = declaredBytes <= EnvelopeBounds.MaximumBytes
            && changes <= EnvelopeBounds.MaximumChanges
            && longest <= EnvelopeBounds.LongestStringLength;

        if (judged.MayBeRead != within)
        {
            findings.Add(new Finding(
                "bounds-answer-disagrees-with-its-own-bounds",
                body,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{declaredBytes} bytes, {changes} changes, longest string {longest}, answered {judged.Answer}")));
        }

        return judged.Answer.ToString();
    }

    /// <summary>
    /// How many changes the body carries, counted as a reader counts them: the members of the
    /// change list where the body is an object carrying an array under that name, and none
    /// otherwise.
    /// </summary>
    /// <param name="body">The bytes.</param>
    /// <returns>The count.</returns>
    private static int CountedChanges(string body)
    {
        using var document = Parsed(body);

        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, ChangesMember, StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Array)
            {
                return property.Value.GetArrayLength();
            }
        }

        return 0;
    }

    /// <summary>
    /// The longest string anywhere in the body, counted in the characters a person sees, which
    /// is the unit the bound is written in.
    /// </summary>
    /// <param name="body">The bytes.</param>
    /// <returns>The length of the longest string, or zero where there is none.</returns>
    private static int LongestString(string body)
    {
        using var document = Parsed(body);

        if (document is null)
        {
            return 0;
        }

        var longest = 0;
        var pending = new Stack<JsonElement>();
        pending.Push(document.RootElement);

        while (pending.Count > 0)
        {
            var element = pending.Pop();

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        longest = Math.Max(longest, Characters(property.Name));
                        pending.Push(property.Value);
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        pending.Push(item);
                    }

                    break;

                case JsonValueKind.String:
                    longest = Math.Max(longest, Characters(element.GetString() ?? string.Empty));
                    break;

                default:
                    break;
            }
        }

        return longest;
    }

    /// <summary>
    /// The body as a document, or null where it is not one. Parsing here is the harness
    /// measuring its own input and is never the reader's own parse.
    ///
    /// A document rather than a node, because a node keeps its members in a dictionary it builds
    /// the first time one is read, and a body carrying one member twice makes that build throw.
    /// The measurement would then throw on exactly the input #253 is about, one layer away from
    /// the reader it is meant to be measuring. A document keeps what the bytes said, duplicates
    /// included, so the two quantities above are counted off the body a peer actually sent.
    /// </summary>
    /// <param name="body">The bytes.</param>
    /// <returns>The document, or null.</returns>
    private static JsonDocument? Parsed(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (Exception thrown) when (thrown is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The characters a person sees in a string, which is the unit the bound is written in and
    /// is not the number of storage units the runtime keeps it in.
    /// </summary>
    /// <param name="text">The string.</param>
    /// <returns>The count.</returns>
    private static int Characters(string text)
    {
        var counted = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);

        while (enumerator.MoveNext())
        {
            counted++;
        }

        return counted;
    }

    /// <summary>
    /// What one input answered, as the key a run keeps its corpus by.
    /// </summary>
    /// <param name="seen">What the reader answered.</param>
    /// <param name="bounds">What the bounds answered.</param>
    /// <returns>The key.</returns>
    private static string AnswerKey(Observation seen, string bounds) =>
        string.Create(CultureInfo.InvariantCulture, $"{seen.Answer}/{seen.MissingMember ?? "-"}/{bounds}");

    /// <summary>
    /// Judges one input and keeps it where its answer is one this run has not seen.
    ///
    /// Keeping by answer is the whole of what stands in for coverage here, and it is weaker in a
    /// way worth naming rather than leaving to be discovered: two inputs that reach different
    /// code and come back with the same answer are one entry, so what a run archives is a corpus
    /// of answers and not of paths.
    /// </summary>
    /// <param name="body">The input.</param>
    /// <param name="read">The reader.</param>
    /// <param name="findings">Where its findings go.</param>
    /// <param name="kept">The corpus being built.</param>
    /// <param name="seen">The answers already seen.</param>
    private static void JudgeAndKeep(
        string body,
        Reader read,
        List<Finding> findings,
        List<string> kept,
        HashSet<string> seen)
    {
        var judged = Judge(body, read);

        findings.AddRange(judged.Findings);

        if (seen.Add(judged.Answer))
        {
            kept.Add(body);
        }
    }

    private static string Truncated(string body, Random random) =>
        body.Length == 0 ? body : body[..random.Next(body.Length)];

    private static string WithACharacterChanged(string body, Random random)
    {
        if (body.Length == 0)
        {
            return body;
        }

        var characters = body.ToCharArray();
        characters[random.Next(body.Length)] = Interesting(random);

        return new string(characters);
    }

    private static string WithACharacterInserted(string body, Random random) =>
        body.Insert(random.Next(body.Length + 1), Interesting(random).ToString());

    private static string Spliced(string left, string right, Random random)
    {
        var at = left.Length == 0 ? 0 : random.Next(left.Length);
        var from = right.Length == 0 ? 0 : random.Next(right.Length);

        return string.Concat(left.AsSpan(0, at), right.AsSpan(from));
    }

    private static string WithAVersionOf(string body, string version) =>
        body.Contains("\"version\":", StringComparison.Ordinal)
            ? Regex.Replace(body, "\"version\":[^,}]*", "\"version\":" + version)
            : "{\"version\":" + version + ",\"changes\":[]}";

    private static string VersionLiteral(Random random) =>
        random.Next(8) switch
        {
            0 => "\"1\"",
            1 => "0",
            2 => "-1",
            3 => "1.5",
            4 => "true",
            5 => "null",
            6 => int.MaxValue.ToString(CultureInfo.InvariantCulture),
            _ => "9999999999999999999999",
        };

    private static string WithAChangeListOf(int count) =>
        "{\"version\":1,\"changes\":[" + string.Join(',', Enumerable.Repeat("{\"a\":1}", count)) + "]}";

    private static string WithALongStringOf(int length) =>
        "{\"version\":1,\"changes\":[\"" + new string('k', length) + "\"]}";

    /// <summary>
    /// A body past the byte bound, and past that bound alone.
    ///
    /// Every other mutation here is capped by its own shape well below the byte bound, so a run
    /// of any length produced no body large enough to reach it and <c>TooManyBytes</c> was the
    /// one refusal in the set that only the cases could produce. What caps them is the mutation
    /// rather than the iteration count, which is why more iterations were never the answer.
    ///
    /// <para>
    /// It crosses one bound rather than three. The change count stays under its own limit and no
    /// string in it is longer than the string bound, so the refusal this produces is the byte
    /// bound answering rather than the byte bound being asked first. Both numbers are read off
    /// <see cref="EnvelopeBounds"/> and the member count is computed from them, so the shape
    /// follows either bound moving instead of going quietly under one of them.
    /// </para>
    /// </summary>
    /// <param name="random">The run's own source of choices.</param>
    /// <returns>The body.</returns>
    private static string PastTheByteBound(Random random)
    {
        var member = "{\"a\":\"" + new string('k', EnvelopeBounds.LongestStringLength) + "\"}";
        var members = (EnvelopeBounds.MaximumBytes / (member.Length + 1)) + 1 + random.Next(0, 16);

        return "{\"version\":1,\"changes\":["
            + string.Join(',', Enumerable.Repeat(member, members))
            + "]}";
    }

    private static string Nested(int depth) =>
        "{\"version\":1,\"changes\":[" + new string('[', depth) + "1" + new string(']', depth) + "]}";

    private static string Repeated(string body, int times) =>
        string.Concat(Enumerable.Repeat(body, times));

    private static char Interesting(Random random) =>
        InterestingCharacters[random.Next(InterestingCharacters.Length)];
}
