using System.Security.Cryptography;
using Samaat.Core.Commands;
using Samaat.Handoff.Discovery;
using Samaat.Handoff.Protocol;
using Samaat.Handoff.Security;
using Samaat.Handoff.Storage;

namespace Samaat.Handoff.Tests;

public class CryptoTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void Seal_ThenOpen_RoundTrips()
    {
        using var send = new SessionCipher(Key);
        using var receive = new SessionCipher(Key);

        Assert.Equal("hello"u8.ToArray(), receive.Open(send.Seal("hello"u8)));
        Assert.Equal("again"u8.ToArray(), receive.Open(send.Seal("again"u8)));
    }

    [Fact]
    public void Open_TamperedFrame_Throws()
    {
        using var send = new SessionCipher(Key);
        using var receive = new SessionCipher(Key);
        var frame = send.Seal("hello"u8);
        frame[0] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => receive.Open(frame));
    }

    [Fact]
    public void Open_WrongKey_Throws()
    {
        using var send = new SessionCipher(Key);
        using var receive = new SessionCipher(RandomNumberGenerator.GetBytes(32));

        Assert.ThrowsAny<CryptographicException>(() => receive.Open(send.Seal("hello"u8)));
    }

    [Fact]
    public void Open_ReplayedFrame_Throws()
    {
        using var send = new SessionCipher(Key);
        using var receive = new SessionCipher(Key);
        var frame = send.Seal("hello"u8);
        receive.Open(frame);

        // The counter has moved on, so the same frame no longer authenticates.
        Assert.ThrowsAny<CryptographicException>(() => receive.Open(frame));
    }

    [Fact]
    public void Open_ReorderedFrames_Throws()
    {
        using var send = new SessionCipher(Key);
        using var receive = new SessionCipher(Key);
        send.Seal("first"u8);
        var second = send.Seal("second"u8);

        Assert.ThrowsAny<CryptographicException>(() => receive.Open(second));
    }

    [Fact]
    public void SharedSecret_SameOnBothSides_AndCodesMatch()
    {
        using var phone = new PairingKey();
        using var computer = new PairingKey();
        var phoneNonce = KeyDerivation.NewNonce();
        var computerNonce = KeyDerivation.NewNonce();

        var a = phone.DeriveSharedSecret(computer.PublicKey);
        var b = computer.DeriveSharedSecret(phone.PublicKey);

        Assert.Equal(a, b);
        Assert.Equal(
            KeyDerivation.ComparisonCode(a, phone.PublicKey, computer.PublicKey, phoneNonce, computerNonce),
            KeyDerivation.ComparisonCode(b, phone.PublicKey, computer.PublicKey, phoneNonce, computerNonce));
        Assert.Equal(KeyDerivation.PairingSecret(a, phoneNonce, computerNonce), KeyDerivation.PairingSecret(b, phoneNonce, computerNonce));
    }

    [Fact]
    public void ComparisonCode_DependsOnNonces()
    {
        using var phone = new PairingKey();
        using var computer = new PairingKey();
        var shared = phone.DeriveSharedSecret(computer.PublicKey);
        var codes = Enumerable.Range(0, 8)
            .Select(_ => KeyDerivation.ComparisonCode(shared, phone.PublicKey, computer.PublicKey, KeyDerivation.NewNonce(), KeyDerivation.NewNonce()))
            .ToHashSet();

        Assert.True(codes.Count > 1);
        Assert.All(codes, c => Assert.InRange(c, 0, 999_999));
    }

    [Theory]
    [InlineData(0, "000 000")]
    [InlineData(42, "000 042")]
    [InlineData(123456, "123 456")]
    [InlineData(999999, "999 999")]
    public void FormatCode_GroupsThreeDigits(int code, string text) => Assert.Equal(text, KeyDerivation.FormatCode(code));

    [Fact]
    public void DeriveSharedSecret_GarbageKey_Throws()
    {
        using var key = new PairingKey();

        Assert.ThrowsAny<CryptographicException>(() => key.DeriveSharedSecret([1, 2, 3]));
    }

    [Fact]
    public void SessionKeys_DifferPerDirection()
    {
        var (a, b) = KeyDerivation.SessionKeys(Key, KeyDerivation.NewNonce(), KeyDerivation.NewNonce());

        Assert.NotEqual(a, b);
        Assert.Equal(32, a.Length);
    }
}

