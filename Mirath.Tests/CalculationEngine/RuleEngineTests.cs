using Mirath.Domain.Enums;
using Mirath.Domain.Services;
using Mirath.Shared.Entities;
using Mirath.Shared.Enums;

namespace Mirath.Tests.CalculationEngine;

public class RuleEngineTests
{
    private readonly RuleEngine _engine = new();

    [Fact]
    public void Calculate_BlocksSiblingsBeforeShareCalculation()
    {
        var result = _engine.Calculate(new[]
        {
            Heir(HeirType.Son),
            Heir(HeirType.FullBrother)
        }, 120_000m, Madhab.Hanafi);

        var brother = result.Shares.Single(x => x.HeirType == HeirType.FullBrother);
        Assert.Equal(0m, brother.Ratio);
        Assert.Equal("Blocked", brother.ShareType);
        Assert.Equal("son", brother.BlockedBy);
    }

    [Fact]
    public void Calculate_DistributesResidueBetweenSonsAndDaughtersTwoToOne()
    {
        var result = _engine.Calculate(new[]
        {
            Heir(HeirType.Husband),
            Heir(HeirType.Son, count: 2),
            Heir(HeirType.Daughter)
        }, 100_000m, Madhab.Hanafi);

        AssertShare(result, HeirType.Husband, 1m / 4m);
        AssertShare(result, HeirType.Son, 3m / 5m);
        AssertShare(result, HeirType.Daughter, 3m / 20m);
        Assert.Equal(1m, Math.Round(result.TotalRatio, 8));
    }

    [Fact]
    public void Calculate_AppliesAwlWhenFixedSharesExceedEstate()
    {
        var result = _engine.Calculate(new[]
        {
            Heir(HeirType.Husband),
            Heir(HeirType.Mother),
            Heir(HeirType.Daughter, count: 2)
        }, 100_000m, Madhab.Hanafi);

        Assert.True(result.IsAwlApplied);
        Assert.Equal(1m, Math.Round(result.TotalRatio, 8));
    }

    [Fact]
    public void Calculate_AppliesRaddToBloodHeirsNotSpouse()
    {
        var result = _engine.Calculate(new[]
        {
            Heir(HeirType.Wife),
            Heir(HeirType.Mother),
            Heir(HeirType.Daughter)
        }, 100_000m, Madhab.Hanafi);

        Assert.True(result.IsRaddApplied);
        AssertShare(result, HeirType.Wife, 1m / 8m);
        Assert.Equal(1m, Math.Round(result.TotalRatio, 8));
    }

    [Fact]
    public void Calculate_HanafiGrandfatherBlocksFullBrotherButShafiiDoesNot()
    {
        var hanafi = _engine.Calculate(new[]
        {
            Heir(HeirType.GrandFather),
            Heir(HeirType.FullBrother)
        }, 100_000m, Madhab.Hanafi);

        var shafii = _engine.Calculate(new[]
        {
            Heir(HeirType.GrandFather),
            Heir(HeirType.FullBrother)
        }, 100_000m, Madhab.Shafii);

        Assert.DoesNotContain(hanafi.Shares, x => x.HeirType == HeirType.FullBrother && x.Ratio > 0m);
        Assert.Contains(shafii.Shares, x => x.HeirType == HeirType.FullBrother && x.Ratio > 0m);
    }

    [Fact]
    public void CoverageSnapshot_IsComputedFromRuleDatabaseCounts()
    {
        var result = _engine.Calculate(new[] { Heir(HeirType.Daughter) }, 100_000m, Madhab.Hanafi);

        Assert.Equal(22, result.Coverage.SupportedHeirs);
        Assert.Equal(5, result.Coverage.SupportedMadhhabs);
        Assert.True(result.Coverage.BlockingRules > 0);
        Assert.True(result.Coverage.FixedShareRules > 0);
    }

    private static Heir Heir(HeirType type, int count = 1) => new()
    {
        Type = type,
        Name = type.ToString(),
        Count = count
    };

    private static void AssertShare(RuleCalculationResult result, HeirType type, decimal expected)
    {
        var share = result.Shares.Single(x => x.HeirType == type);
        Assert.Equal(expected, share.Ratio, 8);
    }
}
