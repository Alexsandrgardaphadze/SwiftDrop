using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SwiftDrop.Desktop.ViewModels;

public class UserViewModel : INotifyPropertyChanged
{
    private bool _isOnline;
    private bool _isSelected;
    private bool _isStarred;
    private string _lastMessage = "";
    private int _unreadCount;
    private string _status = "online";

    public System.Guid Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";

    public bool IsOnline
    {
        get => _isOnline;
        set { _isOnline = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsStarred
    {
        get => _isStarred;
        set { _isStarred = value; OnPropertyChanged(); }
    }

    public string LastMessage
    {
        get => _lastMessage;
        set { _lastMessage = value; OnPropertyChanged(); }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set { _unreadCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnread)); }
    }

    public bool HasUnread => _unreadCount > 0;

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string AvatarLetter => Username.Length > 0
        ? Username[0].ToString().ToUpper() : "?";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}