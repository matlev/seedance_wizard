using System.Numerics;

namespace ReelForge.Core;

/// <summary>
/// The explicitly selected rule for converting an exact time into an integer clock domain.
/// </summary>
public enum ExactTimeRounding
{
    Floor,
    Ceiling,
    NearestTiesToEven
}

/// <summary>
/// A canonical rational number of seconds. This is portable project meaning; double seconds are display-only.
/// </summary>
public sealed record ExactTime : IComparable<ExactTime>
{
    public ExactTime(long numerator, long denominator)
        : this(Normalize(numerator, denominator))
    {
    }

    public long Numerator { get; }

    public long Denominator { get; }

    public int CompareTo(ExactTime? other)
    {
        if (other is null)
        {
            return 1;
        }

        var left = (BigInteger)Numerator * other.Denominator;
        var right = (BigInteger)other.Numerator * Denominator;
        return left.CompareTo(right);
    }

    public static bool operator <(ExactTime left, ExactTime right) => left.CompareTo(right) < 0;

    public static bool operator <=(ExactTime left, ExactTime right) => left.CompareTo(right) <= 0;

    public static bool operator >(ExactTime left, ExactTime right) => left.CompareTo(right) > 0;

    public static bool operator >=(ExactTime left, ExactTime right) => left.CompareTo(right) >= 0;

    public static ExactTime operator +(ExactTime left, ExactTime right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return FromBigInteger(
            ((BigInteger)left.Numerator * right.Denominator) +
            ((BigInteger)right.Numerator * left.Denominator),
            (BigInteger)left.Denominator * right.Denominator);
    }

    public static ExactTime operator -(ExactTime left, ExactTime right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return FromBigInteger(
            ((BigInteger)left.Numerator * right.Denominator) -
            ((BigInteger)right.Numerator * left.Denominator),
            (BigInteger)left.Denominator * right.Denominator);
    }

    /// <summary>
    /// Converts this value to an integer clock whose units-per-second are supplied by the caller.
    /// </summary>
    public long RescaleToInteger(long unitsPerSecond, ExactTimeRounding rounding)
    {
        if (unitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitsPerSecond), "Units per second must be positive.");
        }

        var scaledNumerator = (BigInteger)Numerator * unitsPerSecond;
        var scaledDenominator = (BigInteger)Denominator;
        var rounded = rounding switch
        {
            ExactTimeRounding.Floor => FloorDivide(scaledNumerator, scaledDenominator),
            ExactTimeRounding.Ceiling => CeilingDivide(scaledNumerator, scaledDenominator),
            ExactTimeRounding.NearestTiesToEven => RoundToNearestTiesToEven(scaledNumerator, scaledDenominator),
            _ => throw new ArgumentOutOfRangeException(nameof(rounding), rounding, "Unknown time rounding policy.")
        };

        return ToInt64Checked(rounded);
    }

    /// <summary>
    /// Returns an approximation for display only. It must not be used to reconstruct project time.
    /// </summary>
    public double ToDoubleSeconds() => Numerator / (double)Denominator;

    internal static ExactTime FromBigInteger(BigInteger numerator, BigInteger denominator)
        => new(Normalize(numerator, denominator));

    private ExactTime((long Numerator, long Denominator) normalized)
    {
        Numerator = normalized.Numerator;
        Denominator = normalized.Denominator;
    }

    private static (long Numerator, long Denominator) Normalize(BigInteger numerator, BigInteger denominator)
    {
        if (denominator <= BigInteger.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "The denominator must be positive.");
        }

        if (numerator.IsZero)
        {
            return (0, 1);
        }

        var divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        return (ToInt64Checked(numerator / divisor), ToInt64Checked(denominator / divisor));
    }

    private static BigInteger FloorDivide(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - BigInteger.One : quotient;
    }

    private static BigInteger CeilingDivide(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        return remainder.Sign > 0 ? quotient + BigInteger.One : quotient;
    }

    private static BigInteger RoundToNearestTiesToEven(BigInteger numerator, BigInteger denominator)
    {
        var floor = FloorDivide(numerator, denominator);
        var remainder = numerator - (floor * denominator);
        var twiceRemainder = remainder * 2;
        var comparison = twiceRemainder.CompareTo(denominator);

        if (comparison < 0)
        {
            return floor;
        }

        var ceiling = floor + BigInteger.One;
        if (comparison > 0)
        {
            return ceiling;
        }

        return floor.IsEven ? floor : ceiling;
    }

    private static long ToInt64Checked(BigInteger value)
    {
        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new OverflowException("The exact time result cannot be represented by a signed 64-bit integer.");
        }

        return (long)value;
    }
}

/// <summary>
/// A source-native video presentation timestamp and its inspected time base.
/// </summary>
public sealed record VideoPresentationTime
{
    public VideoPresentationTime(long presentationTimestamp, int timeBaseNumerator, int timeBaseDenominator)
    {
        if (timeBaseNumerator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBaseNumerator), "The time-base numerator must be positive.");
        }

        if (timeBaseDenominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeBaseDenominator), "The time-base denominator must be positive.");
        }

        PresentationTimestamp = presentationTimestamp;
        TimeBaseNumerator = timeBaseNumerator;
        TimeBaseDenominator = timeBaseDenominator;
    }

    public long PresentationTimestamp { get; }

    public int TimeBaseNumerator { get; }

    public int TimeBaseDenominator { get; }

    public ExactTime ToExactTime() => ExactTime.FromBigInteger(
        (BigInteger)PresentationTimestamp * TimeBaseNumerator,
        TimeBaseDenominator);
}

/// <summary>
/// A source-native audio sample-frame offset and its selected stream sample rate.
/// </summary>
public sealed record AudioSampleTime
{
    public AudioSampleTime(long sampleFrameOffset, int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "The sample rate must be positive.");
        }

        SampleFrameOffset = sampleFrameOffset;
        SampleRate = sampleRate;
    }

    public long SampleFrameOffset { get; }

    public int SampleRate { get; }

    public ExactTime ToExactTime() => ExactTime.FromBigInteger(SampleFrameOffset, SampleRate);
}
