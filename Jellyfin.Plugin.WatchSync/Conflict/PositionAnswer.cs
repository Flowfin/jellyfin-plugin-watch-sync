namespace Jellyfin.Plugin.WatchSync.Conflict;

/// <summary>
/// What settling two positions by the later play answered.
///
/// Four values, and only two of them decide anything. The other two are the rule saying that
/// the pair in front of it is not its own: a completion is the ratchet's pair rather than this
/// one's, and a peer whose clock says the play happened after this server's present moment is
/// a machine to repair rather than a reading to compare against.
///
/// The two that decide are separate values although both name a position, because they are
/// reached for different reasons and an operator reads them differently. One is a comparison
/// of two moments that were far enough apart to be compared. The other is the tie rule, taken
/// where there was no comparison to make, and a pair answered by it is one where nothing about
/// the two clocks was trusted.
/// </summary>
public enum PositionAnswer
{
    /// <summary>
    /// The two last played dates are further apart than the tolerated skew, so the later of
    /// them is the play the person was actually doing and its position is the answer.
    ///
    /// The other position is discarded and carried out rather than dropped inside the rule, so
    /// that #36 can record what lost.
    /// </summary>
    LaterPlayStands,

    /// <summary>
    /// There was no comparison to make, so the greater of the two positions is the answer.
    ///
    /// This is the tie rule, and it is reached where the two dates are closer together than the
    /// tolerated skew, and where one side has no date at all. Both are the same situation from
    /// the rule's side: nothing separates the two moments, so the question of which reading is
    /// newer has no answer and is not guessed at.
    ///
    /// The greater position is the answer because it is the one the person is further into, and
    /// it is written down here as a choice rather than left to whichever side the resolver
    /// happens to hold first. A rule that answered by side would answer the same pair
    /// differently in the two directions, and the two servers would then disagree forever about
    /// a pair they both resolved.
    /// </summary>
    TheGreaterPositionStands,

    /// <summary>
    /// The peer's last played date is further ahead of this server's present moment than the
    /// tolerated skew allows, so nothing here is decided.
    ///
    /// It is a refusal of its own rather than one of the answers because a clock failure that
    /// reads as anything else costs an evening: an operator told an item did not match, or that
    /// a peer was unreachable, looks at the library and the network, and the machine that needs
    /// the attention is the one whose clock is wrong. A play cannot have happened after the
    /// present moment, so this is the one case where a reading can be known to be false rather
    /// than merely older.
    /// </summary>
    PeerClockOutsideTolerance,

    /// <summary>
    /// At least one side holds the work played, so this is not the rule for this pair.
    ///
    /// Nothing is discarded and nothing is decided here. A completion held against a position
    /// is the ratchet in #31, which answers it whatever the two dates say, and the two rules
    /// are kept apart so that neither has to carry a branch belonging to the other.
    /// </summary>
    ACompletionIsHeld,
}
