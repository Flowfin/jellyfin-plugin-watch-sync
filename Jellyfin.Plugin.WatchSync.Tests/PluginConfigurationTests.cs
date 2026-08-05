using System;
using System.IO;
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
    /// The configuration class carries no settings. An empty type is not automatically a type the
    /// serializer round-trips: a class the serializer cannot construct, or one whose only
    /// constructor takes arguments, throws on the first save, and a plugin that cannot save its
    /// configuration is one the dashboard reports a failure for with no setting on the page to
    /// explain it. The empty state is the state this plugin ships in until #58, so it is the state
    /// that has to be known good rather than assumed good.
    /// </summary>
    [Fact]
    public void AnEmptyConfigurationSurvivesTheServersSerializer()
    {
        var written = new PluginConfiguration();

        using var stream = new MemoryStream();
        SerializeToStream(written, stream);
        stream.Position = 0;
        var read = DeserializeFromStream(typeof(PluginConfiguration), stream);

        Assert.IsType<PluginConfiguration>(read);
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
}
