using System.Numerics;

namespace kajarlabs.Framework.Bindables;

/// <summary>
/// A numeric <see cref="Bindable{T}"/> powered by generic math supporting range clamping
/// (<see cref="MinValue"/> / <see cref="MaxValue"/>) and optional <see cref="Precision"/> snapping
/// without runtime reflection.
/// </summary>
public class BindableNumber<T> : Bindable<T>
    where T : struct, INumber<T>, IMinMaxValue<T>
{
    private T minValue = T.MinValue;
    private T maxValue = T.MaxValue;
    private T precision = T.Zero;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindableNumber{T}"/> class.
    /// </summary>
    /// <param name="defaultValue">The initial <see cref="Bindable{T}.Value"/> and <see cref="Bindable{T}.Default"/>.</param>
    public BindableNumber(T defaultValue = default)
        : base(validateFinite(defaultValue, nameof(defaultValue)))
    {
    }

    /// <summary>
    /// Occurs when <see cref="MinValue"/> changes.
    /// </summary>
    public event Action<T>? MinValueChanged;

    /// <summary>
    /// Occurs when <see cref="MaxValue"/> changes.
    /// </summary>
    public event Action<T>? MaxValueChanged;

    /// <summary>
    /// Occurs when <see cref="Precision"/> changes.
    /// </summary>
    public event Action<T>? PrecisionChanged;

    /// <summary>
    /// Gets or sets the inclusive minimum allowed value.
    /// </summary>
    public T MinValue
    {
        get => minValue;
        set => setRangeFrom(value, maxValue, precision, source: null);
    }

    /// <summary>
    /// Gets or sets the inclusive maximum allowed value.
    /// </summary>
    public T MaxValue
    {
        get => maxValue;
        set => setRangeFrom(minValue, value, precision, source: null);
    }

    /// <summary>
    /// Gets or sets the snapping step applied to values, or <see cref="INumberBase{TSelf}.Zero"/> to disable snapping.
    /// </summary>
    public T Precision
    {
        get => precision;
        set => setRangeFrom(minValue, maxValue, value, source: null);
    }

    /// <inheritdoc />
    public override void UnbindEvents()
    {
        base.UnbindEvents();
        MinValueChanged = null;
        MaxValueChanged = null;
        PrecisionChanged = null;
    }

    /// <inheritdoc />
    protected override Bindable<T> CreateInstance() => new BindableNumber<T>();

    /// <inheritdoc />
    protected override T NormalizeValue(T value)
    {
        validateFinite(value, nameof(value));

        var snapped = snapToPrecision(value, precision);
        return T.Clamp(snapped, minValue, maxValue);
    }

    /// <inheritdoc />
    protected override void SyncFrom(Bindable<T> source)
    {
        if (source is BindableNumber<T> sourceNumber)
        {
            setRangeFrom(sourceNumber.minValue, sourceNumber.maxValue, sourceNumber.precision, sourceNumber);
        }

        base.SyncFrom(source);
    }

    private void setRangeFrom(T newMin, T newMax, T newPrecision, BindableNumber<T>? source)
    {
        validateFinite(newMin, nameof(newMin));
        validateFinite(newMax, nameof(newMax));
        validateFinite(newPrecision, nameof(newPrecision));

        if (newMin > newMax)
            throw new ArgumentOutOfRangeException(nameof(newMin), newMin, "MinValue cannot exceed MaxValue.");

        if (newPrecision < T.Zero)
            throw new ArgumentOutOfRangeException(nameof(newPrecision), newPrecision, "Precision cannot be negative.");

        var minChanged = minValue != newMin;
        var maxChanged = maxValue != newMax;
        var precisionChanged = precision != newPrecision;

        if (!minChanged && !maxChanged && !precisionChanged)
            return;

        minValue = newMin;
        maxValue = newMax;
        precision = newPrecision;

        if (minChanged)
            MinValueChanged?.Invoke(newMin);

        if (maxChanged)
            MaxValueChanged?.Invoke(newMax);

        if (precisionChanged)
            PrecisionChanged?.Invoke(newPrecision);

        RevalidateValue();

        var peers = GetLivePeers();

        for (var i = 0; i < peers.Count; i++)
        {
            if (minValue != newMin || maxValue != newMax || precision != newPrecision)
                break;

            if (peers[i] is BindableNumber<T> peerNumber && !ReferenceEquals(peerNumber, source))
                peerNumber.setRangeFrom(newMin, newMax, newPrecision, this);
        }
    }

    private static T snapToPrecision(T value, T step)
    {
        if (step <= T.Zero)
            return value;

        if (typeof(T) == typeof(float))
        {
            var fVal = float.CreateChecked(value);
            var fStep = float.CreateChecked(step);
            return T.CreateChecked(MathF.Round(fVal / fStep, MidpointRounding.AwayFromZero) * fStep);
        }

        if (typeof(T) == typeof(double))
        {
            var dVal = double.CreateChecked(value);
            var dStep = double.CreateChecked(step);
            return T.CreateChecked(Math.Round(dVal / dStep, MidpointRounding.AwayFromZero) * dStep);
        }

        var decVal = decimal.CreateChecked(value);
        var decStep = decimal.CreateChecked(step);
        return T.CreateChecked(Math.Round(decVal / decStep, MidpointRounding.AwayFromZero) * decStep);
    }

    private static T validateFinite(T value, string paramName)
    {
        if (T.IsNaN(value))
            throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be NaN.");

        return value;
    }
}
