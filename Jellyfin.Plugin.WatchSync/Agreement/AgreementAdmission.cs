namespace Jellyfin.Plugin.WatchSync.Agreement;

/// <summary>
/// What offering one agreement to a record produced: the record carrying it, or the reason it
/// carries nothing.
///
/// The refusal carries what the record held when it refused rather than only the answer, because
/// the operator reading it is being told that a bound was reached and the count is half of that
/// sentence. The bound itself is <see cref="AgreedRecords.MaximumEntries"/> and is not copied
/// into this type, so the two cannot come to disagree.
/// </summary>
public sealed class AgreementAdmission
{
    private AgreementAdmission(AgreementAdmissionAnswer answer, AgreedRecords? records, int held)
    {
        Answer = answer;
        Records = records;
        Held = held;
    }

    /// <summary>
    /// Gets what the offer came back with.
    /// </summary>
    public AgreementAdmissionAnswer Answer { get; }

    /// <summary>
    /// Gets the record carrying the agreement, where it was admitted, and null where it was not.
    /// </summary>
    public AgreedRecords? Records { get; }

    /// <summary>
    /// Gets how many items the record held when it answered.
    /// </summary>
    public int Held { get; }

    /// <summary>
    /// Gets a value indicating whether the agreement was refused.
    /// </summary>
    public bool IsRefused => Answer is not AgreementAdmissionAnswer.Agreed;

    /// <summary>
    /// An agreement the record took.
    /// </summary>
    /// <param name="records">The record carrying it.</param>
    /// <returns>The answer.</returns>
    internal static AgreementAdmission Agreed(AgreedRecords records) =>
        new AgreementAdmission(AgreementAdmissionAnswer.Agreed, records, records.Count);

    /// <summary>
    /// An agreement about an item the record does not already hold, offered to a record that is
    /// full.
    /// </summary>
    /// <param name="held">How many items the record holds.</param>
    /// <returns>The answer.</returns>
    internal static AgreementAdmission AtTheBound(int held) =>
        new AgreementAdmission(AgreementAdmissionAnswer.AtTheBound, null, held);
}
