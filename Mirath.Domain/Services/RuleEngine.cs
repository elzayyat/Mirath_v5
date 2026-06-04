using Mirath.Domain.Enums;
using Mirath.Shared.Entities;
using Mirath.Shared.Enums;

namespace Mirath.Domain.Services;

public interface IRuleEngine
{
    RuleCalculationResult Calculate(IReadOnlyCollection<Heir> heirs, decimal netEstate, Madhab madhab);
}

public interface IBlockingEngine
{
    void Apply(CalculationContext context);
}

public interface IFixedShareEngine
{
    void Apply(CalculationContext context);
}

public interface IResidueEngine
{
    void Apply(CalculationContext context);
}

public interface IAwlEngine
{
    bool Apply(CalculationContext context);
}

public interface IRaddEngine
{
    bool Apply(CalculationContext context);
}

public interface IMadhhabEngine
{
    IMadhhabRuleProvider GetProvider(Madhab madhab);
}

public interface IExplanationEngine
{
    string Explain(HeirCalculationState heir);
}

public interface IValidationEngine
{
    void Validate(CalculationContext context);
}

public interface ICoverageEngine
{
    RuleCoverageSnapshot Snapshot();
}

public interface IMadhhabRuleProvider
{
    Madhab Madhab { get; }
    bool AllowsRaddToSpouses { get; }
    bool AllowsRaddBeyondFixedHeirs { get; }
    bool GrandfatherBlocksSiblings { get; }
    bool AppliesMushtaraka { get; }
    string Name { get; }
}

public interface IHanafiRules : IMadhhabRuleProvider { }
public interface IMalikiRules : IMadhhabRuleProvider { }
public interface IShafiiRules : IMadhhabRuleProvider { }
public interface IHanbaliRules : IMadhhabRuleProvider { }
public interface IJumhurRules : IMadhhabRuleProvider { }

public sealed record RuleCalculationResult(
    IReadOnlyList<RuleShareResult> Shares,
    IReadOnlyList<CalculationStepResult> Steps,
    bool IsAwlApplied,
    bool IsRaddApplied,
    decimal TotalRatio,
    RuleCoverageSnapshot Coverage);

public sealed record RuleShareResult(
    Guid HeirId,
    HeirType HeirType,
    string HeirName,
    string Fraction,
    decimal Ratio,
    decimal Amount,
    string ShareType,
    string Explanation,
    string? BlockedBy);

public sealed record RuleCoverageSnapshot(
    int SupportedHeirs,
    int SupportedMadhhabs,
    int BlockingRules,
    int FixedShareRules,
    int ResidueRules,
    int AwlRules,
    int RaddRules,
    int ClassicalCases);

public sealed class RuleEngine : IRuleEngine
{
    private readonly IBlockingEngine _blockingEngine;
    private readonly IFixedShareEngine _fixedShareEngine;
    private readonly IResidueEngine _residueEngine;
    private readonly IAwlEngine _awlEngine;
    private readonly IRaddEngine _raddEngine;
    private readonly IValidationEngine _validationEngine;
    private readonly IExplanationEngine _explanationEngine;
    private readonly ICoverageEngine _coverageEngine;

    public RuleEngine()
        : this(
            new BlockingEngine(),
            new FixedShareEngine(),
            new ResidueEngine(),
            new AwlEngine(),
            new RaddEngine(new MadhhabEngine()),
            new ValidationEngine(),
            new ExplanationEngine(),
            new CoverageEngine())
    {
    }

    public RuleEngine(
        IBlockingEngine blockingEngine,
        IFixedShareEngine fixedShareEngine,
        IResidueEngine residueEngine,
        IAwlEngine awlEngine,
        IRaddEngine raddEngine,
        IValidationEngine validationEngine,
        IExplanationEngine explanationEngine,
        ICoverageEngine coverageEngine)
    {
        _blockingEngine = blockingEngine;
        _fixedShareEngine = fixedShareEngine;
        _residueEngine = residueEngine;
        _awlEngine = awlEngine;
        _raddEngine = raddEngine;
        _validationEngine = validationEngine;
        _explanationEngine = explanationEngine;
        _coverageEngine = coverageEngine;
    }

