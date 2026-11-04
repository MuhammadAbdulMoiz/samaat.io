using System.Text.Json;
using System.Text.Json.Serialization;
using Samaat.Core.Commands;

namespace Samaat.Handoff.Protocol;

/// <summary>Which way the earbuds move.</summary>
public enum HandoffDirection
{
    /// <summary>From this computer to the phone.</summary>
    ToPhone,

    /// <summary>From the phone to this computer.</summary>
    ToComputer,
}

/// <summary>Where a move is. Shown live on both devices.</summary>
public enum HandoffStage
{
    Checking,
    Releasing,
    Connecting,
    RollingBack,
    Done,
}

public enum HandoffOutcome
{
    /// <summary>The earbuds are on the other device now.</summary>
    Moved,

    /// <summary>Refused before anything changed.</summary>
    NotStarted,

    /// <summary>The move failed and the earbuds are back where they were.</summary>
    RolledBack,

    /// <summary>The move failed and putting them back failed too.</summary>
    Stranded,
}

/// <summary>One earbud device as one side sees it. Earbuds are matched across devices by <see cref="Address"/>.</summary>
public sealed record PeerDevice(string Address, string Name, bool Connected, bool CanConnect, bool CanDisconnect);

/// <summary>
/// Everything the two apps say to each other once a session is open. <see cref="Id"/> is set by the channel;
/// a <see cref="Reply"/> names the request it answers in <see cref="Reply.ReplyTo"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(PingMessage), "ping")]
[JsonDerivedType(typeof(DeviceListMessage), "devices")]
[JsonDerivedType(typeof(StartHandoffMessage), "startHandoff")]
[JsonDerivedType(typeof(PrepareMessage), "prepare")]
[JsonDerivedType(typeof(ConnectAudioMessage), "connectAudio")]
[JsonDerivedType(typeof(ReleaseAudioMessage), "releaseAudio")]
[JsonDerivedType(typeof(HandoffProgressMessage), "progress")]
[JsonDerivedType(typeof(ReplyMessage), "reply")]
public abstract record PeerMessage
{
    public int Id { get; init; }

    /// <summary>True for messages the other side must answer with a <see cref="ReplyMessage"/>.</summary>
    [JsonIgnore]
    public virtual bool ExpectsReply => false;
}

/// <summary>The first encrypted message each way. A wrong key fails here.</summary>
public sealed record HelloMessage(string Name, string Platform, int ProtocolVersion) : PeerMessage;

public sealed record PingMessage : PeerMessage
{
    public override bool ExpectsReply => true;
}

/// <summary>The sender's earbuds, sent on connect and whenever they change.</summary>
public sealed record DeviceListMessage(IReadOnlyList<PeerDevice> Devices) : PeerMessage;

/// <summary>Phone → computer: please move these earbuds. Answered when the move is over.</summary>
public sealed record StartHandoffMessage(string Address, HandoffDirection Direction) : PeerMessage
{
    public override bool ExpectsReply => true;
}

/// <summary>Computer → phone: could you take part in this move? Nothing changes yet.</summary>
public sealed record PrepareMessage(string Address, HandoffDirection Direction) : PeerMessage
{
    public override bool ExpectsReply => true;
}

/// <summary>Computer → phone: connect these earbuds now. Confirmed once the phone's audio profile is connected.</summary>
public sealed record ConnectAudioMessage(string Address) : PeerMessage
{
    public override bool ExpectsReply => true;
}

/// <summary>Computer → phone: disconnect these earbuds from the phone.</summary>
public sealed record ReleaseAudioMessage(string Address) : PeerMessage
{
    public override bool ExpectsReply => true;
}

/// <summary>Computer → phone: where a move is, for the phone's screen.</summary>
public sealed record HandoffProgressMessage(string Address, HandoffDirection Direction, HandoffStage Stage, string Text) : PeerMessage;

/// <summary>The answer to a request. <see cref="Outcome"/> is set for <see cref="StartHandoffMessage"/>.</summary>
public sealed record ReplyMessage(int ReplyTo, CommandStatus Status, string? Message = null, HandoffOutcome? Outcome = null) : PeerMessage
{
    public static ReplyMessage From(int replyTo, CommandResult result) => new(replyTo, result.Status, result.Message);

    public CommandResult ToResult() => new(Status, Message);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PeerMessage))]
[JsonSerializable(typeof(Pairing.HandshakeFrame))]
[JsonSerializable(typeof(Pairing.PairConfirmFrame))]
[JsonSerializable(typeof(Discovery.Beacon))]
[JsonSerializable(typeof(Storage.PeerStoreFile))]
internal sealed partial class HandoffJson : JsonSerializerContext;

public static class MessageCodec
{
    public const int ProtocolVersion = 1;

    public static byte[] Encode(PeerMessage message) => JsonSerializer.SerializeToUtf8Bytes(message, HandoffJson.Default.PeerMessage);

    /// <summary>Null for a message this version doesn't know (a newer peer) or a malformed one: callers skip it.</summary>
    public static PeerMessage? Decode(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return JsonSerializer.Deserialize(utf8, HandoffJson.Default.PeerMessage);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Bluetooth addresses in one form on both sides: upper-case, colon-separated.</summary>
    public static string NormalizeAddress(string address) =>
        address.Trim().Replace('-', ':').ToUpperInvariant();
}
