using System.Security.Cryptography;
using System.Text;

namespace HackITSentry.Server.Services;

public class LicenseEncryptionService
{
    private readonly byte[] _key;

    public LicenseEncryptionService(IConfiguration configuration)
    {
        var keyString = configuration["Encryption:Key"]!;
        // Ensure exactly 32 bytes for AES-256
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Prepend IV to ciphertext, encode as Base64
        var result = new byte[aes.IV.Length + ciphertext.Length];
        aes.IV.CopyTo(result, 0);
        ciphertext.CopyTo(result, aes.IV.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherBase64)
    {
        var data = Convert.FromBase64String(cipherBase64);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = data[..16];
        var ciphertext = data[16..];
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return Encoding.UTF8.GetString(plaintext);
    }
}
