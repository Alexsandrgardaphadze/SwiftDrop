using SwiftDrop.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftDrop.Desktop.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ApiService _api = new("https://localhost:7161");
    private readonly ChatService _chat = new();
    private readonly FileTransferService _fileService = new("https://localhost:7161");

    private string _email = "";
    private string _password = "";
    private string _username = "";
    private string _messageInput = "";
    private string _statusText = "Not logged in";
    private string _transferStatus = "";
    private double _transferProgress = 0;
    private bool _isLoggedIn = false;
    private UserDto? _currentUser;
    private UserDto? _selectedReceiver;

    public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
    public string MessageInput { get => _messageInput; set { _messageInput = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string TransferStatus { get => _transferStatus; set { _transferStatus = value; OnPropertyChanged(); } }
    public double TransferProgress { get => _transferProgress; set { _transferProgress = value; OnPropertyChanged(); } }
    public bool IsLoggedIn { get => _isLoggedIn; set { _isLoggedIn = value; OnPropertyChanged(); } }

    public UserDto? SelectedReceiver
    {
        get => _selectedReceiver;
        set { _selectedReceiver = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReceiverLabel)); }
    }

    public string ReceiverLabel => _selectedReceiver is null
        ? "No user selected" : $"Sending to: {_selectedReceiver.Username}";

    public ObservableCollection<string> Messages { get; } = new();
    public ObservableCollection<UserDto> AvailableUsers { get; } = new();
    public Dictionary<string, (string FileName, long Size)> PendingDownloads { get; } = new();

    public ICommand LoginCommand => new RelayCommand(async () =>
    {
        var user = await _api.LoginAsync(Email, Password);
        if (user is null) { StatusText = "Login failed!"; return; }
        _currentUser = user;
        IsLoggedIn = true;
        StatusText = $"Logged in as {user.Username}";

        await _chat.ConnectAsync("https://localhost:7161", user.Id.ToString());

        _chat.MessageReceived += (senderId, content, sentAt) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Messages.Add($"[{sentAt:HH:mm}] {senderId[..8]}: {content}"));
        };

        _chat.FileNotificationReceived += (senderId, transferId, fileName, fileSizeBytes) =>
        {
            PendingDownloads[transferId] = (fileName, fileSizeBytes);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Messages.Add($"[{DateTime.Now:HH:mm}] 📁 {senderId[..8]} sent you '{fileName}' " +
                             $"({fileSizeBytes / 1024.0 / 1024.0:F2} MB) — type /download {transferId[..8]} to save it"));
        };

        // Load available users (excluding self)
        var users = await _api.GetUsersAsync();
        AvailableUsers.Clear();
        foreach (var u in users.Where(u => u.Id != user.Id))
            AvailableUsers.Add(u);

        if (AvailableUsers.Count > 0)
            SelectedReceiver = AvailableUsers[0];
    });

    public ICommand RegisterCommand => new RelayCommand(async () =>
    {
        var user = await _api.RegisterAsync(Username, Email, Password);
        if (user is null) { StatusText = "Registration failed!"; return; }
        StatusText = "Registered! Now log in.";
    });

    public ICommand RefreshUsersCommand => new RelayCommand(async () =>
    {
        if (_currentUser is null) return;
        var users = await _api.GetUsersAsync();
        AvailableUsers.Clear();
        foreach (var u in users.Where(u => u.Id != _currentUser.Id))
            AvailableUsers.Add(u);
        if (AvailableUsers.Count > 0 && SelectedReceiver is null)
            SelectedReceiver = AvailableUsers[0];
        StatusText = $"Found {AvailableUsers.Count} other user(s)";
    });

    public ICommand SendCommand => new RelayCommand(async () =>
    {
        if (_currentUser is null || string.IsNullOrWhiteSpace(MessageInput)) return;

        if (MessageInput.StartsWith("/download "))
        {
            var shortId = MessageInput.Split(' ')[1].Trim();
            var match = PendingDownloads.Keys.FirstOrDefault(k => k.StartsWith(shortId));
            if (match is not null)
            {
                var (fileName, _) = PendingDownloads[match];
                await DownloadFileAsync(Guid.Parse(match), fileName);
                MessageInput = "";
                return;
            }
        }

        var receiverId = SelectedReceiver?.Id.ToString() ?? Guid.Empty.ToString();
        await _chat.SendMessageAsync(_currentUser.Id.ToString(), receiverId, MessageInput);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            Messages.Add($"[{DateTime.Now:HH:mm}] You: {MessageInput}"));
        MessageInput = "";
    });

    public ICommand SendFileCommand => new RelayCommand(async () =>
    {
        if (_currentUser is null) return;
        if (SelectedReceiver is null) { StatusText = "Select a receiver first!"; return; }

        var window = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window is null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select a file to send",
                AllowMultiple = false
            });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;
        var fileInfo = new FileInfo(filePath);
        TransferStatus = "Uploading...";
        TransferProgress = 0;

        try
        {
            var transferId = await _fileService.SendFileAsync(filePath, _currentUser.Id,
                SelectedReceiver.Id,
                progress =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        TransferProgress = progress;
                        TransferStatus = $"Uploading... {progress:F0}%";
                    });
                });

            TransferStatus = "Upload complete! ✅";
            TransferProgress = 100;

            await _chat.NotifyFileReadyAsync(
                _currentUser.Id.ToString(),
                SelectedReceiver.Id.ToString(),
                transferId.ToString(),
                fileInfo.Name,
                fileInfo.Length);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Messages.Add($"[{DateTime.Now:HH:mm}] 📁 File '{fileInfo.Name}' sent to {SelectedReceiver.Username}!"));
        }
        catch (Exception ex)
        {
            TransferStatus = $"Error: {ex.Message}";
        }
    });

    private async Task DownloadFileAsync(Guid transferId, string fileName)
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;
        if (window is null) return;

        var savePath = await window.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save file as",
                SuggestedFileName = fileName
            });

        if (savePath is null) return;

        TransferStatus = "Downloading...";
        TransferProgress = 0;

        try
        {
            await _fileService.DownloadFileAsync(transferId, savePath.Path.LocalPath,
                progress =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        TransferProgress = progress;
                        TransferStatus = $"Downloading... {progress:F0}%";
                    });
                });

            TransferStatus = "Download complete! ✅";
            TransferProgress = 100;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                Messages.Add($"[{DateTime.Now:HH:mm}] ✅ '{fileName}' saved successfully!"));
        }
        catch (Exception ex)
        {
            TransferStatus = $"Download error: {ex.Message}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    public RelayCommand(Func<Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute();
}