using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Math;

namespace UnMessage;

public sealed class IdentityCrypto
{
    public byte[] IdentityPrivateKey { get; }
    public byte[] IdentityPublicKey { get; }
    public byte[] SignedPreKeyPrivateKey { get; }
    public byte[] SignedPreKeyPublicKey { get; }
    public byte[] SignedPreKeySignature { get; }

    public IdentityCrypto()
    {
        var random = new SecureRandom();

        IdentityPrivateKey = new byte[32];
        random.NextBytes(IdentityPrivateKey);
        var idPriv = new Ed25519PrivateKeyParameters(IdentityPrivateKey, 0);
        IdentityPublicKey = idPriv.GeneratePublicKey().GetEncoded();

        var spkPriv = new X25519PrivateKeyParameters(random);
        SignedPreKeyPrivateKey = spkPriv.GetEncoded();
        SignedPreKeyPublicKey = spkPriv.GeneratePublicKey().GetEncoded();

        SignedPreKeySignature = SignRaw(IdentityPrivateKey, SignedPreKeyPublicKey);
    }

    public static string CreateIdentityId(byte[] identityPublicKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(identityPublicKey);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static byte[] SignRaw(byte[] privateKeySeed, byte[] data)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(privateKeySeed, 0));
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    public static bool VerifyRaw(byte[] publicKey, byte[] data, byte[] signature)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(signature);
    }

    public static byte[] DeriveSharedKeyX25519(byte[] privateSeed, byte[] peerPublic)
    {
        var priv = new X25519PrivateKeyParameters(privateSeed, 0);
        var pub = new X25519PublicKeyParameters(peerPublic, 0);
        var shared = new byte[32];
        priv.GenerateSecret(pub, shared, 0);
        return System.Security.Cryptography.SHA256.HashData(shared);
    }

    public static (byte[] privateKey, byte[] publicKey) GenerateX25519KeyPair()
    {
        var random = new SecureRandom();
        var privObj = new X25519PrivateKeyParameters(random);
        var priv = privObj.GetEncoded();
        var pubObj = privObj.GeneratePublicKey();
        return (priv, pubObj.GetEncoded());
    }
}
