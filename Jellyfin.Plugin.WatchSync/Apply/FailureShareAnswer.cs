namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// What the rule over the failures in one walk answered.
///
/// Three values, and the first two are separate on purpose. A walk that has attempted too few
/// items for a proportion to mean anything and a walk that has been judged and found ordinary
/// are different states, and collapsing them into one answer would let a caller report a run as
/// having passed this rule when the rule declined to decide. It is the same distinction this
/// repository draws everywhere else between not evaluated and evaluated and negative.
/// </summary>
public enum FailureShareAnswer
{
    /// <summary>
    /// Too few items have been attempted for a share to be read as anything, so the rule
    /// declines. A walk carries on and is asked again after the next item.
    /// </summary>
    TooFewToJudge,

    /// <summary>
    /// The failures are within the share, so the walk carries on. Some items failing is the
    /// ordinary outcome of an exchange and never on its own a reason to stop.
    /// </summary>
    Within,

    /// <summary>
    /// The failures are above the share, so the walk stops. What is being reported is that this
    /// side, this person's record or the mapping is wrong, rather than that some items are.
    /// </summary>
    Systematic,
}
