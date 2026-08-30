using System;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What keeps two exchanges for one pairing and one mapped user from running at once.
///
/// The exclusion is over the pair and not over the pairing alone, which is what
/// <c>docs/transfer.md</c> fixes and is the half that is easy to get wrong in the expensive
/// direction. Two mapped users of one pairing share no agreed record and no watermark, so
/// nothing they write can collide, and an exclusion taken over the pairing would refuse honest
/// work on every household with more than one person in it. The pair is also the unit one
/// exchange covers, so it is the unit the exclusion is over for the same reason.
///
/// A start that meets one in progress is refused and never queued. That is the answer to a
/// scheduled sweep starting on top of an event-driven exchange and to an operator pressing the
/// button while one is running. Holding the start instead would read its conditions at one
/// moment and act on them at another, and the pairing state is the thing that can have changed
/// in between, which is #45. A refusal costs one interval, because the exchange in progress
/// reaches the state the refused one would have.
///
/// It holds pairs that are running and nothing else. Nothing is remembered about a pair that
/// finished, so this is not a record of runs: what an exchange did is what the record it writes
/// is for, and a second, quieter history here would be one nobody knows to look at.
/// </summary>
public sealed class OneExchangeAtATime
{
    private readonly ConcurrentDictionary<(Guid PairingId, Guid MappedUserId), byte> _running =
        new ConcurrentDictionary<(Guid, Guid), byte>();

    /// <summary>
    /// Gets how many exchanges are running.
    ///
    /// It is here for the status page in #62 and for a test to read, rather than for a caller to
    /// decide anything with: a count read and then acted on is the check whose answer is stale
    /// by the time it is used, and <see cref="Admit"/> is the one call that decides.
    /// </summary>
    public int Running => _running.Count;

    /// <summary>
    /// Asks to start an exchange for one pairing and one mapped user.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user, as this server names them.</param>
    /// <returns>The answer, and the place it holds where it was admitted.</returns>
    /// <exception cref="ArgumentException">
    /// Either identifier is empty. An exchange covers one pairing and one mapped user, so a
    /// start that names neither would take one place for every caller that forgot to name one,
    /// and the exclusion would then refuse two exchanges that have nothing to do with each
    /// other.
    /// </exception>
    public ExchangeAdmission Admit(Guid pairingId, Guid mappedUserId)
    {
        RefuseAnEmptyIdentifier(pairingId, nameof(pairingId));
        RefuseAnEmptyIdentifier(mappedUserId, nameof(mappedUserId));

        var admitted = _running.TryAdd((pairingId, mappedUserId), 0);

        return new ExchangeAdmission(
            admitted ? ExchangeAdmissionAnswer.Admitted : ExchangeAdmissionAnswer.AlreadyRunning,
            admitted ? this : null,
            pairingId,
            mappedUserId);
    }

    /// <summary>
    /// Gives one place back.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="mappedUserId">The mapped user.</param>
    internal void Release(Guid pairingId, Guid mappedUserId) =>
        _running.TryRemove((pairingId, mappedUserId), out _);

    private static void RefuseAnEmptyIdentifier(Guid identifier, string name)
    {
        if (identifier == Guid.Empty)
        {
            throw new ArgumentException(
                "An exchange is over one pairing and one mapped user, and this one is empty.",
                name);
        }
    }
}