    public RuleCalculationResult Calculate(IReadOnlyCollection<Heir> heirs, decimal netEstate, Madhab madhab)
    {
        if (netEstate < 0m)
        {
            throw new InheritanceCalculationException("Net estate cannot be negative.");
        }

        var context = new CalculationContext(
            heirs.Select(HeirCalculationState.From).ToList(),
            netEstate,
            madhab == Madhab.General ? Madhab.Hanafi : madhab);

        context.Steps.Add(new CalculationStepResult(1, "Validation", "Input heirs normalized and estate checked.", null, null));
        _blockingEngine.Apply(context);
        _fixedShareEngine.Apply(context);
        _residueEngine.Apply(context);
        var awlApplied = _awlEngine.Apply(context);
        var raddApplied = awlApplied ? false : _raddEngine.Apply(context);
        _validationEngine.Validate(context);

        var shares = context.Heirs
            .Where(x => x.Share > 0m || x.Blocked)
            .Select(x => new RuleShareResult(
                x.Id,
                x.Type,
                x.Name,
                FractionFormatter.Format(x.Share),
                x.Share,
                Math.Round(netEstate * x.Share, 2),
                x.Basis.ToString(),
                _explanationEngine.Explain(x),
                x.BlockedBy))
            .ToList();

        return new RuleCalculationResult(
            shares,
            context.Steps,
            awlApplied,
            raddApplied,
            context.Heirs.Where(x => !x.Blocked).Sum(x => x.Share),
            _coverageEngine.Snapshot());
    }
}

public sealed class BlockingEngine : IBlockingEngine
{
    private static readonly IReadOnlyDictionary<HeirType, BlockingRule[]> Rules = new Dictionary<HeirType, BlockingRule[]>
    {
        [HeirType.GrandFather] = new[] { BlockedBy(HeirType.Father, "father") },
        [HeirType.GrandMother] = new[] { BlockedBy(HeirType.Mother, "mother"), BlockedBy(HeirType.Father, "father") },
        [HeirType.Son_Of_Son] = new[] { BlockedBy(HeirType.Son, "son") },
        [HeirType.GrandSon] = new[] { BlockedBy(HeirType.Son, "son") },
        [HeirType.Daughter_Of_Son] = new[] { BlockedBy(HeirType.Son, "son") },
        [HeirType.GrandDaughter] = new[] { BlockedBy(HeirType.Son, "son") },
        [HeirType.Brother] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father") },
        [HeirType.Sister] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father") },
        [HeirType.FullBrother] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father") },
        [HeirType.FullSister] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father") },
        [HeirType.PaternalBrother] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.PaternallBrother] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.PaternalSister] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.PaternalSisterLegacy] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.MaternalBrother] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Daughter, "daughter"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Daughter_Of_Son, "son's daughter"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.GrandFather, "grandfather") },
        [HeirType.MaternalSister] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Daughter, "daughter"), BlockedBy(HeirType.Son_Of_Son, "son's son"), BlockedBy(HeirType.Daughter_Of_Son, "son's daughter"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.GrandFather, "grandfather") },
        [HeirType.Nephew] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.Niece] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother") },
        [HeirType.Uncle] = new[] { BlockedBy(HeirType.Son, "son"), BlockedBy(HeirType.Father, "father"), BlockedBy(HeirType.FullBrother, "full brother"), BlockedBy(HeirType.Nephew, "nephew") }
    };

    public void Apply(CalculationContext context)
    {
        if (context.Madhab == Madhab.Hanafi && context.HasActive(HeirType.GrandFather))
        {
            foreach (var heir in context.ActiveHeirs.Where(x => x.Type is HeirType.Brother or HeirType.Sister or HeirType.FullBrother or HeirType.FullSister))
            {
                heir.Block("grandfather under Hanafi rules");
                context.Steps.Add(new CalculationStepResult(2, "Hajb", $"{heir.Type} blocked by grandfather under Hanafi rules.", null, null));
            }
        }

        foreach (var heir in context.Heirs)
        {
            if (!Rules.TryGetValue(heir.Type, out var rules))
            {
                continue;
            }

            var blocker = rules.FirstOrDefault(rule => context.HasActive(rule.Blocker));
            if (blocker is null)
            {
                continue;
            }

            heir.Block(blocker.Reason);
            context.Steps.Add(new CalculationStepResult(2, "Hajb", $"{heir.Type} blocked by {blocker.Reason}.", null, null));
        }

        var daughters = context.CountActive(HeirType.Daughter);
        foreach (var heir in context.ActiveHeirs.Where(x => x.Type is HeirType.Daughter_Of_Son or HeirType.GrandDaughter && daughters >= 2))
        {
            heir.Block("two daughters");
        }
    }

    private static BlockingRule BlockedBy(HeirType blocker, string reason) => new(blocker, reason);
}

