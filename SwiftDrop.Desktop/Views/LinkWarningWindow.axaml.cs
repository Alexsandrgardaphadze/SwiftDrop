using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Diagnostics;

namespace SwiftDrop.Desktop.Views;

public partial class LinkWarningWindow : Window
{
    private readonly string _url;

    public LinkWarningWindow(string url)
    {
        AvaloniaXamlLoader.Load(this);
        _url = url;
        var urlText = this.FindControl<TextBlock>("UrlText");
        if (urlText != null) urlText.Text = url;
    }

    private void OnCancel(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private void OnOpen(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        Close();
    }
}