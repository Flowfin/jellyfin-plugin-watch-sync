using System;
using System.IO;
using System.Text;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Reading the body of an envelope without reading past what it may carry, which is the second
/// condition of #19.
///
/// The facts here are about what was taken off the stream and not only about the answer that came
/// back. A rule that reads the whole body and then refuses it answers every question about the
/// answer correctly, and it is the rule this condition exists against, so the stream counts what
/// it handed over and the counting is what the assertions are made against.
/// </summary>
public class EnvelopeBodyTests
{
    private static readonly byte[] _empty = Array.Empty<byte>();

    /// <summary>
    /// A body at the bound is read, and the text is the whole of it.
    ///
    /// The boundary is asserted rather than a value well inside it, because a bound written with
    /// the wrong one of two operators answers every other length correctly.
    /// </summary>
    [Fact]
    public void ABodyAtTheBoundIsRead()
    {
        var body = Body.Of(Filled(EnvelopeBounds.MaximumBytes));

        var reading = EnvelopeBody.Read(body, null);

        Assert.Equal(EnvelopeBodyAnswer.Read, reading.Answer);
        Assert.False(reading.IsRefused);
        Assert.Equal(EnvelopeBounds.MaximumBytes, reading.Text!.Length);
        Assert.Equal(EnvelopeBounds.MaximumBytes, reading.BytesRead);
        Assert.Null(reading.Bound);
    }

    /// <summary>
    /// A body one byte past the bound is refused, and what the refusal says about its length is
    /// that the body is at least that long rather than that it is that long.
    /// </summary>
    [Fact]
    public void ABodyOneBytePastTheBoundIsRefused()
    {
        var body = Body.Of(Filled(EnvelopeBounds.MaximumBytes + 1));

        var reading = EnvelopeBody.Read(body, null);

        Assert.Equal(EnvelopeBodyAnswer.TooManyBytes, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Equal(EnvelopeBounds.MaximumBytes, reading.Bound);
        Assert.Equal(EnvelopeBounds.MaximumBytes + 1L, reading.BytesRead);
        Assert.Null(reading.DeclaredBytes);
    }

    /// <summary>
    /// A body the peer declares to be past the bound is refused with nothing read off it at all.
    ///
    /// This is the condition in its strongest form: the refusal happens before the allocation
    /// rather than after a read that discovered the size.
    /// </summary>
    [Fact]
    public void NothingIsReadOffABodyThatDeclaresMoreThanTheBound()
    {
        var body = Body.Of(Filled(EnvelopeBounds.MaximumBytes + 1));

        var reading = EnvelopeBody.Read(body, EnvelopeBounds.MaximumBytes + 1L);

        Assert.Equal(EnvelopeBodyAnswer.TooManyBytes, reading.Answer);
        Assert.Equal(EnvelopeBounds.MaximumBytes, reading.Bound);
        Assert.Equal(EnvelopeBounds.MaximumBytes + 1L, reading.DeclaredBytes);
        Assert.Equal(0, reading.BytesRead);
        Assert.Equal(0, body.Reads);
        Assert.Equal(0, body.BytesHandedOver);
    }

    /// <summary>
    /// A body that never ends is never read more than one byte past the bound.
    ///
    /// The peer that this bound exists against does not declare its length and does not stop, and
    /// what has to hold for it is a ceiling on what this side takes rather than a refusal it
    /// reaches eventually.
    /// </summary>
    [Fact]
    public void ABodyIsNeverReadMoreThanOneBytePastTheBound()
    {
        var body = Body.Endless();

        var reading = EnvelopeBody.Read(body, null);

        Assert.Equal(EnvelopeBodyAnswer.TooManyBytes, reading.Answer);
        Assert.Equal(EnvelopeBounds.MaximumBytes + 1L, body.BytesHandedOver);
    }

    /// <summary>
    /// A declaration inside the bound does not admit a body past it.
    ///
    /// A declaration is what a peer said, and the bytes are what it sent. The number in front of
    /// a body may refuse it and may never excuse it, because a peer whose two disagree is exactly
    /// the peer this bound is for.
    /// </summary>
    [Fact]
    public void ADeclarationInsideTheBoundDoesNotAdmitABodyPastIt()
    {
        var body = Body.Of(Filled(EnvelopeBounds.MaximumBytes + 1));

        var reading = EnvelopeBody.Read(body, 10);

        Assert.Equal(EnvelopeBodyAnswer.TooManyBytes, reading.Answer);
        Assert.Equal(10L, reading.DeclaredBytes);
        Assert.Equal(EnvelopeBounds.MaximumBytes + 1L, reading.BytesRead);
    }

    /// <summary>
    /// Bytes that are not text are refused rather than repaired into text.
    ///
    /// A replacement character substituted for a byte a peer sent is this plugin inventing a
    /// character and then reading its own invention, and what it hides is which side is wrong.
    /// </summary>
    [Fact]
    public void BytesThatAreNotTextAreRefusedRatherThanReplaced()
    {
        var body = Body.Of(new byte[] { 0x7B, 0xC3, 0x28, 0x7D });

        var reading = EnvelopeBody.Read(body, null);

        Assert.Equal(EnvelopeBodyAnswer.NotText, reading.Answer);
        Assert.True(reading.IsRefused);
        Assert.Null(reading.Text);
        Assert.Equal(4, reading.BytesRead);
    }

    /// <summary>
    /// The text is the characters the bytes carry, and the count of bytes is not the count of
    /// characters.
    ///
    /// The bound is on bytes, so a body of characters that take more than one byte each is
    /// shorter as text than as a body, and a rule counting the wrong one of the two answers this
    /// case wrongly in the permissive direction.
    /// </summary>
    [Fact]
    public void TheTextIsTheCharactersTheBytesCarry()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"peer\":\"Über\"}");
        var body = Body.Of(bytes);

