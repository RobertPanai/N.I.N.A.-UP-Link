using System.ComponentModel.Composition;
using System.Windows;

namespace MyUniverseUplink.Options;

/// <summary>ResourceDictionary esportato via MEF: NINA lo carica e ne prende il DataTemplate delle opzioni.</summary>
[Export(typeof(ResourceDictionary))]
public partial class PluginTemplates : ResourceDictionary
{
    public PluginTemplates()
    {
        InitializeComponent();
    }
}
