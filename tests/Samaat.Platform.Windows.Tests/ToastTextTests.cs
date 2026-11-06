using System.Xml.Linq;

namespace Samaat.Platform.Windows.Tests;

/// <summary>The toast XML: a generic two-line toast, with device names that can contain any character.</summary>
public sealed class ToastTextTests
{
    [Fact]
    public void Toast_IsGenericWithTitleThenBody()
    {
        var xml = XDocument.Parse(WindowsToastNotifier.ToastXml("Buds Pro", "Left bud at 10 %"));

        var binding = xml.Root!.Element("visual")!.Element("binding")!;
        Assert.Equal("ToastGeneric", binding.Attribute("template")!.Value);
        Assert.Equal(["Buds Pro", "Left bud at 10 %"], binding.Elements("text").Select(t => t.Value));
    }

    [Fact]
    public void Toast_EscapesMarkupInNames()
    {
        const string name = "Tom's <Buds> & \"Co\"";

        var xml = XDocument.Parse(WindowsToastNotifier.ToastXml(name, "a < b"));

        Assert.Equal([name, "a < b"], xml.Root!.Descendants("text").Select(t => t.Value));
    }
}
