namespace ThreeByThree.Centar.Scoreboard.Infrastructure.Persistence;

public sealed record GameStoragePaths(
    string ActiveDirectory,
    string CompletedGamesDirectory)
{
    private const string ProductDataDirectoryName = "3x3 Centar Scoreboard";
    private const string LegacyDataDirectoryName = "3x3 Trebinje Scoreboard";

    public static GameStoragePaths ForCurrentUser()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var documents = Environment.GetFolderPath(
            Environment.SpecialFolder.MyDocuments);
        var localDataRoot = ResolveDataRoot(localApplicationData);
        var documentsRoot = ResolveDataRoot(documents);

        return new GameStoragePaths(
            Path.Combine(localDataRoot, "ActiveGame"),
            Path.Combine(documentsRoot, "Games"));
    }

    private static string ResolveDataRoot(string parentDirectory)
    {
        var renamedRoot = Path.Combine(parentDirectory, ProductDataDirectoryName);
        var legacyRoot = Path.Combine(parentDirectory, LegacyDataDirectoryName);

        return Directory.Exists(renamedRoot) || !Directory.Exists(legacyRoot)
            ? renamedRoot
            : legacyRoot;
    }
}
