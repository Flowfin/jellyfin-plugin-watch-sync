using System;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// The short suppression window around this plugin's own write, which is the second of the two
/// mechanisms #16 takes and the one that was missing.
///
/// The first mechanism is the agreed record in #14: an echo is a value already equal to what the
/// two sides agreed, so it leaves nothing outstanding and never becomes a change. That covers
/// every echo the server hands back unchanged. What it does not cover is the write the server
/// normalised on the way in, where the value this server now holds is this plugin's own write and
/// is not the value that was sent, so the comparison against the agreement finds a difference and
/// the difference is real. Without this rule that difference leaves as a local change, the peer
/// applies it, its own server normalises something, and one watched episode becomes an endless
/// exchange.
///
/// <para>
/// The second mechanism exists only to cover that gap in the first, and the order the questions
/// are asked in is what keeps it there. Whether anything is outstanding is asked first, and where
/// nothing is the window is not consulted at all, which <see cref="TheWindowWasAsked"/> carries
/// out. A window used as the primary mechanism would hide a defect in the agreed record rather
/// than compensating for one: an echo suppressed here on a value the server did not normalise
/// means the record was not updated correctly, and nothing would say so.
/// </para>
///
/// <para>
/// It is a rule over one field of one subject rather than over a stream, which is
/// <see cref="PositionThreshold"/>'s shape and for the same reason: the handler in #15 reads the
/// event, asks this, and holds nothing between calls. What stands in for the stream is the moment
/// this plugin last wrote this field itself, which arrives here as a parameter.
/// </para>
///
/// <para>
/// Nothing here reads a clock. Both moments are parameters, which is the
/// <c>waiting-is-on-the-injected-clock</c> invariant and the headless rule the suite is held to.
/// A window driven by a real wait would be a rule that cannot be tested, and this one is a
/// comparison of two moments the caller supplies.
/// </para>
///
/// <para>
/// What it cannot see is stated here rather than left to be found. It is told that this plugin
/// wrote the field and when, and never what was written, so a person who changes the same field
/// of the same item inside the window has their change read as the server's normalisation and it
/// does not leave. Nothing is lost on this server, the person's value stands, and what converges
/// it is the full reconciliation in #52 rather than anything here. That residual is the price of
/// the window and it is why the window is short and bounded rather than generous.
/// </para>
///
/// <para>
/// <c>docs/sync-model.md</c> carries the rule and the two numbers under
/// <c>## The suppression window</c>, and <c>docs/configuration.md</c> carries the row that says
/// where each of them lives.
/// </para>
/// </summary>
public sealed class EchoWindow
{
    private EchoWindow(EchoWindowAnswer answer, bool theWindowWasAsked)
    {
        Answer = answer;
        TheWindowWasAsked = theWindowWasAsked;
    }

    /// <summary>
    /// Gets how long after this plugin's own write a difference is read as the server's
    /// normalisation of it, where an operator has chosen nothing.
    ///
    /// Thirty seconds. What it has to cover is one process finishing a write and the server
    /// raising the event that write caused, which is the same machine doing one thing and is
    /// measured in milliseconds. The number is an order of magnitude above that rather than a
    /// comfortable fit, because what it buys at the far end is the difference between a window
    /// that occasionally misses an echo under load and one that swallows a person's own action.
    ///
    /// What it costs is the residual on the rule above: a change the person makes to the same
    /// field of the same item inside those thirty seconds does not leave this server until the
    /// reconciliation in #52 runs. Somebody who un-marks an episode a second after a sync applied
    /// it is the case, and the reason the cost is acceptable rather than merely small is that the
    /// value stands here and the failure at the other end is a household's evening handed back
    /// and forth between two servers forever.
    /// </summary>
    public static TimeSpan DefaultWindow => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the widest window this rule accepts.
    ///
    /// Five minutes, and it is a bound on the rule rather than advice to whoever sets the
    /// setting. Past it the window stops covering a server normalising a value and starts
    /// covering a person acting: somebody who un-marks a work a few minutes after a sync applied
    /// it is making the deliberate change #34 exists to carry, and a window that read it as an
    /// echo would make the second mechanism the thing that decides what syncs, which is what the
    /// order of the questions here is written against.
    /// </summary>
    public static TimeSpan MaximumWindow => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets what the rule answered.
    /// </summary>
    public EchoWindowAnswer Answer { get; }

    /// <summary>
    /// Gets a value indicating whether the window was consulted at all.
    ///
    /// It is false where nothing was outstanding, which is the agreed record having answered on
    /// its own. The fact is carried out rather than inferred from the answer so that the property
    /// this rule is bounded by is one a caller and a fact can both read: the window suppresses
    /// nothing where the stored value and the agreed value are equal, so it cannot quietly become
    /// the thing that makes the sync work while a defect in the record goes unreported.
    /// </summary>
    public bool TheWindowWasAsked { get; }

    /// <summary>
    /// Gets a value indicating whether this answer leaves the server as a change.
    /// </summary>
    public bool CarriesOutbound => Answer == EchoWindowAnswer.TheChangeIsLocal;

    /// <summary>
    /// Gets a value indicating whether the caller agrees what this server holds instead of
    /// sending it.
    ///
    /// This is the second condition of #16: the difference the server's own normalisation
    /// produced is agreed rather than sent back, so the next reading finds nothing outstanding
    /// and the exchange ends at one write on each side.
    /// </summary>
    public bool AgreesWhatIsStored =>
        Answer == EchoWindowAnswer.TheServerNormalisedThisPluginsOwnWrite;

    /// <summary>
    /// Judges one field of one mapped user and one leaf item against the window.
    /// </summary>
    /// <param name="theValueIsOutstanding">
    /// Whether this server's own value for the field differs from what the two sides last agreed.
    /// It is <c>OutstandingChanges.Since</c> answered for one field and handed here rather than
    /// re-derived, because the record it reads is the caller's and the first mechanism is not
    /// this rule.
    /// </param>
    /// <param name="thisPluginWroteAt">
    /// When this plugin last wrote this field of this subject itself, or null where it never has.
    /// A subject this plugin has never written can produce no echo, and the null is that state
    /// rather than a moment long ago.
    /// </param>
    /// <param name="observedAt">When this server saw the reading the question is about.</param>
    /// <param name="window">How long after a write of this plugin's the difference is its own.</param>
    /// <returns>What the field is.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A window below zero or above <see cref="MaximumWindow"/>. Or a reading observed before the
    /// write it would be an echo of, which is a caller handing two moments from two clocks rather
    /// than a window that is too narrow: both come from the one injected clock in one process, so
    /// the order between them is not a thing this rule may quietly accept.
    /// </exception>
    public static EchoWindow Judge(
        bool theValueIsOutstanding,
        DateTimeOffset? thisPluginWroteAt,
        DateTimeOffset observedAt,
        TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(window, TimeSpan.Zero, nameof(window));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(window, MaximumWindow, nameof(window));

        if (thisPluginWroteAt is DateTimeOffset wrote && observedAt < wrote)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                observedAt,
                "The reading was observed before the write it would be an echo of. Both moments come from the one injected clock in one process, so this is a caller mixing two sources rather than a window that is too narrow.");
        }

        if (!theValueIsOutstanding)
        {
            return new EchoWindow(EchoWindowAnswer.NothingIsOutstanding, false);
        }

        if (thisPluginWroteAt is DateTimeOffset written && observedAt - written <= window)
        {
            return new EchoWindow(
                EchoWindowAnswer.TheServerNormalisedThisPluginsOwnWrite,
                true);
        }

        return new EchoWindow(EchoWindowAnswer.TheChangeIsLocal, true);
    }
}
