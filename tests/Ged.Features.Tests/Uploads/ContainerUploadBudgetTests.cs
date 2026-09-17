using System.Globalization;

namespace Ged.Features.Tests.Uploads;

/// <summary>
/// Reads <c>compose.yaml</c> and checks that the three numbers governing an upload in the container
/// agree with each other.
/// </summary>
/// <remarks>
/// <para>
/// They are written in three places and enforced in none, which is how they drifted apart: the
/// ceiling is configuration, the tmpfs is a mount option, and the memory limit belongs to the
/// orchestrator. Nothing fails at build time when a change to one makes another impossible, and the
/// symptom arrives as an OOM kill or an ENOSPC under load rather than as a failing test.
/// </para>
/// <para>
/// A test rather than a script, because the arithmetic follows from behaviour this assembly owns:
/// the form binder buffers the body to <c>/tmp</c> before a handler is reached, and
/// <see cref="StagedContent"/> then writes it there a second time. The ceiling admitted by
/// <see cref="UploadTransportLimits"/> is what both of those copies are measured against.
/// </para>
/// <para>
/// Every value is read from the <c>api</c> service rather than from the file at large. A limit
/// belonging to another service would otherwise satisfy an assertion about this one — which is the
/// same class of mistake the test exists to catch.
/// </para>
/// </remarks>
public sealed class ContainerUploadBudgetTests
{
    /// <summary>The form binder buffers the body to <c>/tmp</c>, then staging copies it beside.</summary>
    private const long CopiesPerUpload = 2;

    /// <summary>How many uploads at the ceiling the tmpfs is expected to hold at once.</summary>
    private const long ConcurrentUploads = 2;

    /// <summary>
    /// The share of the memory limit the tmpfs may take: the GC claims 75% of a cgroup limit for its
    /// heap by default, and what is left has to cover the tmpfs and the rest of the runtime.
    /// </summary>
    private const long TmpfsShareDivisor = 4;

    /// <summary>The hardening block that carries the tmpfs the staging area depends on.</summary>
    private const string HardeningAnchor = "<<: *api-hardening";

    private static readonly string[] Compose = File.ReadAllLines(RepositoryFile("compose.yaml"));

    [Fact]
    public void The_stack_states_the_upload_ceiling_it_was_sized_for()
    {
        Ceiling().ShouldNotBeNull(
            "compose.yaml must set Ged__Uploads__MaxSizeBytes on the api service: without it the "
            + "API applies the 256 MiB default from appsettings.json, which no tmpfs in this file "
            + "can hold.");
    }

    [Fact]
    public void The_api_service_takes_the_hardening_that_carries_the_tmpfs()
    {
        // The tmpfs is declared once, on an anchor, and reaches the container only through this
        // merge key. Dropped, it leaves a read_only container with nowhere to stage an upload —
        // and the sizes below would still agree with each other while every upload failed.
        ApiService().Any(line => line.Trim() == HardeningAnchor).ShouldBeTrue(
            $"The api service no longer merges {HardeningAnchor}, so it has no /tmp to stage in.");
    }

    [Fact]
    public void The_tmpfs_holds_the_uploads_the_transport_admits()
    {
        var ceiling = Ceiling().ShouldNotBeNull();

        // The transport admits the ceiling plus the multipart envelope, and the form buffer holds
        // all of it. Staging refuses at the ceiling itself — but only after writing that much.
        var perUpload = (CopiesPerUpload * ceiling) + UploadTransportLimits.MultipartEnvelopeBytes;

        TmpfsBytes().ShouldBeGreaterThanOrEqualTo(
            ConcurrentUploads * perUpload,
            "An upload lands in /tmp twice — the form buffer, then the staged copy — so a tmpfs "
            + "that does not hold both copies of every upload in flight turns the largest accepted "
            + "one into an ENOSPC.");
    }

    [Fact]
    public void The_tmpfs_leaves_the_runtime_room_under_the_memory_limit()
    {
        TmpfsBytes().ShouldBeLessThanOrEqualTo(
            MemoryLimitBytes() / TmpfsShareDivisor,
            "tmpfs pages are charged to the container's memory limit, so a tmpfs sized like the "
            + "limit is an OOM kill one upload away — and it takes every in-flight request with it.");
    }

    /// <summary>The upload ceiling the api service states, or <c>null</c>.</summary>
    private static long? Ceiling()
    {
        var value = Value("Ged__Uploads__MaxSizeBytes:");

        return value is null ? null : long.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>The size of the <c>/tmp</c> tmpfs, in bytes.</summary>
    private static long TmpfsBytes()
    {
        var mount = Compose
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("- /tmp:", StringComparison.Ordinal))
            .ShouldNotBeNull("compose.yaml no longer mounts a tmpfs on /tmp; uploads need one.");

        var size = mount.IndexOf("size=", StringComparison.Ordinal);

        size.ShouldBeGreaterThan(-1, "An unsized tmpfs may grow to half the host's memory.");

        return ParseSize(mount[(size + "size=".Length)..]);
    }

    /// <summary>The memory limit the api service runs under, in bytes.</summary>
    private static long MemoryLimitBytes() =>
        ParseSize(Value("memory:").ShouldNotBeNull("The api service states no memory limit."));

    /// <summary>The value of a <c>key: value</c> line of the api service, commented ones aside.</summary>
    private static string? Value(string key) => ApiService()
        .Select(line => line.Trim())
        .Where(line => line.StartsWith(key, StringComparison.Ordinal))
        .Select(line => line[key.Length..].Trim().Trim('"'))
        .FirstOrDefault();

    /// <summary>The lines of the <c>api</c> service, by indentation.</summary>
    private static string[] ApiService()
    {
        var start = Array.FindIndex(Compose, line => line.Trim() == "api:");

        start.ShouldBeGreaterThan(-1, "compose.yaml no longer declares an api service.");

        var indent = Indentation(Compose[start]);

        return Compose.Skip(start + 1)
            .TakeWhile(line => line.Trim().Length == 0 || Indentation(line) > indent)
            .ToArray();
    }

    /// <summary>How deep a line is nested.</summary>
    private static int Indentation(string line) => line.Length - line.TrimStart().Length;

    /// <summary>Reads a Compose size — a number, optionally suffixed with k, m or g.</summary>
    private static long ParseSize(string value)
    {
        var digits = value.TakeWhile(char.IsAsciiDigit).Count();
        var number = long.Parse(value[..digits], CultureInfo.InvariantCulture);

        return digits == value.Length
            ? number
            : char.ToLowerInvariant(value[digits]) switch
            {
                'k' => number * 1024,
                'm' => number * 1024 * 1024,
                'g' => number * 1024 * 1024 * 1024,
                _ => number,
            };
    }

    /// <summary>Locates a file at the repository root, walking up from the test binaries.</summary>
    private static string RepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MiniGed.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("The repository root was not found above the test binaries.");

        return Path.Combine(directory.FullName, name);
    }
}
