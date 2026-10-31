namespace RbmTool.Core.Models;

/// <summary>
/// Trade as described in GT_Valo (one row = one trade).
/// Mirrors models.TradeInfo in the Python reference implementation.
/// </summary>
public sealed class TradeInfo
{
    public int TranNum { get; init; }
    public int InsNum { get; init; }
    public string ExternalParty { get; init; } = string.Empty;
    public string PricingModel { get; init; } = string.Empty;
    public double MtM { get; init; }
    public string Ccy { get; init; } = string.Empty;
    public string Way { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
}

/// <summary>
/// Aggregated sensitivity of one trade: (Index, Tenor) -> Delta.
/// Equivalent of the Python Sensitivities dict.
/// </summary>
public sealed class Sensitivities
{
    private readonly Dictionary<(string Index, string Tenor), double> _values = new();

    public int Count => _values.Count;

    public IEnumerable<(string Index, string Tenor)> Keys => _values.Keys;

    public IEnumerable<KeyValuePair<(string Index, string Tenor), double>> Items => _values;

    public double Get((string Index, string Tenor) key) => _values.GetValueOrDefault(key, 0.0);

    public void Set((string Index, string Tenor) key, double value)
    {
        if (Math.Abs(value) == 0.0)
        {
            _values.Remove(key);
        }
        else
        {
            _values[key] = value;
        }
    }

    public void Add((string Index, string Tenor) key, double delta)
    {
        _values[key] = _values.GetValueOrDefault(key, 0.0) + delta;
    }

    /// <summary>
    /// Add usage * impact to the current sensitivities (mirrors SubPortfolioState.apply).
    /// Buckets below zeroTol are removed.
    /// </summary>
    public void Apply(Sensitivities impact, double usage, double zeroTol)
    {
        foreach (var ((index, tenor), e) in impact.Items)
        {
            var key = (index, tenor);
            var value = Get(key) + usage * e;
            if (Math.Abs(value) < zeroTol)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }
        }
    }

    public double L1()
    {
        var sum = 0.0;
        foreach (var v in _values.Values)
        {
            sum += Math.Abs(v);
        }
        return sum;
    }

    public Sensitivities Clone()
    {
        var clone = new Sensitivities();
        foreach (var (k, v) in _values)
        {
            clone._values[k] = v;
        }
        return clone;
    }
}
