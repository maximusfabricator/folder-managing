using System.Windows.Markup;
using FolderManaging.Core.Localization;

namespace FolderManaging.Setup.Localization;

public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        string.IsNullOrEmpty(Key) ? "" : UiText.Get(Key);
}
