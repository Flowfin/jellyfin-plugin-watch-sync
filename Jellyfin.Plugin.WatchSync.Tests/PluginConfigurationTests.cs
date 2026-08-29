using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Jellyfin.Plugin.WatchSync.Configuration;
using MediaBrowser.Model.IO;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Covers the configuration document the server writes and reads for this plugin.
/// </summary>
public class PluginConfigurationTests
{
    /// <summary>
    /// The names the plugin template shipped as demonstration settings.
    /// </summary>
    private static readonly string[] _templateSettingNames =
    [
        "SomeOptions",
        "TrueFalseSetting",
        "AnInteger",
        "AString",
    ];

    /// <summary>
    /// A configuration nobody has touched survives the server's serializer.
    ///
    /// A class the serializer cannot construct, or one whose only constructor takes arguments,
    /// throws on the first save, and a plugin that cannot save its configuration is one the
    /// dashboard reports a failure for with no setting on the page to explain it.
    /// </summary>
    [Fact]
    public void AnUntouchedConfigurationSurvivesTheServersSerializer()
    {
        var written = new PluginConfiguration();

        using var stream = new MemoryStream();
        SerializeToStream(written, stream);
        stream.Position = 0;
        var read = DeserializeFromStream(typeof(PluginConfiguration), stream);

        Assert.IsType<PluginConfiguration>(read);
    }

    /// <summary>
    /// Every setting an operator chose comes back as the value they chose.
    ///
    /// Each is set to something that is neither its default nor any other setting's, so a member
    /// dropped on the way through, or read back off the wrong element, is a different number
    /// rather than a coincidence. Comparing the type against itself after the round trip is the
    /// whole point: what this refuses is a setting that saves without an error and comes back as
    /// something else, which is the failure the operator cannot see, because the page they are
    /// looking at shows what they typed.
    /// </summary>
    [Fact]
    public void EverySettingSurvivesTheServersSerializer()
    {
        var written = new PluginConfiguration
        {
            PositionMoveSeconds = 91,
            PositionFinishSeconds = 92,
            PositionShortestItemSeconds = 993,
            EchoWindowSeconds = 94,
            ConflictRetentionDays = 15,
            ProvenanceRetentionDays = 96,
        };

        using var stream = new MemoryStream();
        SerializeToStream(written, stream);
        stream.Position = 0;
        var read = Assert.IsType<PluginConfiguration>(
            DeserializeFromStream(typeof(PluginConfiguration), stream));

        Assert.Equal(written.PositionMoveSeconds, read.PositionMoveSeconds);
        Assert.Equal(written.PositionFinishSeconds, read.PositionFinishSeconds);
        Assert.Equal(written.PositionShortestItemSeconds, read.PositionShortestItemSeconds);
        Assert.Equal(written.EchoWindowSeconds, read.EchoWindowSeconds);
        Assert.Equal(written.ConflictRetentionDays, read.ConflictRetentionDays);
        Assert.Equal(written.ProvenanceRetentionDays, read.ProvenanceRetentionDays);
    }