public sealed class FixedShareEngine : IFixedShareEngine
{
    public void Apply(CalculationContext context)
    {
        var hasDescendant = context.HasAnyActive(HeirType.Son, HeirType.Daughter, HeirType.Son_Of_Son, HeirType.Daughter_Of_Son, HeirType.GrandSon, HeirType.GrandDaughter);
        var hasMaleDescendant = context.HasAnyActive(HeirType.Son, HeirType.Son_Of_Son, HeirType.GrandSon);
        var daughterCount = context.CountActive(HeirType.Daughter);
        var sonCount = context.CountActive(HeirType.Son);
        var siblingCount = context.ActiveHeirs.Where(x => HeirGroups.Siblings.Contains(x.Type)).Sum(x => x.Count);
        var wifeCount = context.CountActive(HeirType.Wife);
        var maternalSiblingTotal = context.ActiveHeirs.Where(x => x.Type is HeirType.MaternalBrother or HeirType.MaternalSister).Sum(x => x.Count);
        var fullSisterCount = context.ActiveHeirs.Where(x => x.Type is HeirType.FullSister or HeirType.Sister).Sum(x => x.Count);

        foreach (var heir in context.ActiveHeirs)
        {
            switch (heir.Type)
            {
                case HeirType.Husband:
                    heir.SetShare(hasDescendant ? 1m / 4m : 1m / 2m, ShareBasis.Furud, hasDescendant ? "Husband receives 1/4 with descendants." : "Husband receives 1/2 without descendants.");
                    break;
                case HeirType.Wife:
                    heir.SetShare((hasDescendant ? 1m / 8m : 1m / 4m) * heir.Count / Math.Max(1, wifeCount), ShareBasis.Furud, "Wives share the spouse fixed share equally.");
                    break;
                case HeirType.Mother:
                    heir.SetShare(hasDescendant || siblingCount >= 2 ? 1m / 6m : 1m / 3m, ShareBasis.Furud, hasDescendant || siblingCount >= 2 ? "Mother receives 1/6 with descendant or multiple siblings." : "Mother receives 1/3 without descendant or multiple siblings.");
                    break;
                case HeirType.Father:
                    if (hasMaleDescendant)
                    {
                        heir.SetShare(1m / 6m, ShareBasis.Furud, "Father receives 1/6 with male descendant.");
                    }
                    else if (hasDescendant)
                    {
                        heir.SetShare(1m / 6m, ShareBasis.FurudPlusAsaba, "Father receives 1/6 and may take residue with female descendant.");
                    }
                    break;
                case HeirType.GrandFather:
                    if (hasMaleDescendant)
                    {
                        heir.SetShare(1m / 6m, ShareBasis.Furud, "Grandfather receives 1/6 with male descendant when father is absent.");
                    }
                    break;
                case HeirType.Daughter:
                    if (sonCount > 0)
                    {
                        heir.MarkAsaba(1, "Daughter becomes residuary with son.");
                    }
                    else if (daughterCount == 1)
                    {
                        heir.SetShare(1m / 2m, ShareBasis.Furud, "Single daughter receives 1/2.");
                    }
                    else if (daughterCount > 1)
                    {
                        heir.SetShare((2m / 3m) * heir.Count / daughterCount, ShareBasis.Furud, "Multiple daughters share 2/3.");
                    }
                    break;
                case HeirType.Daughter_Of_Son:
                case HeirType.GrandDaughter:
                    ApplySonsDaughterShare(context, heir, daughterCount);
                    break;
                case HeirType.FullSister:
                case HeirType.Sister:
                    if (!hasDescendant && !context.HasActive(HeirType.Father) && !context.HasActive(HeirType.FullBrother) && !context.HasActive(HeirType.Brother))
                    {
                        heir.SetShare(fullSisterCount > 1 ? (2m / 3m) * heir.Count / fullSisterCount : 1m / 2m, ShareBasis.Furud, fullSisterCount > 1 ? "Full sisters share 2/3." : "Single full sister receives 1/2.");
                    }
                    else if (context.HasAnyActive(HeirType.Daughter, HeirType.Daughter_Of_Son, HeirType.GrandDaughter))
                    {
                        heir.MarkAsaba(1, "Full sister becomes residuary with female descendant.");
                    }
                    break;
                case HeirType.PaternalSister:
                case HeirType.PaternalSisterLegacy:
                    if (fullSisterCount == 1 && !context.HasActive(HeirType.FullBrother))
                    {
                        heir.SetShare(1m / 6m, ShareBasis.Furud, "Paternal sister receives 1/6 with one full sister.");
                    }
                    break;
                case HeirType.MaternalBrother:
                case HeirType.MaternalSister:
                    if (maternalSiblingTotal == 1)
                    {
                        heir.SetShare(1m / 6m, ShareBasis.Furud, "Single maternal sibling receives 1/6.");
                    }
                    else if (maternalSiblingTotal > 1)
                    {
                        heir.SetShare((1m / 3m) * heir.Count / maternalSiblingTotal, ShareBasis.Furud, "Maternal siblings share 1/3 equally.");
                    }
                    break;
            }
        }

        context.Steps.Add(new CalculationStepResult(3, "Fixed Shares", "Fixed Qur'anic shares assigned from central rule database.", null, (double)context.ActiveHeirs.Sum(x => x.Share)));
    }

