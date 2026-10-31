namespace Samaat.Core.Commands;

public enum CommandStatus
{
    /// <summary>The device confirmed the change.</summary>
    Confirmed,

    /// <summary>The device refused the command (busy, out of range, wrong state).</summary>
    Rejected,

    /// <summary>The device or adapter can't do this at all.</summary>
    Unsupported,

    /// <summary>No confirmation arrived in time; the device state is unknown until it reports again.</summary>
    TimedOut,

    /// <summary>Transport or adapter error.</summary>
    Failed,
}

/// <summary>Outcome of a write to a device. Controls only commit a change once it is confirmed.</summary>
public sealed record CommandResult(CommandStatus Status, string? Message = null)
{
    public static CommandResult Confirmed { get; } = new(CommandStatus.Confirmed);

    public static CommandResult Rejected(string message) => new(CommandStatus.Rejected, message);

    public static CommandResult Unsupported(string? message = null) => new(CommandStatus.Unsupported, message);

    public bool Succeeded => Status == CommandStatus.Confirmed;

    /// <summary>Text for the UI when a command didn't succeed.</summary>
    public string? UserMessage => Status switch
    {
        CommandStatus.Confirmed => null,
        _ when Message is not null => Message,
        CommandStatus.Rejected => "The device refused the change.",
        CommandStatus.Unsupported => "This device doesn't support that.",
        CommandStatus.TimedOut => "The device didn't confirm the change in time.",
        _ => "Something went wrong talking to the device.",
    };
}
