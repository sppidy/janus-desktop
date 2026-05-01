// Centralised TLS-trust policy for outbound HTTP/WS to the trading backends.
//
// History: this client originally accepted ANY certificate (`(_, _, _, _) => true`)
// because the backend uses a self-signed cert intended for private-network use.
// That made every connection MITM-able by anyone on the same network.
//
// New policy:
//   1. Cert chains that validate cleanly (real CA-signed) → trusted.
//   2. Otherwise, if env var TRADER_BACKEND_CERT_THUMBPRINT is set, the
//      certificate's SHA-1 thumbprint must equal it (case-insensitive,
//      ignoring colons / spaces). This pins the self-signed cert safely.
//   3. Otherwise, if TRADER_ALLOW_INSECURE_TLS=1 is set, trust anyway
//      (legacy behaviour — only safe on a fully-controlled private network).
//   4. Otherwise → reject.

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Janus.Desktop.Services;

public static class TlsTrust
{
    public static bool Validate(
        object sender,
        X509Certificate? cert,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;

        if (cert is not null)
        {
            var pinned = Environment.GetEnvironmentVariable("TRADER_BACKEND_CERT_THUMBPRINT");
            if (!string.IsNullOrWhiteSpace(pinned))
            {
                var actual = (cert as X509Certificate2)?.Thumbprint ?? cert.GetCertHashString();
                if (Normalize(actual).Equals(Normalize(pinned), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (Environment.GetEnvironmentVariable("TRADER_ALLOW_INSECURE_TLS") == "1")
            return true;

        return false;
    }

    public static RemoteCertificateValidationCallback MakeCallback() => Validate;

    private static string Normalize(string s)
        => s.Replace(":", string.Empty).Replace(" ", string.Empty);
}