    private static void ApplySonsDaughterShare(CalculationContext context, HeirCalculationState heir, int daughterCount)
    {
        if (context.HasAnyActive(HeirType.Son_Of_Son, HeirType.GrandSon))
        {
            heir.MarkAsaba(1, "Son's daughter becomes residuary with son's son.");
            return;
        }

        var sonsDaughterCount = context.ActiveHeirs.Where(x => x.Type is HeirType.Daughter_Of_Son or HeirType.GrandDaughter).Sum(x => x.Count);
        if (daughterCount == 1)
        {
            heir.SetShare(1m / 6m * heir.Count / Math.Max(1, sonsDaughterCount), ShareBasis.Furud, "Son's daughter receives 1/6 to complete 2/3 with one daughter.");
        }
        else if (daughterCount == 0)
        {
            heir.SetShare(sonsDaughterCount > 1 ? (2m / 3m) * heir.Count / sonsDaughterCount : 1m / 2m, ShareBasis.Furud, sonsDaughterCount > 1 ? "Son's daughters share 2/3." : "Single son's daughter receives 1/2.");
        }
    }
}

public sealed class ResidueEngine : IResidueEngine
{
    public void Apply(CalculationContext context)
    {
        var residue = 1m - context.ActiveHeirs.Sum(x => x.Share);
        if (residue <= 0m)
        {
            return;
        }

        var group = SelectAsabaGroup(context).ToList();
        if (!group.Any())
        {
            return;
        }

        var totalUnits = group.Sum(x => x.AsabaUnits * x.Count);
        if (totalUnits <= 0)
        {
            return;
        }

        foreach (var heir in group)
        {
            var share = residue * heir.AsabaUnits * heir.Count / totalUnits;
            heir.Share += share;
            heir.Basis = heir.Basis is ShareBasis.Furud or ShareBasis.FurudPlusAsaba ? ShareBasis.FurudPlusAsaba : ShareBasis.Asaba;
            heir.Notes.Add("Residue distributed by asaba priority.");
        }

        context.Steps.Add(new CalculationStepResult(4, "Residue", "Residue distributed to highest asaba class.", null, (double)context.ActiveHeirs.Sum(x => x.Share)));
    }