public class FramingTests
{
    [Fact]
    public async Task WriteThenRead_RoundTrips()
    {
        var buffer = new MemoryStream();
        var writer = new FrameStream(buffer);
        await writer.WriteAsync("one"u8.ToArray());
        await writer.WriteAsync(Array.Empty<byte>());
        await writer.WriteAsync("three"u8.ToArray());

        buffer.Position = 0;
        var reader = new FrameStream(buffer);
        Assert.Equal("one"u8.ToArray(), await reader.ReadAsync());
        Assert.Equal(Array.Empty<byte>(), await reader.ReadAsync());
        Assert.Equal("three"u8.ToArray(), await reader.ReadAsync());
        Assert.Null(await reader.ReadAsync());
    }

    [Fact]
    public async Task Read_OversizedLength_Throws()
    {
        var reader = new FrameStream(new MemoryStream([0x7F, 0xFF, 0xFF, 0xFF]));

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Read_EndsMidFrame_Throws()
    {
        var reader = new FrameStream(new MemoryStream([0, 0, 0, 5, 1, 2]));

        await Assert.ThrowsAsync<EndOfStreamException>(() => reader.ReadAsync());
    }

    [Fact]
    public async Task Write_OversizedPayload_Throws()
    {
        var writer = new FrameStream(new MemoryStream());

        await Assert.ThrowsAsync<InvalidDataException>(() => writer.WriteAsync(new byte[FrameStream.MaxFrameLength + 1]));
    }
}

public class MessageTests
{
    public static TheoryData<PeerMessage> Messages() =>
    [
        new HelloMessage("Pixel", "android", 1),
        new PingMessage(),
        new DeviceListMessage([new PeerDevice("AA:BB:CC:DD:EE:01", "Buds", true, true, false)]),
        new StartHandoffMessage("AA:BB:CC:DD:EE:01", HandoffDirection.ToComputer),
        new PrepareMessage("AA:BB:CC:DD:EE:01", HandoffDirection.ToPhone),
        new ConnectAudioMessage("AA:BB:CC:DD:EE:01"),
        new ReleaseAudioMessage("AA:BB:CC:DD:EE:01"),
        new HandoffProgressMessage("AA:BB:CC:DD:EE:01", HandoffDirection.ToPhone, HandoffStage.Connecting, "Connecting…"),
        new ReplyMessage(7, CommandStatus.Rejected, "nope", HandoffOutcome.RolledBack),
    ];

    [Theory]
    [MemberData(nameof(Messages))]
    public void EncodeDecode_RoundTripsEveryType(PeerMessage message)
    {
        var decoded = MessageCodec.Decode(MessageCodec.Encode(message with { Id = 5 }));

        Assert.NotNull(decoded);
        Assert.Equal(message.GetType(), decoded.GetType());
        Assert.Equal(5, decoded.Id);
        if (message is DeviceListMessage list)
        {
            Assert.Equal(list.Devices, ((DeviceListMessage)decoded).Devices);
        }
        else
        {
            Assert.Equal(message with { Id = 5 }, decoded);
        }
    }

    [Fact]
    public void Encode_UsesReadableJson()
    {
        var json = System.Text.Encoding.UTF8.GetString(MessageCodec.Encode(new PrepareMessage("AA", HandoffDirection.ToPhone) { Id = 3 }));

        Assert.Contains("\"type\":\"prepare\"", json);
        Assert.Contains("\"direction\":\"ToPhone\"", json);
    }

    [Fact]
    public void Decode_UnknownType_ReturnsNull() =>
        Assert.Null(MessageCodec.Decode("""{"type":"fromTheFuture","id":1}"""u8));

    [Fact]
    public void Decode_Garbage_ReturnsNull() => Assert.Null(MessageCodec.Decode("not json"u8));

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:01", "AA:BB:CC:DD:EE:01")]
    [InlineData(" AA-BB-CC-DD-EE-01 ", "AA:BB:CC:DD:EE:01")]
    public void NormalizeAddress_OneForm(string raw, string expected) => Assert.Equal(expected, MessageCodec.NormalizeAddress(raw));

    [Fact]
    public void Beacon_RoundTrips()
    {
        var beacon = new Beacon(1, "pc1", "Desk PC", 47821, true);

        Assert.Equal(beacon, Beacon.Decode(beacon.Encode()));
    }

    [Theory]
    [InlineData("""{"v":2,"id":"pc","name":"x","port":1,"pairing":false}""")]
    [InlineData("""{"v":1,"id":"","name":"x","port":1,"pairing":false}""")]
    [InlineData("""{"v":1,"id":"pc","name":"x","port":0,"pairing":false}""")]
    [InlineData("garbage")]
    public void Beacon_Invalid_Ignored(string json) => Assert.Null(Beacon.Decode(System.Text.Encoding.UTF8.GetBytes(json)));
}

public sealed class PeerStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "samaat-peers-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "phones.json");

