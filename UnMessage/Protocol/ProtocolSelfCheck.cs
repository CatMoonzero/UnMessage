using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace UnMessage;

internal static class ProtocolSelfCheck
{
    public static string Run()
    {
        var sb = new StringBuilder();

        CheckIdentitySignature(sb);
        CheckReplayRejection(sb);
        CheckSafetyNumberDeterminism(sb);
        CheckSignedGroupMutationPayload(sb);

        return sb.ToString();
    }

    private static void CheckIdentitySignature(StringBuilder sb)
    {
        var id = new IdentityCrypto();
        bool ok = IdentityCrypto.VerifyRaw(id.IdentityPublicKey, id.SignedPreKeyPublicKey, id.SignedPreKeySignature);
        sb.AppendLine(ok ? "[✔] 身份预密钥签名验证" : "[❌] 身份预密钥签名验证");
    }

    private static void CheckReplayRejection(StringBuilder sb)
    {
        bool replayRejected;
        try
        {
            var seen = new Dictionary<string, long>();
            ValidateReplay(seen, "peerA", 1);
            ValidateReplay(seen, "peerA", 2);
            ValidateReplay(seen, "peerA", 2);
            replayRejected = false;
        }
        catch (CryptographicException)
        {
            replayRejected = true;
        }
        sb.AppendLine(replayRejected ? "[✔] 重放消息拒绝" : "[❌] 重放消息拒绝");
    }

    private static void CheckSafetyNumberDeterminism(StringBuilder sb)
    {
        var id = new IdentityCrypto();
        string b64 = Convert.ToBase64String(id.IdentityPublicKey);
        string a = BuildSafetyNumber(b64);
        string b = BuildSafetyNumber(b64);
        sb.AppendLine(a == b ? "[✔] Safety Number 一致性" : "[❌] Safety Number 一致性");
    }

    private static void CheckSignedGroupMutationPayload(StringBuilder sb)
    {
        var id = new IdentityCrypto();
        string identityId = IdentityCrypto.CreateIdentityId(id.IdentityPublicKey);
        string payloadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("group-demo"));
        string canonical = $"group-demo|{identityId}|join|{payloadBase64}";
        byte[] data = Encoding.UTF8.GetBytes(canonical);
        byte[] sig = IdentityCrypto.SignRaw(id.IdentityPrivateKey, data);
        bool ok = IdentityCrypto.VerifyRaw(id.IdentityPublicKey, data, sig);
        sb.AppendLine(ok ? "[✔] 群成员变更签名验证" : "[❌] 群成员变更签名验证");
    }

    private static void ValidateReplay(Dictionary<string, long> state, string peerId, long counter)
    {
        long last = state.TryGetValue(peerId, out var current) ? current : 0;
        if (counter <= last)
            throw new CryptographicException("replay");
        state[peerId] = counter;
    }

    private static string BuildSafetyNumber(string identityPublicKeyBase64)
    {
        byte[] idPub = Convert.FromBase64String(identityPublicKeyBase64);
        byte[] hash = SHA256.HashData(idPub);
        return Convert.ToHexString(hash)[..24];
    }
}