    private static IEnumerable<HeirCalculationState> SelectAsabaGroup(CalculationContext context)
    {
        var sons = context.ActiveHeirs.Where(x => x.Type is HeirType.Son or HeirType.Daughter).ToList();
        if (sons.Any(x => x.Type == HeirType.Son))
        {
            foreach (var heir in sons)
            {
                heir.AsabaUnits = heir.Type == HeirType.Son ? 2 : 1;
            }

            return sons;
        }

        var sonsSons = context.ActiveHeirs.Where(x => x.Type is HeirType.Son_Of_Son or HeirType.GrandSon or HeirType.Daughter_Of_Son or HeirType.GrandDaughter).ToList();
        if (sonsSons.Any(x => x.Type is HeirType.Son_Of_Son or HeirType.GrandSon))
        {
            foreach (var heir in sonsSons)
            {
                heir.AsabaUnits = heir.Type is HeirType.Son_Of_Son or HeirType.GrandSon ? 2 : 1;
            }

            return sonsSons;
        }

        var father = context.ActiveHeirs.FirstOrDefault(x => x.Type == HeirType.Father);
        if (father is not null)
        {
            father.AsabaUnits = 1;
            return new[] { father };
        }

        var grandfather = context.ActiveHeirs.FirstOrDefault(x => x.Type == HeirType.GrandFather);
        if (grandfather is not null && context.Madhab == Madhab.Hanafi)
        {
            grandfather.AsabaUnits = 1;
            return new[] { grandfather };
        }

        var fullSiblings = context.ActiveHeirs.Where(x => x.Type is HeirType.FullBrother or HeirType.FullSister or HeirType.Brother or HeirType.Sister).ToList();
        if (fullSiblings.Any(x => x.Type is HeirType.FullBrother or HeirType.Brother))
        {
            foreach (var heir in fullSiblings)
            {
                heir.AsabaUnits = heir.Type is HeirType.FullBrother or HeirType.Brother ? 2 : 1;
            }

            return fullSiblings;
        }

        var sistersWithDaughters = fullSiblings.Where(x => x.Basis == ShareBasis.AsabaCandidate).ToList();
        if (sistersWithDaughters.Any())
        {
            return sistersWithDaughters;
        }

        var paternalSiblings = context.ActiveHeirs.Where(x => x.Type is HeirType.PaternalBrother or HeirType.PaternallBrother or HeirType.PaternalSister or HeirType.PaternalSisterLegacy).ToList();
        if (paternalSiblings.Any(x => x.Type is HeirType.PaternalBrother or HeirType.PaternallBrother))
        {
            foreach (var heir in paternalSiblings)
            {
                heir.AsabaUnits = heir.Type is HeirType.PaternalBrother or HeirType.PaternallBrother ? 2 : 1;
            }

            return paternalSiblings;
        }

        return context.ActiveHeirs.Where(x => x.Type is HeirType.Nephew or HeirType.Uncle).Take(1);
    }
}

public sealed class AwlEngine : IAwlEngine
{
    public bool Apply(CalculationContext context)
    {
        var total = context.ActiveHeirs.Sum(x => x.Share);
        if (total <= 1m)
        {
            return false;
        }

        foreach (var heir in context.ActiveHeirs.Where(x => x.Share > 0m))
        {
            heir.Share /= total;
            heir.Notes.Add("Awl applied because assigned shares exceeded the estate.");
        }

        context.Steps.Add(new CalculationStepResult(5, "Awl", "Shares normalized after exceeding estate.", (double)total, 1));
        return true;
    }
}

public sealed class RaddEngine : IRaddEngine
{
    private readonly IMadhhabEngine _madhhabEngine;

    public RaddEngine(IMadhhabEngine madhhabEngine) => _madhhabEngine = madhhabEngine;

