using ThreeByThree.Centar.Scoreboard.Domain.Models;

namespace ThreeByThree.Centar.Scoreboard.Domain.Tests;

[TestClass]
public sealed class GameGroupsTests
{
    [TestMethod]
    public void All_ContainsNumericOneThroughTwentyThenLettersAThroughZ()
    {
        string[] expected =
        [
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        ];

        CollectionAssert.AreEqual(expected, GameGroups.All);
        Assert.HasCount(46, GameGroups.All);
    }

    [TestMethod]
    [DataRow("1", true)]
    [DataRow("20", true)]
    [DataRow("A", true)]
    [DataRow("Z", true)]
    [DataRow(null, false)]
    [DataRow("", false)]
    [DataRow("0", false)]
    [DataRow("21", false)]
    [DataRow("a", false)]
    [DataRow("AA", false)]
    [DataRow(" A", false)]
    public void IsValid_BoundaryAndInvalidValues_ReturnsExpectedResult(
        string? value,
        bool expected)
    {
        Assert.AreEqual(expected, GameGroups.IsValid(value));
    }
}