        var reading = EnvelopeBody.Read(body, bytes.LongLength);

        Assert.Equal(EnvelopeBodyAnswer.Read, reading.Answer);
        Assert.Equal("{\"version\":1,\"peer\":\"Über\"}", reading.Text);
        Assert.Equal(bytes.LongLength, reading.BytesRead);
        Assert.True(reading.BytesRead > reading.Text!.Length);
    }

    /// <summary>
    /// A body arriving a byte at a time is whole when it is read.
    ///
    /// A stream is allowed to answer with fewer bytes than were asked for, and a reader that took
    /// the first answer for the whole body would pass every fact above, because a fixture handing
    /// its bytes over at once never exercises the second call.
    /// </summary>
    [Fact]
    public void ABodyArrivingAByteAtATimeIsWholeWhenItIsRead()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":1}");
        var body = Body.Of(bytes, mostPerRead: 1);

        var reading = EnvelopeBody.Read(body, null);

        Assert.Equal(EnvelopeBodyAnswer.Read, reading.Answer);
        Assert.Equal("{\"version\":1}", reading.Text);
        Assert.Equal(bytes.Length + 1, body.Reads);
    }

    /// <summary>
    /// A body inside the bound is the text an envelope is then read from.
    ///
    /// The two rules are separate on purpose, and this is the one fact that holds them together,
    /// so that a change to either that made the pair unusable is refused by something.
    /// </summary>
    [Fact]
    public void ABodyInsideTheBoundIsTheTextAnEnvelopeIsReadFrom()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"changes\":[]}");
        var reading = EnvelopeBody.Read(Body.Of(bytes), bytes.LongLength);

        var envelope = Envelope.Read(reading.Text!, EnvelopeVersions.Supported);

        Assert.Equal(EnvelopeAnswer.Readable, envelope.Answer);
        Assert.Equal(1, envelope.Envelope!.Version);
    }

    /// <summary>
    /// An empty body is read as empty text rather than refused.
    ///
    /// What an empty body is is the parser's answer and not this bound's, and a rule refusing it
    /// here would answer for a rule that already answers.
    /// </summary>
    [Fact]
    public void AnEmptyBodyIsReadAsEmptyText()
    {
        var reading = EnvelopeBody.Read(Body.Of(_empty), 0);

        Assert.Equal(EnvelopeBodyAnswer.Read, reading.Answer);
        Assert.Equal(string.Empty, reading.Text);
        Assert.Equal(0, reading.BytesRead);
    }

    /// <summary>
    /// A length below zero is a caller that computed it wrongly rather than a peer that sent
    /// something.
    /// </summary>
    [Fact]
    public void ADeclarationBelowZeroIsACallersMistake()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EnvelopeBody.Read(Body.Of(_empty), -1));
    }

    /// <summary>
    /// No body at all is a caller's mistake as well, and it is a different one.
    /// </summary>
    [Fact]
    public void NoBodyIsACallersMistake()
    {
        Assert.Throws<ArgumentNullException>(() => EnvelopeBody.Read(null!, null));
    }

    private static byte[] Filled(int length)
    {
        var bytes = new byte[length];

        Array.Fill(bytes, (byte)'a');

        return bytes;
    }

    /// <summary>
    /// A body the test hands over, which counts what was taken off it.
    ///
    /// It answers with at most <c>mostPerRead</c> bytes per call, because a stream that always
    /// answers in full hides a reader that stops at its first answer.
    /// </summary>
    private sealed class Body : Stream
    {
        private readonly byte[]? _bytes;
        private readonly int _mostPerRead;
        private int _taken;

        private Body(byte[]? bytes, int mostPerRead)
        {
            _bytes = bytes;
            _mostPerRead = mostPerRead;
        }

        public int Reads { get; private set; }

        public long BytesHandedOver { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public static Body Of(byte[] bytes, int mostPerRead = int.MaxValue) =>
            new Body(bytes, mostPerRead);

        public static Body Endless(int mostPerRead = int.MaxValue) =>
            new Body(null, mostPerRead);

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            Reads++;

            var wanted = Math.Min(count, _mostPerRead);

            if (_bytes is not null)
            {
                wanted = Math.Min(wanted, _bytes.Length - _taken);
            }

            if (wanted <= 0)
            {
                return 0;
            }

            for (var i = 0; i < wanted; i++)
            {
                buffer[offset + i] = _bytes is null ? (byte)'a' : _bytes[_taken + i];
            }

            _taken += wanted;
            BytesHandedOver += wanted;

            return wanted;
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