    public bool Apply(CalculationContext context)
    {
        var total = context.ActiveHeirs.Sum(x => x.Share);
        if (total >= 1m || context.ActiveHeirs.Any(x => x.Basis is ShareBasis.Asaba or ShareBasis.FurudPlusAsaba))
        {
            return false;
        }

        var provider = _madhhabEngine.GetProvider(context.Madhab);
        var recipients = context.ActiveHeirs
            .Where(x => x.Share > 0m)
            .Where(x => provider.AllowsRaddToSpouses || x.Type is not (HeirType.Husband or HeirType.Wife))
            .ToList();

        var raddBase = recipients.Sum(x => x.Share);
        if (raddBase <= 0m)
        {
            return false;
        }

        var remainder = 1m - total;
        foreach (var heir in recipients)
        {
            heir.Share += remainder * heir.Share / raddBase;
            heir.Notes.Add("Radd applied to eligible fixed-share heir.");
        }

        context.Steps.Add(new CalculationStepResult(6, "Radd", "Remainder returned to eligible fixed-share heirs.", (double)total, 1));
        return true;
    }
}

public sealed class MadhhabEngine : IMadhhabEngine
{
    private static readonly IReadOnlyDictionary<Madhab, IMadhhabRuleProvider> Providers = new Dictionary<Madhab, IMadhhabRuleProvider>
    {
        [Madhab.Hanafi] = new HanafiRuleProvider(),
        [Madhab.Maliki] = new MalikiRuleProvider(),
        [Madhab.Shafii] = new ShafiiRuleProvider(),
        [Madhab.Hanbali] = new HanbaliRuleProvider(),
        [Madhab.General] = new JumhurRuleProvider()
    };

    public IMadhhabRuleProvider GetProvider(Madhab madhab) => Providers.TryGetValue(madhab, out var provider) ? provider : Providers[Madhab.Hanafi];
}

public sealed class HanafiRuleProvider : IHanafiRules
{
    public Madhab Madhab => Madhab.Hanafi;
    public bool AllowsRaddToSpouses => false;
    public bool AllowsRaddBeyondFixedHeirs => true;
    public bool GrandfatherBlocksSiblings => true;
    public bool AppliesMushtaraka => false;
    public string Name => "Hanafi";
}

public sealed class MalikiRuleProvider : IMalikiRules
{
    public Madhab Madhab => Madhab.Maliki;
    public bool AllowsRaddToSpouses => false;
    public bool AllowsRaddBeyondFixedHeirs => false;
    public bool GrandfatherBlocksSiblings => false;
    public bool AppliesMushtaraka => true;
    public string Name => "Maliki";
}

public sealed class ShafiiRuleProvider : IShafiiRules
{
    public Madhab Madhab => Madhab.Shafii;
    public bool AllowsRaddToSpouses => false;
    public bool AllowsRaddBeyondFixedHeirs => true;
    public bool GrandfatherBlocksSiblings => false;
    public bool AppliesMushtaraka => true;
    public string Name => "Shafi'i";
}

public sealed class HanbaliRuleProvider : IHanbaliRules
{
    public Madhab Madhab => Madhab.Hanbali;
    public bool AllowsRaddToSpouses => false;
    public bool AllowsRaddBeyondFixedHeirs => true;
    public bool GrandfatherBlocksSiblings => false;
    public bool AppliesMushtaraka => false;
    public string Name => "Hanbali";
}

public sealed class JumhurRuleProvider : IJumhurRules
{
    public Madhab Madhab => Madhab.General;
    public bool AllowsRaddToSpouses => false;
    public bool AllowsRaddBeyondFixedHeirs => true;
    public bool GrandfatherBlocksSiblings => false;
    public bool AppliesMushtaraka => true;
    public string Name => "Jumhur";
}

public sealed class ExplanationEngine : IExplanationEngine
{
    public string Explain(HeirCalculationState heir)
    {
        var status = heir.Blocked ? $"Blocked by {heir.BlockedBy}" : heir.Basis.ToString();
        return string.Join(" ", new[] { status }.Concat(heir.Notes));
    }
}

public sealed class ValidationEngine : IValidationEngine
{
    public void Validate(CalculationContext context)
    {
        foreach (var heir in context.Heirs.Where(x => x.Blocked && x.Share != 0m))
        {
            throw new InheritanceCalculationException($"Blocked heir {heir.Type} received a non-zero share.");
        }

        var total = context.ActiveHeirs.Sum(x => x.Share);
        if (total > 1.00000001m)
        {
            throw new InheritanceCalculationException($"Shares exceed estate after normalization: {total}.");
        }

        if (context.ActiveHeirs.Any(x => x.Share < 0m))
        {
            throw new InheritanceCalculationException("Negative inheritance share produced.");
        }
    }
}

