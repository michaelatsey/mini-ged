using System.Security.Cryptography;
using System.Text;

namespace Ged.Features.Tests;

/// <summary>
/// Fixed values shared by the tests. The instants are constants rather than
/// <c>DateTimeOffset.UtcNow</c> so assertions can compare exactly — which is why the handlers take
/// their instant from <see cref="IClock"/> instead of reading the wall clock themselves.
/// </summary>
internal static class Fixed
{
    public static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset Later = Now.AddDays(40);
    public static readonly Actor Me = new("u-42");
    public const string By = "u-42";
}

/// <summary>
/// One piece of content, with the digest its own bytes hash to. The tests need the digest up front
/// to seed a blob the upload will resolve to — which is the whole point of content addressing.
/// </summary>
internal static class Content
{
    public static readonly byte[] Bytes = Encoding.UTF8.GetBytes("%PDF-1.7 one small invoice");

    public static string Digest => Convert.ToHexStringLower(SHA256.HashData(Bytes));

    public static BlobId Id => BlobId.FromSha256(Digest);

    public static Stream Open() => new MemoryStream(Bytes);
}