    /// <summary>
    /// Every setting is a whole number of the unit its name ends in, and none of them is a
    /// <see cref="TimeSpan"/>.
    ///
    /// Six of these are durations, so a span is the type somebody reaches for, and the reason it
    /// is refused is the control rather than the serializer. What an operator meets on the page
    /// is a number box; a span reaching it has to be formatted into one and parsed back out of
    /// it, and the two conversions are where a value stops being the one that was typed. With a
    /// count in a named unit, the number on the page, the number in the document and the number
    /// the rule is handed are the same number, and the only conversion left is the one
    /// <c>ServerWideSettings</c> makes in one place.
    ///
    /// THE SERIALIZER IS NOT THE REASON AND THIS SENTENCE ONCE SAID IT WAS. It said a span is
    /// written as an empty element and read back as zero. The fact below was written to execute
    /// that and it failed, because the server's serializer round-trips a span perfectly well. The
    /// claim was made before the run rather than after it, which is the defect this repository
    /// names first, and what stands here now is what the run showed.
    /// </summary>
    [Fact]
    public void EverySettingIsAWholeNumberRatherThanASpan()
    {
        Assert.Empty(typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.PropertyType != typeof(int))
            .Select(property =>
                $"{property.Name} is a {property.PropertyType.Name}, and a setting this page offers in a number box is a count of the unit its name ends in"));
    }

    /// <summary>
    /// A span setting would survive the server's serializer.
    ///
    /// It is kept as a fact rather than deleted with the claim it disproved, because the claim is
    /// the one somebody makes again. Whoever next proposes spans here will be right about the
    /// serializer and still has to answer the reason above, and a run in the tree saying so is
    /// cheaper than the same measurement taken twice.
    /// </summary>
    [Fact]
    public void ASpanSettingWouldSurviveTheServersSerializerToo()
    {
        var written = new SpanSetting { Window = TimeSpan.FromSeconds(30) };

        using var stream = new MemoryStream();
        SerializeToStream(written, stream);
        stream.Position = 0;
        var read = Assert.IsType<SpanSetting>(DeserializeFromStream(typeof(SpanSetting), stream));

        Assert.Equal(TimeSpan.FromSeconds(30), read.Window);
    }

    /// <summary>
    /// Nothing in the build refuses a control bound to a setting that no longer exists. The page
    /// would load, the binding would write undefined into the configuration, and the operator
    /// would see a control that saves and does nothing. This names the four the template shipped
    /// so that removing them from the class and leaving them on the page cannot pass.
    /// </summary>
    [Fact]
    public void TheConfigurationPageBindsNoneOfTheTemplatesSettings()
    {
        var resource = typeof(Plugin).Namespace + ".Configuration.configPage.html";
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(resource);
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var html = reader.ReadToEnd();

        foreach (var name in _templateSettingNames)
        {
            Assert.DoesNotContain(name, html, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The same names, refused on the configuration class itself, so that a later change cannot
    /// bring one back under the same name.
    /// </summary>
    [Fact]
    public void TheConfigurationClassDeclaresNoneOfTheTemplatesSettings()
    {
        var declared = Array.ConvertAll(
            typeof(PluginConfiguration).GetProperties(),
            property => property.Name);

        foreach (var name in _templateSettingNames)
        {
            Assert.DoesNotContain(name, declared);
        }

        Assert.Null(typeof(PluginConfiguration).Assembly.GetType(
            "Jellyfin.Plugin.WatchSync.Configuration.SomeOptions"));
    }

    // The two methods below reproduce the path the server takes, call for call, rather than
    // referencing it. The server's IXmlSerializer is MyXmlSerializer in
    // Emby.Server.Implementations, which is not among the packages a plugin is built against, so
    // it cannot be referenced here. What it does is a thin wrapper over the framework serializer,
    // and this is that wrapper:
    // https://github.com/jellyfin/jellyfin/blob/v10.9.11/Emby.Server.Implementations/Serialization/MyXmlSerializer.cs
    // The bound is that this proves the framework serializer handles the type, not that a copy of
    // the server's file is byte-identical to the original on the day the test runs.
    private static void SerializeToStream(object value, Stream stream)
    {
        using var writer = new StreamWriter(stream, null, IODefaults.StreamWriterBufferSize, true);
        using var textWriter = new XmlTextWriter(writer);
        textWriter.Formatting = Formatting.Indented;
        new XmlSerializer(value.GetType()).Serialize(textWriter, value);
    }

    private static object? DeserializeFromStream(Type type, Stream stream)
    {
        using var reader = XmlReader.Create(stream);
        return new XmlSerializer(type).Deserialize(reader);
    }
    /// <summary>
    /// A configuration written the way somebody writes it the first time, kept here so what the
    /// serializer does with a span is executed rather than assumed from a document.
    ///
    /// It is public because <c>XmlSerializer</c> requires it, and it is nested so that nothing
    /// mistakes it for a type this plugin ships.
    /// </summary>
    public sealed class SpanSetting
    {
        /// <summary>
        /// Gets or sets a window written as the type it is.
        /// </summary>
        public TimeSpan Window { get; set; }
    }
}
