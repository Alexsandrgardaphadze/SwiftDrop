namespace SwiftDrop.Desktop.ViewModels;

public class ChatItemViewModel
{
    public bool IsDateSeparator { get; set; }
    public bool IsMessage => !IsDateSeparator;
    public string? DateLabel { get; set; }
    public MessageViewModel? Message { get; set; }

    public static ChatItemViewModel ForDate(string label) => new()
    {
        IsDateSeparator = true,
        DateLabel = label
    };

    public static ChatItemViewModel ForMessage(MessageViewModel msg) => new()
    {
        IsDateSeparator = false,
        Message = msg
    };
}