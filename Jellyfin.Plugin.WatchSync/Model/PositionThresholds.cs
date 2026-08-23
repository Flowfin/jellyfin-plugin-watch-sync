using System;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// The three numbers the position rule is bounded by, in one place.
///
/// They are one type rather than three parameters because they are not independent of each
/// other. A finish distance at or above the shortest item length makes every position on the
/// shortest item this plugin will carry one a finish, which is a rule that silently stops being
/// two rules, and a caller passing three loose numbers has nowhere for that refusal to live.
///
/// Each of them is a setting an operator changes, and none of them is a setting yet.
/// <c>PluginConfiguration</c> carries none and says so in its own body: which settings exist
/// and where each one is stored is #58, and a setting invented before that is one an operator
/// can change with no effect. What is here is the value each setting defaults to, the bound
/// this rule refuses outside, and the reason for both, which is what #17 asks
/// <c>docs/sync-model.md</c> to carry and what <c>PositionThresholdDocumentTests</c> holds that
/// document and this type to.
/// </summary>
public sealed class PositionThresholds
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PositionThresholds"/> class.
    /// </summary>
    /// <param name="move">
    /// How far a position has to move while something is still playing before the move is a
    /// change worth carrying.
    /// </param>
    /// <param name="finish">
    /// How close to the end of an item a position has to be before it is a finish rather than a
    /// place to resume from.
    /// </param>
    /// <param name="shortestItem">
    /// The length below which no position is carried for an item at all.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any of the three below zero or above its own maximum, or a finish distance at or above
    /// the shortest item length, which would make every position on the shortest item this
    /// plugin carries one a finish.
    /// </exception>
    public PositionThresholds(TimeSpan move, TimeSpan finish, TimeSpan shortestItem)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(move, TimeSpan.Zero, nameof(move));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(move, MaximumMove, nameof(move));
        ArgumentOutOfRangeException.ThrowIfLessThan(finish, TimeSpan.Zero, nameof(finish));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(finish, MaximumFinish, nameof(finish));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            shortestItem,
            TimeSpan.Zero,
            nameof(shortestItem));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            shortestItem,
            MaximumShortestItem,
            nameof(shortestItem));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            finish,
            shortestItem,
            nameof(finish));

        Move = move;
        Finish = finish;
        ShortestItem = shortestItem;
    }

    /// <summary>
    /// Gets how far a position moves before the move is a change, where an operator has chosen
    /// nothing.
    ///
    /// Five minutes. What it buys is the whole of this rule's reason for existing: the server
    /// saves a progress report every few seconds, so a two hour film produces several hundred
    /// of them, and at five minutes it produces at most twenty four changes however many
    /// reports the client sent. The count follows the length of the work rather than the
    /// chattiness of the player, which is the property #17 asks for.
    ///
    /// What it costs is the residual and it is stated rather than left to be discovered. A
    /// playback that ends without the server saving a stop, which is a client killed or a
    /// server restarted mid-film, loses up to five minutes of progress on the peer's side. The
    /// person resumes at most five minutes early. That is the same order as the cost of the
    /// version rule under <c>## One work held in several versions</c>, and it is recoverable by
    /// the person in seconds, where the failure at the other end of the trade is a peer sent a
    /// position every few seconds for as long as anybody in the household is watching
    /// anything.
    /// </summary>
    public static TimeSpan DefaultMove => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets how close to the end a position has to be to be a finish, where an operator has
    /// chosen nothing.
    ///
    /// Two minutes. It is the length of what sits after the last thing anybody watches: credits
    /// on an episode, a distributor card, the black at the end of a container. Somebody who
    /// reached that point watched the work, and carrying the number instead of the fact makes
    /// the peer offer to resume them into the credits.
    ///
    /// It is short rather than generous on purpose, and the two mistakes it sits between do not
    /// cost the same. A finish read as a position costs the person one click. A position read
    /// as a finish marks something watched that they had not finished, which is a claim about
    /// them that they did not make and that the ratchet in #31 then holds against the other
    /// server correcting it.
    /// </summary>
    public static TimeSpan DefaultFinish => TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets the length below which no position is carried, where an operator has chosen
    /// nothing.
    ///
    /// Five minutes. Below it a resume point is not a thing anybody uses: a trailer, a music
    /// video, an extra, a clip. What a position on one of those does instead is fill the record
    /// of what two sides last agreed with rows nobody will ever read, on the part of a library
    /// that has the most items in it.
    ///
    /// The work still syncs. This number decides the position and nothing else, so a short item
    /// somebody watched arrives at the peer as watched.
    /// </summary>
    public static TimeSpan DefaultShortestItem => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the widest move threshold this rule accepts.
    ///
    /// Half an hour, and it is a bound on the rule rather than advice to whoever sets the
    /// setting. Above it the threshold stops being coarse and starts being a different rule:
    /// most episodes are shorter than an hour, so a threshold of half an hour means the only
    /// position such an item ever produces is the one it stopped at, while the setting still
    /// reads as a threshold.
    /// </summary>
    public static TimeSpan MaximumMove => TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets the widest finish distance this rule accepts.
    ///
    /// A quarter of an hour. Beyond it the distance covers a real part of the work rather than
    /// what sits after it, and a person who stopped a quarter of an hour from the end of a film
    /// has not finished it. The refusal is here rather than only in the document, because a
    /// number a document declares and no code refuses is one a later caller passes straight
    /// through.
    /// </summary>
    public static TimeSpan MaximumFinish => TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets the longest shortest-item length this rule accepts.
    ///
    /// An hour. Above it the ordinary television episode is on the wrong side of the line, and
    /// a setting meant to keep clips out of the record would be switching resume off for most
    /// of a library instead.
    /// </summary>
    public static TimeSpan MaximumShortestItem => TimeSpan.FromHours(1);

    /// <summary>
    /// Gets the three defaults together, which is what a caller with no operator to ask uses.
    /// </summary>
    public static PositionThresholds Default =>
        new(DefaultMove, DefaultFinish, DefaultShortestItem);

    /// <summary>
    /// Gets how far a position moves while something is still playing before the move is a
    /// change.
    /// </summary>
    public TimeSpan Move { get; }

    /// <summary>
    /// Gets how close to the end of an item a position has to be to be a finish.
    /// </summary>
    public TimeSpan Finish { get; }

    /// <summary>
    /// Gets the length below which no position is carried for an item at all.
    /// </summary>
    public TimeSpan ShortestItem { get; }
}
