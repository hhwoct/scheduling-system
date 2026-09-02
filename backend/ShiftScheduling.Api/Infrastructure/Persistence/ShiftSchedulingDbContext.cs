using Microsoft.EntityFrameworkCore;
using ShiftScheduling.Api.Infrastructure.Persistence.Entities;

namespace ShiftScheduling.Api.Infrastructure.Persistence;

public sealed class ShiftSchedulingDbContext : DbContext
{
    public ShiftSchedulingDbContext(DbContextOptions<ShiftSchedulingDbContext> options) : base(options)
    {
    }

    public DbSet<StoreEntity> Stores => Set<StoreEntity>();

    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    public DbSet<WorkstationEntity> Workstations => Set<WorkstationEntity>();

    public DbSet<ShiftTemplateEntity> ShiftTemplates => Set<ShiftTemplateEntity>();

    public DbSet<ShiftWorkstationEntity> ShiftWorkstations => Set<ShiftWorkstationEntity>();

    public DbSet<EmployeeSkillEntity> EmployeeSkills => Set<EmployeeSkillEntity>();

    public DbSet<RuleConfigEntity> RuleConfigs => Set<RuleConfigEntity>();

    public DbSet<DateParameterEntity> DateParameters => Set<DateParameterEntity>();

    public DbSet<StaffingRequirementEntity> StaffingRequirements => Set<StaffingRequirementEntity>();

    public DbSet<SchedulePlanEntity> SchedulePlans => Set<SchedulePlanEntity>();

    public DbSet<ScheduleResultEntity> ScheduleResults => Set<ScheduleResultEntity>();

    public DbSet<ScheduleSummaryEntity> ScheduleSummaries => Set<ScheduleSummaryEntity>();

    public DbSet<ScheduleIssueEntity> ScheduleIssues => Set<ScheduleIssueEntity>();

    public DbSet<NotificationEntity> Notifications => Set<NotificationEntity>();

    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    public DbSet<LeaveRequestEntity> LeaveRequests => Set<LeaveRequestEntity>();

    public DbSet<ShiftSwapEntity> ShiftSwaps => Set<ShiftSwapEntity>();

    public DbSet<PeakRestrictedHourEntity> PeakRestrictedHours => Set<PeakRestrictedHourEntity>();

    public DbSet<AiConfigEntity> AiConfigs => Set<AiConfigEntity>();

    public DbSet<EmployeePreferenceEntity> EmployeePreferences => Set<EmployeePreferenceEntity>();

    public DbSet<PreferenceTrendEntity> PreferenceTrends => Set<PreferenceTrendEntity>();

