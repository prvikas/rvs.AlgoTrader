using rvs.AlgoTrader.Application.Services;
using rvs.AlgoTrader.Domain.Enums;
using rvs.AlgoTrader.Infrastructure.Constants;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// Computes full Indian market transaction costs for one order side.
/// Rates as of FY2025–26 (SEBI fee updated to ₹10/crore).
///
/// Brokerage:  mStock/Zerodha/Upstox = ₹20 flat per executed order (capped at 2.5% of trade value).
///             Delivery equity = 0 (Zerodha/Upstox demat transfer model).
/// STT:        0.1% on sell side (equity delivery); 0.025% on sell side (intraday equity);
///             0.0125% on buy side (equity futures); 0.0625% on sell (options on premium).
/// Exchange:   NSE equity: 0.00345%; NSE F&amp;O: 0.002% on premium (options), 0.002% (futures).
/// GST:        18% on brokerage + exchange fee + SEBI charges.
/// SEBI:       ₹10 per crore of turnover.
/// Stamp:      0.003% on buy side (equity/F&amp;O); 0.002% on buy (options premium).
/// </summary>
public sealed class IndianMarketCommissionModel : ICommissionModel
{
    private readonly string _brokerName;

    /// <summary>
    /// DI constructor — reads active broker name from <c>Broker:ActiveBroker</c> config key.
    /// Defaults to empty string (₹20 flat brokerage path) when key is absent.
    /// </summary>
    public IndianMarketCommissionModel(Microsoft.Extensions.Configuration.IConfiguration config)
        => _brokerName = config["Broker:ActiveBroker"] ?? string.Empty;

    /// <summary>Direct constructor for factory / test use.</summary>
    public IndianMarketCommissionModel(string brokerName)
        => _brokerName = brokerName;

    private const decimal GstRate          = 0.18m;
    private const decimal SebiRatePerCrore = 10m;

    public CommissionBreakdown Calculate(
        decimal price, int quantity,
        OrderDirection direction,
        ProductType productType,
        InstrumentType instrumentType)
    {
        decimal tradeValue = price * quantity;
        if (tradeValue <= 0) return new CommissionBreakdown(0, 0, 0, 0, 0, 0);

        bool isBuy      = direction == OrderDirection.Buy;
        bool isDelivery = productType == ProductType.CNC;
        bool isOptions  = instrumentType == InstrumentType.Options;
        bool isFutures  = instrumentType == InstrumentType.Futures;
        bool isEquity   = instrumentType == InstrumentType.Equity;

        // ── Brokerage ────────────────────────────────────────────────────────
        decimal brokerage;
        if (isDelivery && isEquity && _brokerName is BrokerNames.Zerodha or BrokerNames.Upstox)
            brokerage = 0m;  // free delivery
        else
            brokerage = Math.Min(20m, tradeValue * 0.025m);  // ₹20 flat, capped at 2.5%

        // ── STT ──────────────────────────────────────────────────────────────
        decimal stt = 0m;
        if (isEquity && isDelivery && !isBuy)
            stt = tradeValue * 0.001m;              // 0.1% on sell side only (SEBI: STT for CNC delivery = sell-side)
        else if (isEquity && !isDelivery && !isBuy)
            stt = tradeValue * 0.00025m;            // 0.025% sell-side intraday
        else if (isFutures && !isBuy)
            stt = tradeValue * 0.0000125m;          // 0.0125% on futures sell
        else if (isOptions)
            stt = isBuy ? 0m : price * quantity * 0.000625m; // 0.0625% on options premium (sell)

        // ── Exchange fee ─────────────────────────────────────────────────────
        decimal exchangeFee;
        if (isOptions)
            exchangeFee = price * quantity * 0.00053m;  // 0.053% on premium (NSE F&O FY2025-26)
        else if (isFutures)
            exchangeFee = tradeValue * 0.000002m;       // 0.0002%
        else
            exchangeFee = tradeValue * 0.0000345m;      // 0.00345% NSE equity

        // ── SEBI fee ─────────────────────────────────────────────────────────
        decimal sebi = tradeValue / 10_000_000m * SebiRatePerCrore;

        // ── GST (on brokerage + exchange + SEBI) ────────────────────────────
        decimal gst = (brokerage + exchangeFee + sebi) * GstRate;

        // ── Stamp duty (buy side only) ────────────────────────────────────────
        decimal stamp = 0m;
        if (isBuy)
            stamp = isOptions
                ? price * quantity * 0.00002m   // 0.002% on options premium
                : tradeValue * 0.00003m;         // 0.003%

        return new CommissionBreakdown(brokerage, stt, exchangeFee, gst, sebi, stamp);
    }
}

/// <summary>
/// Factory that returns the correct ICommissionModel for the active broker.
/// Registered as a scoped service; reads active broker from IConfiguration.
/// </summary>
public sealed class CommissionModelFactory
{
    public static ICommissionModel Create(string brokerName)
        => new IndianMarketCommissionModel(brokerName);
}
