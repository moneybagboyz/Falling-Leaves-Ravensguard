using Ravensguard.Sim.Core;

namespace Ravensguard.Sim.Tests.Core;

/// <summary>Fixed is range-checked (OverflowException, never silent wrap) and rounds toward negative infinity everywhere.</summary>
public class FixedRangeTests
{
    [Fact]
    public void FromInt_RangeIsInt16()
    {
        Assert.Equal(short.MaxValue, Fixed.FromInt(short.MaxValue).Floor());
        Assert.Equal(short.MinValue, Fixed.FromInt(short.MinValue).Floor());
        Assert.Throws<OverflowException>(() => Fixed.FromInt(32768));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(-32769));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(1 << 20));
    }

    [Fact]
    public void AddSubtractNegate_Overflow_Throws()
    {
        Assert.Throws<OverflowException>(() => Fixed.MaxValue + Fixed.One);
        Assert.Throws<OverflowException>(() => Fixed.MinValue - Fixed.One);
        Assert.Throws<OverflowException>(() => -Fixed.MinValue);
        Assert.Throws<OverflowException>(() => Fixed.MinValue.Abs());
        Assert.Equal(Fixed.MaxValue, Fixed.MaxValue + Fixed.Zero);
        Assert.Equal(Fixed.FromRaw(int.MinValue + 1).Raw, -Fixed.MaxValue.Raw);
    }

    [Fact]
    public void MultiplyDivide_Overflow_Throws()
    {
        Assert.Throws<OverflowException>(() => Fixed.FromInt(20000) * Fixed.FromInt(20000));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(30000) / Fixed.FromRatio(1, 4));
        Assert.Throws<OverflowException>(() => Fixed.MinValue / -1);
        Assert.Throws<OverflowException>(() => Fixed.MinValue / Fixed.FromInt(-1));
        Assert.Throws<OverflowException>(() => Fixed.FromInt(30000) * 3);
        Assert.Throws<OverflowException>(() => Fixed.Lerp(Fixed.FromInt(-30000), Fixed.FromInt(30000), Fixed.FromInt(2)));
        Assert.Throws<DivideByZeroException>(() => Fixed.One / Fixed.Zero);
        Assert.Throws<OverflowException>(() => Fixed.FromRatio(int.MaxValue, 1));
    }

    [Fact]
    public void FromRatio_RoundsTowardNegativeInfinity()
    {
        // 65536/3 = 21845.33: positive floors to 21845, negative floors to -21846 (documented asymmetry)
        Assert.Equal(21845, Fixed.FromRatio(1, 3).Raw);
        Assert.Equal(-21846, Fixed.FromRatio(-1, 3).Raw);
        Assert.Equal(-21846, Fixed.FromRatio(1, -3).Raw);
        Assert.Equal(21845, Fixed.FromRatio(-1, -3).Raw);
        Assert.Equal(-32768, Fixed.FromRatio(-1, 2).Raw);  // exact: no rounding
    }

    [Fact]
    public void Divide_RoundsTowardNegativeInfinity()
    {
        Assert.Equal(21845, (Fixed.One / Fixed.FromInt(3)).Raw);
        Assert.Equal(-21846, (Fixed.FromInt(-1) / Fixed.FromInt(3)).Raw);
        Assert.Equal(-21846, (Fixed.One / Fixed.FromInt(-3)).Raw);
        Assert.Equal(21845, (Fixed.FromInt(-1) / Fixed.FromInt(-3)).Raw);
        Assert.Equal(21845, (Fixed.One / 3).Raw);
        Assert.Equal(-21846, (Fixed.FromInt(-1) / 3).Raw);
        Assert.Equal(-1, (Fixed.FromRaw(-1) / 2).Raw);
        Assert.Equal(0, (Fixed.FromRaw(1) / 2).Raw);
    }

    [Fact]
    public void Multiply_RoundsTowardNegativeInfinity()
    {
        var tiny = Fixed.FromRaw(1);
        Assert.Equal(0, (tiny * Fixed.Half).Raw);
        Assert.Equal(-1, (Fixed.FromRaw(-1) * Fixed.Half).Raw);
        Assert.Equal(-1, (tiny * Fixed.FromRaw(-32768)).Raw);
        Assert.Equal(0, (Fixed.FromRaw(-1) * Fixed.FromRaw(-32768)).Raw);
    }
}
