using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using SwiftDrop.Desktop.ViewModels;
using System;
using System.IO;

namespace SwiftDrop.Desktop.Views;

public partial class MediaViewerWindow : Window
{
    public MediaViewerWindow(MessageViewModel msg)
    {
        AvaloniaXamlLoader.Load(this);
        Title = $"SwiftDrop — {msg.FileName}";

        var image = this.FindControl<Image>("PreviewImage");
        var nameText = this.FindControl<TextBlock>("FileNameText");

        if (nameText != null)
            nameText.Text = $"{msg.FileName}  •  {msg.FileSizeDisplay}";

        if (image != null && msg.MediaPath != null && File.Exists(msg.MediaPath))
        {
            try
            {
                image.Source = new Bitmap(msg.MediaPath);
            }
            catch { /* video or unsupported — show placeholder */ }
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}