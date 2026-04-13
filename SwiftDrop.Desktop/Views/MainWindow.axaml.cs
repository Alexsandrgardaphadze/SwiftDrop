using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SwiftDrop.Desktop.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftDrop.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // User list click handler
        var userList = this.FindControl<ItemsControl>("UserList");
        userList?.AddHandler(PointerPressedEvent,
            OnUserListPointerPressed, RoutingStrategies.Tunnel);

        var starredList = this.FindControl<ItemsControl>("StarredUserList");
        starredList?.AddHandler(PointerPressedEvent,
            OnUserListPointerPressed, RoutingStrategies.Tunnel);

        // Drag and drop
        var chatArea = this.FindControl<Border>("ChatDropArea");
        if (chatArea != null)
        {
            DragDrop.SetAllowDrop(chatArea, true);
            chatArea.AddHandler(DragDrop.DropEvent, OnFileDrop);
            chatArea.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        // Scroll detection for scroll-to-bottom button
        var scroller = this.FindControl<ScrollViewer>("MessageScroller");
        if (scroller != null)
            scroller.ScrollChanged += OnScrollChanged;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && DataContext is MainWindowViewModel vm)
        {
            var distFromBottom = sv.Extent.Height
                - sv.Offset.Y - sv.Viewport.Height;
            vm.ShowScrollToBottom = distFromBottom > 200;
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // In Avalonia 12, use e.Handled to determine if it has files
        e.DragEffects = DragDropEffects.None;
        // Check if sender is the drop area
        if (sender is Border) e.DragEffects = DragDropEffects.Copy;
    }

    private async void OnFileDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.SelectedUser is null) return;

        // In Avalonia 12, file data is accessed through the storage provider
        vm.SendFileCommand.Execute(null);
        await Task.CompletedTask;
    }

    private void OnUserListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var source = e.Source as Avalonia.Layout.Layoutable;
        while (source != null)
        {
            if (source.DataContext is UserViewModel user)
            {
                vm.SelectedUser = user;
                break;
            }
            source = source.Parent as Avalonia.Layout.Layoutable;
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CurrentMessages.CollectionChanged += (_, _) =>
            {
                if (!vm.ShowScrollToBottom) ScrollToBottom();
            };
        }
    }

    public void ScrollToBottom()
    {
        var scroller = this.FindControl<ScrollViewer>("MessageScroller");
        scroller?.ScrollToEnd();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Enter to send
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var inputBox = this.FindControl<TextBox>("MessageInputBox");
            if (inputBox?.IsFocused == true && DataContext is MainWindowViewModel vm)
            {
                vm.SendCommand.Execute(null);
                e.Handled = true;
            }
        }

        // Ctrl+F to search
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ToggleSearchCommand.Execute(null);
                e.Handled = true;
            }
        }

        // Escape to cancel reply/search
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm2)
        {
            if (vm2.IsReplying) vm2.CancelReplyCommand.Execute(null);
            if (vm2.IsSearching) vm2.ToggleSearchCommand.Execute(null);
            e.Handled = true;
        }

        // Ctrl+V for image paste
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.PasteImageCommand.Execute(null);
            }
        }
    }
}