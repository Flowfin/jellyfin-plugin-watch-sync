namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What reading the version off an envelope from a peer answered.
///
/// Five values, four of which are refusals, and each refusal is its own value rather than one
/// code with a detail beside it. That is the same rule <see cref="EnvelopeBoundsAnswer"/> is
/// written under and for the same reason: what an operator does next differs per answer. A peer
/// speaking a version this server does not know is a pair of servers to upgrade, a peer sending
/// an envelope with a member missing is a peer sending something truncated, a peer sending one
/// member twice is a peer whose serializer is wrong, and bytes that are not an envelope at all
/// are a transport to look at rather than a peer.
/// </summary>
public enum EnvelopeAnswer
{
    /// <summary>
    /// The envelope carries a version this plugin speaks and every member that version requires.
    ///
    /// It is not a statement that the envelope is good. What it carries is judged afterwards, by
    /// the bounds in <see cref="EnvelopeBounds"/> where the caller has not judged them already
    /// and by the rules each change then reaches.
    /// </summary>
    Readable,

    /// <summary>
    /// The envelope carries a version this plugin does not speak, so it is refused whole.
    ///
    /// It covers both directions rather than only the newer one, which is where this differs
    /// from <c>DocumentAnswer.FromTheFuture</c>. A document older than the code reading it is an
    /// operator's own data and is carried forward; an envelope older than the set is a peer that
    /// can be asked again in a shape both sides agree on, so there is nothing to carry forward
    /// and no reason to have two answers for one refusal.
    ///
    /// The reading names the whole supported set, because a refusal that says only that the
    /// version is wrong leaves an operator with two servers and no way to tell which of them to
    /// move.
    /// </summary>
    VersionNotSupported,

    /// <summary>
    /// The version is one this plugin speaks and a member that version requires is not there.
    ///
    /// Refused rather than defaulted. The default that would be taken here is the empty change
    /// list, and an exchange that read a truncated message as one carrying no changes is an
    /// exchange that reports nothing happened, advances nothing and leaves both sides believing
    /// they agree.
    /// </summary>
    MemberMissing,

    /// <summary>
    /// The envelope carries one member twice, so what it says is ambiguous and it is refused
    /// whole.
    ///
    /// JSON permits the bytes and decides nothing about them: two members of one name leave a
    /// reader choosing the first, the last, or neither, and every one of those three is a guess
    /// at what the sender meant. A version carried twice is the sharpest form, because the number
    /// deciding which rules the rest is read under is the one in doubt. A duplicate deeper inside
    /// the envelope is the same defect one layer down and is refused for the same reason: what
    /// would otherwise happen is that the reading succeeds and the ambiguity is handed to the
    /// path that applies a change, which is one layer too late for it to be recorded against the
    /// peer that sent it.
    ///
    /// The reading names the member that arrived twice. A peer sending this has a serializer to
    /// repair, and a refusal saying only that something was duplicated leaves whoever operates
    /// that server reading the whole body to find out what.
    ///
    /// It is distinct from <see cref="NotAnEnvelope"/> because the repairs differ. Bytes that are
    /// not an envelope are repaired by finding out what sent them; these are an envelope, from a
    /// peer speaking this protocol, whose serializer emits a member twice.
    /// </summary>
    MemberCarriedTwice,

    /// <summary>
    /// The bytes are not an envelope of this plugin's.
    ///
    /// Not readable as an object, or readable and carrying no version, or carrying one that is
    /// not a whole number above zero. It is distinct from <see cref="VersionNotSupported"/>
    /// because the repairs differ: a version this plugin does not speak is repaired by upgrading
    /// one of the two servers, and bytes that are not an envelope are repaired by finding out
    /// what sent them. Reading them as the oldest version this plugin speaks would turn every
    /// truncated transport and every foreign body into an exchange.
    /// </summary>
    NotAnEnvelope,
}