    [Fact]
    public void AddThenReload_KeepsPeersAndSelfId()
    {
        var store = new PeerStore(FilePath);
        store.Add(new PairedPeer("p1", "Pixel", [1, 2, 3], DateTimeOffset.UnixEpoch));

        var reloaded = new PeerStore(FilePath);

        Assert.Equal(store.SelfId, reloaded.SelfId);
        var peer = Assert.Single(reloaded.Peers);
        Assert.Equal("Pixel", peer.Name);
        Assert.Equal([1, 2, 3], peer.Secret);
    }

    [Fact]
    public void Secret_IsProtectedOnDisk()
    {
        var store = new PeerStore(FilePath, new XorProtector());
        store.Add(new PairedPeer("p1", "Pixel", [1, 2, 3], DateTimeOffset.UnixEpoch));

        Assert.DoesNotContain(Convert.ToBase64String([1, 2, 3]), File.ReadAllText(FilePath));
        Assert.Equal([1, 2, 3], new PeerStore(FilePath, new XorProtector()).Peers.Single().Secret);
    }

    [Fact]
    public void Add_SameId_Replaces()
    {
        var store = new PeerStore(null);
        store.Add(new PairedPeer("p1", "Old", [1], DateTimeOffset.UnixEpoch));
        store.Add(new PairedPeer("p1", "New", [2], DateTimeOffset.UnixEpoch));

        Assert.Equal("New", Assert.Single(store.Peers).Name);
    }

    [Fact]
    public void Remove_RaisesChanged()
    {
        var store = new PeerStore(null);
        store.Add(new PairedPeer("p1", "Pixel", [1], DateTimeOffset.UnixEpoch));
        var raised = 0;
        store.Changed += (_, _) => raised++;

        Assert.True(store.Remove("p1"));
        Assert.False(store.Remove("p1"));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void UpdateEndpoint_IsSaved()
    {
        var store = new PeerStore(FilePath);
        store.Add(new PairedPeer("pc", "Desk", [1], DateTimeOffset.UnixEpoch));
        store.UpdateEndpoint("pc", "192.168.1.5:47821");

        Assert.Equal("192.168.1.5:47821", new PeerStore(FilePath).Find("pc")?.Endpoint);
    }

    [Fact]
    public void CorruptFile_StartsUnpaired_WithError()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ nope");

        var store = new PeerStore(FilePath);

        Assert.Empty(store.Peers);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void CorruptFile_IsKeptAsBad_BeforeALaterSaveOverwritesIt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ nope");

        var store = new PeerStore(FilePath);
        store.Add(new PairedPeer("p1", "Pixel", [1], DateTimeOffset.UnixEpoch));

        Assert.Equal("{ nope", File.ReadAllText(FilePath + ".bad"));
    }

    [Fact]
    public void CorruptFile_SetsLastError()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ nope");

        Assert.NotNull(new PeerStore(FilePath).LastError);
    }

    [Theory]
    [InlineData("""{"id":"b","name":"Bad","pairedAt":"1970-01-01T00:00:00+00:00"}""")]
    [InlineData("""{"id":"b","name":"Bad","secret":null,"pairedAt":"1970-01-01T00:00:00+00:00"}""")]
    [InlineData("""{"id":null,"name":"Bad","secret":"AQID","pairedAt":"1970-01-01T00:00:00+00:00"}""")]
    public void BadEntry_IsSkipped_AndTheGoodOneStillLoads(string bad)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{"selfId":"me","peers":[""" + bad + ""","""
            + """{"id":"a","name":"Good","secret":"AQID","pairedAt":"1970-01-01T00:00:00+00:00"}]}""");

        var store = new PeerStore(FilePath);

        var peer = Assert.Single(store.Peers);
        Assert.Equal("Good", peer.Name);
        Assert.Equal("me", store.SelfId);
    }

    [Fact]
    public void ProtectorFailure_SkipsThatEntryOnly()
    {
        var good = new PeerStore(FilePath);
        good.Add(new PairedPeer("a", "Good", [1], DateTimeOffset.UnixEpoch));
        good.Add(new PairedPeer("b", "Other account", [2], DateTimeOffset.UnixEpoch));

        var store = new PeerStore(FilePath, new FailingFor(2));

        Assert.Equal("Good", Assert.Single(store.Peers).Name);
        Assert.NotNull(store.LastError);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FailingFor(byte first) : ISecretProtector
    {
        public byte[] Protect(byte[] secret) => secret;

        public byte[] Unprotect(byte[] stored) => stored[0] == first ? throw new InvalidOperationException("no key") : stored;
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] secret) => [.. secret.Select(b => (byte)(b ^ 0x5A))];

        public byte[] Unprotect(byte[] stored) => Protect(stored);
    }
}
