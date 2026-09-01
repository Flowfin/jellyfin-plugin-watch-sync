using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Model;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What a peer can make this side hold across exchanges, which is #313.
///
/// The wire is bounded already: <see cref="EnvelopeBounds"/> refuses an envelope carrying more
/// than its own counts and lengths. What these facts are about is the other end, where a peer
/// offering items this side has never agreed accumulates one entry per item, one exchange at a
/// time, with every exchange inside every bound the wire carries.
///
/// Nothing here reads a clock. Every moment is a parameter, which is the headless rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class AgreedRecordsBoundTests
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The second condition of #313. An agreement past the bound is refused, and the refusal says
    /// what the record holds rather than only that something went wrong.
    ///
    /// The last two assertions are the ones that matter. A record answering this by dropping its
    /// oldest entry would satisfy every other line here, and the item it dropped would be one two
    /// servers had settled, which is a first exchange the next time anybody looks at it.
    /// </summary>
    [Fact]
    public void AnAgreementPastTheBoundIsRefusedRatherThanDroppingAnOlderOne()
    {
        var full = AtTheBound();

        var admission = full.Agreeing(Agreement(Item(AgreedRecords.MaximumEntries + 1)));

        Assert.True(admission.IsRefused);
        Assert.Equal(AgreementAdmissionAnswer.AtTheBound, admission.Answer);
        Assert.Null(admission.Records);
        Assert.Equal(AgreedRecords.MaximumEntries, admission.Held);

        Assert.Equal(AgreedRecords.MaximumEntries, full.Count);
        Assert.NotNull(full.For(Item(1)));
        Assert.Null(full.For(Item(AgreedRecords.MaximumEntries + 1)));
    }

    /// <summary>
    /// A record at the bound has stopped taking new items and has not frozen.
    ///
    /// This is the half a count-only bound gets wrong, and it is the expensive half. An item this
    /// record already holds is agreed again every time somebody watches it, and a rule refusing
    /// that at the bound would leave the last position of every item in a full record stuck at
    /// whatever it was on the day the bound was reached, with nothing saying so.
    /// </summary>
    [Fact]
    public void ARecordAtTheBoundGoesOnAgreeingItemsItAlreadyHolds()
    {
        var full = AtTheBound();

        var admission = full.Agreeing(new AgreedRecord(
            Subject(Item(1)),
            new SyncedState(true, 3, 99 * TimeSpan.TicksPerSecond, _evening.UtcDateTime),
            _evening.AddHours(2),
            1));

        Assert.False(admission.IsRefused);
        Assert.Equal(AgreedRecords.MaximumEntries, admission.Records!.Count);
        Assert.Equal(3, admission.Records.For(Item(1))!.Agreed.PlayCount);
    }

    /// <summary>
    /// The fourth condition of #313: what one pairing and one mapped user can be made to hold.
    ///
    /// The peer is driven the way a peer would drive it, one exchange after another, each of them
    /// carrying <see cref="EnvelopeBounds.MaximumChanges"/> items this side has never agreed,
    /// which is the largest envelope the wire admits. Sixty four of those is what
    /// <see cref="EnvelopeBounds.MaximumEnvelopesInAWindow"/> lets one peer send inside one
    /// <see cref="EnvelopeBounds.Window"/>, so this is one peer's ten minutes at full rate, and it
    /// offers more than three times the bound.
    ///
    /// <para>
    /// The record is filled to the bound through the reader rather than through twenty thousand
    /// admissions, because a record is immutable and every admission copies what it was handed, so
    /// filling it one agreement at a time costs the square of the bound and measures nothing this
    /// fact is about. The exchanges below are driven against the record for real.
    /// </para>
    ///
    /// <para>
    /// The byte count is asserted beside the entry count, because the entry count is the number
    /// somebody chose and the bytes are what it costs. A full document measured 4280098
    /// characters when this was written, and the ceiling is eight mebibytes, which is a little
    /// under twice that: what the range refuses is an entry doubling in size without anybody
    /// weighing the bound again. It is not a claim that the ceiling is where a document stops
    /// being writable, and nothing here measures that.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatOnePairingAndOneMappedUserCanBeMadeToHoldAcrossManyExchanges()
    {
        var records = AtTheBound();
        var offered = AgreedRecords.MaximumEntries;

        for (var exchange = 1; exchange <= EnvelopeBounds.MaximumEnvelopesInAWindow; exchange++)
        {
            for (var change = 1; change <= EnvelopeBounds.MaximumChanges; change++)
            {
                offered++;

                var admission = records.Agreeing(Agreement(Item(offered)));

                Assert.True(admission.IsRefused);
                Assert.Equal(AgreedRecords.MaximumEntries, admission.Held);
            }

            Assert.Equal(AgreedRecords.MaximumEntries, records.Count);
        }

        Assert.True(
            offered > 3 * AgreedRecords.MaximumEntries,
            "the peer offered "
            + offered.ToString(CultureInfo.InvariantCulture)
            + " items, which is not past the bound");

        var bytes = records.ToDocument().Fields.ToJsonString(new JsonSerializerOptions()).Length;

        Assert.InRange(bytes, 1, 8 * 1024 * 1024);
    }

    /// <summary>
    /// The route that cannot answer refuses rather than losing the agreement.
    ///
    /// <c>With</c> answers with a record and has nowhere to put a refusal, so at the bound it
    /// throws. The two things it could do instead are the two the bound exists against: returning
    /// what it was handed loses the agreement in silence, and making room unagrees an item two
    /// servers had settled.
    /// </summary>
    [Fact]
    public void TheRouteThatCannotAnswerRefusesRatherThanLosingTheAgreement()
    {
        var full = AtTheBound();

        var refusal = Assert.Throws<InvalidOperationException>(
            () => full.With(Agreement(Item(AgreedRecords.MaximumEntries + 1))));

        Assert.Contains(
            AgreedRecords.MaximumEntries.ToString(CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A document already holding more than the bound is read as it stands.
    ///
    /// Refusing it would unagree every item in it at once, which is the outcome the bound exists
    /// against arrived at through the bound, and this is the record a full reconciliation has to
    /// rebuild from the peer. What such a record does instead is take nothing new until it holds
    /// fewer than the bound again, which is the second half of this fact.
    /// </summary>
    [Fact]
    public void ADocumentHoldingMoreThanTheBoundIsReadAsItStandsAndTakesNothingNew()
    {
        var reading = AgreedRecords.Read(Document(AgreedRecords.MaximumEntries + 5));

        Assert.False(reading.IsRefused);
        Assert.Equal(AgreedRecords.MaximumEntries + 5, reading.Records!.Count);

        var admission = reading.Records.Agreeing(Agreement(Item(AgreedRecords.MaximumEntries + 6)));

        Assert.True(admission.IsRefused);
        Assert.Equal(AgreedRecords.MaximumEntries + 5, admission.Held);
    }

    /// <summary>
    /// A record holding exactly <see cref="AgreedRecords.MaximumEntries"/> items, built through
    /// the reader so the cost is the size of the record rather than its square.
    /// </summary>
    /// <returns>The record.</returns>
    private static AgreedRecords AtTheBound()
    {
        var reading = AgreedRecords.Read(Document(AgreedRecords.MaximumEntries));

        Assert.False(reading.IsRefused);
        Assert.Equal(AgreedRecords.MaximumEntries, reading.Records!.Count);

        return reading.Records;
    }

    /// <summary>
    /// A document of this record's own making, holding one entry per item: one agreement written
    /// through the record's own writer, copied under further items.
    ///
    /// The entry is copied rather than composed here on purpose. A shape written in this file
    /// would be this file's idea of what an entry is, and a member renamed in the record would
    /// leave these facts passing against a document the record no longer writes.
    /// </summary>
    /// <param name="items">How many items the document holds.</param>
    /// <returns>The document.</returns>
    private static StoredDocument Document(int items)
    {
        var document = AgreedRecords.NoneYet(_pairing, _user)
            .With(Agreement(Item(1)))
            .ToDocument();

        var entries = (JsonObject)document.Fields["items"]!;
        var written = entries[Name(Item(1))]!;

        for (var item = 2; item <= items; item++)
        {
            entries[Name(Item(item))] = written.DeepClone();
        }

        return document;
    }

    private static string Name(Guid item) => item.ToString("n", CultureInfo.InvariantCulture);

    private static Guid Item(int number) => new Guid(number, 0, 0, new byte[8]);

    private static TransferSubject Subject(Guid item) =>
        TransferSubject.From(_user, item, BaseItemKind.Movie).Value!;

    private static AgreedRecord Agreement(Guid item) =>
        new AgreedRecord(
            Subject(item),
            new SyncedState(true, 1, 0, _evening.UtcDateTime),
            _evening,
            1);
}
