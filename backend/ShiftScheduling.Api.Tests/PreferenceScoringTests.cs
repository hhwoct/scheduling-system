using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 偏好学习连续权重（EffectiveScore）单元测试：
/// 综合分 = 技能分 + 偏好认可频次（封顶 10）× 权重。
/// 权重 = 0（默认）时必须等于纯技能分，保证未启用时行为与原来完全一致。
/// </summary>
public sealed class PreferenceScoringTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(12)]
    public void EffectiveScore_WeightZero_EqualsPureSkillScore(int skillScore)
    {
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 10, 0m));
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 3, 0m));
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 0, 0m));
    }

    [Fact]
    public void EffectiveScore_PositiveWeight_AddsPreferenceTimesWeight()
    {
        Assert.Equal(5m + 10m * 0.3m, PreferenceScoring.EffectiveScore(5, 10, 0.3m));
        Assert.Equal(4m + 5m * 0.3m, PreferenceScoring.EffectiveScore(4, 5, 0.3m));
        Assert.Equal(2m + 7m * 1m, PreferenceScoring.EffectiveScore(2, 7, 1m));
    }

    [Fact]
    public void EffectiveScore_PreferenceCappedAtTen()
    {
        // 频次 15 与 10 同分：封顶 10
        Assert.Equal(PreferenceScoring.EffectiveScore(3, 10, 0.5m), PreferenceScoring.EffectiveScore(3, 15, 0.5m));
        Assert.Equal(3m + 10m * 0.5m, PreferenceScoring.EffectiveScore(3, 15, 0.5m));
    }

    [Fact]
    public void EffectiveScore_NegativeWeightOrFrequency_NoPenalty()
    {
        Assert.Equal(5m, PreferenceScoring.EffectiveScore(5, 8, -0.3m));
        Assert.Equal(5m, PreferenceScoring.EffectiveScore(5, -2, 0.3m));
    }

    [Fact]
    public void EffectiveScore_HigherWeight_MakesPreferenceFlipSkillGap()
    {
        // 权重小：技能高者仍胜（5+0 > 3+1）
        Assert.True(PreferenceScoring.EffectiveScore(5, 0, 0.1m) > PreferenceScoring.EffectiveScore(3, 10, 0.1m));
        // 权重大：认可 10 次的习惯可翻盘 1 分技能差
        Assert.True(PreferenceScoring.EffectiveScore(4, 10, 0.5m) > PreferenceScoring.EffectiveScore(5, 0, 0.5m));
        // 权重 0.3：认可 10 次 ≈ +3 分，仍不足以翻盘 4 分技能差
        Assert.True(PreferenceScoring.EffectiveScore(4, 10, 0.3m) < PreferenceScoring.EffectiveScore(8, 0, 0.3m));
    }
}
