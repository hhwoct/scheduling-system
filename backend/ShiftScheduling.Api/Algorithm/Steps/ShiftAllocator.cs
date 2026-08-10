namespace ShiftScheduling.Api.Algorithm.Steps;

public sealed class ShiftAllocator
{
    private static readonly Dictionary<string, int> ShiftPriorityMap = new()
    {
        ["S7"] = 1,
        ["S8"] = 2,
        ["S2"] = 3,
        ["S3"] = 4,
        ["S1"] = 5,
        ["S6"] = 6,
        ["S4"] = 7,
        ["S5"] = 8
    };

    public IReadOnlyList<ShiftAssignment> Allocate(
        SchedulingInput input,
        IReadOnlyList<RestDayAssignment> restDays)
    {
        var assignments = new List<ShiftAssignment>();
        var restSet = restDays
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.WorkDate).ToHashSet());

        var skillsByEmployee = input.Skills
            .Where(x => x.SkillScore > 0)
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(s => s.WorkstationId, s => s.SkillScore));

        var dates = input.DateParameters
            .Where(x => x.WorkDate >= input.StartDate && x.WorkDate <= input.EndDate)
            .OrderBy(x => x.WorkDate)
            .ToList();

        var weeklyHours = new Dictionary<long, decimal>();
        foreach (var employee in input.Employees)
        {
            weeklyHours[employee.Id] = 0m;
        }

        foreach (var date in dates)
        {
            var workingEmployees = input.Employees
                .Where(e => !restSet.TryGetValue(e.Id, out var rest) || !rest.Contains(date.WorkDate))
                .ToList();

            var dayRequirements = CalculateDayRequirements(input, date);
            var assignedToday = new HashSet<long>();

            // 按稀缺班次优先级分配
            var orderedShifts = input.ShiftTemplates
                .OrderBy(s => ShiftPriorityMap.TryGetValue(s.Code, out var p) ? p : int.MaxValue)
                .ThenBy(s => s.Priority)
                .ToList();

            foreach (var shift in orderedShifts)
            {
                var required = dailyRequiredForShift(dayRequirements, shift);
                if (required <= 0)
                {
                    continue;
                }

                var candidates = workingEmployees
                    .Where(e => !assignedToday.Contains(e.Id))
                    .Where(e => HasSkillForShift(e.Id, shift, skillsByEmployee))
                    .OrderByDescending(e => SkillCoverage(e.Id, shift, skillsByEmployee) * 100 + MaxSkillScore(e.Id, shift, skillsByEmployee))
                    .ThenBy(e => weeklyHours.GetValueOrDefault(e.Id))
                    .ToList();

                foreach (var employee in candidates.Take(required))
                {
                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, shift.Id, shift.Code));
                    assignedToday.Add(employee.Id);
                    weeklyHours[employee.Id] =
                        weeklyHours.GetValueOrDefault(employee.Id) + SchedulingTimeHelper.GetShiftHours(shift.StartTime, shift.EndTime, shift.IsCrossDay);
                }
            }

            // 剩余员工分配到需求缺口最大的班次
            var unassigned = workingEmployees.Where(e => !assignedToday.Contains(e.Id)).ToList();
            foreach (var employee in unassigned)
            {
                var bestShift = input.ShiftTemplates
                    .Where(s => HasSkillForShift(employee.Id, s, skillsByEmployee))
                    .OrderByDescending(s => ShiftDemandScore(s, dayRequirements))
                    .ThenBy(s => SchedulingTimeHelper.GetShiftHours(s.StartTime, s.EndTime, s.IsCrossDay))
                    .FirstOrDefault();

                if (bestShift is not null)
                {
                    assignments.Add(new ShiftAssignment(employee.Id, date.WorkDate, bestShift.Id, bestShift.Code));
                    assignedToday.Add(employee.Id);
                    weeklyHours[employee.Id] =
                        weeklyHours.GetValueOrDefault(employee.Id) + SchedulingTimeHelper.GetShiftHours(bestShift.StartTime, bestShift.EndTime, bestShift.IsCrossDay);
                }
            }
        }

        return assignments;
    }

    private static bool HasSkillForShift(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return false;
        }

        return shift.WorkstationIds.Any(ws => skills.ContainsKey(ws));
    }

    private static int SkillCoverage(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return 0;
        }

        return shift.WorkstationIds.Count(ws => skills.ContainsKey(ws));
    }

    private static int MaxSkillScore(
        long employeeId,
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, Dictionary<long, int>> skillsByEmployee)
    {
        if (!skillsByEmployee.TryGetValue(employeeId, out var skills))
        {
            return 0;
        }

        return shift.WorkstationIds.Where(skills.ContainsKey).Max(ws => skills[ws]);
    }

    private static IReadOnlyDictionary<long, int> CalculateDayRequirements(
        SchedulingInput input,
        DateParameterInput date)
    {
        var dayType = date.DayType;
        return input.StaffingRequirements
            .Where(r => r.DayType == dayType)
            .GroupBy(r => r.WorkstationId)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.RequiredCount));
    }

    private static int dailyRequiredForShift(
        IReadOnlyDictionary<long, int> dayRequirements,
        ShiftTemplateInput shift)
    {
        return shift.WorkstationIds.Sum(ws => dayRequirements.GetValueOrDefault(ws));
    }

    private static int ShiftDemandScore(
        ShiftTemplateInput shift,
        IReadOnlyDictionary<long, int> dayRequirements)
    {
        return shift.WorkstationIds.Sum(ws => dayRequirements.GetValueOrDefault(ws));
    }
}
