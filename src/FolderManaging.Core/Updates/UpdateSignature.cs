// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 maximusfabricator

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FolderManaging.Core.Updates;

/// <summary>
/// Checks <c>update.json.sig</c>: one base64 signature per line over the exact bytes of <c>update.json</c>
/// (RSA PKCS#1 v1.5 with SHA-256, or ECDSA P-256 with SHA-256). Valid when any line verifies with any trusted key.
/// Rotating the key: one release ships an app that trusts the old and the new key, the next one is signed with the
/// new key only.
/// </summary>
public static class UpdateSignature
{
    /// <summary>
    /// Public keys (SubjectPublicKeyInfo, base64) whose signatures the app accepts. The first one is the key of the
    /// release certificate <c>CN=Folder Managing</c> (thumbprint D18107EDFD3C1D13E7C56A828383F09FA12073D5) that also
    /// signs the package; its trust and expiry do not matter here.
    /// </summary>
    public static IReadOnlyList<string> TrustedKeys { get; } =
    [
        "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA6Ilr+/ywf+fLASIKUxpanu5fhSjnI2ro75NOgbTY0SzYPncmGq+hhsb3MO7HEJ3II6pF" +
        "Qn0P2rkTRnJSv8i46a06AFTo6ObVya05A615NHBK0Z2rQas6DCPABZ9lAt48zt1Z8yxZP0P+zHJLpYosrlQTbsYh9nJw7ux486DRG48A6IdMB9Oj" +
        "F7kxIFkw2n1cQEYhFnF08dfBSLa7SkpDAI50Ce/Xctw+utRfGPWYOTy3ZBqTgNZkXPv1eI+OlWPF6X9ZMvr4qGa1VUIpY0uw5mf7dLM/raNQmDn0" +
        "aznen+lwPzR7GrEr61f4nvvaLmEwF3yEv1N2D5iUFmKB3TRG+QIDAQAB",
    ];

    /// <summary>The signature file is a few lines of base64.</summary>
    public const int MaxSignatureFileBytes = 16 * 1024;

    public static bool Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureFile) =>
        Verify(manifest, signatureFile, TrustedKeys);

    public static bool Verify(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signatureFile, IReadOnlyList<string> keys)
    {
        if (signatureFile.Length is 0 or > MaxSignatureFileBytes)
            return false;

        var signatures = new List<byte[]>();
        foreach (var line in Encoding.ASCII.GetString(signatureFile).Split('\n'))
        {
            var text = line.Trim();
            if (text.Length == 0)
                continue;
            try
            {
                signatures.Add(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                // a line we do not understand cannot make the file valid
            }
        }

        foreach (var key in keys)
        {
            PublicKey publicKey;
            try
            {
                publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                continue;
            }

            using var rsa = publicKey.GetRSAPublicKey();
            using var ecdsa = rsa is null ? publicKey.GetECDsaPublicKey() : null;
            foreach (var signature in signatures)
            {
                try
                {
                    if (rsa is not null && rsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                        return true;
                    if (ecdsa is not null && ecdsa.VerifyData(manifest, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                        return true;
                }
                catch (CryptographicException)
                {
                    // wrong length for this key
                }
            }
        }

        return false;
    }
}
