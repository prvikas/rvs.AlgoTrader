using System.Security.Cryptography;
using System.Text;
using rvs.AlgoTrader.Application.Services;
using Microsoft.Extensions.Configuration;
namespace rvs.AlgoTrader.Infrastructure.Services;

public sealed class FieldEncryptionService : IFieldEncryptionService
{
    private readonly byte[] _key;

    public FieldEncryptionService(IConfiguration config)
    {
        var keyStr = config["FieldEncryption:Key"] ?? throw new InvalidOperationException("FieldEncryption:Key not configured");
        _key = Convert.FromBase64String(keyStr);
        if (_key.Length != 32) throw new InvalidOperationException("FieldEncryption:Key must be 32 bytes (256-bit) base64");
    }


    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        var fullBytes = Convert.FromBase64String(ciphertext);
        using var aes = Aes.Create();
        aes.Key = _key;
        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullBytes.Length - iv.Length];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullBytes, iv.Length, cipher, 0, cipher.Length);
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
