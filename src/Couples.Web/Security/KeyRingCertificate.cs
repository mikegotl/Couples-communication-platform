using System.Security.Cryptography.X509Certificates;

namespace Couples.Web.Security;

/// <summary>Loads the certificate that encrypts the Data Protection key ring.</summary>
public static class KeyRingCertificate
{
    /// <summary>
    /// Reads a PKCS#12 file. Accepts the raw binary file, or the same bytes base64-encoded, which is
    /// what Azure Key Vault returns for a certificate's secret and so what a Container Apps secret
    /// volume contains.
    /// </summary>
    public static X509Certificate2 Load(string path, string? password)
    {
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.ASCII.GetString(bytes).Trim();
        var raw = new byte[text.Length];
        if (bytes.Length > 0 && bytes.All(b => b < 0x80) && Convert.TryFromBase64String(text, raw, out var written))
        {
            bytes = raw[..written];
        }

        return X509CertificateLoader.LoadPkcs12(bytes, password);
    }
}
