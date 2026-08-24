using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>
/// 偏好学习连续权重（EffectiveScore）单元测试（0-1 语义，20260829 v2）：
/// 综合分 = 技能分×(1−w) + 偏好归一化分×w，偏好频次（封顶 10）按 skillMax 归一化到技能分标尺。
/// w = 0（默认）必须等于纯技能分（未启用行为不变）；w = 1 完全按偏好。
/// </summary>
public sealed class PreferenceScoringTests
{
    private const int SkillMax = PreferenceScoring.SingleStationSkillMax; // 5

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public void EffectiveScore_WeightZero_EqualsPureSkillScore(int skillScore)
    {
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 10, 0m, SkillMax));
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 3, 0m, SkillMax));
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 0, 0m, SkillMax));
        Assert.Equal(skillScore, PreferenceScoring.EffectiveScore(skillScore, 10, 0m, 0));
    }

    [Fact]
    public void EffectiveScore_WeightOne_IgnoresSkillScore()
    {
        // w = 1：完全按偏好（偏好满分 = skillMax，任何技能分都不参与）
        Assert.Equal(10m / 10m * SkillMax, PreferenceScoring.EffectiveScore(5, 10, 1m, SkillMax));
        Assert.Equal(10m / 10m * SkillMax, PreferenceScoring.EffectiveScore(0, 10, 1m, SkillMax));
        Assert.Equal(5m / 10m * SkillMax, PreferenceScoring.EffectiveScore(5, 5, 1m, SkillMax));
        // 偏好相同 → 平局（技能分被完全忽略）
        Assert.Equal(
            PreferenceScoring.EffectiveScore(5, 8, 1m, SkillMax),
            PreferenceScoring.EffectiveScore(1, 8, 1m, SkillMax));
    }

    [Fact]
    public void EffectiveScore_MidWeight_LinearBlend()
    {
        // 技能 4、偏好 10（归一化 5）、w=0.5：4×0.5 + 5×0.5 = 4.5
        Assert.Equal(4m * 0.5m + 5m * 0.5m, PreferenceScoring.EffectiveScore(4, 10, 0.5m, SkillMax));
        // 技能 5、偏好 0、w=0.3：5×0.7 + 0 = 3.5
        Assert.Equal(5m * 0.7m, PreferenceScoring.EffectiveScore(5, 0, 0.3m, SkillMax));
        // 无偏好时权重不产生任何加分
        Assert.Equal(4m * 0.5m, PreferenceScoring.EffectiveScore(4, 0, 0.5m, SkillMax));
    }

    [Fact]
    public void EffectiveScore_PreferenceCappedAtTen()
    {
        // 频次 15 与 10 同分：封顶 10
        Assert.Equal(
            PreferenceScoring.EffectiveScore(3, 10, 0.5m, SkillMax),
            PreferenceScoring.EffectiveScore(3, 15, 0.5m, SkillMax));
    }

    [Fact]
    public void EffectiveScore_WeightClampedToUnitInterval()
    {
        // 负权重按 0 处理 = 纯技能分
        Assert.Equal(5m, PreferenceScoring.EffectiveScore(5, 8, -0.3m, SkillMax));
        // >1 的权重按 1 处理 = 完全按偏好
        Assert.Equal(10m / 10m * SkillMax, PreferenceScoring.EffectiveScore(5, 10, 2m, SkillMax));
    }

    [Fact]
    public void EffectiveScore_WeightScalesPreferenceDominance()
    {
        // 技能 5 vs 技能 3 + 偏好满：权重小时技能者胜，权重大时偏好者胜
        Assert.True(PreferenceScoring.EffectiveScore(5, 0, 0.1m, SkillMax) > PreferenceScoring.EffectiveScore(3, 10, 0.1m, SkillMax));
        Assert.True(PreferenceScoring.EffectiveScore(5, 0, 0.5m, SkillMax) < PreferenceScoring.EffectiveScore(3, 10, 0.5m, SkillMax));
        // w=1：技能 5 无偏好者也输给任何有偏好的候选（完全按偏好）
        Assert.True(PreferenceScoring.EffectiveScore(5, 0, 1m, SkillMax) < PreferenceScoring.EffectiveScore(1, 1, 1m, SkillMax));
    }

    [Fact]
    public void EffectiveScore_SkillMaxScalesPreferenceNorm()
    {
        // 班次覆盖多岗（ShiftAllocator 总分上限 = 岗数×5）：偏好归一化到同一标尺
        const int multiSkillMax = 3 * SkillMax; // 覆盖 3 岗
        Assert.Equal(12m * 0.6m + 15m * 0.4m, PreferenceScoring.EffectiveScore(12, 10, 0.4m, multiSkillMax));
        Assert.Equal(15m, PreferenceScoring.EffectiveScore(3, 10, 1m, multiSkillMax)); // 偏好满分 = 标尺上限
        // 无偏好：混合后仍等于纯技能分×权重系数——权重不影响无偏好者之间的相对顺序
        Assert.Equal(12m * 0.6m, PreferenceScoring.EffectiveScore(12, 0, 0.4m, multiSkillMax));
    }
}
