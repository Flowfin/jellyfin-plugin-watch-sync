using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The version every envelope carries, the envelope from a version this plugin does not speak
/// that is refused rather than read, and the member a version requires that is refused rather
/// than defaulted. That is #18.
///
/// There is no exchange yet. The transfer plane is #47, the queue that fills an envelope is #48
/// and the path that applies what one carries is #54, and none of them is in this tree. So the
/// facts here drive the version and the members beside it rather than any change shape: what #18
/// asks for is the rule that holds for every envelope, and a rule proven against the first
/// change to arrive would be a rule about that change.
/// </summary>
public class EnvelopeVersionTests
{
    /// <summary>
    /// An envelope from a version this plugin does not speak is refused, and the refusal names
    /// every version it does speak.
    ///
    /// This is the first condition of #18. Both halves matter and the second is the one that is
    /// easy to leave out: a refusal saying only that the version is wrong leaves an operator
    /// holding two servers and no way to tell which of them to move.
    /// </summary>
    [Fact]
    public void AnEnvelopeFromAVersionThisPluginDoesNotSpeakIsRefusedAndTheRefusalNamesTheSet()
    {
        var reading = Envelope.Read(
            """{"version":4,"changes":[]}""",
            EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.VersionNotSupported, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Equal(4, reading.FoundVersion);
        Assert.Equal(EnvelopeVersions.Supported, reading.SupportedVersions);
        Assert.NotEmpty(reading.SupportedVersions);
    }

    /// <summary>
    /// The refusal reaches a version below the set as well as one above it.
    ///
    /// An envelope is not a document. A document older than the code reading it is an operator's
    /// own data and is carried forward one version at a time, which is #71; an envelope older
    /// than the set comes from a machine that can be asked again in a shape both sides agree on.
    /// So there is one refusal rather than two answers, and this is the direction a reader who
    /// carried the document rule across would leave out.
    /// </summary>
    [Fact]
    public void AVersionBelowTheSetIsRefusedByTheSameAnswerAsOneAboveIt()
    {
        var below = Envelope.Read("""{"version":1,"changes":[]}""", new[] { 2, 3 });
        var above = Envelope.Read("""{"version":9,"changes":[]}""", new[] { 2, 3 });

        Assert.Equal(EnvelopeAnswer.VersionNotSupported, below.Answer);
        Assert.Equal(EnvelopeAnswer.VersionNotSupported, above.Answer);
        Assert.Equal(1, below.FoundVersion);
        Assert.Equal(9, above.FoundVersion);
    }

    /// <summary>
    /// A member the envelope's version requires and did not carry is a refusal, never a default.
    ///
    /// This is the second condition of #18. The default that would be taken here is the empty
    /// change list, and it is the dangerous one: an exchange that read a truncated message as one
    /// carrying no changes reports that nothing happened and leaves both sides believing they
    /// agree.
    /// </summary>
    [Fact]
    public void AMissingRequiredMemberIsRefusedRatherThanDefaulted()
    {
        var reading = Envelope.Read("""{"version":1}""", EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.MemberMissing, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Equal("changes", reading.MissingMember);
        Assert.Null(reading.Envelope);
    }

    /// <summary>
    /// A required member present and null is the same absence wearing a different spelling.
    ///
    /// A reader that took the member as present because the key was there would be defaulting
    /// one step later than the case above, and it is the shape a serializer produces on its own
    /// when the field it was given was empty.
    /// </summary>
    [Fact]
    public void ARequiredMemberCarryingNullIsTheSameRefusal()
    {
        var reading = Envelope.Read(
            """{"version":1,"changes":null}""",
            EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.MemberMissing, reading.Answer);
        Assert.Equal("changes", reading.MissingMember);
    }

    /// <summary>
    /// A refused envelope carries nothing to read, whichever refusal it was.
    ///
    /// It is a property of the type rather than a rule a caller follows: there is no member on a
    /// refused reading holding the envelope, so a caller that meant to take the changes out of
    /// one has nothing to take them out of.
    /// </summary>
    [Fact]
    public void ARefusedEnvelopeCarriesNothingToRead()
    {
        var wrongVersion = Envelope.Read(
            """{"version":7,"changes":[]}""",
            EnvelopeVersions.Supported);

        var missingMember = Envelope.Read("""{"version":1}""", EnvelopeVersions.Supported);

        var notAnEnvelope = Envelope.Read("{}", EnvelopeVersions.Supported);

        Assert.Null(wrongVersion.Envelope);
        Assert.Null(missingMember.Envelope);
        Assert.Null(notAnEnvelope.Envelope);
    }

    /// <summary>
    /// Bytes that are not one of these envelopes are their own answer rather than the oldest
    /// version this plugin speaks.
    ///
    /// Seven ways in, and each of them is a body that could arrive. Reading any of them as
    /// version one would turn a truncated transport and a foreign body into an exchange, and the
    /// repair differs: a version this plugin does not speak is repaired by upgrading a server,
    /// and these are repaired by finding out what sent them.
    /// </summary>
    /// <param name="body">The bytes that are not an envelope.</param>
    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"version":"1","changes":[]}""")]
    [InlineData("""{"version":0,"changes":[]}""")]
    [InlineData("""{"version":-1,"changes":[]}""")]
    [InlineData("""{"version":1.5,"changes":[]}""")]
    public void BytesThatAreNotAnEnvelopeAreTheirOwnAnswer(string body)
    {
        var reading = Envelope.Read(body, EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.NotAnEnvelope, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Null(reading.FoundVersion);
        Assert.Null(reading.MissingMember);
    }

    /// <summary>
    /// A readable envelope keeps every member that was beside the version, including one this
    /// code has never heard of.
    ///
    /// Nothing here interprets a change. What a change is is the match key in #22 and #23 beside
    /// the fields in the sync model, and reading one is the apply path in #54; an envelope read
    /// into a shape this type declared would drop whatever a later version put in it, which is
    /// the loss the document rule in #69 is written against on the other side of the same plugin.
    /// </summary>
    [Fact]
    public void AReadableEnvelopeKeepsEveryMemberBesideTheVersion()
    {
        var reading = Envelope.Read(
            """{"version":1,"changes":[1,2],"aMemberThisCodeHasNeverHeardOf":true}""",
            EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.Readable, reading.Answer);
        Assert.False(reading.IsRefused);

        var envelope = Assert.IsType<Envelope>(reading.Envelope);

        Assert.Equal(1, envelope.Version);
        Assert.Equal(1, reading.FoundVersion);
        Assert.False(envelope.Members.ContainsKey("version"));
        Assert.True(envelope.Members.ContainsKey("changes"));
        Assert.True(envelope.Members.ContainsKey("aMemberThisCodeHasNeverHeardOf"));
    }

    /// <summary>
    /// Every version this plugin speaks declares what it requires, and the version it sends is
    /// the newest of them.
    ///
    /// This is the closure that makes the declaration a declaration rather than a list. A version
    /// added to the supported set without its required members would be a version an envelope is
    /// held to nothing by, and it would look exactly like a version whose requirements happened
    /// to be empty.
    /// </summary>
    [Fact]
    public void EverySupportedVersionDeclaresWhatItRequires()
    {
        Assert.NotEmpty(EnvelopeVersions.Supported);
        Assert.Contains(EnvelopeVersions.Current, EnvelopeVersions.Supported);
        Assert.Equal(EnvelopeVersions.Supported.Max(), EnvelopeVersions.Current);

        Assert.All(
            EnvelopeVersions.Supported,
            version => Assert.NotEmpty(EnvelopeVersions.Requires(version)));
    }

    /// <summary>
    /// A version nothing declares requirements for is refused at the declaration rather than
    /// read against nothing.
    ///
    /// It is reachable only from a caller whose supported set disagrees with the declaration,
    /// which is a defect one step earlier than anything an envelope can be. The alternative is
    /// the one worth refusing: a version added to a set somewhere, with no requirements behind
    /// it, silently reading every envelope of that version without checking a single member.
    /// </summary>
    [Fact]
    public void AVersionWithNoDeclaredRequirementsIsRefusedRatherThanReadAgainstNothing()
    {
        var undeclared = EnvelopeVersions.Current + 1;

        Assert.Throws<ArgumentOutOfRangeException>(() => EnvelopeVersions.Requires(undeclared));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Envelope.Read(
                $$"""{"version":{{undeclared}},"changes":[]}""",
                new[] { EnvelopeVersions.Current, undeclared }));
    }

    /// <summary>
    /// A caller supporting no version at all is a defect rather than a refusal.
    ///
    /// Everything would be refused and the refusal would name nothing, which reads as a peer that
    /// is speaking the wrong version rather than as a caller that asked for the impossible.
    /// </summary>
    [Fact]
    public void ACallerSupportingNoVersionIsADefectRatherThanARefusal()
    {
        Assert.Throws<ArgumentException>(
            () => Envelope.Read("""{"version":1,"changes":[]}""", Array.Empty<int>()));

        Assert.Throws<ArgumentNullException>(
            () => Envelope.Read(null!, EnvelopeVersions.Supported));

        Assert.Throws<ArgumentNullException>(
            () => Envelope.Read("{}", (IReadOnlyList<int>)null!));
    }
}
