using System;

namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// The answer to a start, and the place it holds while it runs.
///
/// It is disposable because a place has to be given back on the path where the exchange threw
/// as well as on the path where it finished. An exchange that released its place on its own
/// last line is correct until it fails on the line before, and what that leaves behind is a
/// pairing and a mapped user that never exchange again until the server is restarted, which is
/// the failure this shape exists against rather than a tidiness.
///
/// A refused start is disposable too and gives nothing back. That is what lets a caller write
/// one using block over both answers instead of a branch that only disposes on one of them,
/// which is the branch somebody eventually writes the wrong way round.
/// </summary>
public sealed class ExchangeAdmission : IDisposable
{
    private readonly OneExchangeAtATime? _from;
    private readonly Guid _pairingId;
    private readonly Guid _mappedUserId;

    private bool _released;

    internal ExchangeAdmission(
        ExchangeAdmissionAnswer answer,
        OneExchangeAtATime? from,
        Guid pairingId,
        Guid mappedUserId)
    {
        Answer = answer;
        _from = from;
        _pairingId = pairingId;
        _mappedUserId = mappedUserId;
    }

    /// <summary>
    /// Gets what the start was answered with.
    /// </summary>
    public ExchangeAdmissionAnswer Answer { get; }

    /// <summary>
    /// Gets a value indicating whether this start holds the place.
    /// </summary>
    public bool IsAdmitted => Answer is ExchangeAdmissionAnswer.Admitted;

    /// <summary>
    /// Gives the place back, once.
    ///
    /// Releasing twice releases once. The second release would otherwise give away a place a
    /// later start is holding, and the two exchanges the exclusion exists to keep apart would
    /// then run together, on the pairing where somebody wrote one using block too many.
    ///
    /// A refused start has nothing to give back, and it has nothing rather than being asked not
    /// to: it was never handed the exclusion it would call. One guard rather than two, because a
    /// second one over the same property is a guard no change can be made to redden.
    /// </summary>
    public void Dispose()
    {
        if (_released || _from is null)
        {
            return;
        }

        _released = true;
        _from.Release(_pairingId, _mappedUserId);
    }
}
