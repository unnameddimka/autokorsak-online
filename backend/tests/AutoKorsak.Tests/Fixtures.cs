using AutoKorsak.Domain;
using AutoKorsak.LegacyXml;

namespace AutoKorsak.Tests;

/// <summary>
/// Locates the legacy XML fixtures at their canonical repository paths. The primary fixture is
/// legacy-autokorsak/А_11.12.21.xml (first character: Cyrillic capital А, U+0410).
/// </summary>
public static class Fixtures
{
    public const string ReferenceRelativePath = "legacy-autokorsak/А_11.12.21.xml";
    public const string ByeRelativePath = "legacy-autokorsak/Б_12.12.2021.xml";
    public const string AvailabilityRelativePath = "legacy-autokorsak/13x13.xml";
    public const string OddPlayersRelativePath = "legacy-autokorsak/9х9 5ть турів ПІДСУМОК.xml";

    public static readonly string[] All = [ReferenceRelativePath, ByeRelativePath, AvailabilityRelativePath, OddPlayersRelativePath];

    public static string RepositoryRoot
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("AUTOKORSAK_REPO_ROOT");
            if (!string.IsNullOrEmpty(env)) return env;
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName, "legacy-autokorsak"))) return dir.FullName;
            throw new InvalidOperationException(
                $"Repository root not found: expected a parent directory containing '{ReferenceRelativePath}'. " +
                "Set AUTOKORSAK_REPO_ROOT to the repository root.");
        }
    }

    public static byte[] Read(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot, relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required legacy XML fixture is missing. Expected it at '{relativePath}' (repository root: {RepositoryRoot}).", path);
        return File.ReadAllBytes(path);
    }

    public static Tournament Import(string relativePath = ReferenceRelativePath) =>
        LegacyXmlImporter.Import(Read(relativePath), DateTimeOffset.UnixEpoch);

    public static Participant ByName(this Tournament t, string displayName) =>
        t.Participants.Single(p => p.DisplayName == displayName);
}
