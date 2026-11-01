using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace CfTunnelManager.Services;

public enum ThemeChoice
{
    Auto,
    Light,
    Dark,
}

/// <summary>Swaps the theme dictionary and follows Windows while set to automatic.</summary>
internal sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Application _application;
    private readonly string _path = Path.Combine(AppData.Directory, "theme.txt");

    public ThemeManager(Application application)
    {
        _application = application;
        Choice = Load();
        Apply();
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
    }

    public ThemeChoice Choice { get; private set; }

    public void Select(ThemeChoice choice)
    {
        Choice = choice;
        AppData.WriteAtomically(_path, Encoding.UTF8.GetBytes(choice.ToString()));
        Apply();
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;

    private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && Choice == ThemeChoice.Auto)
        {
            _application.Dispatcher.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        var light = Choice == ThemeChoice.Light || (Choice == ThemeChoice.Auto && SystemUsesLightTheme());
        var source = new Uri($"pack://application:,,,/Themes/{(light ? "Light" : "Dark")}.xaml");
        _application.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = source };
    }

    private ThemeChoice Load()
    {
        var text = File.Exists(_path) ? File.ReadAllText(_path).Trim() : "";
        return Enum.TryParse<ThemeChoice>(text, out var choice) ? choice : ThemeChoice.Auto;
    }

    private static bool SystemUsesLightTheme() =>
        Registry.CurrentUser.OpenSubKey(PersonalizeKey)?.GetValue("AppsUseLightTheme") is not 0;
}
