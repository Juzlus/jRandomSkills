using src.command;

namespace jRandomSkills.Tests;

public class VoteArgsTests
{
    [Theory]
    [InlineData("de_dust2", "de_dust2")]
    [InlineData("  DE_Mirage  ", "de_mirage")]
    [InlineData("workshop/123456789/de_cool", "workshop/123456789/de_cool")]
    [InlineData("3070284539", "3070284539")]
    [InlineData("de_nuke extra ignored", "de_nuke")]
    public void ChangeMap_AcceptsPlainMapNames(string raw, string expected)
    {
        Assert.True(VoteSystem.TryNormalizeArgs(VoteType.ChangeMap, raw, out var args));
        Assert.Equal(expected, args);
    }

    [Theory]
    [InlineData("de_dust2;quit")]
    [InlineData("de_dust2; rcon_password x")]
    [InlineData("de_dust2\nquit")]
    [InlineData("\"de_dust2\"")]
    [InlineData("../../etc/passwd")]
    [InlineData("de_dust2$(x)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ChangeMap_RejectsAnythingThatCouldChainCommands(string? raw)
    {
        Assert.False(VoteSystem.TryNormalizeArgs(VoteType.ChangeMap, raw, out var args));
        Assert.Null(args);
    }

    [Fact]
    public void ChangeMap_RejectsOverlongNames()
    {
        Assert.False(VoteSystem.TryNormalizeArgs(VoteType.ChangeMap, new string('a', 129), out _));
    }

    [Theory]
    [InlineData("5 7", "5 7")]
    [InlineData("  10   0 ", "10 0")]
    [InlineData("3 4 junk", "3 4")]
    public void SetScore_AcceptsTwoNonNegativeNumbers(string raw, string expected)
    {
        Assert.True(VoteSystem.TryNormalizeArgs(VoteType.SetScore, raw, out var args));
        Assert.Equal(expected, args);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("a b")]
    [InlineData("-1 3")]
    [InlineData("1; quit 2")]
    [InlineData("99999 1")]
    [InlineData(null)]
    public void SetScore_RejectsMalformedScores(string? raw)
    {
        Assert.False(VoteSystem.TryNormalizeArgs(VoteType.SetScore, raw, out _));
    }

    [Theory]
    [InlineData(VoteType.StartGame)]
    [InlineData(VoteType.PauseGame)]
    [InlineData(VoteType.ShuffleTeam)]
    [InlineData(VoteType.SwapTeam)]
    public void ArglessVotes_AcceptNoArguments(VoteType type)
    {
        Assert.True(VoteSystem.TryNormalizeArgs(type, null, out var args));
        Assert.Null(args);
        Assert.True(VoteSystem.TryNormalizeArgs(type, "   ", out _));
    }

    [Theory]
    [InlineData(VoteType.StartGame)]
    [InlineData(VoteType.SwapTeam)]
    public void ArglessVotes_RejectInjectedArguments(VoteType type)
    {
        Assert.False(VoteSystem.TryNormalizeArgs(type, "; quit", out _));
    }
}
