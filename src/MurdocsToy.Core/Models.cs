using System.Text.Json.Serialization;

namespace MurdocsToy.Core;

public record EventType(string Id, string Name);
public record EventInfo(string Id, string Name, string? CountryCode, DateTime? OpenDate);
public record CompetitionNode(string Id, string Name, string? Region, int MarketCount);
public record EventNode(string Id, string Name, string? CountryCode, DateTime? OpenDate, int MarketCount);
public record RunnerCatalog(long SelectionId, string RunnerName, double? SortPriority);
public record EventTypeInfo(string Id, string Name);
public record MarketCatalogEntry(string MarketId, string MarketName, DateTime? MarketStartTime,
    double? TotalMatched, EventInfo? Event, List<RunnerCatalog>? Runners, EventTypeInfo? EventType = null);

public record PriceSize(double Price, double Size);
public record ExchangePrices(List<PriceSize>? AvailableToBack, List<PriceSize>? AvailableToLay, List<PriceSize>? TradedVolume);
public record Runner(long SelectionId, string Status, double? LastPriceTraded, double? TotalMatched, ExchangePrices? Ex);
public record MarketBook(string MarketId, string Status, bool IsMarketDataDelayed, bool? InPlay,
    double? TotalMatched, List<Runner>? Runners);

public record PlaceInstruction(long SelectionId, string Side, double Price, double Size, string PersistenceType = "LAPSE");
public record PlaceReport(string Status, string? ErrorCode, string? BetId, double? SizeMatched);
public record CurrentOrder(string BetId, string MarketId, long SelectionId, string Side, string Status,
    double Price, double Size, double SizeMatched, double SizeRemaining, double AveragePriceMatched = 0);
public record AccountDetails(string CurrencyCode, string? CountryCode);
public record AccountFunds(double AvailableToBetBalance, double Exposure);

public class BetfairException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
