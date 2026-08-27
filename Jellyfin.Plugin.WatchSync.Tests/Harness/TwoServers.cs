using System;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.WatchSync.Tests.Harness;

/// <summary>
/// Two servers in one process, which is #77.
///
/// Almost every property below M2 is about two servers rather than one: an echo, a conflict, a
/// first exchange, a peer that came back different. Standing two sides up by hand in each of
/// those cases is how the cases come to differ from each other in ways nobody meant, and it is
/// how one of them ends up sharing a store or a clock and passing for the wrong reason.
///
/// What it stands up: two sides, each with a store under a directory of its own, a user data
/// record of its own, a library of its own and a clock of its own, and one link between them
/// that can be told to drop, delay, duplicate or reorder what it carries. Nothing else is
/// shared, and the four independence facts in <c>TwoServersTests</c> are the assertion of that
/// rather than a claim in this comment.
///
/// What it deliberately does not stand up: a Jellyfin server, a network and a transport. All
/// three are refused by <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>, which names
/// this harness as what replaces them.
///
/// <para>
/// What it does not stand up yet, and the reason: there is no pairing adapter in this tree, so
/// the two sides are joined by the link directly rather than through the interface #40 will put
/// in front of it, and a revocation is a state of a pairing rather than of a link, so this
/// harness cannot produce one. That is the whole of what #77's opening paragraph names and this
/// type does not carry, and it arrives when #40 does rather than being stood in for here.
/// </para>
/// </summary>
internal sealed class TwoServers : IDisposable
{
    /// <summary>
    /// The moment both clocks begin at.
    ///
    /// A constant rather than a reading, because the machine clock is refused and because a case
    /// about skew needs a moment it can add to and subtract from and quote in its own assertion.
    /// The two sides start together so that a case that is not about skew does not have to say
    /// anything about it, and a case that is sets the skew it wants by advancing one side.
    /// </summary>
    internal static readonly DateTimeOffset Epoch = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private TwoServers(HarnessSide here, HarnessSide there)
    {
        Here = here;
        There = there;
        Link = new Link(here, there);
    }

    /// <summary>
    /// Gets one side.
    /// </summary>
    internal HarnessSide Here { get; }

    /// <summary>
    /// Gets the other side.
    /// </summary>
    internal HarnessSide There { get; }

    /// <summary>
    /// Gets the one route a byte takes between the two.
    /// </summary>
    internal Link Link { get; }

    /// <summary>
    /// Gets a local user, of the kind a mapping names.
    ///
    /// One user object is used on both sides on purpose. A mapping is what says which user on one
    /// server answers to which user on the other, and inventing two identifiers here would make
    /// every case carry a mapping it is not about. Nothing on either side reads anything off this
    /// object except its identifier, which is the invariant #42 refuses the other of.
    /// </summary>
    internal User Someone { get; } = UserDataFixtures.Someone();

    /// <summary>
    /// Stands both sides up, each with a directory of its own.
    /// </summary>
    /// <returns>The harness, to be disposed by the case that asked for it.</returns>
    internal static TwoServers Create() =>
        new TwoServers(HarnessSide.Create("here", Epoch), HarnessSide.Create("there", Epoch));

    /// <summary>
    /// Removes both sides' directories, on the failing path as well as on the passing one.
    /// </summary>
    public void Dispose()
    {
        Here.Dispose();
        There.Dispose();
    }
}
