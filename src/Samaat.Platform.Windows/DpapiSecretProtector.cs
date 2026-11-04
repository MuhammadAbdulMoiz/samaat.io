using System.Security.Cryptography;
using Samaat.Handoff.Storage;

namespace Samaat.Platform.Windows;

/// <summary>Encrypts paired phones' secrets with Windows DPAPI, so phones.json is useless on another account or PC.</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = "Samaat phone link v1"u8.ToArray();

    public byte[] Protect(byte[] secret) => ProtectedData.Protect(secret, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] stored) => ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser);
}
