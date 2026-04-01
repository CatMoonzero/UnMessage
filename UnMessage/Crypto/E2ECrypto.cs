using System.Security.Cryptography;
using System.Text;

namespace UnMessage;

/// <summary>
/// 端对端加密核心类。
/// 使用 ECDH（椭圆曲线 Diffie-Hellman）进行密钥交换，
/// 使用 AES-256-GCM 进行对称加解密。
/// </summary>
public sealed class E2ECrypto : IDisposable
{
    private const int AesKeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>本地 ECDH 密钥对（NIST P-256 曲线）。</summary>
    private readonly ECDiffieHellman _ecdh;

    /// <summary>双方协商出的共享密钥（32 字节 = AES-256）。</summary>
    private byte[]? _sharedKey;

    /// <summary>
    /// 构造函数：自动生成一对 ECDH 密钥（NIST P-256）。
    /// </summary>
    public E2ECrypto()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }

    /// <summary>共享密钥是否已建立。</summary>
    public bool IsEstablished => _sharedKey is not null;

    /// <summary>
    /// 导出本地 ECDH 公钥（SubjectPublicKeyInfo 格式），用于发送给对方。
    /// </summary>
    public byte[] GetPublicKey() => _ecdh.PublicKey.ExportSubjectPublicKeyInfo();

    /// <summary>
    /// 使用对方的公钥派生共享密钥。
    /// 内部通过 ECDH + SHA-256 哈希生成 32 字节对称密钥。
    /// </summary>
    /// <param name="otherPartyPublicKey">对方的 ECDH 公钥字节数组。</param>
    public void DeriveSharedKey(byte[] otherPartyPublicKey)
    {
        if (otherPartyPublicKey is null || otherPartyPublicKey.Length == 0)
            throw new ArgumentException("对方公钥不能为空。", nameof(otherPartyPublicKey));

        // 导入对方公钥
        using var otherEcdh = ECDiffieHellman.Create();
        otherEcdh.ImportSubjectPublicKeyInfo(otherPartyPublicKey, out _);

        // 使用 SHA-256 从 ECDH 共享秘密派生 32 字节密钥
        if (_sharedKey is not null)
            CryptographicOperations.ZeroMemory(_sharedKey);
        _sharedKey = _ecdh.DeriveKeyFromHash(otherEcdh.PublicKey, HashAlgorithmName.SHA256);
    }

    public void SetSharedKey(byte[] key)
    {
        if (key is null || key.Length != AesKeySize)
            throw new ArgumentException("共享密钥长度必须为 32 字节。", nameof(key));

        if (_sharedKey is not null)
            CryptographicOperations.ZeroMemory(_sharedKey);
        _sharedKey = key.ToArray();
    }

    /// <summary>
    /// 加密明文消息。
    /// 返回格式：nonce(12字节) + tag(16字节) + 密文。
    /// 每条消息使用独立的随机 nonce，确保相同明文产生不同密文。
    /// </summary>
    /// <param name="plaintext">待加密的明文字符串。</param>
    /// <returns>加密后的字节数组。</returns>
    public byte[] Encrypt(string plaintext)
    {
        if (_sharedKey is null)
            throw new InvalidOperationException("共享密钥尚未建立。");

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = new byte[NonceSize];
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintextBytes.Length];

        // 生成密码学安全的随机 nonce
        RandomNumberGenerator.Fill(nonce);

        // 使用 AES-256-GCM 加密
        using var aes = new AesGcm(_sharedKey, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // 拼接结果：nonce + tag + 密文
        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    /// <summary>
    /// 解密由 <see cref="Encrypt"/> 加密的数据。
    /// </summary>
    /// <param name="data">加密数据（nonce + tag + 密文）。</param>
    /// <returns>解密后的明文字符串。</returns>
    public string Decrypt(byte[] data)
    {
        if (_sharedKey is null)
            throw new InvalidOperationException("共享密钥尚未建立。");

        if (data is null || data.Length < NonceSize + TagSize)
            throw new ArgumentException("密文数据长度无效。", nameof(data));

        // 从数据中拆分 nonce、认证标签和密文
        byte[] nonce = data[..NonceSize];
        byte[] tag = data[NonceSize..(NonceSize + TagSize)];
        byte[] ciphertext = data[(NonceSize + TagSize)..];
        byte[] plaintext = new byte[ciphertext.Length];

        // 使用 AES-256-GCM 解密并验证完整性
        using var aes = new AesGcm(_sharedKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// 使用指定密钥进行 AES-256-GCM 加密（静态方法，供群聊加密使用）。
    /// 返回格式：nonce(12字节) + tag(16字节) + 密文。
    /// </summary>
    public static byte[] EncryptWithKey(byte[] key, byte[] plaintext)
    {
        if (key is null || key.Length != AesKeySize)
            throw new ArgumentException("密钥长度必须为 32 字节（AES-256）。", nameof(key));
        if (plaintext is null)
            throw new ArgumentNullException(nameof(plaintext));

        byte[] nonce = new byte[NonceSize];
        byte[] tag = new byte[TagSize];
        byte[] ciphertext = new byte[plaintext.Length];
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    /// <summary>
    /// 使用指定密钥进行 AES-256-GCM 解密（静态方法，供群聊解密使用）。
    /// </summary>
    public static byte[] DecryptWithKey(byte[] key, byte[] data)
    {
        if (key is null || key.Length != AesKeySize)
            throw new ArgumentException("密钥长度必须为 32 字节（AES-256）。", nameof(key));
        if (data is null || data.Length < NonceSize + TagSize)
            throw new ArgumentException("密文数据长度无效。", nameof(data));

        byte[] nonce = data[..NonceSize];
        byte[] tag = data[NonceSize..(NonceSize + TagSize)];
        byte[] ciphertext = data[(NonceSize + TagSize)..];
        byte[] plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    /// <summary>
    /// 释放资源，安全清零共享密钥。
    /// </summary>
    public void Dispose()
    {
        _ecdh.Dispose();
        if (_sharedKey is not null)
        {
            // 安全擦除内存中的密钥，防止被恶意读取
            CryptographicOperations.ZeroMemory(_sharedKey);
            _sharedKey = null;
        }
    }
}
