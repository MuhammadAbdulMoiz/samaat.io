using System.Text.Json;
using Samaat.Handoff.Protocol;
using Samaat.Core.Diagnostics;
using Samaat.Core.Settings;

namespace Samaat.Handoff.Storage;

/// <summary>Protects stored pairing secrets at rest. Windows uses DPAPI; Android relies on the app's private storage.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] secret);

    /// <exception cref="System.Security.Cryptography.CryptographicException">The data was protected elsewhere (another user or machine).</exception>
    byte[] Unprotect(byte[] stored);
}

/// <summary>Stores secrets as they are, for platforms where the file is already private to the app.</summary>
public sealed class PlainSecretProtector : ISecretProtector
{
    public static PlainSecretProtector Instance { get; } = new();

    public byte[] Protect(byte[] secret) => secret;

    public byte[] Unprotect(byte[] stored) => stored;
}

/// <summary>A device this one is paired with.</summary>
/// <param name="Endpoint">Where the peer was last reached ("192.168.1.20:47821"); only the phone uses it.</param>
public sealed record PairedPeer(string Id, string Name, byte[] Secret, DateTimeOffset PairedAt, string? Endpoint = null);

public sealed record PeerStoreEntry(string Id, string Name, byte[] Secret, DateTimeOffset PairedAt, string? Endpoint = null);

public sealed record PeerStoreFile(string SelfId, IReadOnlyList<PeerStoreEntry> Peers);

/// <summary>
/// This device's own id plus the devices it's paired with, saved as JSON. Like <c>SettingsStore</c>, nothing here throws:
/// an unreadable file means starting unpaired, and failures land in <see cref="LastError"/>.
/// </summary>
public sealed class PeerStore
{
    private readonly string? _path;
    private readonly ISecretProtector _protector;
    private readonly Lock _gate = new();
    private List<PairedPeer> _peers = [];

    /// <param name="path">Null keeps everything in memory (tests, simulator).</param>
    public PeerStore(string? path, ISecretProtector? protector = null)
    {
        _path = path;
        _protector = protector ?? PlainSecretProtector.Instance;
        SelfId = Guid.NewGuid().ToString("N");
        Load();
    }

    public static string DefaultComputerPath { get; } = Path.Combine(AppDataFolder.Path, "phones.json");

    /// <summary>This device's id, created on first run and kept.</summary>
    public string SelfId { get; private set; }

    public string? LastError { get; private set; }

    public IReadOnlyList<PairedPeer> Peers
    {
        get
        {
            lock (_gate)
            {
                return [.. _peers];
            }
        }
    }

    /// <summary>Raised after a peer is added or removed. Any thread.</summary>
    public event EventHandler? Changed;

    public PairedPeer? Find(string id)
    {
        lock (_gate)
        {
            return _peers.FirstOrDefault(p => p.Id == id);
        }
    }

    /// <summary>Adds or replaces (by id) a peer and saves.</summary>
    public void Add(PairedPeer peer)
    {
        lock (_gate)
        {
            _peers = [.. _peers.Where(p => p.Id != peer.Id), peer];
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remembers where a peer was reached, without raising <see cref="Changed"/>.</summary>
    public void UpdateEndpoint(string id, string endpoint)
    {
        lock (_gate)
        {
            var index = _peers.FindIndex(p => p.Id == id);
            if (index < 0 || _peers[index].Endpoint == endpoint)
            {
                return;
            }

            _peers[index] = _peers[index] with { Endpoint = endpoint };
            Save();
        }
    }

    public bool Remove(string id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _peers.RemoveAll(p => p.Id == id) > 0;
            if (removed)
            {
                Save();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            Save();
            return;
        }

        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(_path), HandoffJson.Default.PeerStoreFile)
                ?? throw new JsonException("empty");
            SelfId = string.IsNullOrWhiteSpace(file.SelfId) ? SelfId : file.SelfId;
            var peers = new List<PairedPeer>();
            foreach (var entry in file.Peers ?? [])
            {
                try
                {
                    if (entry is not { Id: not null, Secret: not null })
                    {
                        throw new InvalidDataException("incomplete entry");
                    }

                    peers.Add(new PairedPeer(entry.Id, entry.Name ?? "", _protector.Unprotect(entry.Secret), entry.PairedAt, entry.Endpoint));
                }
                catch (Exception e)
                {
                    // Only the entry's name and the failure kind are logged, never its secret.
                    LastError = $"The pairing with {entry?.Name} can't be read on this account; pair it again.";
                    SamaatLog.Warn("Phone link", $"Skipped a stored pairing ({e.GetType().Name})");
                }
            }

            _peers = peers;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            LastError = $"Paired devices could not be read ({e.Message}); starting unpaired.";
            SamaatLog.Warn("Phone link", LastError, e);
            TryKeepCopy();
        }
    }

    // A later save would overwrite the unreadable file; keep it so the pairings can be recovered by hand.
    private void TryKeepCopy()
    {
        try
        {
            File.Copy(_path!, _path + ".bad", overwrite: true);
        }
        catch (Exception)
        {
            // Best effort; the load already failed.
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        var temp = _path + ".tmp";
        try
        {
            if (Path.GetDirectoryName(_path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            var file = new PeerStoreFile(SelfId, [.. _peers.Select(p => new PeerStoreEntry(p.Id, p.Name, _protector.Protect(p.Secret), p.PairedAt, p.Endpoint))]);
            WriteOwnerOnly(temp, JsonSerializer.Serialize(file, HandoffJson.Default.PeerStoreFile));
            File.Move(temp, _path, overwrite: true);
            LastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            LastError = $"Paired devices could not be saved: {e.Message}";
            SamaatLog.Warn("Phone link", LastError, e);
        }
    }

    /// <summary>
    /// Outside Windows the secrets are stored as they are (<see cref="PlainSecretProtector"/>), so the file is created
    /// readable and writable by its owner only (0600). The temp file is new each time, and the move keeps its mode.
    /// </summary>
    private static void WriteOwnerOnly(string path, string text)
    {
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(path, text);
            return;
        }

        File.Delete(path);
        using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }
}
