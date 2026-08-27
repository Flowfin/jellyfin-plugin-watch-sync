using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Tests.Harness;

/// <summary>
/// The one thing the two sides of the harness have in common, and the only route a byte takes
/// between them.
///
/// It carries envelope bodies as text. Nothing here reads one: what an envelope is is
/// <see cref="Jellyfin.Plugin.WatchSync.Model.Envelope"/> and what one exchange consists of is
/// <c>docs/transfer.md</c>, and a link that understood either would be a second implementation
/// of them living in the suite. What it is for is the four things that happen to a body on the
/// way, which is what several of the rules below M6 are about and none of them can be driven
/// without.
///
/// Carrying is explicit. A body handed over sits in flight until <see cref="Deliver"/> is called,
/// so a case says when the far side gets to see what it was sent instead of the two ends being
/// coupled through a call. That is what makes a delay observable at all: with delivery on the
/// send there is no round for a body to be held over into.
///
/// There is no transport, no port and no wait. The headless rule refuses all three and names
/// this harness as what replaces them.
/// </summary>
internal sealed class Link
{
    private readonly HarnessSide _here;
    private readonly HarnessSide _there;
    private readonly List<Carried> _inFlight = new List<Carried>();

    /// <summary>
    /// Initializes a new instance of the <see cref="Link"/> class.
    /// </summary>
    /// <param name="here">One side.</param>
    /// <param name="there">The other side.</param>
    internal Link(HarnessSide here, HarnessSide there)
    {
        _here = here;
        _there = there;
    }

    /// <summary>
    /// Gets how many bodies this link was told to drop and therefore never carried.
    ///
    /// A dropped body leaves no trace at either end, which is the point of it, so a case that
    /// wanted to tell "nothing was sent" from "what was sent did not arrive" has nothing to read.
    /// This is that reading, and it is on the link because the link is the only thing that knows.
    /// </summary>
    internal int Dropped { get; private set; }

    /// <summary>
    /// Hands a body to the link, to be carried unharmed on the next delivery.
    /// </summary>
    /// <param name="from">The side sending it.</param>
    /// <param name="body">The envelope body, as text.</param>
    internal void Send(HarnessSide from, string body) => Send(from, body, LinkFault.None);

    /// <summary>
    /// Hands a body to the link and tells it what to do to this one.
    /// </summary>
    /// <param name="from">The side sending it.</param>
    /// <param name="body">The envelope body, as text.</param>
    /// <param name="fault">What happens to this body on the way.</param>
    /// <exception cref="ArgumentNullException">The side or the body is null.</exception>
    /// <exception cref="ArgumentException">A side this link does not join.</exception>
    internal void Send(HarnessSide from, string body, LinkFault fault)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(body);

        if (!ReferenceEquals(from, _here) && !ReferenceEquals(from, _there))
        {
            throw new ArgumentException(
                "This link joins two sides and was handed a third, so there is no far side for the body to be carried to.",
                nameof(from));
        }

        if (fault == LinkFault.Drop)
        {
            Dropped++;
            return;
        }

        _inFlight.Add(new Carried(
            from,
            body,
            fault == LinkFault.Delay ? 1 : 0,
            fault == LinkFault.Duplicate ? 2 : 1,
            fault == LinkFault.Reorder));
    }

    /// <summary>
    /// Carries everything that is due into the far side's inbox.
    ///
    /// What is held over is held over whole: a body waiting out a delay is not partly delivered,
    /// and a body waiting behind a reorder is carried on this delivery rather than the next one,
    /// because arriving late and arriving out of order are the two faults a case has to be able
    /// to tell apart.
    /// </summary>
    internal void Deliver()
    {
        var due = new List<Carried>();
        var held = new List<Carried>();

        foreach (var carried in _inFlight)
        {
            if (carried.RoundsHeld == 0)
            {
                due.Add(carried);
            }
            else
            {
                held.Add(carried.HeldOneRoundLess());
            }
        }

        foreach (var carried in InTheOrderTheyArrive(due))
        {
            for (var copy = 0; copy < carried.Copies; copy++)
            {
                FarSideOf(carried.From).Receive(carried.Body);
            }
        }

        _inFlight.Clear();
        _inFlight.AddRange(held);
    }

    /// <summary>
    /// The order the due bodies are carried in, which is the order they were handed over except
    /// where one was told to yield to the body behind it.
    ///
    /// One swap per yielding body rather than a sort, so the fault is exactly what its name says:
    /// the body arrives after the one sent behind it, and everything else keeps the place it had.
    /// A yielding body at the end of the list has nothing to yield to and keeps its place.
    /// </summary>
    /// <param name="due">The bodies due on this delivery, in the order they were handed over.</param>
    /// <returns>The order they are carried in.</returns>
    private static IReadOnlyList<Carried> InTheOrderTheyArrive(List<Carried> due)
    {
        for (var at = 0; at < due.Count - 1; at++)
        {
            if (!due[at].YieldsToTheNext)
            {
                continue;
            }

            (due[at], due[at + 1]) = (due[at + 1], due[at]);
            at++;
        }

        return due;
    }

    private HarnessSide FarSideOf(HarnessSide from) => ReferenceEquals(from, _here) ? _there : _here;

    private sealed class Carried
    {
        internal Carried(HarnessSide from, string body, int roundsHeld, int copies, bool yieldsToTheNext)
        {
            From = from;
            Body = body;
            RoundsHeld = roundsHeld;
            Copies = copies;
            YieldsToTheNext = yieldsToTheNext;
        }

        internal HarnessSide From { get; }

        internal string Body { get; }

        internal int RoundsHeld { get; }

        internal int Copies { get; }

        internal bool YieldsToTheNext { get; }

        internal Carried HeldOneRoundLess() =>
            new Carried(From, Body, RoundsHeld - 1, Copies, YieldsToTheNext);
    }
}
