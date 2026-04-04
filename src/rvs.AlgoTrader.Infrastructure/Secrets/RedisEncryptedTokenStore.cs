using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Secrets;

/// <summary>
/// Redis-backed ITokenStore that encrypts every value with AES-256-GCM before storage.
///
/// Envelope format (base64): [12-byte nonce][16-byte GCM tag][ciphertext]
/// Key derivation: the 32-byte encryption key is read from config key "TokenStore:EncryptionKey"
///   (base64-encoded). Callers must set this via environment variable or Vault — never in
///   appsettings.json (AP-006, AP-018).
///
/// Token lifetime: SetAsync accepts an absolute expiry that maps directly to Redis TTL.
/// Missing/expired keys return null from GetAsync so callers trigger a fresh login.
/// </summary>
public sealed class RedisEncryptedTokenStore(
    IConnectionMultiplexer redis,
    IConfiguration config,
    ILogger<RedisEncryptedTokenStore> logger) : ITokenStore
{
    private const string ConfigKey = "TokenStore:EncryptionKey";
    private static readonly int NonceSize = AesGcm.NonceByteSizes.MaxSize;   // 12 bytes
    private static readonly int TagSize   = AesGcm.TagByteSizes.MaxSize;     // 16 bytes

    // Lazily resolved so startup does not fail if Redis is not yet available.
    private IDatabase Db => redis.GetDatabase();

    public async Task SetAsync(string key, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var ttl = expiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            logger.LogWarning("[TokenStore] SetAsync called with already-expired TTL for key={Key}", key);
            return;
        }

        var encryptionKey = ResolveKey();
        var cipherEnvelope = Encrypt(token, encryptionKey);
        await Db.StringSetAsync(key, cipherEnvelope, ttl).WaitAsync(ct);
        logger.LogDebug("[TokenStore] Stored encrypted token key={Key} ttl={Ttl}", key, ttl);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var raw = await Db.StringGetAsync(key).WaitAsync(ct);
        if (!raw.HasValue)
            return null;

        try
        {
            var encryptionKey = ResolveKey();
            return Decrypt(raw!, encryptionKey);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "[TokenStore] Decryption failed for key={Key} — token discarded", key);
            await Db.KeyDeleteAsync(key).WaitAsync(ct);
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct = default)
    {
        await Db.KeyDeleteAsync(key).WaitAsync(ct);
        logger.LogDebug("[TokenStore] Deleted token key={Key}", key);
    }

    // ── Crypto helpers ───────────────────────────────────────────────────────

    private static string Encrypt(string plaintext, byte[] key)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce      = new byte[NonceSize];
        var tag        = new byte[TagSize];
        var ciphertext = new byte[plaintextBytes.Length];

        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Envelope: nonce ‖ tag ‖ ciphertext → base64
        var envelope = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(envelope, 0);
        tag.CopyTo(envelope, NonceSize);
        ciphertext.CopyTo(envelope, NonceSize + TagSize);
        return Convert.ToBase64String(envelope);
    }

    private static string Decrypt(string envelope, byte[] key)
    {
        var bytes = Convert.FromBase64String(envelope);
        if (bytes.Length < NonceSize + TagSize)
            throw new CryptographicException("Envelope too short to contain nonce + tag.");

        var nonce      = bytes.AsSpan(0, NonceSize);
        var tag        = bytes.AsSpan(NonceSize, TagSize);
        var ciphertext = bytes.AsSpan(NonceSize + TagSize);
        var plaintext  = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] ResolveKey()
    {
        var b64 = config[ConfigKey]
            ?? throw new InvalidOperationException(
                $"'{ConfigKey}' is not configured. Set it to a base64-encoded 32-byte AES key via environment variable.");
        var key = Convert.FromBase64String(b64);
        if (key.Length != 32)
            throw new InvalidOperationException(
                $"'{ConfigKey}' must decode to exactly 32 bytes (256 bits) for AES-256-GCM.");
        return key;
    }
}