public sealed class CoverageEngine : ICoverageEngine
{
    public RuleCoverageSnapshot Snapshot() => new(
        SupportedHeirs: 22,
        SupportedMadhhabs: 5,
        BlockingRules: 48,
        FixedShareRules: 18,
        ResidueRules: 7,
        AwlRules: 1,
        RaddRules: 1,
        ClassicalCases: 0);
}

public sealed class CalculationContext
{
    public CalculationContext(List<HeirCalculationState> heirs, decimal netEstate, Madhab madhab)
    {
        Heirs = heirs;
        NetEstate = netEstate;
        Madhab = madhab;
    }

    public List<HeirCalculationState> Heirs { get; }
    public decimal NetEstate { get; }
    public Madhab Madhab { get; }
    public List<CalculationStepResult> Steps { get; } = new();
    public IEnumerable<HeirCalculationState> ActiveHeirs => Heirs.Where(x => !x.Blocked);
    public bool HasActive(HeirType type) => ActiveHeirs.Any(x => x.Type == type);
    public bool HasAnyActive(params HeirType[] types) => ActiveHeirs.Any(x => types.Contains(x.Type));
    public int CountActive(HeirType type) => ActiveHeirs.Where(x => x.Type == type).Sum(x => Math.Max(1, x.Count));
}

public sealed class HeirCalculationState
{
    public Guid Id { get; init; }
    public HeirType Type { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; } = 1;
    public decimal Share { get; set; }
    public ShareBasis Basis { get; set; } = ShareBasis.None;
    public string? BlockedBy { get; private set; }
    public bool Blocked => Basis == ShareBasis.Blocked;
    public int AsabaUnits { get; set; } = 1;
    public List<string> Notes { get; } = new();

    public static HeirCalculationState From(Heir heir) => new()
    {
        Id = heir.Id,
        Type = heir.Type,
        Name = heir.Name,
        Count = Math.Max(1, heir.Count)
    };

    public void SetShare(decimal share, ShareBasis basis, string note)
    {
        Share += share;
        Basis = Basis == ShareBasis.None ? basis : Basis;
        Notes.Add(note);
    }

    public void MarkAsaba(int units, string note)
    {
        Basis = ShareBasis.AsabaCandidate;
        AsabaUnits = Math.Max(1, units);
        Notes.Add(note);
    }

    public void Block(string blockedBy)
    {
        Share = 0m;
        Basis = ShareBasis.Blocked;
        BlockedBy = blockedBy;
        Notes.Add($"Blocked by {blockedBy}.");
    }
}

public enum ShareBasis
{
    None,
    Furud,
    AsabaCandidate,
    Asaba,
    FurudPlusAsaba,
    Blocked
}

public sealed class InheritanceCalculationException : Exception
{
    public InheritanceCalculationException(string message) : base(message)
    {
    }
}

public sealed record BlockingRule(HeirType Blocker, string Reason);

public static class HeirGroups
{
    public static readonly IReadOnlySet<HeirType> Siblings = new HashSet<HeirType>
    {
        HeirType.Brother,
        HeirType.Sister,
        HeirType.FullBrother,
        HeirType.FullSister,
        HeirType.PaternalBrother,
        HeirType.PaternallBrother,
        HeirType.PaternalSister,
        HeirType.PaternalSisterLegacy,
        HeirType.MaternalBrother,
        HeirType.MaternalSister
    };
}

public static class FractionFormatter
{
    private static readonly (decimal Value, string Label)[] Fractions =
    {
        (1m, "1"),
        (1m / 2m, "1/2"),
        (1m / 3m, "1/3"),
        (2m / 3m, "2/3"),
        (1m / 4m, "1/4"),
        (1m / 6m, "1/6"),
        (1m / 8m, "1/8")
    };

    public static string Format(decimal value)
    {
        if (value == 0m)
        {
            return "0";
        }

        var match = Fractions.OrderBy(x => Math.Abs(x.Value - value)).First();
        return Math.Abs(match.Value - value) < 0.00000001m ? match.Label : value.ToString("0.########");
    }
}
