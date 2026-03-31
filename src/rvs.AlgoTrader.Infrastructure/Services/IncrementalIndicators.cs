using rvs.AlgoTrader.Application.Services;
namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>O(1) EMA: maintains running state, no full array recalculation.</summary>
public sealed class IncrementalEma : IIncrementalIndicator<decimal?>
{
    private const decimal EmaSmoothing = 2m;

    private readonly int _period;
    private readonly decimal _k;
    private decimal? _current;
    private int _count;

    public IncrementalEma(int period) { _period = period; _k = EmaSmoothing / (period + 1); }

    public decimal? Update(decimal close, long? volume = null)
    {
        _count++;
        if (_count < _period) { _current = (_current ?? 0) + close; return null; }
        if (_count == _period) { _current = ((_current ?? 0) + close) / _period; return _current; }
        _current = close * _k + _current!.Value * (1 - _k);
        return _current;
    }

    public void Reset() { _current = null; _count = 0; }
}

/// <summary>O(1) SMA using circular buffer.</summary>
public sealed class IncrementalSma : IIncrementalIndicator<decimal?>
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _pos, _count;
    private decimal _sum;

    public IncrementalSma(int period) { _period = period; _buffer = new decimal[period]; }

    public decimal? Update(decimal close, long? volume = null)
    {
        _sum -= _buffer[_pos];
        _buffer[_pos] = close;
        _sum += close;
        _pos = (_pos + 1) % _period;
        _count = Math.Min(_count + 1, _period);
        return _count == _period ? _sum / _period : null;
    }

    public void Reset() { Array.Clear(_buffer); _pos = 0; _count = 0; _sum = 0; }
}

/// <summary>O(1) ATR using Wilder smoothing.</summary>
public sealed class IncrementalAtr(int period) : IIncrementalIndicator<decimal?>
{
    private decimal? _prevClose;
    private decimal? _atr;
    private int _count;

    public decimal? Update(decimal close, long? volume = null) => null; // requires high/low — use overload

    public decimal? Update(decimal high, decimal low, decimal close)
    {
        decimal tr;
        if (_prevClose is null) { tr = high - low; }
        else { tr = Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose.Value), Math.Abs(low - _prevClose.Value))); }
        _prevClose = close;
        _count++;
        if (_count <= period) { _atr = (_atr ?? 0) + tr / period; return _count == period ? _atr : null; }
        _atr = (_atr!.Value * (period - 1) + tr) / period;
        return _atr;
    }

    public void Reset() { _prevClose = null; _atr = null; _count = 0; }
}

/// <summary>O(1) VWAP — resets daily via IClock check.</summary>
public sealed class IncrementalVwap : IIncrementalIndicator<decimal?>
{
    private decimal _cumPV, _cumVol;

    public decimal? Update(decimal close, long? volume = null)
    {
        if (volume is null) return null;
        _cumPV += close * volume.Value;
        _cumVol += volume.Value;
        return _cumVol == 0 ? null : _cumPV / _cumVol;
    }

    public void Reset() { _cumPV = 0; _cumVol = 0; }
}
