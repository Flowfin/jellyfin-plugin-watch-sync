namespace Jellyfin.Plugin.WatchSync.Exchange;

/// <summary>
/// Which of the two runs an exchange is.
///
/// #37 asks for the first exchange to be a distinct, named mode rather than the ordinary path
/// meeting an empty record, and this is the name. The difference is not in the rules, which are
/// the same table either way, and it is in what the run may assume: an ordinary exchange asks
/// its peer for what moved since a point both sides confirmed, and a first exchange has no such
/// point and is looking at two whole histories that have never met.
///
/// It is derived rather than declared, from the record of what the two sides last agreed, so a
/// run cannot be told it is one kind while the record says the other. <c>docs/conflicts.md</c>
/// fixes what the first run does under <c>## The first exchange is this table and nothing
/// else</c>.
/// </summary>
public enum ExchangeMode
{
    /// <summary>
    /// The two sides have confirmed no point for this pairing and this mapped user, so nothing
    /// they hold has ever been agreed.
    ///
    /// An interrupted first exchange is still this one. It leaves agreements behind for the
    /// items it reached and confirms no point, because the point is what says the whole set was
    /// exchanged, so the run that resumes it reads the same mode out of the same record.
    /// </summary>
    First,

    /// <summary>
    /// The two sides have confirmed a point, so a first exchange has already run to its end and
    /// every later one asks for what moved since.
    /// </summary>
    Ordinary,
}
