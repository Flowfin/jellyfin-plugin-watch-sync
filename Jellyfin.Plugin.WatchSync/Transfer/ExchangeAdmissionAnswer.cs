namespace Jellyfin.Plugin.WatchSync.Transfer;

/// <summary>
/// What asking to start an exchange for one pairing and one mapped user was answered with.
/// </summary>
public enum ExchangeAdmissionAnswer
{
    /// <summary>
    /// Nothing else is running for that pairing and that mapped user, so this start holds the
    /// place until it is released.
    /// </summary>
    Admitted,

    /// <summary>
    /// An exchange is already running for that pairing and that mapped user.
    ///
    /// It is a refusal and never a wait. A held start is a start whose conditions were read at
    /// one time and acted on at another, and the pairing state is exactly the thing that can
    /// have changed in between, which is #45. A refusal costs one interval and nothing else,
    /// because the exchange in progress reaches the state the refused one would have.
    /// </summary>
    AlreadyRunning,
}
