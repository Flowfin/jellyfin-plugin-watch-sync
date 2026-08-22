namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What reading a mapped user and an item as a transfer subject answered: a subject, or the
/// reason there is none.
///
/// The reason is carried rather than dropped, for the same reason a key refusal carries one.
/// An item that is not a transfer subject is an ordinary outcome rather than an error, and a
/// bare null leaves the caller to guess at why or to record nothing.
/// </summary>
public sealed class TransferSubjectReading
{
    private TransferSubjectReading(TransferSubject? subject, TransferSubjectRefusal refusal)
    {
        Value = subject;
        Refusal = refusal;
    }

    /// <summary>
    /// Gets the subject, or null where the pair is not one.
    /// </summary>
    public TransferSubject? Value { get; }

    /// <summary>
    /// Gets the reason the pair is not a transfer subject, or
    /// <see cref="TransferSubjectRefusal.None"/>.
    /// </summary>
    public TransferSubjectRefusal Refusal { get; }

    /// <summary>
    /// Gets a value indicating whether there is a subject to carry.
    /// </summary>
    public bool IsSubject => Refusal == TransferSubjectRefusal.None;

    /// <summary>
    /// A reading that produced a subject.
    /// </summary>
    /// <param name="subject">The subject.</param>
    /// <returns>The reading.</returns>
    internal static TransferSubjectReading Subject(TransferSubject subject) =>
        new TransferSubjectReading(subject, TransferSubjectRefusal.None);

    /// <summary>
    /// A reading that produced no subject, with the reason.
    /// </summary>
    /// <param name="refusal">Why the pair is not a transfer subject.</param>
    /// <returns>The reading.</returns>
    internal static TransferSubjectReading Refused(TransferSubjectRefusal refusal) =>
        new TransferSubjectReading(null, refusal);
}
