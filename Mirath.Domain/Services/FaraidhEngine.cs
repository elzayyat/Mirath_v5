using Mirath.Shared.Entities;
using Mirath.Shared.Enums;

namespace Mirath.Domain.Services;

public interface IFaraidhEngine
{
    Task<CalculationResult> CalculateAsync(
        List<Heir> heirs,
        decimal netEstate,
        Mirath.Domain.Enums.Madhab madhab,
        CancellationToken cancellationToken = default);
}

public record CalculationResult(
    List<HeirShareResult> Shares,
    List<CalculationStepResult> Steps,
    bool IsAwlApplied,
    bool IsRaddApplied,
    double TotalRatio);

public record HeirShareResult(
    HeirType HeirType,
    string HeirName,
    string Fraction,
    double Ratio,
    decimal Amount,
    string ShareType,
    string Explanation);

public record CalculationStepResult(
    int StepOrder,
    string Title,
    string Description,
    double? BeforeValue,
    double? AfterValue);

public class FaraidhEngine : IFaraidhEngine
{
    private readonly IRuleEngine _ruleEngine;

    public FaraidhEngine() : this(new RuleEngine())
    {
    }

    public FaraidhEngine(IRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    public Task<CalculationResult> CalculateAsync(
        List<Heir> heirs,
        decimal netEstate,
        Mirath.Domain.Enums.Madhab madhab,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = _ruleEngine.Calculate(heirs, netEstate, madhab);
        var shares = result.Shares
            .Where(x => x.Ratio > 0m)
            .Select(x => new HeirShareResult(
                x.HeirType,
                x.HeirName,
                x.Fraction,
                (double)x.Ratio,
                x.Amount,
                x.ShareType,
                x.Explanation))
            .ToList();

        return Task.FromResult(new CalculationResult(
            shares,
            result.Steps.ToList(),
            result.IsAwlApplied,
            result.IsRaddApplied,
            (double)result.TotalRatio));
    }
}
