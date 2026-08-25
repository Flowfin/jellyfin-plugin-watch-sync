using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The one command a fuzz run is started with, which is #102's first condition.
///
/// It lives in the suite's own assembly for the reason the killed writer beside it does: what is
/// being run has to be the code this plugin ships rather than a second arrangement made to be
/// runnable. The suite never reaches it, because the runner loads the assembly through the
/// adapter and starts at the facts.
///
/// <para>Locally, and in the scheduled workflow, the same line:</para>
///
/// <code>
/// dotnet run --project Jellyfin.Plugin.WatchSync.Tests --framework net10.0 -- fuzz 200000 1 fuzz-out
/// </code>
///
/// <para>The exit code is one where anything was found, so a run that found a crasher is red
/// without anybody reading the log. What happens next is the triage in <c>docs/fuzz.md</c>: a
/// crasher is a security finding with its own issue and its own fix, never a quiet patch inside
/// the harness.</para>
/// </summary>
internal static class EnvelopeFuzzRun
{
    /// <summary>
    /// The verb that asks this assembly for a fuzz run.
    /// </summary>
    internal const string Verb = "fuzz";

    /// <summary>
    /// Runs one sweep and writes what it found and what it kept.
    /// </summary>
    /// <param name="args">The verb, the iterations, the seed, and the directory to write into.</param>
    /// <returns>One where anything was found, two where the run was asked for wrongly, zero otherwise.</returns>
    internal static int Execute(string[] args)
    {
        if (args is null || args.Length != 4)
        {
            Console.Error.WriteLine("fuzz <iterations> <seed> <output directory>");

            return 2;
        }

        if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations)
            || !int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)
            || iterations < 0)
        {
            Console.Error.WriteLine("The iterations and the seed are whole numbers, and the iterations are not negative.");

            return 2;
        }

        var output = args[3];
        var seeds = EnvelopeCorpus.Seeds();

        var sweep = EnvelopeFuzz.Run(seeds, iterations, seed, EnvelopeFuzz.TheRealReader());

        Directory.CreateDirectory(output);

        Write(Path.Combine(output, "corpus.txt"), sweep.Corpus);
        Write(Path.Combine(output, "findings.txt"), sweep.Findings.Select(Line));

        Console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{sweep.Inputs} inputs from {seeds.Count} seeds at seed {seed}, {sweep.Findings.Count} finding(s), {sweep.Corpus.Count} kept."));

        foreach (var finding in sweep.Findings)
        {
            Console.Out.WriteLine(Line(finding));
        }

        // A run that found nothing says what it could not have found, because a green job is read
        // as an absence of defects by whoever did not open the harness.
        Console.Out.WriteLine(
            "Nothing here is coverage guided: an input is kept for producing an answer this run had not seen, so a path reached by no mutation is a path this run says nothing about.");

        return sweep.Findings.Count == 0 ? 0 : 1;
    }

    private static string Line(EnvelopeFuzz.Finding finding) =>
        $"{finding.Rule} :: {finding.Detail} :: {finding.Body}";

    private static void Write(string path, IEnumerable<string> lines) =>
        File.WriteAllLines(path, lines);
}
