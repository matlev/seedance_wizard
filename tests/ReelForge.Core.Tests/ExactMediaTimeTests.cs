namespace ReelForge.Core.Tests;

public sealed class ExactMediaTimeTests
{
    [Fact]
    public void ExactTimeNormalizesAndUsesCanonicalEquality()
    {
        var reduced = new ExactTime(2, 4);

        Assert.Equal(new ExactTime(1, 2), reduced);
        Assert.Equal(1, reduced.Numerator);
        Assert.Equal(2, reduced.Denominator);
        Assert.Equal(new ExactTime(0, 1), new ExactTime(0, 99));
    }

    [Fact]
    public void ExactTimeComparesCrossDenominatorsWithoutOverflow()
    {
        var smaller = new ExactTime(long.MaxValue, long.MaxValue - 1);
        var larger = new ExactTime(long.MaxValue - 1, long.MaxValue - 2);

        Assert.True(smaller.CompareTo(larger) < 0);
        Assert.True(larger.CompareTo(smaller) > 0);
        Assert.Equal(0, new ExactTime(1, 2).CompareTo(new ExactTime(500_000_000, 1_000_000_000)));
    }

    [Fact]
    public void ExactTimeAddsAndSubtractsCrossDenominatorsExactly()
    {
        var left = new ExactTime(1, 3);
        var right = new ExactTime(1, 6);

        Assert.Equal(new ExactTime(1, 2), left + right);
        Assert.Equal(new ExactTime(1, 6), left - right);
        Assert.Equal(new ExactTime(-1, 6), right - left);
    }

    [Fact]
    public void ExactTimeArithmeticReducesBeforeCheckingInt64Range()
    {
        var value = new ExactTime(long.MaxValue, long.MaxValue - 1);

        Assert.Equal(new ExactTime(long.MaxValue, long.MaxValue - 1), value + new ExactTime(0, 1));
        Assert.Equal(new ExactTime(0, 1), value - value);
    }

    [Fact]
    public void ExactTimeArithmeticThrowsWhenReducedResultCannotFitInt64()
    {
        Assert.Throws<OverflowException>(() => new ExactTime(long.MaxValue, 1) + new ExactTime(1, 1));
        Assert.Throws<OverflowException>(() => new ExactTime(long.MinValue, 1) - new ExactTime(1, 1));
    }

    [Theory]
    [InlineData(1, 3, 10, ExactTimeRounding.Floor, 3)]
    [InlineData(1, 3, 10, ExactTimeRounding.Ceiling, 4)]
    [InlineData(1, 3, 10, ExactTimeRounding.NearestTiesToEven, 3)]
    [InlineData(-1, 3, 10, ExactTimeRounding.Floor, -4)]
    [InlineData(-1, 3, 10, ExactTimeRounding.Ceiling, -3)]
    [InlineData(-1, 3, 10, ExactTimeRounding.NearestTiesToEven, -3)]
    [InlineData(1, 4, 10, ExactTimeRounding.NearestTiesToEven, 2)]
    [InlineData(3, 4, 10, ExactTimeRounding.NearestTiesToEven, 8)]
    [InlineData(-1, 4, 10, ExactTimeRounding.NearestTiesToEven, -2)]
    public void ExactTimeRescalesWithExplicitRounding(
        long numerator,
        long denominator,
        long unitsPerSecond,
        ExactTimeRounding rounding,
        long expected)
    {
        Assert.Equal(expected, new ExactTime(numerator, denominator).RescaleToInteger(unitsPerSecond, rounding));
    }

    [Fact]
    public void AudioSampleTimesPreserve44100And48000DomainsExactly()
    {
        Assert.Equal(new ExactTime(1, 44_100), new AudioSampleTime(1, 44_100).ToExactTime());
        Assert.Equal(new ExactTime(1, 48_000), new AudioSampleTime(1, 48_000).ToExactTime());
        Assert.Equal(new ExactTime(1, 1), new AudioSampleTime(44_100, 44_100).ToExactTime());
    }

    [Fact]
    public void VideoPresentationTimePreservesNonUnitTimeBase()
    {
        var time = new VideoPresentationTime(3, 1001, 60_000).ToExactTime();

        Assert.Equal(new ExactTime(1001, 20_000), time);
    }

    [Fact]
    public void VideoPresentationTimeAllowsNegativePts()
    {
        Assert.Equal(new ExactTime(-1001, 20_000), new VideoPresentationTime(-3, 1001, 60_000).ToExactTime());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void ExactTimeRejectsNonPositiveDenominator(long numerator, long denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExactTime(numerator, denominator));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void VideoPresentationTimeRejectsInvalidTimeBase(int numerator, int denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoPresentationTime(0, numerator, denominator));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AudioSampleTimeRejectsInvalidSampleRate(int sampleRate)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleTime(0, sampleRate));
    }

    [Fact]
    public void ExactTimeThrowsWhenRescalingCannotFitInInt64()
    {
        Assert.Throws<OverflowException>(() => new ExactTime(long.MaxValue, 1)
            .RescaleToInteger(2, ExactTimeRounding.Floor));
    }

    [Fact]
    public void VideoPresentationTimeThrowsWhenExactResultCannotFitInInt64()
    {
        Assert.Throws<OverflowException>(() => new VideoPresentationTime(long.MaxValue, 2, 1).ToExactTime());
    }
}
