using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Mirath.Domain.Entities;
using Mirath.Domain.Enums;
using Mirath.Domain.Services;
using Mirath.Infrastructure.Persistence;
using SharedHeir = Mirath.Shared.Entities.Heir;
using HeirType = Mirath.Shared.Enums.HeirType;

namespace Mirath.Application.Services;

public interface IFarayidCalculationService
{
    Task<FarayidCalculationResponse> CalculateAsync(Guid caseId, FarayidCalculationRequest request, CancellationToken cancellationToken = default);
}

public sealed class FarayidCalculationService : IFarayidCalculationService
{
    private const string Version = "farayid-engine-2026.06.04-centralized-v2";
    private readonly ApplicationDbContext _db;
    private readonly IRuleEngine _ruleEngine;

    public FarayidCalculationService(ApplicationDbContext db, IRuleEngine ruleEngine)
    {
        _db = db;
        _ruleEngine = ruleEngine;
    }

    public async Task<FarayidCalculationResponse> CalculateAsync(Guid caseId, FarayidCalculationRequest request, CancellationToken cancellationToken = default)
    {
        var inheritanceCase = await _db.Cases
            .Include(x => x.Decedent)
            .Include(x => x.Assets)
            .Include(x => x.Debts)
            .Include(x => x.Heirs)
            .FirstOrDefaultAsync(x => x.Id == caseId, cancellationToken)
            ?? throw new InvalidOperationException("Case not found");

        var madhab = ParseMadhab(request.Madhab);
        var currency = inheritanceCase.Assets.FirstOrDefault()?.Currency ?? "EGP";
        var estate = CalculateEstate(inheritanceCase, request);
        var disqualified = SelectDisqualifiedHeirs(inheritanceCase).ToList();
        var eligibleHeirs = inheritanceCase.Heirs
            .Where(x => x.IsAlive)
            .Where(x => disqualified.All(d => d.Id != x.Id))
            .Select(ToSharedHeir)
            .ToList();

        var ruleResult = _ruleEngine.Calculate(eligibleHeirs, estate.NetEstate, madhab);
        var calculated = ruleResult.Shares
            .Select(x => ToDto(x, estate.NetEstate, currency))
            .ToList();
        calculated.AddRange(disqualified.Select(x => ToDisqualifiedDto(x, currency)));

        var allResults = inheritanceCase.Heirs
            .Select(heir => calculated.FirstOrDefault(x => x.HeirId == heir.Id) ?? ToNonInheritingDto(heir, currency))
            .ToList();

        ValidateApplicationResult(allResults, estate.NetEstate);

        var special = DetectSpecialCase(eligibleHeirs, madhab);
        var response = new FarayidCalculationResponse(
            inheritanceCase.Id,
            estate.TotalEstate,
            estate.NetEstate,
            estate.DeductedDebts,
            estate.FuneralExpenses,
            estate.DeductedBequest,
            madhab.ToString(),
            DateTime.UtcNow,
            Version,
            allResults,
            allResults.Where(x => x.CalculationBasis is "Blocked" or "Disqualified").Select(x => new BlockedHeirDto(x.HeirId, x.Name, x.Relationship, x.BlockedBy ?? x.CalculationBasis)).ToList(),
            ruleResult.IsAwlApplied,
            ruleResult.IsRaddApplied,
            special,
            BuildArabicSummary(estate.NetEstate, allResults, madhab, ruleResult.IsAwlApplied, ruleResult.IsRaddApplied, special),
            BuildEnglishSummary(estate.NetEstate, allResults, madhab, ruleResult.IsAwlApplied, ruleResult.IsRaddApplied, special),
            BuildScenarioWarnings(inheritanceCase.Heirs));

        foreach (var heir in inheritanceCase.Heirs)
        {
            var result = allResults.FirstOrDefault(x => x.HeirId == heir.Id);
            if (result is null)
            {
                continue;
            }

            heir.ShareFraction = result.QuranicShare;
            heir.ShareValue = result.ShareValue;
            heir.BlockedBy = result.BlockedBy;
            heir.IsBlocked = result.CalculationBasis is "Blocked" or "Disqualified";
        }

        _db.InheritanceResults.Add(new InheritanceResult
        {
            CaseId = inheritanceCase.Id,
            CalculatedAt = response.CalculationTimestamp,
            TotalEstate = estate.TotalEstate,
            TotalDebts = estate.DeductedDebts,
            NetEstate = estate.NetEstate,
            Algorithm = ToAlgorithm(madhab),
            Results = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }),
            Notes = $"{Version}; {special}; supported-heirs={ruleResult.Coverage.SupportedHeirs}; blocking-rules={ruleResult.Coverage.BlockingRules}".Trim(';', ' ')
        });

        inheritanceCase.Status = CaseStatus.Calculated;
        inheritanceCase.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        return response;
    }

    private static EstateCalculation CalculateEstate(Case inheritanceCase, FarayidCalculationRequest request)
    {
        var goldPrice = request.GoldPricePerGram ?? ReadDecimalEnv("GOLD_PRICE_PER_GRAM", 0m);
        var silverPrice = request.SilverPricePerGram ?? ReadDecimalEnv("SILVER_PRICE_PER_GRAM", 0m);
        var totalEstate = inheritanceCase.Assets.Sum(asset => asset.Type switch
        {
            AssetType.Gold when asset.Weight.HasValue && goldPrice > 0m => asset.Weight.Value * goldPrice,
            AssetType.Silver when asset.Weight.HasValue && silverPrice > 0m => asset.Weight.Value * silverPrice,
            _ => asset.Value
        });
        var deductedDebts = request.IncludeDebts ? inheritanceCase.Debts.Sum(x => x.Amount) + inheritanceCase.Assets.Where(x => x.Type == AssetType.Debt).Sum(x => x.Value) : 0m;
        var funeralExpenses = request.FuneralExpenses ?? inheritanceCase.FuneralExpenses;
        var requestedBequest = request.BequestAmount ?? inheritanceCase.WillAmount;
        var deductedBequest = Math.Min(Math.Max(0m, requestedBequest), totalEstate / 3m);
        var netEstate = Math.Max(0m, totalEstate - deductedDebts - funeralExpenses - deductedBequest);
        return new EstateCalculation(totalEstate, netEstate, deductedDebts, funeralExpenses, deductedBequest);
    }

    private static IEnumerable<Heir> SelectDisqualifiedHeirs(Case inheritanceCase)
    {
        var deceasedMuslim = inheritanceCase.Decedent?.Religion != Religion.NonMuslim;
        return inheritanceCase.Heirs.Where(heir =>
            heir.IsAlive &&
            (heir.IsMurderer || deceasedMuslim && heir.Religion == Religion.NonMuslim));
    }

    private static SharedHeir ToSharedHeir(Heir heir) => new()
    {
        Id = heir.Id,
        CaseId = heir.CaseId,
        Name = heir.Name,
        Type = heir.Relationship,
        Count = Math.Max(1, heir.Count),
        IsAlive = heir.IsAlive
    };

    private static HeirCalculationDto ToDto(RuleShareResult share, decimal netEstate, string currency) => new(
        share.HeirId,
        share.HeirName,
        share.HeirType.ToString(),
        share.Fraction,
        Math.Round(share.Ratio, 8),
        Math.Round(netEstate * share.Ratio, 2),
        currency,
        share.ShareType,
        share.BlockedBy,
        share.Explanation);

    private static HeirCalculationDto ToDisqualifiedDto(Heir heir, string currency)
    {
        var reason = heir.IsMurderer
            ? "Murderer cannot inherit from the victim"
            : "Non-Muslim heir cannot inherit from Muslim decedent";

        return new HeirCalculationDto(heir.Id, heir.Name, heir.Relationship.ToString(), "0", 0m, 0m, currency, "Disqualified", reason, reason);
    }

    private static HeirCalculationDto ToNonInheritingDto(Heir heir, string currency)
    {
        var basis = heir.IsAlive ? "None" : "Disqualified";
        var reason = heir.IsAlive ? "No inheritable share under the selected rule set." : "Heir is not alive.";
        return new HeirCalculationDto(heir.Id, heir.Name, heir.Relationship.ToString(), "0", 0m, 0m, currency, basis, basis == "Disqualified" ? reason : null, reason);
    }

    private static void ValidateApplicationResult(IReadOnlyCollection<HeirCalculationDto> results, decimal netEstate)
    {
        var blockedWithMoney = results.FirstOrDefault(x => x.CalculationBasis is "Blocked" or "Disqualified" && x.ShareValue != 0m);
        if (blockedWithMoney is not null)
        {
            throw new InheritanceCalculationException($"Blocked or disqualified heir {blockedWithMoney.HeirId} received money.");
        }

        var distributed = results.Sum(x => x.ShareValue);
        if (netEstate > 0m && Math.Abs(distributed - netEstate) > 0.05m)
        {
            throw new InheritanceCalculationException($"Distributed amount {distributed} does not equal net estate {netEstate}.");
        }
    }

    private static string? DetectSpecialCase(IReadOnlyCollection<SharedHeir> heirs, Madhab madhab)
    {
        if (Has(heirs, HeirType.Husband) && Has(heirs, HeirType.Mother)
            && heirs.Any(x => x.Type is HeirType.MaternalBrother or HeirType.MaternalSister)
            && heirs.Any(x => x.Type is HeirType.FullBrother or HeirType.FullSister or HeirType.Brother or HeirType.Sister))
        {
            return madhab is Madhab.Maliki or Madhab.Shafii or Madhab.General ? "Mushtaraka" : "Himariyya";
        }

        if (Has(heirs, HeirType.GrandFather) && Has(heirs, HeirType.Husband) && Has(heirs, HeirType.Mother)
            && heirs.Any(x => x.Type is HeirType.FullSister or HeirType.Sister))
        {
            return "Akdariyya";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildScenarioWarnings(IEnumerable<Heir> heirs)
    {
        var warnings = new List<string>();
        if (heirs.Any(x => x.IsPregnant))
        {
            warnings.Add("Pregnancy scenario detected: calculate with and without unborn child and hold the safer share until birth.");
        }

        if (heirs.Any(x => x.IsMissing))
        {
            warnings.Add("Missing person scenario detected: calculate with and without the missing heir and hold disputed share in escrow.");
        }

        return warnings;
    }

    private static string BuildArabicSummary(decimal netEstate, IReadOnlyList<HeirCalculationDto> results, Madhab madhab, bool awl, bool radd, string? special)
        => $"تم حساب صافي التركة {netEstate:N2} وفق مذهب {madhab}. عدد الورثة المستحقين: {results.Count(IsInheriting)}."
           + (awl ? " طُبق العول." : "")
           + (radd ? " طُبق الرد." : "")
           + (special is not null ? $" تنبيه لمسألة خاصة: {special}." : "");

    private static string BuildEnglishSummary(decimal netEstate, IReadOnlyList<HeirCalculationDto> results, Madhab madhab, bool awl, bool radd, string? special)
        => $"Net estate {netEstate:N2} calculated under {madhab}. Eligible heirs: {results.Count(IsInheriting)}."
           + (awl ? " Awl was applied." : "")
           + (radd ? " Radd was applied." : "")
           + (special is not null ? $" Special-case warning: {special}." : "");

    private static bool IsInheriting(HeirCalculationDto result) => result.ShareDecimal > 0m && result.CalculationBasis is not ("Blocked" or "Disqualified");
    private static bool Has(IEnumerable<SharedHeir> heirs, HeirType type) => heirs.Any(x => x.Type == type);
    private static Madhab ParseMadhab(string value) => value.Equals("Shafi", StringComparison.OrdinalIgnoreCase) ? Madhab.Shafii : Enum.TryParse<Madhab>(value, true, out var madhab) ? madhab : Madhab.Hanafi;
    private static InheritanceAlgorithm ToAlgorithm(Madhab madhab) => madhab switch { Madhab.Maliki => InheritanceAlgorithm.Maliki, Madhab.Shafii => InheritanceAlgorithm.Shafi, Madhab.Hanbali => InheritanceAlgorithm.Hanbali, _ => InheritanceAlgorithm.Hanafi };
    private static decimal ReadDecimalEnv(string key, decimal fallback) => decimal.TryParse(Environment.GetEnvironmentVariable(key), out var value) ? value : fallback;

    private sealed record EstateCalculation(decimal TotalEstate, decimal NetEstate, decimal DeductedDebts, decimal FuneralExpenses, decimal DeductedBequest);
}

public sealed record FarayidCalculationRequest(string Madhab, bool IncludeDebts, decimal? GoldPricePerGram = null, decimal? SilverPricePerGram = null, decimal? FuneralExpenses = null, decimal? BequestAmount = null);
public sealed record FarayidCalculationResponse(Guid CaseId, decimal TotalEstate, decimal NetEstate, decimal DeductedDebts, decimal FuneralExpenses, decimal DeductedBequest, string MadhabUsed, DateTime CalculationTimestamp, string Version, IReadOnlyList<HeirCalculationDto> Heirs, IReadOnlyList<BlockedHeirDto> BlockedHeirs, bool AwlApplied, bool RaddApplied, string? SpecialCase, string SummaryArabic, string SummaryEnglish, IReadOnlyList<string> ScenarioWarnings);
public sealed record HeirCalculationDto(Guid HeirId, string Name, string Relationship, string QuranicShare, decimal ShareDecimal, decimal ShareValue, string Currency, string CalculationBasis, string? BlockedBy, string Notes);
public sealed record BlockedHeirDto(Guid HeirId, string Name, string Relationship, string Reason);
