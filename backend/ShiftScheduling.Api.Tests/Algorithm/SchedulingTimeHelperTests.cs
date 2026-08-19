using ShiftScheduling.Api.Algorithm;

namespace ShiftScheduling.Api.Tests.Algorithm;

/// <summary>排班时间工具（时段/工时/跨天判定）单元测试。</summary>
public sealed class SchedulingTimeHelperTests
{
    [Fact]
    public void GetShiftSlots_NormalShift_ReturnsHalfHourSlotsExcludingEnd()
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(new TimeSpan(13, 0, 0), new TimeSpan(15, 0, 0), 0);
        Assert.Equal(4, slots.Count);
        Assert.Equal(new TimeSpan(13, 0, 0), slots[0]);
        Assert.Equal(new TimeSpan(14, 30, 0), slots[^1]);
    }

    [Fact]
    public void GetShiftSlots_CrossDay_ReturnsSlotsAcrossMidnight()
    {
        var slots = SchedulingTimeHelper.GetShiftSlots(new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0), 1);
        Assert.Equal(8, slots.Count);
        Assert.Contains(TimeSpan.Zero, slots);
        Assert.Contains(new TimeSpan(1, 30, 0), slots);
        Assert.DoesNotContain(new TimeSpan(2, 0, 0), slots);
    }

    [Fact]
    public void GetShiftSlots_EndExactly24h_IsNotTreatedAsExtraDay()
    {
        // 结束时间 24:00 归一化为当日边界（修复回归）
        var slots = SchedulingTimeHelper.GetShiftSlots(new TimeSpan(20, 0, 0), TimeSpan.FromHours(24), 1);
        Assert.Equal(8, slots.Count);
        Assert.Equal(new TimeSpan(23, 30, 0), slots[^1]);
    }

    [Theory]
    [InlineData(18, 0, 3, 0, 9.0)]   // 18:00-03:00 跨天 9 小时
    [InlineData(13, 0, 22, 0, 9.0)]  // 13:00-22:00 当天 9 小时
    [InlineData(20, 0, 24, 0, 4.0)]  // 20:00-24:00 4 小时
    public void GetShiftHours_ComputesCorrectDuration(int startH, int startM, int endH, int endM, decimal expected)
    {
        var hours = SchedulingTimeHelper.GetShiftHours(
            new TimeSpan(startH, startM, 0),
            new TimeSpan(endH, endM, 0),
            isCrossDay: endH < startH || (startH >= 20 && endH <= 6) ? 1 : 0);
        Assert.Equal(expected, hours);
    }

    [Theory]
    [InlineData(18, 0, 3, 0, true)]   // 结束早于开始 → 跨天
    [InlineData(22, 0, 2, 0, true)]   // 晚间开始凌晨结束 → 跨天
    [InlineData(13, 0, 22, 0, false)] // 正常白天班
    public void IsCrossDay_DetectsCorrectly(int startH, int startM, int endH, int endM, bool expected)
    {
        Assert.Equal(expected, SchedulingTimeHelper.IsCrossDay(new TimeSpan(startH, startM, 0), new TimeSpan(endH, endM, 0)));
    }

    [Fact]
    public void SlotCalendarDate_MidnightWrapBelongsToNextDay()
    {
        // 22:00-02:00 班次 8/10 开始：00:00 时段属于 8/11
        var date = new DateOnly(2026, 8, 10);
        Assert.Equal(new DateOnly(2026, 8, 10), SchedulingTimeHelper.SlotCalendarDate(new TimeSpan(22, 0, 0), new TimeSpan(22, 0, 0), date));
        Assert.Equal(new DateOnly(2026, 8, 11), SchedulingTimeHelper.SlotCalendarDate(TimeSpan.Zero, new TimeSpan(22, 0, 0), date));
        Assert.Equal(new DateOnly(2026, 8, 11), SchedulingTimeHelper.SlotCalendarDate(new TimeSpan(1, 30, 0), new TimeSpan(22, 0, 0), date));
    }

    [Fact]
    public void IsNightShift_CrossDayOrLateEndIsNight()
    {
        var crossDay = new ShiftTemplateInput(1, "N", "夜班", new TimeSpan(22, 0, 0), new TimeSpan(2, 0, 0), 1, 1, new List<long> { 1 });
        var late = new ShiftTemplateInput(2, "L", "晚班", new TimeSpan(13, 0, 0), new TimeSpan(22, 0, 0), 0, 1, new List<long> { 1 });
        var day = new ShiftTemplateInput(3, "D", "白班", new TimeSpan(8, 0, 0), new TimeSpan(16, 0, 0), 0, 1, new List<long> { 1 });

        Assert.True(SchedulingTimeHelper.IsNightShift(crossDay));
        Assert.True(SchedulingTimeHelper.IsNightShift(late));
        Assert.False(SchedulingTimeHelper.IsNightShift(day));
    }
}
