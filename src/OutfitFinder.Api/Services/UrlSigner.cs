using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace OutfitFinder.Api.Services;

/// <summary>
/// Short-lived signed links for images and PDFs, so &lt;img&gt; and download links work without auth headers.
/// The HMAC key is Api:UrlSigningKey (Key Vault in Azure, shared by all instances).
/// </summary>
public sealed class UrlSigner
{
    private readonly byte[] _key;
    private readonly string _publicBase;

    public UrlSigner(IConfiguration config, IHostEnvironment env, ILogger<UrlSigner> log)
    {
        var secret = config["Api:UrlSigningKey"];
        if (string.IsNullOrEmpty(secret))
        {
            if (!env.IsDevelopment()) throw new InvalidOperationException("Api:UrlSigningKey is required.");
            log.LogWarning("Api:UrlSigningKey not set: using a per-process key. Image links stop working after a restart.");
            _key = RandomNumberGenerator.GetBytes(32);
        }
        else _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        _publicBase = (config["Api:PublicBaseUrl"] ?? "").TrimEnd('/');
    }

    public string Sign(string path, TimeSpan lifetime)
    {
        var exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
        var sig = Compute(path, exp);
        var sep = path.Contains('?') ? '&' : '?';
        return $"{_publicBase}{path}{sep}exp={exp}&sig={sig}";
    }

    public bool Verify(string path, long exp, string? sig)
    {
        if (string.IsNullOrEmpty(sig) || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;
        var expected = Compute(path, exp);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(sig));
    }

    private string Compute(string path, long exp) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{path}|{exp}")))[..32];

    public string SearchImage(Guid searchId) => Sign($"/api/images/{searchId:N}", TimeSpan.FromHours(6));
    public string PatternPdf(Guid jobId, string kind) => Sign($"/api/patterns/{jobId:N}/pdf/{kind}", TimeSpan.FromHours(6));
}
