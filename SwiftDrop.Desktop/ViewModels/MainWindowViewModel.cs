using Avalonia.Threading;
using SwiftDrop.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SwiftDrop.Desktop.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ApiService _api = new("https://localhost:7161");
    private readonly ChatService _chat = new();
    private readonly FileTransferService _fileService = new("https://localhost:7161");
    private readonly MediaService _mediaService = new("https://localhost:7161");

    private string _email = "";
    private string _password = "";
    private string _username = "";
    private string _messageInput = "";
    private string _statusText = "Not logged in";
    private string _transferStatus = "";
    private string _searchQuery = "";
    private string _typingIndicator = "";
    private double _transferProgress = 0;
    private bool _isLoggedIn = false;
    private bool _isSearching = false;
    private bool _showScrollToBottom = false;
    private UserDto? _currentUser;
    private UserViewModel? _selectedUser;
    private MessageViewModel? _replyingTo;
    private MessageViewModel? _editingMessage;
    private CancellationTokenSource? _typingCts;

    private readonly Dictionary<Guid, MessageViewModel> _messageIndex = new();

    public string Email { get => _email; set { _email = value; OnPropertyChanged(); } }
    public string Password { get => _password; set { _password = value; OnPropertyChanged(); } }
    public string Username { get => _username; set { _username = value; OnPropertyChanged(); } }
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
    public string TransferStatus { get => _transferStatus; set { _transferStatus = value; OnPropertyChanged(); } }
    public string TypingIndicator { get => _typingIndicator; set { _typingIndicator = value; OnPropertyChanged(); } }
    public double TransferProgress { get => _transferProgress; set { _transferProgress = value; OnPropertyChanged(); } }
    public bool IsLoggedIn { get => _isLoggedIn; set { _isLoggedIn = value; OnPropertyChanged(); } }
    public bool IsSearching { get => _isSearching; set { _isSearching = value; OnPropertyChanged(); } }
    public bool ShowScrollToBottom { get => _showScrollToBottom; set { _showScrollToBottom = value; OnPropertyChanged(); } }
    public bool IsTypingIndicatorVisible => !string.IsNullOrEmpty(_typingIndicator);

    public string MessageInput
    {
        get => _messageInput;
        set
        {
            _messageInput = value;
            OnPropertyChanged();
            // Trigger typing indicator
            if (_currentUser != null && _selectedUser != null && !string.IsNullOrEmpty(value))
                _ = SendTypingAsync(true);
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            _searchQuery = value;
            OnPropertyChanged();
            FilterMessages();
        }
    }

    public MessageViewModel? ReplyingTo
    {
        get => _replyingTo;
        set { _replyingTo = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReplying)); }
    }
    public bool IsReplying => _replyingTo != null;

    public UserViewModel? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (_selectedUser != null) _selectedUser.IsSelected = false;
            _selectedUser = value;
            if (value != null)
            {
                value.IsSelected = true;
                value.UnreadCount = 0;
                TypingIndicator = "";
                _ = LoadConversationAsync(value.Id);
            }
            OnPropertyChanged();
        }
    }

    public UserDto? CurrentUser => _currentUser;
    public string CurrentUserAvatarLetter => _currentUser?.Username.Length > 0
        ? _currentUser.Username[0].ToString().ToUpper() : "?";

    public ObservableCollection<UserViewModel> Users { get; } = new();
    public ObservableCollection<UserViewModel> StarredUsers { get; } = new();
    public ObservableCollection<MessageViewModel> CurrentMessages { get; } = new();
    public ObservableCollection<MessageViewModel> FilteredMessages { get; } = new();
    public ObservableCollection<ChatItemViewModel> ChatItems { get; } = new();
    public Dictionary<Guid, ObservableCollection<MessageViewModel>> AllConversations { get; } = new();
    public Dictionary<string, (string FileName, long Size)> PendingDownloads { get; } = new();
    public ObservableCollection<string> QuickEmojis { get; } = new(
        new[] { "👍", "❤️", "😂", "😮", "😢", "🔥", "✅", "👀" });

    // Commands
    public ICommand LoginCommand => new RelayCommand(async () => await DoLogin());
    public ICommand RegisterCommand => new RelayCommand(async () => await DoRegister());
    public ICommand SendCommand => new RelayCommand(async () => await DoSend());
    public ICommand SendFileCommand => new RelayCommand(async () => await DoSendFile());
    public ICommand RefreshUsersCommand => new RelayCommand(async () => await RefreshUsers());
    public ICommand CancelReplyCommand => new RelayCommand(async () =>
    {
        ReplyingTo = null;
        await Task.CompletedTask;
    });
    public ICommand ToggleSearchCommand => new RelayCommand(async () =>
    {
        IsSearching = !IsSearching;
        if (!IsSearching) SearchQuery = "";
        await Task.CompletedTask;
    });
    public ICommand ScrollToBottomCommand => new RelayCommand(async () =>
    {
        ShowScrollToBottom = false;
        await Task.CompletedTask;
        // Actual scroll handled in code-behind
    });
    public ICommand QuitCommand => new RelayCommand(async () =>
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime app)
            app.Shutdown();
        await Task.CompletedTask;
    });
    public ICommand SelectUserCommand => new RelayCommandParam(user =>
    {
        if (user is UserViewModel u) SelectedUser = u;
    });
    public ICommand ToggleStarCommand => new RelayCommandParam(user =>
    {
        if (user is UserViewModel u)
        {
            u.IsStarred = !u.IsStarred;
            if (u.IsStarred && !StarredUsers.Contains(u))
                StarredUsers.Add(u);
            else
                StarredUsers.Remove(u);
        }
    });
    public ICommand ReplyCommand => new RelayCommandParam(msg =>
    {
        if (msg is MessageViewModel m) ReplyingTo = m;
    });
    public ICommand EditMessageCommand => new RelayCommandParam(msg =>
    {
        if (msg is MessageViewModel m && m.IsOwnMessage)
        {
            MessageInput = m.Content;
            _editingMessage = m;
        }
    });
    public ICommand DeleteMessageCommand => new RelayCommandParam(async msg =>
    {
        if (msg is MessageViewModel m && m.IsOwnMessage)
        {
            await _chat.DeleteMessageAsync(m.Id.ToString());
            m.IsDeleted = true;
            m.Content = "Message deleted";
        }
    });
    public ICommand ReactCommand => new RelayCommandParam(async param =>
    {
        if (param is not ValueTuple<MessageViewModel, string> tuple) return;
        var (msg, emoji) = tuple;
        await _api.ReactToMessageAsync(msg.Id, emoji,
            _currentUser?.Id.ToString() ?? "");
        var existing = msg.Reactions.FirstOrDefault(r => r.Emoji == emoji);
        if (existing != null)
        {
            if (existing.ReactedByMe) { existing.Count--; existing.ReactedByMe = false; }
            else { existing.Count++; existing.ReactedByMe = true; }
            if (existing.Count <= 0) msg.Reactions.Remove(existing);
        }
        else
            msg.Reactions.Add(new ReactionViewModel
            { Emoji = emoji, Count = 1, ReactedByMe = true });
    });
    public ICommand DownloadCommand => new RelayCommandParam(async obj =>
    {
        if (obj is MessageViewModel msg && msg.TransferId is not null
            && msg.FileName is not null)
            await DownloadFileAsync(msg.TransferId, msg.FileName);
    });
    public ICommand ViewMediaCommand => new RelayCommandParam(async obj =>
    {
        if (obj is MessageViewModel msg)
            new Views.MediaViewerWindow(msg).Show();
        await Task.CompletedTask;
    });
    public ICommand OpenLinkCommand => new RelayCommandParam(async link =>
    {
        if (link is string url) await OpenLinkWithWarningAsync(url);
    });
    public ICommand PasteImageCommand => new RelayCommand(async () =>
        await PasteFromClipboardAsync());

    private async Task SendTypingAsync(bool isTyping)
    {
        if (_currentUser is null || _selectedUser is null) return;
        await _chat.SendTypingAsync(_currentUser.Id.ToString(),
            _selectedUser.Id.ToString(), isTyping);

        // Auto-stop typing after 3 seconds
        if (isTyping)
        {
            _typingCts?.Cancel();
            _typingCts = new CancellationTokenSource();
            var token = _typingCts.Token;
            _ = Task.Delay(3000, token).ContinueWith(async t =>
            {
                if (!t.IsCanceled)
                    await _chat.SendTypingAsync(_currentUser.Id.ToString(),
                        _selectedUser.Id.ToString(), false);
            });
        }
    }

    private async Task DoLogin()
    {
        var user = await _api.LoginAsync(Email, Password);
        if (user is null) { StatusText = "Login failed!"; return; }
        _currentUser = user;
        IsLoggedIn = true;
        StatusText = $"Logged in as {user.Username}";
        OnPropertyChanged(nameof(CurrentUser));
        OnPropertyChanged(nameof(CurrentUserAvatarLetter));

        await _chat.ConnectAsync("https://localhost:7161", user.Id.ToString());

        _chat.MessageReceived += payload =>
        {
            if (payload.SenderId == _currentUser?.Id.ToString()) return;
            Dispatcher.UIThread.Post(() =>
            {
                var senderUser = Users.FirstOrDefault(
                    u => u.Id.ToString() == payload.SenderId);
                var msg = BuildMessage(payload, senderUser, false);
                AddToConversation(msg, Guid.Parse(payload.SenderId));

                if (SelectedUser?.Id.ToString() == payload.SenderId)
                    _ = _chat.MarkReadAsync(payload.Id.ToString(),
                        _currentUser?.Id.ToString() ?? "");

                // Desktop notification if window not focused
                ShowDesktopNotification(
                    senderUser?.Username ?? "Someone",
                    payload.Content);
            });
        };

        _chat.MessageSentConfirmed += payload =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                var pending = CurrentMessages.FirstOrDefault(
                    m => m.IsOwnMessage
                    && m.Status == MessageStatus.Sending
                    && m.Content == payload.Content);
                if (pending != null)
                {
                    pending.Id = payload.Id;
                    pending.Status = payload.IsDelivered
                        ? MessageStatus.Delivered : MessageStatus.Sent;
                    _messageIndex[payload.Id] = pending;
                }
            });
        };

        _chat.MessageEditedReceived += (msgId, newContent) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Guid.TryParse(msgId, out var g)
                    && _messageIndex.TryGetValue(g, out var msg))
                {
                    msg.Content = newContent;
                    msg.IsEdited = true;
                }
            });
        };

        _chat.MessageDeletedReceived += msgId =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Guid.TryParse(msgId, out var g)
                    && _messageIndex.TryGetValue(g, out var msg))
                {
                    msg.IsDeleted = true;
                    msg.Content = "Message deleted";
                }
            });
        };

        _chat.MessageReadReceived += msgId =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (Guid.TryParse(msgId, out var g)
                    && _messageIndex.TryGetValue(g, out var msg))
                    msg.Status = MessageStatus.Read;
            });
        };

        _chat.UserTypingReceived += (senderId, isTyping) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (SelectedUser?.Id.ToString() != senderId) return;
                var sender = Users.FirstOrDefault(
                    u => u.Id.ToString() == senderId);
                TypingIndicator = isTyping
                    ? $"{sender?.Username ?? "Someone"} is typing..."
                    : "";
                OnPropertyChanged(nameof(IsTypingIndicatorVisible));
            });
        };

        _chat.FileNotificationReceived += (senderId, transferId, fileName, size) =>
        {
            PendingDownloads[transferId] = (fileName, size);
            Dispatcher.UIThread.Post(() =>
            {
                var senderUser = Users.FirstOrDefault(
                    u => u.Id.ToString() == senderId);
                var msg = new MessageViewModel
                {
                    Id = Guid.NewGuid(),
                    SenderId = senderId,
                    SenderName = senderUser?.Username ?? senderId[..8],
                    Content = "Sent you a file",
                    SentAt = DateTime.Now,
                    IsOwnMessage = false,
                    MessageType = MessageType.File,
                    TransferId = transferId,
                    FileName = fileName,
                    FileSizeBytes = size,
                    Status = MessageStatus.Delivered
                };
                AddToConversation(msg, Guid.Parse(senderId));
                ShowDesktopNotification(
                    senderUser?.Username ?? "Someone",
                    $"📁 Sent you {fileName}");
            });
        };

        _chat.MediaNotificationReceived += (senderId, mediaUrl, fileName,
            mediaType, sizeBytes) =>
        {
            Dispatcher.UIThread.Post(async () =>
            {
                var senderUser = Users.FirstOrDefault(
                    u => u.Id.ToString() == senderId);
                var msgType = mediaType == "gif" ? MessageType.Gif
                    : mediaType == "video" ? MessageType.Video
                    : MessageType.Image;
                var fullUrl = $"https://localhost:7161{mediaUrl}";
                var cachedPath = await _mediaService.DownloadToCache(fullUrl);
                var msg = new MessageViewModel
                {
                    Id = Guid.NewGuid(),
                    SenderId = senderId,
                    SenderName = senderUser?.Username ?? senderId[..8],
                    Content = fileName,
                    SentAt = DateTime.Now,
                    IsOwnMessage = false,
                    MessageType = msgType,
                    FileName = fileName,
                    FileSizeBytes = sizeBytes,
                    MediaUrl = fullUrl,
                    MediaPath = cachedPath,
                    Status = MessageStatus.Delivered
                };
                AddToConversation(msg, Guid.Parse(senderId));
            });
        };

        await RefreshUsers();
    }

    private MessageViewModel BuildMessage(MessagePayload payload,
        UserViewModel? sender, bool isOwn)
    {
        return new MessageViewModel
        {
            Id = payload.Id,
            SenderId = payload.SenderId,
            SenderName = isOwn
                ? (_currentUser?.Username ?? "")
                : (sender?.Username ?? payload.SenderId[..8]),
            ReceiverId = payload.ReceiverId,
            Content = payload.Content,
            // Always convert to local time correctly
            SentAt = payload.SentAt.Kind == DateTimeKind.Utc
                ? payload.SentAt.ToLocalTime()
                : payload.SentAt,
            IsOwnMessage = isOwn,
            MessageType = MessageType.Text,
            Status = isOwn
                ? (payload.IsDelivered
                    ? MessageStatus.Delivered : MessageStatus.Sent)
                : MessageStatus.Delivered
        };
    }

    private void AddToConversation(MessageViewModel msg, Guid conversationKey)
    {
        if (!AllConversations.ContainsKey(conversationKey))
            AllConversations[conversationKey] = new();

        var conversation = AllConversations[conversationKey];

        // Grouping logic — 5 min window
        if (conversation.Count > 0)
        {
            var prev = conversation.Last();
            var diff = msg.SentAt - prev.SentAt;
            if (prev.SenderId == msg.SenderId && diff.TotalMinutes <= 5)
                msg.IsGrouped = true;
        }

        conversation.Add(msg);
        if (msg.Id != Guid.Empty)
            _messageIndex[msg.Id] = msg;

        var isActive = msg.IsOwnMessage
            ? SelectedUser?.Id.ToString() == msg.ReceiverId
            : SelectedUser?.Id.ToString() == msg.SenderId;

        if (isActive)
        {
            CurrentMessages.Add(msg);
            RebuildChatItems();
        }
        else if (!msg.IsOwnMessage)
        {
            var u = Users.FirstOrDefault(u => u.Id == conversationKey);
            if (u != null)
            {
                u.UnreadCount++;
                u.LastMessage = msg.IsFileMessage ? $"📁 {msg.FileName}"
                    : msg.IsImageMessage ? "🖼 Image"
                    : msg.Content;
            }
        }
    }

    // Rebuild ChatItems with date separators
    private void RebuildChatItems()
    {
        ChatItems.Clear();
        DateTime? lastDate = null;
        foreach (var msg in CurrentMessages)
        {
            var msgDate = msg.SentAt.Date;
            if (lastDate == null || msgDate != lastDate)
            {
                string label;
                if (msgDate == DateTime.Today) label = "Today";
                else if (msgDate == DateTime.Today.AddDays(-1)) label = "Yesterday";
                else label = msgDate.ToString("MMMM d, yyyy");
                ChatItems.Add(ChatItemViewModel.ForDate(label));
                lastDate = msgDate;
            }
            ChatItems.Add(ChatItemViewModel.ForMessage(msg));
        }
    }

    private void FilterMessages()
    {
        FilteredMessages.Clear();
        if (string.IsNullOrWhiteSpace(_searchQuery)) return;
        var q = _searchQuery.ToLowerInvariant();
        foreach (var m in CurrentMessages
            .Where(m => m.Content.ToLowerInvariant().Contains(q)))
            FilteredMessages.Add(m);
    }

    private void ShowDesktopNotification(string sender, string content)
    {
        // Avalonia doesn't have built-in toast notifications yet
        // This is a placeholder — we'll use the tray/taskbar flash instead
        Console.WriteLine($"[NOTIFY] {sender}: {content}");
    }

    private async Task PasteFromClipboardAsync()
    {
        try
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes
                .IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow : null;
            if (mainWindow?.Clipboard is null) return;

            // Avalonia 12 clipboard API is limited; for now we'll skip clipboard paste
            // This feature would require platform-specific implementations
            StatusText = "Clipboard paste not currently supported";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusText = $"Paste error: {ex.Message}";
        }
    }

    private async Task DoRegister()
    {
        var user = await _api.RegisterAsync(Username, Email, Password);
        if (user is null) { StatusText = "Registration failed!"; return; }
        StatusText = "Registered! Now log in.";
    }

    private async Task DoSend()
    {
        if (_currentUser is null || string.IsNullOrWhiteSpace(MessageInput)) return;
        if (SelectedUser is null) { StatusText = "Select a user first!"; return; }

        // Stop typing indicator
        await SendTypingAsync(false);

        // Handle edit mode
        if (_editingMessage != null)
        {
            var editContent = MessageInput;
            MessageInput = "";
            await _chat.EditMessageAsync(_editingMessage.Id.ToString(), editContent);
            _editingMessage.Content = editContent;
            _editingMessage.IsEdited = true;
            _editingMessage = null;
            return;
        }

        var content = MessageInput;
        MessageInput = "";
        var replyId = ReplyingTo?.Id.ToString();
        var replyRef = ReplyingTo;
        ReplyingTo = null;

        var msg = new MessageViewModel
        {
            Id = Guid.NewGuid(),
            SenderId = _currentUser.Id.ToString(),
            SenderName = _currentUser.Username,
            ReceiverId = SelectedUser.Id.ToString(),
            Content = content,
            SentAt = DateTime.Now,
            IsOwnMessage = true,
            MessageType = MessageType.Text,
            Status = MessageStatus.Sending,
            ReplyTo = replyRef
        };

        // Apply grouping
        if (CurrentMessages.Count > 0)
        {
            var prev = CurrentMessages.Last();
            if (prev.SenderId == msg.SenderId
                && (msg.SentAt - prev.SentAt).TotalMinutes <= 5)
                msg.IsGrouped = true;
        }

        if (!AllConversations.ContainsKey(SelectedUser.Id))
            AllConversations[SelectedUser.Id] = new();
        AllConversations[SelectedUser.Id].Add(msg);
        CurrentMessages.Add(msg);
        RebuildChatItems();
        SelectedUser.LastMessage = content;

        await _chat.SendMessageAsync(_currentUser.Id.ToString(),
            SelectedUser.Id.ToString(), content, replyId);
    }

    private async Task DoSendFile()
    {
        if (_currentUser is null || SelectedUser is null)
        {
            StatusText = "Select a user first!";
            return;
        }

        var window = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow : null;
        if (window is null) return;

        var files = await window.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select file, image, or video",
                AllowMultiple = false
            });

        if (files.Count == 0) return;
        var filePath = files[0].Path.LocalPath;
        var fileInfo = new FileInfo(filePath);

        if (_mediaService.IsMediaFile(filePath))
        {
            await DoSendMedia(filePath, fileInfo);
            return;
        }

        await DoSendLargeFile(filePath, fileInfo);
    }

    private async Task DoSendLargeFile(string filePath, FileInfo fileInfo)
    {
        TransferStatus = "Uploading...";
        TransferProgress = 0;

        var uploadingMsg = new MessageViewModel
        {
            Id = Guid.NewGuid(),
            SenderId = _currentUser!.Id.ToString(),
            SenderName = _currentUser.Username,
            ReceiverId = SelectedUser!.Id.ToString(),
            Content = "Uploading...",
            SentAt = DateTime.Now,
            IsOwnMessage = true,
            MessageType = MessageType.File,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            Status = MessageStatus.Sending
        };

        ApplyGrouping(uploadingMsg);
        if (!AllConversations.ContainsKey(SelectedUser.Id))
            AllConversations[SelectedUser.Id] = new();
        AllConversations[SelectedUser.Id].Add(uploadingMsg);
        CurrentMessages.Add(uploadingMsg);
        RebuildChatItems();

        try
        {
            var transferId = await _fileService.SendFileAsync(filePath,
                _currentUser.Id, SelectedUser.Id,
                progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        TransferProgress = progress;
                        TransferStatus = $"Uploading... {progress:F0}%";
                        uploadingMsg.Content = $"Uploading... {progress:F0}%";
                    });
                });

            uploadingMsg.Content = "Sent ✅";
            uploadingMsg.TransferId = transferId.ToString();
            uploadingMsg.Status = MessageStatus.Sent;
            TransferStatus = "Upload complete! ✅";
            TransferProgress = 100;
            SelectedUser.LastMessage = $"📁 {fileInfo.Name}";

            await _chat.NotifyFileReadyAsync(_currentUser.Id.ToString(),
                SelectedUser.Id.ToString(), transferId.ToString(),
                fileInfo.Name, fileInfo.Length);
        }
        catch (Exception ex)
        {
            TransferStatus = $"Error: {ex.Message}";
            uploadingMsg.Content = "Failed ❌";
        }
    }

    private async Task DoSendMedia(string filePath, FileInfo fileInfo)
    {
        TransferStatus = "Uploading media...";
        var msgType = _mediaService.IsGifFile(filePath) ? MessageType.Gif
            : _mediaService.IsVideoFile(filePath) ? MessageType.Video
            : MessageType.Image;

        var previewMsg = new MessageViewModel
        {
            Id = Guid.NewGuid(),
            SenderId = _currentUser!.Id.ToString(),
            SenderName = _currentUser.Username,
            ReceiverId = SelectedUser!.Id.ToString(),
            Content = "Uploading...",
            SentAt = DateTime.Now,
            IsOwnMessage = true,
            MessageType = msgType,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            MediaPath = filePath,
            Status = MessageStatus.Sending
        };

        ApplyGrouping(previewMsg);
        if (!AllConversations.ContainsKey(SelectedUser.Id))
            AllConversations[SelectedUser.Id] = new();
        AllConversations[SelectedUser.Id].Add(previewMsg);
        CurrentMessages.Add(previewMsg);
        RebuildChatItems();

        try
        {
            var result = await _mediaService.UploadMediaAsync(filePath,
                _currentUser.Id.ToString(), SelectedUser.Id.ToString());
            if (result is null) { previewMsg.Content = "Upload failed ❌"; return; }

            var fullUrl = $"https://localhost:7161{result.Url}";
            previewMsg.Content = "Sent ✅";
            previewMsg.MediaUrl = fullUrl;
            previewMsg.Status = MessageStatus.Sent;
            TransferStatus = "Media sent! ✅";
            SelectedUser.LastMessage = msgType == MessageType.Gif ? "🎞 GIF"
                : msgType == MessageType.Video ? "🎥 Video" : "🖼 Image";

            await _chat.NotifyMediaReadyAsync(_currentUser.Id.ToString(),
                SelectedUser.Id.ToString(), result.Url, fileInfo.Name,
                result.MediaType, fileInfo.Length);
        }
        catch (Exception ex)
        {
            TransferStatus = $"Error: {ex.Message}";
            previewMsg.Content = "Failed ❌";
        }
    }

    private void ApplyGrouping(MessageViewModel msg)
    {
        if (CurrentMessages.Count <= 0) return;
        var prev = CurrentMessages.Last();
        if (prev.SenderId == msg.SenderId
            && (msg.SentAt - prev.SentAt).TotalMinutes <= 5)
            msg.IsGrouped = true;
    }

    public async Task DownloadFileAsync(string transferId, string fileName)
    {
        var window = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow : null;
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
            await _fileService.DownloadFileAsync(Guid.Parse(transferId),
                savePath.Path.LocalPath,
                progress =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        TransferProgress = progress;
                        TransferStatus = $"Downloading... {progress:F0}%";
                    });
                });
            TransferStatus = "Download complete! ✅";
            TransferProgress = 100;
        }
        catch (Exception ex)
        {
            TransferStatus = $"Download error: {ex.Message}";
        }
    }

    private async Task OpenLinkWithWarningAsync(string url)
    {
        var mainWindow = (Avalonia.Application.Current?.ApplicationLifetime
            as Avalonia.Controls.ApplicationLifetimes
            .IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (mainWindow is null) return;
        var dialog = new Views.LinkWarningWindow(url);
        await dialog.ShowDialog(mainWindow);
    }

    private async Task LoadConversationAsync(Guid userId)
    {
        CurrentMessages.Clear();
        ChatItems.Clear();

        if (!AllConversations.ContainsKey(userId) && _currentUser != null)
        {
            var history = await _api.GetConversationAsync(_currentUser.Id, userId);
            var conversation = new ObservableCollection<MessageViewModel>();
            var senderUser = Users.FirstOrDefault(u => u.Id == userId);
            MessageViewModel? prev = null;

            foreach (var h in history)
            {
                var isOwn = h.SenderId == _currentUser.Id;
                var msg = new MessageViewModel
                {
                    Id = h.Id,
                    SenderId = h.SenderId.ToString(),
                    SenderName = isOwn
                        ? _currentUser.Username
                        : senderUser?.Username ?? h.SenderId.ToString()[..8],
                    ReceiverId = h.ReceiverId.ToString(),
                    Content = h.Content,
                    SentAt = h.SentAt.Kind == DateTimeKind.Utc
                        ? h.SentAt.ToLocalTime() : h.SentAt,
                    IsOwnMessage = isOwn,
                    MessageType = MessageType.Text,
                    IsEdited = h.EditedAt.HasValue,
                    Status = h.ReadAt.HasValue ? MessageStatus.Read
                        : h.DeliveredAt.HasValue ? MessageStatus.Delivered
                        : MessageStatus.Sent
                };

                if (prev != null)
                {
                    var diff = msg.SentAt - prev.SentAt;
                    if (prev.SenderId == msg.SenderId && diff.TotalMinutes <= 5)
                        msg.IsGrouped = true;
                }

                conversation.Add(msg);
                _messageIndex[msg.Id] = msg;
                prev = msg;
            }

            AllConversations[userId] = conversation;
        }

        if (AllConversations.TryGetValue(userId, out var msgs))
            foreach (var m in msgs)
                CurrentMessages.Add(m);

        RebuildChatItems();
    }

    private async Task RefreshUsers()
    {
        if (_currentUser is null) return;
        var users = await _api.GetUsersAsync();
        var existing = Users.ToDictionary(u => u.Id);

        foreach (var u in users.Where(u => u.Id != _currentUser.Id))
        {
            if (!existing.ContainsKey(u.Id))
                Users.Add(new UserViewModel
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    IsOnline = true
                });
            else
                existing[u.Id].IsOnline = true;
        }

        if (SelectedUser is null && Users.Count > 0)
            SelectedUser = Users[0];

        StatusText = $"Logged in as {_currentUser.Username}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class RelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    public RelayCommand(Func<Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute();
}

public class RelayCommandParam : ICommand
{
    private readonly Action<object?> _execute;
    public RelayCommandParam(Action<object?> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class ProgressVisibilityConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly ProgressVisibilityConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
        => value is double d && d > 0 && d < 100;
    public object ConvertBack(object? value, Type targetType, object? parameter,
        System.Globalization.CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}