using System;
using System.IO;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The body of an envelope as a peer hands it over, which counts what was taken off it.
///
/// It is one double rather than two. The cases in <see cref="EnvelopeBodyTests"/> assert on what
/// was handed over rather than only on the answer, because a rule that reads the whole body and
/// then refuses it answers every question about the answer correctly; the fuzz harness judges the
/// same quantity for the same reason, over inputs nobody chose. A second double written beside
/// this one would be two answers to the question "how much did this side take", and the one that
/// drifted would be the one nobody reads.
///
/// <para>It answers with at most <c>mostPerRead</c> bytes per call, because a stream that always
/// answers in full hides a reader that stops at its first answer.</para>
/// </summary>
internal sealed class PeerBody : Stream
{
    private readonly byte[]? _bytes;
    private readonly int _mostPerRead;
    private readonly long _ceiling;
    private int _taken;

    private PeerBody(byte[]? bytes, int mostPerRead, long ceiling)
    {
        _bytes = bytes;
        _mostPerRead = mostPerRead;
        _ceiling = ceiling;
    }

    /// <summary>
    /// Gets how many times the reader asked for bytes.
    /// </summary>
    public int Reads { get; private set; }

    /// <summary>
    /// Gets how many bytes were taken off this body.
    /// </summary>
    public long BytesHandedOver { get; private set; }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// A body of exactly these bytes.
    /// </summary>
    /// <param name="bytes">What the peer sent.</param>
    /// <param name="mostPerRead">The most it answers with per call.</param>
    /// <param name="ceiling">
    /// How much it will hand over before it stops rather than answering another read. Unbounded by
    /// default, because a body of known length ends on its own.
    /// </param>
    /// <returns>The body.</returns>
    public static PeerBody Of(byte[] bytes, int mostPerRead = int.MaxValue, long ceiling = long.MaxValue) =>
        new PeerBody(bytes, mostPerRead, ceiling);

    /// <summary>
    /// A body that never ends, which is the peer this bound exists against.
    /// </summary>
    /// <param name="mostPerRead">The most it answers with per call.</param>
    /// <param name="ceiling">
    /// How much it will hand over before it gives way. A caller that means to prove a reader stops
    /// gives it one, so a reader that does not stop fails rather than running until somebody kills
    /// it; a caller with a person watching the run may leave it unbounded.
    /// </param>
    /// <returns>The body.</returns>
    public static PeerBody Endless(int mostPerRead = int.MaxValue, long ceiling = long.MaxValue) =>
        new PeerBody(null, mostPerRead, ceiling);

    /// <inheritdoc/>
    /// <exception cref="ReadPastTheCeiling">
    /// The reader asked for more than the ceiling this body was given. It throws rather than
    /// answering zero, because a stream that quietly ends is a stream a runaway reader looks
    /// correct against, and the whole subject here is how much a peer can make this side take.
    /// </exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        Reads++;

        if (BytesHandedOver >= _ceiling)
        {
            throw new ReadPastTheCeiling(BytesHandedOver, _ceiling);
        }

        var wanted = Math.Min(count, _mostPerRead);

        if (_bytes is not null)
        {
            wanted = Math.Min(wanted, _bytes.Length - _taken);
        }

        wanted = (int)Math.Min(wanted, _ceiling - BytesHandedOver);

        if (wanted <= 0)
        {
            return 0;
        }

        if (_bytes is null)
        {
            Array.Fill(buffer, (byte)'a', offset, wanted);
        }
        else
        {
            Array.Copy(_bytes, _taken, buffer, offset, wanted);
        }

        _taken += wanted;
        BytesHandedOver += wanted;

        return wanted;
    }

    /// <inheritdoc/>
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// A reader asked this body for more than it was given a ceiling for.
    ///
    /// Its own type rather than a general one, so a harness can tell a reader that read too far
    /// from a reader that threw for a reason of its own. The two are opposite findings and would
    /// be one line in a report that collapsed them.
    /// </summary>
    public sealed class ReadPastTheCeiling : IOException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadPastTheCeiling"/> class.
        /// </summary>
        /// <param name="handedOver">How much had been taken when the reader asked again.</param>
        /// <param name="ceiling">The ceiling this body was given.</param>
        public ReadPastTheCeiling(long handedOver, long ceiling)
            : base($"The reader asked for more after {handedOver} byte(s), past the ceiling of {ceiling}.")
        {
            HandedOver = handedOver;
            Ceiling = ceiling;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadPastTheCeiling"/> class.
        /// </summary>
        public ReadPastTheCeiling()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadPastTheCeiling"/> class.
        /// </summary>
        /// <param name="message">What happened.</param>
        public ReadPastTheCeiling(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadPastTheCeiling"/> class.
        /// </summary>
        /// <param name="message">What happened.</param>
        /// <param name="innerException">What it happened under.</param>
        public ReadPastTheCeiling(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets how much had been taken off the body when the reader asked again.
        /// </summary>
        public long HandedOver { get; }

        /// <summary>
        /// Gets the ceiling this body was given.
        /// </summary>
        public long Ceiling { get; }
    }
}
