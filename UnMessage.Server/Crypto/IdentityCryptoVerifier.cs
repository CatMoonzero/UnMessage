using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace UnMessage.Server;

internal static class IdentityCryptoVerifier
{
    public static bool VerifyRaw(byte[] publicKey, byte[] data, byte[] signature)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
        verifier.BlockUpdate(data, 0, data.Length);
        return verifier.VerifySignature(signature);
    }
}