    public DbSet<ScheduleAdjustmentEntity> ScheduleAdjustments => Set<ScheduleAdjustmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreEntity>(entity =>
        {
            entity.ToTable("stores");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Code).HasColumnName("code");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Address).HasColumnName("address");
            entity.Property(x => x.MaxEmployeeCount).HasColumnName("max_employee_count");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
            entity.Property(x => x.Nickname).HasColumnName("nickname").HasMaxLength(100);
            entity.Property(x => x.Role).HasColumnName("role").HasMaxLength(50);
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.PasswordVersion).HasColumnName("password_version").IsConcurrencyToken();
            entity.HasIndex(x => x.Username).IsUnique();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<EmployeeEntity>(entity =>
        {
            entity.ToTable("employees");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.EmployeeNo).HasColumnName("employee_no").HasMaxLength(50).IsRequired();
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(30);
            entity.Property(x => x.Department).HasColumnName("department").HasMaxLength(50);
            entity.Property(x => x.HireDate).HasColumnName("hire_date");
            entity.Property(x => x.PrimaryPosition).HasColumnName("primary_position");
            entity.Property(x => x.MaxWeeklyHours).HasColumnName("max_weekly_hours").HasPrecision(5, 2);
            entity.Property(x => x.WeeklyHoursFollowDefault).HasColumnName("weekly_hours_follow_default");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.IsParttime).HasColumnName("is_parttime");
            entity.Property(x => x.IsGeneralist).HasColumnName("is_generalist");
            entity.HasIndex(x => new { x.StoreId, x.EmployeeNo }).IsUnique();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<WorkstationEntity>(entity =>
        {
            entity.ToTable("workstations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Code).HasColumnName("code");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            entity.Property(x => x.IsLowSkill).HasColumnName("is_low_skill");
            entity.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
            entity.Property(x => x.Remark).HasColumnName("remark");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ShiftTemplateEntity>(entity =>
        {
            entity.ToTable("shift_templates");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Code).HasColumnName("code");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.StartTime).HasColumnName("start_time");
            entity.Property(x => x.EndTime).HasColumnName("end_time");
            entity.Property(x => x.IsCrossDay).HasColumnName("is_cross_day");
            entity.Property(x => x.Priority).HasColumnName("priority");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ShiftWorkstationEntity>(entity =>
        {
            entity.ToTable("shift_workstations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ShiftTemplateId).HasColumnName("shift_template_id");
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(x => new { x.ShiftTemplateId, x.WorkstationId }).IsUnique();
        });

        modelBuilder.Entity<EmployeeSkillEntity>(entity =>
        {
            entity.ToTable("employee_skills");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.SkillScore).HasColumnName("skill_score");
            entity.HasIndex(x => new { x.EmployeeId, x.WorkstationId }).IsUnique();
            entity.Property(x => x.IsPrimarySkill).HasColumnName("is_primary_skill");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<RuleConfigEntity>(entity =>
        {
            entity.ToTable("rule_configs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.RuleKey).HasColumnName("rule_key");
            entity.Property(x => x.RuleName).HasColumnName("rule_name");
            entity.HasIndex(x => new { x.StoreId, x.RuleKey }).IsUnique();
            entity.Property(x => x.RuleValue).HasColumnName("rule_value");
            entity.Property(x => x.ValueType).HasColumnName("value_type");
            entity.Property(x => x.Remark).HasColumnName("remark");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<DateParameterEntity>(entity =>
        {
            entity.ToTable("date_parameters");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.WorkDate).HasColumnName("work_date");
            entity.Property(x => x.WeekDay).HasColumnName("week_day");
            entity.HasIndex(x => new { x.StoreId, x.WorkDate }).IsUnique();
            entity.Property(x => x.DayType).HasColumnName("day_type");
            entity.Property(x => x.IsLegalHoliday).HasColumnName("is_legal_holiday");
            entity.Property(x => x.IsHolidayEve).HasColumnName("is_holiday_eve");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<StaffingRequirementEntity>(entity =>
        {
            entity.ToTable("staffing_requirements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DayType).HasColumnName("day_type");
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.TimeSlot).HasColumnName("time_slot");
            entity.Property(x => x.RequiredCount).HasColumnName("required_count");
            entity.HasIndex(x => new { x.StoreId, x.DayType, x.WorkstationId, x.TimeSlot }).IsUnique();
            entity.Property(x => x.IdealCount).HasColumnName("ideal_count");
            entity.Property(x => x.Remark).HasColumnName("remark").HasMaxLength(200);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<SchedulePlanEntity>(entity =>
        {
            entity.ToTable("schedule_plans");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.PlanName).HasColumnName("plan_name");
            entity.Property(x => x.StartDate).HasColumnName("start_date");
            entity.Property(x => x.EndDate).HasColumnName("end_date");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.Source).HasColumnName("source");
            entity.HasIndex(x => new { x.StoreId, x.PlanName }).IsUnique();
            // 同门店同周期唯一：加入 source 后允许「真实班表(REAL)」与「算法排班(ALGO)」同周期并存以便对比
            entity.HasIndex(x => new { x.StoreId, x.StartDate, x.EndDate, x.Source }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.Status });
            entity.Property(x => x.CreatedBy).HasColumnName("created_by");
            entity.Property(x => x.PublishedAt).HasColumnName("published_at");
            entity.Property(x => x.GeneratedSummarySnapshot).HasColumnName("generated_summary_snapshot");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ScheduleResultEntity>(entity =>
        {
            entity.ToTable("schedule_results");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.WorkDate).HasColumnName("work_date");
            entity.Property(x => x.ShiftTemplateId).HasColumnName("shift_template_id");
            entity.Property(x => x.TimeSlot).HasColumnName("time_slot");
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.SkillScore).HasColumnName("skill_score");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            entity.HasIndex(x => new { x.PlanId, x.WorkDate });
            entity.HasIndex(x => new { x.EmployeeId, x.WorkDate });
            entity.HasIndex(x => new { x.WorkstationId, x.WorkDate, x.TimeSlot });
            entity.HasIndex(x => new { x.PlanId, x.EmployeeId, x.WorkDate, x.TimeSlot }).IsUnique();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ScheduleSummaryEntity>(entity =>
        {
            entity.ToTable("schedule_summaries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.WorkDate).HasColumnName("work_date");
            entity.Property(x => x.IsRestDay).HasColumnName("is_rest_day");
            entity.HasIndex(x => new { x.PlanId, x.EmployeeId, x.WorkDate }).IsUnique();
            entity.HasIndex(x => new { x.PlanId, x.WorkDate });
            entity.Property(x => x.ShiftTemplateId).HasColumnName("shift_template_id");
            entity.Property(x => x.StartTime).HasColumnName("start_time");
            entity.Property(x => x.EndTime).HasColumnName("end_time");
            entity.Property(x => x.WorkHours).HasColumnName("work_hours").HasPrecision(5, 2);
            entity.Property(x => x.CoveredWorkstations).HasColumnName("covered_workstations");
            entity.Property(x => x.BreakStartTime).HasColumnName("break_start_time");
            entity.Property(x => x.BreakEndTime).HasColumnName("break_end_time");
            entity.Property(x => x.BreakCoverEmployeeId).HasColumnName("break_cover_employee_id");
            entity.Property(x => x.BreakWorkstationId).HasColumnName("break_workstation_id");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ScheduleIssueEntity>(entity =>
        {
            entity.ToTable("schedule_issues");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.IssueType).HasColumnName("issue_type");
            entity.Property(x => x.Severity).HasColumnName("severity");
            entity.Property(x => x.WorkDate).HasColumnName("work_date");
            entity.Property(x => x.TimeSlot).HasColumnName("time_slot");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.Description).HasColumnName("description");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.HasIndex(x => new { x.PlanId, x.IssueType });
            entity.HasIndex(x => new { x.WorkDate, x.TimeSlot });
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<NotificationEntity>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.ReceiverUserId).HasColumnName("receiver_user_id");
            entity.Property(x => x.ReceiverEmployeeId).HasColumnName("receiver_employee_id");
            entity.Property(x => x.NotificationType).HasColumnName("notification_type");
            entity.Property(x => x.Title).HasColumnName("title");
            entity.Property(x => x.Content).HasColumnName("content");
            entity.Property(x => x.IsRead).HasColumnName("is_read");
            entity.HasIndex(x => new { x.StoreId, x.IsRead });
            entity.HasIndex(x => x.ReceiverUserId);
            entity.HasIndex(x => x.ReceiverEmployeeId);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.ReadAt).HasColumnName("read_at");
        });

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.OperatorUserId).HasColumnName("operator_user_id");
            entity.Property(x => x.OperatorName).HasColumnName("operator_name");
            entity.Property(x => x.ActionType).HasColumnName("action_type");
            entity.Property(x => x.TargetType).HasColumnName("target_type");
            entity.Property(x => x.TargetId).HasColumnName("target_id");
            entity.Property(x => x.BeforeContent).HasColumnName("before_content");
            entity.Property(x => x.AfterContent).HasColumnName("after_content");
            entity.Property(x => x.Remark).HasColumnName("remark");
            entity.HasIndex(x => new { x.StoreId, x.CreatedAt });
            entity.HasIndex(x => x.ActionType);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<LeaveRequestEntity>(entity =>
        {
            entity.ToTable("leave_requests");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.LeaveType).HasColumnName("leave_type");
            entity.Property(x => x.StartDate).HasColumnName("start_date");
            entity.Property(x => x.EndDate).HasColumnName("end_date");
            entity.Property(x => x.Reason).HasColumnName("reason");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.ReviewUserId).HasColumnName("review_user_id");
            entity.Property(x => x.ReviewTime).HasColumnName("review_time");
            entity.Property(x => x.ReviewRemark).HasColumnName("review_remark");
            entity.Property(x => x.EarlyReturned).HasColumnName("early_returned");
            entity.HasIndex(x => new { x.EmployeeId, x.StartDate });
            entity.HasIndex(x => new { x.StoreId, x.Status });
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => new { x.EmployeeId, x.StartDate, x.EndDate });
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<ShiftSwapEntity>(entity =>
        {
            entity.ToTable("shift_swaps");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.RequesterEmployeeId).HasColumnName("requester_employee_id");
            entity.Property(x => x.TargetEmployeeId).HasColumnName("target_employee_id");
            entity.Property(x => x.SwapDate).HasColumnName("swap_date");
            entity.Property(x => x.Reason).HasColumnName("reason");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.ReviewUserId).HasColumnName("review_user_id");
            entity.Property(x => x.ReviewTime).HasColumnName("review_time");
            entity.Property(x => x.ReviewRemark).HasColumnName("review_remark");
            entity.HasIndex(x => new { x.RequesterEmployeeId, x.Status });
            entity.HasIndex(x => new { x.StoreId, x.Status });
            entity.HasIndex(x => new { x.PlanId, x.SwapDate });
            entity.HasIndex(x => x.TargetEmployeeId);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<PeakRestrictedHourEntity>(entity =>
        {
            entity.ToTable("peak_restricted_hours");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.StartTime).HasColumnName("start_time");
            entity.Property(x => x.EndTime).HasColumnName("end_time");
            entity.Property(x => x.Status).HasColumnName("status");
            entity.HasIndex(x => x.StoreId);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<AiConfigEntity>(entity =>
        {
            entity.ToTable("ai_configs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(30);
            entity.Property(x => x.ApiKey).HasColumnName("api_key").HasMaxLength(255);
            entity.HasIndex(x => new { x.StoreId, x.Provider }).IsUnique();
            entity.Property(x => x.BaseUrl).HasColumnName("base_url").HasMaxLength(255);
            entity.Property(x => x.Model).HasColumnName("model").HasMaxLength(80);
            entity.Property(x => x.Status).HasColumnName("status");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<EmployeePreferenceEntity>(entity =>
        {
            entity.ToTable("employee_preferences");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.DayType).HasColumnName("day_type").HasMaxLength(20);
            entity.Property(x => x.ShiftCode).HasColumnName("shift_code").HasMaxLength(20);
            entity.Property(x => x.WorkstationId).HasColumnName("workstation_id");
            entity.Property(x => x.Freq).HasColumnName("freq");
            entity.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            entity.HasIndex(x => new { x.StoreId, x.EmployeeId, x.DayType, x.ShiftCode, x.WorkstationId }).IsUnique();
        });

        modelBuilder.Entity<PreferenceTrendEntity>(entity =>
        {
            entity.ToTable("preference_trends");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.PlanName).HasColumnName("plan_name").HasMaxLength(200);
            entity.Property(x => x.PublishedAt).HasColumnName("published_at");
            entity.Property(x => x.AdherencePct).HasColumnName("adherence_pct");
            entity.Property(x => x.CoveragePct).HasColumnName("coverage_pct");
            entity.Property(x => x.SampleDays).HasColumnName("sample_days");
            entity.Property(x => x.Adjustments).HasColumnName("adjustments");
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(x => new { x.StoreId, x.PlanId }).IsUnique();
        });

        modelBuilder.Entity<ScheduleAdjustmentEntity>(entity =>
        {
            entity.ToTable("schedule_adjustments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.PlanId).HasColumnName("plan_id");
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.WorkDate).HasColumnName("work_date");
            entity.Property(x => x.TimeSlot).HasColumnName("time_slot");
            entity.Property(x => x.ActionType).HasColumnName("action_type").HasMaxLength(30);
            entity.Property(x => x.BeforeJson).HasColumnName("before_json").HasMaxLength(1000);
            entity.Property(x => x.AfterJson).HasColumnName("after_json").HasMaxLength(1000);
            entity.Property(x => x.OperatorUserId).HasColumnName("operator_user_id");
            entity.Property(x => x.OperatorName).HasColumnName("operator_name").HasMaxLength(50);
            entity.Property(x => x.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(x => new { x.PlanId, x.CreatedAt });
        });
    }
}
