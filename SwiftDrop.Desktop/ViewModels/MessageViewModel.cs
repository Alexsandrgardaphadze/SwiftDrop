using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwiftDrop.Desktop.ViewModels;

public enum MessageType { Text, File, Image, Gif, Video }

public enum MessageStatus { Sending, Sent, Delivered, Read }

public class ReactionViewModel
{
    public string Emoji { get; set; } = "";
    public int Count { get; set; }
    public bool ReactedByMe { get; set; }
}

public class MessageViewModel : INotifyPropertyChanged
{
    private string _content = "";
    private MessageStatus _status = MessageStatus.Sending;
    private bool _isEdited;
    private bool _isDeleted;
    private bool _isGrouped; // true = hide avatar/name (continuation message)
    private bool _showEmojiPicker;

    public Guid Id { get; set; }
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string ReceiverId { get; set; } = "";
    public bool IsOwnMessage { get; set; }
    public MessageType MessageType { get; set; } = MessageType.Text;
    public string? TransferId { get; set; }
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? MediaPath { get; set; }
    public string? MediaUrl { get; set; }
    public DateTime SentAt { get; set; }
    public MessageViewModel? ReplyTo { get; set; }
    public ObservableCollection<ReactionViewModel> Reactions { get; } = new();

    public string Content
    {
        get => _content;
        set { _content = value; OnPropertyChanged(); }
    }

    public MessageStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); }
    }

    public bool IsEdited
    {
        get => _isEdited;
        set { _isEdited = value; OnPropertyChanged(); }
    }

    public bool IsDeleted
    {
        get => _isDeleted;
        set { _isDeleted = value; OnPropertyChanged(); }
    }

    public bool IsGrouped
    {
        get => _isGrouped;
        set { _isGrouped = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowHeader)); }
    }

    public bool ShowEmojiPicker
    {
        get => _showEmojiPicker;
        set { _showEmojiPicker = value; OnPropertyChanged(); }
    }

    public bool ShowHeader => !IsGrouped;
    public bool IsTextMessage => MessageType == MessageType.Text;
    public bool IsFileMessage => MessageType == MessageType.File;
    public bool IsImageMessage => MessageType == MessageType.Image
                               || MessageType == MessageType.Gif;
    public bool IsVideoMessage => MessageType == MessageType.Video;
    public bool HasReply => ReplyTo != null;
    public bool HasReactions => Reactions.Count > 0;

    public string AvatarLetter => SenderName.Length > 0
        ? SenderName[0].ToString().ToUpper() : "?";

    // Always convert UTC to local time
    public string TimeDisplay => SentAt.Kind == DateTimeKind.Utc
        ? SentAt.ToLocalTime().ToString("HH:mm")
        : SentAt.ToString("HH:mm");

    public string StatusIcon => Status switch
    {
        MessageStatus.Sending => "🕐",
        MessageStatus.Sent => "✓",
        MessageStatus.Delivered => "✓✓",
        MessageStatus.Read => "✓✓",
        _ => ""
    };

    public string FileSizeDisplay => FileSizeBytes.HasValue
        ? FileSizeBytes.Value >= 1024 * 1024
            ? $"{FileSizeBytes.Value / 1024.0 / 1024.0:F2} MB"
            : $"{FileSizeBytes.Value / 1024.0:F1} KB"
        : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}