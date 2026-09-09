using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Domain.Entities;

/// <summary>Unique conflict key used by frontend: (doctor_id, day_of_week).</summary>
public class DoctorSchedule : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public short DayOfWeek { get; set; } // 0-6
    public bool IsActive { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public Doctor? Doctor { get; set; }
}

public class DoctorBlockedDate
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly BlockedDate { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore] public Doctor? Doctor { get; set; }
}

/// <summary>Conflict key used by frontend: (doctor_id, status_date).</summary>
public class DoctorDayStatus : IHasUpdatedAt
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public DateOnly StatusDate { get; set; }
    public DoctorDayStatusEnum Status { get; set; }
    public int? RunningLateMinutes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore] public Doctor? Doctor { get; set; }
}
