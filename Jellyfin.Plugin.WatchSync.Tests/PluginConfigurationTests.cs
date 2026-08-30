using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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
    ///
    /// THE SETTINGS ARE WALKED RATHER THAN LISTED, AND THIS FACT USED TO LIST THEM. It named six
    /// of the seven the type declares: <c>MaximumFailureSharePercent</c> arrived after the list
    /// was written and was never written, never read back and never compared, so the one setting
    /// this fact did not cover was the newest one, which is the direction a hand-written list
    /// always drifts in. The set now comes off the members themselves, so a setting added
    /// tomorrow is covered without anybody remembering to add it.
    /// </summary>
    [Fact]
    public void EverySettingSurvivesTheServersSerializer()
    {
        var declared = Declared();

        Assert.NotEmpty(declared);

        var written = new PluginConfiguration();
        var untouched = new PluginConfiguration();
        var chosen = new Dictionary<string, int>(StringComparer.Ordinal);
        var next = 101;

        foreach (var property in declared.Values)
        {
            // Neither this setting's own default nor any value already handed to another one, so
            // a member read back off the wrong element cannot come out looking correct.
            while (chosen.Values.Contains(next)
                || declared.Values.Any(other => (int)other.GetValue(untouched)! == next))
            {
                next++;
            }

            property.SetValue(written, next);
            chosen[property.Name] = next;
            next++;
        }

        using var stream = new MemoryStream();
        SerializeToStream(written, stream);
        stream.Position = 0;
        var read = Assert.IsType<PluginConfiguration>(
            DeserializeFromStream(typeof(PluginConfiguration), stream));

        Assert.Empty(declared.Values
            .Where(property => (int)property.GetValue(read)! != chosen[property.Name])
            .Select(property =>
                $"{property.Name} was stored as {chosen[property.Name]} and came back as {property.GetValue(read)}"));
    }

    /// <summary>
    /// A stored value that is not a whole number is refused by the serializer rather than read as
    /// something else, for every setting.
    ///
    /// This is the third of the three inputs #61 asks each setting to be covered against, and it
    /// is the one that never reaches <c>ServerWideSettings</c>: a document arrives from a form,
    /// from an edit somebody made by hand, and from a backup written by an older version, and a
    /// value of the wrong type is refused a layer earlier than a value out of range is.
    ///
    /// Two spellings, and they are two different mistakes. A word is what a hand edit produces.
    /// A number with a fractional part is what a value that used to be a span or a share turns
    /// into, and it is the one worth an assertion of its own: read as a number and rounded, it
    /// would be a setting the operator did not choose, arriving with no error anywhere.
    /// </summary>
    [Fact]
    public void AStoredValueThatIsNotAWholeNumberIsRefusedRatherThanReadAsSomethingElse()
    {
        var document = Written(new PluginConfiguration());

        foreach (var name in Declared().Keys)
        {
            foreach (var instead in new[] { "five", "3.7" })
            {
                var edited = Regex.Replace(
                    document,
                    $"<{name}>[^<]*</{name}>",
                    $"<{name}>{instead}</{name}>");

                Assert.NotEqual(document, edited);

                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(edited));
                    DeserializeFromStream(typeof(PluginConfiguration), stream);
                });
            }
        }
    }

    /// <summary>
    /// The settings the configuration type declares, by name.
    /// </summary>
    /// <returns>The settings.</returns>
    private static IReadOnlyDictionary<string, PropertyInfo> Declared() =>
        typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite)
            .ToDictionary(property => property.Name, property => property, StringComparer.Ordinal);

    /// <summary>
    /// One configuration document as the server would write it.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The document.</returns>
    private static string Written(PluginConfiguration configuration)
    {
        using var stream = new MemoryStream();
        SerializeToStream(configuration, stream);

        return Encoding.UTF8.GetString(stream.ToArray());
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
