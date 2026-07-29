using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class MatchMetadataTests
{
    [TestMethod]
    [DataRow(GameType.Group, "", "GROUP")]
    [DataRow(GameType.Group, "20", "GROUP 20")]
    [DataRow(GameType.Group, " z ", "GROUP Z")]
    [DataRow(GameType.Qualifier, "A", "QUALIFIER")]
    [DataRow(GameType.Quarterfinal, "A", "QUARTERFINAL")]
    [DataRow(GameType.Semifinal, "A", "SEMIFINAL")]
    [DataRow(GameType.Final, "A", "FINAL")]
    public void GetGameTypeLabel_GameTypeAndGroup_ReturnsExpectedLabel(
        GameType gameType,
        string group,
        string expected)
    {
        var metadata = new MatchMetadata
        {
            GameType = gameType,
            Group = group,
            Category = "Legacy category",
        };

        Assert.AreEqual(expected, metadata.GetGameTypeLabel());
    }

    [TestMethod]
    public void GetGameTypeLabel_UnspecifiedType_ReturnsLegacyCategoryLabel()
    {
        var metadata = new MatchMetadata
        {
            Category = "  under 18 women  ",
        };

        Assert.AreEqual("UNDER 18 WOMEN", metadata.GetGameTypeLabel());
        Assert.AreEqual(GameType.Unspecified, metadata.GameType);
        Assert.AreEqual(string.Empty, metadata.Group);
    }
}
