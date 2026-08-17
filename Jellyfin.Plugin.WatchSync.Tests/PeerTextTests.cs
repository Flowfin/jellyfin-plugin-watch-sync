using System;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Peer;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// A string from another machine, bounded and stripped before anything shows it, which is #63.
///
/// The second condition of that issue is the fact this file leads with. The first and third
/// are about the status page rendering peer values, and there is no status page: that is #62,
/// and nothing in the tree shows anything to an operator. So what is driven here is the rule
/// both surfaces share, and the page's own facts are owed on the day it exists.
///
/// Every character these facts are about is written as an escape and never as itself. A file
/// carrying the literal would be refused by this repository's own unicode guard, which is the
/// same reasoning the guard exists for: a bidirectional override in a source file makes the
/// file render differently from how it runs, and a test file is not exempt from that because
/// of what it is testing.
/// </summary>
public class PeerTextTests
{
    /// <summary>
    /// A log line built out of a peer value carries no newline and no control character.
    ///
    /// This is the second condition of #63. A forged line is the cheap attack on a log: a peer
    /// name carrying a newline writes a second line that a reader attributes to this plugin,
    /// and the C1 range and the line and paragraph separators do the same thing while looking
    /// like nothing at all in a diff. The line here is built the way a log line is built, with
    /// the value in the middle of it, because a value that is safe alone and unsafe in a
    /// sentence is not safe.
    /// </summary>
    [Fact]
    public void ALogLineBuiltFromAPeerValueCarriesNoNewlineAndNoControlCharacter()
    {
        var sent = "the peer\r\n2026-01-01 the plugin refused\u0085everything\u2028now\u2029then";

        var line = "peer=" + PeerText.Bounded(sent, PeerText.DefaultLimit) + " reason=unreachable";

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.DoesNotContain('\u2028', line);
        Assert.DoesNotContain('\u2029', line);
        Assert.False(line.Any(char.IsControl));
        Assert.Equal(
            "peer=the peer2026-01-01 the plugin refusedeverythingnowthen reason=unreachable",
            line);
    }

    /// <summary>
    /// An invisible character is removed rather than displayed.
    ///
    /// The bidirectional overrides and isolates are what make a string render in an order it is
    /// not stored in, which is the attack this repository already refuses in its own sources.
    /// The zero-width characters and the byte order mark are the other half: they are not
    /// visible anywhere, so a value carrying them looks equal to one that does not and is not.
    /// </summary>
    [Fact]
    public void AnInvisibleCharacterIsStrippedRatherThanDisplayed()
    {
        var sent = "liv\u202Eing\u2066 \u2069room\u200B\u200D\u2060\uFEFF\u061C\u200E\u200F";

        Assert.Equal("living room", PeerText.Bounded(sent, PeerText.DefaultLimit));
    }

    /// <summary>
    /// The length is bounded before the value reaches anything.
    ///
    /// A peer chooses what it sends and how much of it. Bounding at the point of display rather
    /// than trusting the sender is what stops one peer name from being the whole page, and the
    /// bound is counted after the stripping so that a value padded with invisible characters
    /// does not spend the budget on characters nobody sees.
    /// </summary>
    [Fact]
    public void TheLengthIsBoundedAndTheBoundIsCountedAfterTheStripping()
    {
        var many = new string('a', 5000);

        Assert.Equal(10, PeerText.Bounded(many, 10).Length);
        Assert.Equal(new string('a', 10), PeerText.Bounded(many, 10));

        var padded = string.Concat(Enumerable.Repeat("\u200Bb", 10));

        Assert.Equal("bbbbbbbbbb", PeerText.Bounded(padded, 10));
    }

    /// <summary>
    /// A value cut at the bound is not cut through the middle of a character.
    ///
    /// The runtime stores a character outside the first sixty five thousand as two units, so a
    /// bound counted in units cuts one in half and produces a half character that is not text.
    /// What that costs is not cosmetic: it is refused by a serializer, it is replaced by
    /// whatever reads it next, and the first place it is noticed is a store document that will
    /// not parse.
    /// </summary>
    [Fact]
    public void ACutValueIsNotCutThroughTheMiddleOfACharacter()
    {
        var sent = string.Concat(Enumerable.Repeat("\U0001F3AC", 20));

        var bounded = PeerText.Bounded(sent, 5);

        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001F3AC", 5)), bounded);
        Assert.Equal(5, bounded.EnumerateRunes().Count());
        Assert.All(bounded.EnumerateRunes(), rune => Assert.NotEqual(0xFFFD, rune.Value));
    }

    /// <summary>
    /// Markup is carried through unchanged, because escaping belongs where the value is
    /// rendered.
    ///
    /// This fact exists to stop the helpful repair. Escaping here would mean a value stored
    /// escaped, which every later reader has to guess the state of, and escaped again by the
    /// page that renders it, which shows an operator the escape sequence instead of the name.
    /// #63 fixes that escaping happens where a value is rendered, so what reaches the page is
    /// still the peer's own characters and the page is what makes them inert. The hazard is
    /// real and is stated rather than softened: a caller that writes this output into markup
    /// without escaping it has written the defect this rule does not prevent.
    /// </summary>
    [Fact]
    public void MarkupIsCarriedThroughBecauseEscapingBelongsWhereItIsRendered()
    {
        var sent = "<script>alert('living room')</script>";

        Assert.Equal(sent, PeerText.Bounded(sent, PeerText.DefaultLimit));
    }

    /// <summary>
    /// A value the peer did not send is an empty string and never a null.
    ///
    /// A page or a log line built out of a null is a crash on the path that reports a peer
    /// failing, which is the moment the report is wanted most.
    /// </summary>
    [Fact]
    public void AnAbsentValueIsAnEmptyString()
    {
        Assert.Equal(string.Empty, PeerText.Bounded(null, PeerText.DefaultLimit));
        Assert.Equal(string.Empty, PeerText.Bounded(string.Empty, PeerText.DefaultLimit));
        Assert.Equal(string.Empty, PeerText.Bounded("\u202E\u200B", PeerText.DefaultLimit));
    }

    /// <summary>
    /// A bound below one is a defect in the caller and is refused as one.
    ///
    /// Showing nothing is a decision, and it is taken by not calling this. A bound of zero
    /// answered with an empty string would let a mistake in a caller silently blank every peer
    /// value on a page, which reads as a peer that sent nothing.
    /// </summary>
    [Fact]
    public void ABoundBelowOneIsRefusedAsACallersMistake()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PeerText.Bounded("living room", 0));
    }
}
