using System;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.WatchSync.Peer;

/// <summary>
/// A string that came from another machine, bounded and stripped before anything shows it.
///
/// A paired server is a machine this operator trusts and not one this code trusts. What
/// arrives from it is a peer name, a refusal reason, a user name on the far side, and every
/// one of them reaches a dashboard page an administrator is signed in to and a log file that
/// gets copied into a support thread.
///
/// Two of the four rules in #63 are here, and they are the two that are the same on both
/// surfaces. The length is bounded before the value reaches anything, so a peer cannot make a
/// page or a log line as long as it likes. Every character that is invisible, that is a
/// control, or that separates lines is removed, so a value cannot forge a second log line, and
/// cannot reorder what a reader sees around it.
///
/// The other two rules are not here and are not owed here. Escaping happens where a value is
/// rendered rather than where it is stored, which is the page in #62: a value escaped early is
/// a value escaped twice by the time it is shown, and a value stored escaped is one every
/// later reader has to guess the state of. So markup is carried through this rule unchanged,
/// and that is a decision rather than an oversight.
/// </summary>
public static class PeerText
{
    /// <summary>
    /// How many characters of a peer value are shown, until #58 decides the settings.
    ///
    /// It is long enough for a server name, a user name and a refusal reason to arrive whole,
    /// and short enough that a page showing a hundred of them is a page. A peer that wants
    /// more than this said is a peer saying something the operator did not ask for.
    /// </summary>
    public const int DefaultLimit = 200;

    /// <summary>
    /// Bounds a value from a peer and strips what may never be displayed or logged.
    ///
    /// The refused set is a question about categories rather than a list of code points, and
    /// that is a deliberate difference from `.github/workflows/unicode-guard.yml`, which names
    /// eleven ranges. That guard reads this repository's own source, where a leading byte order
    /// mark is legitimate and a list is what stops it false-positiving. A value a peer chose
    /// has no legitimate use for any format character, so the subject here is the category, it
    /// is wider than the guard's list on purpose, and it does not go stale on the day Unicode
    /// adds one.
    ///
    /// What is removed: every control character, which is where a newline, a carriage return
    /// and the C1 range sit, and each of them can forge a line in a log; every format
    /// character, which is where the bidirectional overrides and isolates, the zero-width
    /// characters and the byte order mark sit; every line and paragraph separator, which are
    /// neither of those two and are line breaks to a browser; and the replacement character,
    /// which is what an unpaired surrogate becomes on the way through and is not something a
    /// peer can have meant.
    ///
    /// The bound is counted in characters as a reader sees them rather than in the units the
    /// runtime stores them as, so a value cut at the limit is never cut through the middle of
    /// one.
    /// </summary>
    /// <param name="value">What the peer sent, which may be null.</param>
    /// <param name="limit">How many characters are kept.</param>
    /// <returns>What may be displayed and logged, which is never null.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A limit below one, which is a defect in the caller. A bound of zero is the decision to
    /// show nothing, and it is taken by not calling this rather than by calling it in a way
    /// that answers an empty string for every value.
    /// </exception>
    public static string Bounded(string? value, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var kept = new StringBuilder();
        var count = 0;

        foreach (var rune in value.EnumerateRunes())
        {
            if (IsRefused(rune))
            {
                continue;
            }

            if (count == limit)
            {
                break;
            }

            kept.Append(rune.ToString());
            count++;
        }

        return kept.ToString();
    }

    /// <summary>
    /// Whether one character may never reach a page or a log.
    /// </summary>
    /// <param name="rune">The character.</param>
    /// <returns>True where it is removed.</returns>
    private static bool IsRefused(System.Text.Rune rune)
    {
        if (rune.Value == 0xFFFD)
        {
            return true;
        }

        return System.Text.Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.LineSeparator => true,
            UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
    }
}
