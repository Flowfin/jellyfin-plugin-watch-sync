using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The seed corpus and the bodies the envelope cases hand the reader, both read out of the
/// tracked tree.
/// </summary>
internal static class EnvelopeCorpus
{
    private const string TestProject = "Jellyfin.Plugin.WatchSync.Tests";

    private const string CaseFile = "EnvelopeVersionTests.cs";

    /// <summary>
    /// The seeds, in the order the file carries them.
    /// </summary>
    /// <returns>The corpus.</returns>
    internal static IReadOnlyList<string> Seeds() =>
        File.ReadAllLines(Path.Combine(Root(), TestProject, "Envelope", "corpus.txt"))
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    /// <summary>
    /// The source of the cases the corpus is derived from.
    /// </summary>
    /// <returns>Its text.</returns>
    internal static string EnvelopeCaseSource() =>
        File.ReadAllText(Path.Combine(Root(), TestProject, CaseFile));

    /// <summary>
    /// One of the two fixtures the guard is proven on.
    /// </summary>
    /// <param name="name">The fixture file name.</param>
    /// <returns>Its text.</returns>
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(Root(), TestProject, "Envelope", name));

    /// <summary>
    /// Every body a piece of source hands the reader.
    ///
    /// Two shapes are read, because the cases use both: the first argument of a call to the
    /// reader, and the single string a row of a data-driven case carries.
    ///
    /// What this cannot see is a body assembled at run time. The cases hold three such call
    /// sites today, where the argument is interpolated or is null on purpose, and they are
    /// outside the corpus rather than exempted by name: what makes a body seedable is that
    /// it is bytes rather than an expression.
    /// </summary>
    /// <param name="source">The source to read.</param>
    /// <returns>The bodies it hands over.</returns>
    internal static IReadOnlyList<string> BodiesIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var bodies = new List<string>();

        Collect(source, "Envelope.Read(", bodies);
        Collect(source, "[InlineData(", bodies);

        return bodies;
    }

    private static void Collect(string source, string opening, List<string> bodies)
    {
        var at = source.IndexOf(opening, StringComparison.Ordinal);

        while (at >= 0)
        {
            var after = at + opening.Length;

            while (after < source.Length && char.IsWhiteSpace(source[after]))
            {
                after++;
            }

            var literal = LiteralAt(source, after);

            if (literal is not null)
            {
                bodies.Add(literal);
            }

            at = source.IndexOf(opening, after, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The string literal beginning at a position, or null where what is there is not one.
    ///
    /// A raw literal is read first, because it begins with the character an ordinary one
    /// does: reading it as an ordinary literal would take the empty string between the first
    /// two quotes and leave the body behind.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="at">Where to read from.</param>
    /// <returns>The literal, or null.</returns>
    private static string? LiteralAt(string source, int at)
    {
        const string Raw = "\"\"\"";

        if (at >= source.Length)
        {
            return null;
        }

        if (at + Raw.Length <= source.Length
            && string.CompareOrdinal(source, at, Raw, 0, Raw.Length) == 0)
        {
            var from = at + Raw.Length;
            var to = source.IndexOf(Raw, from, StringComparison.Ordinal);

            return to < 0 ? null : source[from..to];
        }

        if (source[at] != '"')
        {
            return null;
        }

        var text = new StringBuilder();

        for (var index = at + 1; index < source.Length; index++)
        {
            var character = source[index];

            if (character == '\\' && index + 1 < source.Length)
            {
                text.Append(source[index + 1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    var other => other,
                });

                index++;
                continue;
            }

            if (character == '"')
            {
                return text.ToString();
            }

            text.Append(character);
        }

        return null;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var marker = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(marker) || File.Exists(marker))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No repository root above {AppContext.BaseDirectory}. The corpus is read from the tracked tree, and a run with none has nothing to judge.");
    }
}
