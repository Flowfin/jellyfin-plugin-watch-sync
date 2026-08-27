using System;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The suppression window around this plugin's own write, which is the second of the two
/// mechanisms #16 takes.
///
/// The set is written against one property more than the issue asks for, and it is the one the
/// first reading on that issue argues for: the window suppresses nothing where the stored value
/// and the agreed value are already equal. Without it a window that fired on a value the server
/// did not normalise would be hiding a defect in the agreed record, the sync would still work,
/// and nothing would say which of the two mechanisms was carrying it.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c> and the
/// <c>waiting-is-on-the-injected-clock</c> invariant beside it.
/// </summary>
public class EchoWindowTests
{
    private static readonly DateTimeOffset _evening = new(2026, 8, 27, 21, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Where nothing is outstanding the window is not consulted, and the answer says so.
    ///
    /// This is the rule that keeps the second mechanism second. The agreed record makes an echo a
    /// value already equal to what the two sides agreed, and that is what stops the ordinary echo;
    /// a window asked here would be answering a question the record has already answered, and a
    /// window that started suppressing on values the server never normalised would make a broken
    /// record look like a working sync.
    /// </summary>
    [Fact]
    public void NothingOutstandingIsNotTakenToTheWindow()
    {
        var judged = EchoWindow.Judge(
            false,
            _evening,
            _evening.AddSeconds(1),
            EchoWindow.DefaultWindow);

        Assert.Equal(EchoWindowAnswer.NothingIsOutstanding, judged.Answer);
        Assert.False(judged.TheWindowWasAsked);
        Assert.False(judged.CarriesOutbound);
        Assert.False(judged.AgreesWhatIsStored);
    }

    /// <summary>
    /// A difference standing on this plugin's own write inside the window is the server's
    /// normalisation of it, and it is agreed rather than sent back.
    ///
    /// This is the case the whole rule exists for. The value this server holds is what this
    /// plugin wrote as the server stored it, so it is not equal to what was agreed and the first
    /// mechanism finds a real difference. Sending it is the endless exchange; leaving it
    /// outstanding is the same exchange one round slower, because the difference does not go away
    /// on its own.
    /// </summary>
    [Fact]
    public void ADifferenceOnThisPluginsOwnWriteIsAgreedRatherThanSent()
    {
        var judged = EchoWindow.Judge(
            true,
            _evening,
            _evening.AddSeconds(2),
            EchoWindow.DefaultWindow);

        Assert.Equal(EchoWindowAnswer.TheServerNormalisedThisPluginsOwnWrite, judged.Answer);
        Assert.True(judged.TheWindowWasAsked);
        Assert.True(judged.AgreesWhatIsStored);
        Assert.False(judged.CarriesOutbound);
    }

    /// <summary>
    /// A difference past the window is a local change and leaves.
    /// </summary>
    [Fact]
    public void ADifferencePastTheWindowIsLocal()
    {
        var judged = EchoWindow.Judge(
            true,
            _evening,
            _evening.Add(EchoWindow.DefaultWindow).AddSeconds(1),
            EchoWindow.DefaultWindow);

        Assert.Equal(EchoWindowAnswer.TheChangeIsLocal, judged.Answer);
        Assert.True(judged.TheWindowWasAsked);
        Assert.True(judged.CarriesOutbound);
    }

    /// <summary>
    /// A write exactly the window ago is still this plugin's own, because the window is the
    /// widest gap that still counts as one.
    ///
    /// The boundary is drawn in that direction on purpose and a later reader should not tidy it.
    /// The number is chosen as an upper bound on how long a server takes to raise the event its
    /// own write caused, and a rule that excluded the boundary would be a window one tick
    /// narrower than the one the document declares.
    /// </summary>
    [Fact]
    public void AWriteExactlyTheWindowAgoIsStillThisPluginsOwn()
    {
        var judged = EchoWindow.Judge(
            true,
            _evening,
            _evening.Add(EchoWindow.DefaultWindow),
            EchoWindow.DefaultWindow);

        Assert.Equal(EchoWindowAnswer.TheServerNormalisedThisPluginsOwnWrite, judged.Answer);
    }

    /// <summary>
    /// A subject this plugin has never written can produce no echo, so a difference on it is
    /// local however recent it is.
    ///
    /// The absent moment is that state rather than a write long ago, and the difference matters:
    /// a rule that read absence as a write at the beginning of time would answer the same way
    /// here and the opposite way for a window an operator widened.
    /// </summary>
    [Fact]
    public void ADifferenceOnASubjectThisPluginNeverWroteIsLocal()
    {
        var judged = EchoWindow.Judge(true, null, _evening, EchoWindow.DefaultWindow);

        Assert.Equal(EchoWindowAnswer.TheChangeIsLocal, judged.Answer);
        Assert.True(judged.TheWindowWasAsked);
    }

    /// <summary>
    /// A window of nothing leaves the first mechanism carrying the rule alone, which is what an
    /// operator who sets it to zero has asked for.
    ///
    /// It is legal rather than refused because it is the state the plugin is in every time the
    /// agreed record is enough, and a rule that refused it would be refusing the configuration in
    /// which the second mechanism is switched off rather than hiding a defect.
    /// </summary>
    [Fact]
    public void AWindowOfNothingSuppressesNothingAfterTheWrite()
    {
        var judged = EchoWindow.Judge(
            true,
            _evening,
            _evening.AddTicks(1),
            TimeSpan.Zero);

        Assert.Equal(EchoWindowAnswer.TheChangeIsLocal, judged.Answer);
    }

    /// <summary>
    /// A window wider than the maximum is refused, and so is one below zero.
    ///
    /// The bound is on the rule rather than only in the document, because a number a document
    /// declares and no code refuses is one a later caller passes straight through. Past five
    /// minutes the window stops covering a server normalising a value and starts covering a
    /// person acting, which is the deliberate unmark #34 exists to carry.
    /// </summary>
    [Fact]
    public void AWindowOutsideTheBoundsIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EchoWindow.Judge(
            true,
            _evening,
            _evening,
            EchoWindow.MaximumWindow.Add(TimeSpan.FromSeconds(1))));

        Assert.Throws<ArgumentOutOfRangeException>(() => EchoWindow.Judge(
            true,
            _evening,
            _evening,
            TimeSpan.FromSeconds(-1)));
    }

    /// <summary>
    /// A reading observed before the write it would be an echo of is refused rather than read as
    /// outside the window.
    ///
    /// Both moments come from the one injected clock in one process, so an event that precedes
    /// the write is a caller mixing two sources. Reading it as a change that happened here would
    /// be the quiet answer, and what it produces is an echo carried outbound at exactly the
    /// moment the rule that exists to stop it decided nothing was wrong.
    /// </summary>
    [Fact]
    public void AReadingObservedBeforeTheWriteIsRefused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => EchoWindow.Judge(
            true,
            _evening,
            _evening.AddSeconds(-1),
            EchoWindow.DefaultWindow));

        Assert.Equal("observedAt", refused.ParamName);
    }

    /// <summary>
    /// The default sits inside the bound the rule refuses outside, so the value an operator gets
    /// without choosing anything is one the rule accepts.
    ///
    /// A default above its own maximum is the pair of numbers that reads as sensible in two
    /// separate paragraphs and refuses every call in between.
    /// </summary>
    [Fact]
    public void TheDefaultIsInsideTheBound()
    {
        Assert.True(EchoWindow.DefaultWindow > TimeSpan.Zero);
        Assert.True(EchoWindow.DefaultWindow <= EchoWindow.MaximumWindow);
    }
}
