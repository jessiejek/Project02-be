//JJQR - AI (Nov 21, 2026) cloud sync: which tables sync, under which aggregate name, and their root ids
using System.Globalization;
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicApp.Infrastructure.Sync;

public static class SyncAggregates
{
    // Pushable aggregates (node may write them).
    public const string Patient = "patient";
    public const string Booking = "booking";
    public const string Payment = "payment";
    public const string Consultation = "consultation";
    public const string VitalReading = "vital_reading";
    public const string AuditLog = "audit_log";

    //JJQR - AI (21 Nov 2026) consents captured on a node are pushed (append-only); they are still pulled via PullOnly below
    public const string PatientConsent = "patient_consent";
    //JJQR - END

    public static readonly IReadOnlySet<string> Pushable = new HashSet<string>
    {
        Patient, Booking, Payment, Consultation, VitalReading, AuditLog,
        PatientConsent //JJQR - AI (21 Nov 2026) // JJQR - END
    };

    //JJQR - AI (Nov 21, 2026) offline self-edits: still cloud-owned and pulled, but a node may push them for the doctor himself (or an admin)
    public const string Doctor = "doctor";
    public const string DoctorSchedule = "doctor_schedule";
    public const string DoctorBlockedDate = "doctor_blocked_date";

    //JJQR - AI (21 Nov 2026) G5: doctor_day_status is also pushable (and still pulled)
    public const string DoctorDayStatus = "doctor_day_status";
    //JJQR - END
    public static readonly IReadOnlySet<string> SelfEditable = new HashSet<string> { Doctor, DoctorSchedule, DoctorBlockedDate, DoctorDayStatus /*JJQR - AI (21 Nov 2026) G5*/ /*JJQR - END*/ };
    //JJQR - END

    /// <summary>Cloud-owned (pull-only) aggregates: one table each, the row is the document.</summary>
    public static readonly IReadOnlyDictionary<string, Type> PullOnly = new Dictionary<string, Type>
    {
        ["clinic_setting"] = typeof(ClinicSetting),
        ["clinic_operating_hour"] = typeof(ClinicOperatingHour),
        ["clinic_accepted_payment_method"] = typeof(ClinicAcceptedPaymentMethod),
        ["service"] = typeof(Service),
        ["doctor_service"] = typeof(DoctorService),
        ["doctor"] = typeof(Doctor),
        ["doctor_schedule"] = typeof(DoctorSchedule),
        ["doctor_blocked_date"] = typeof(DoctorBlockedDate),
        ["staff_account"] = typeof(StaffAccount),
        ["user"] = typeof(User),
        ["profile"] = typeof(Profile),
        ["vital_field_template"] = typeof(VitalFieldTemplate),
        ["medicine"] = typeof(Medicine),
        ["lab_test_catalog"] = typeof(LabTestCatalog),
        ["soap_phrase"] = typeof(SoapPhrase),
        ["soap_template"] = typeof(SoapTemplate),
        ["medical_certificate_template"] = typeof(MedicalCertificateTemplate),
        ["doctor_diagnosis_template"] = typeof(DoctorDiagnosisTemplate),
        ["prescription_template"] = typeof(PrescriptionTemplate),
        //JJQR - AI (Nov 21, 2026) lookup catalogs / pickers the desktop needs offline
        ["icd10_code"] = typeof(Icd10Code),
        ["doctor_favorite_medicine"] = typeof(DoctorFavoriteMedicine),
        ["doctor_builtin_template_copy"] = typeof(DoctorBuiltinTemplateCopy),
        ["announcement"] = typeof(Announcement),
        ["doctor_day_status"] = typeof(DoctorDayStatus),
        //JJQR - END
        //JJQR - AI (21 Nov 2026) consents, lab values and document / lab-result METADATA (files stay online); no schema change (plain pull-only rows)
        ["patient_consent"] = typeof(PatientConsent),
        ["patient_lab_value"] = typeof(PatientLabValue),
        ["patient_document"] = typeof(PatientDocument),
        ["patient_lab_result"] = typeof(PatientLabResult),
        //JJQR - END
    };

    private static readonly Dictionary<Type, string> PullOnlyByType = PullOnly.ToDictionary(kv => kv.Value, kv => kv.Key);

    public static bool IsKnown(string aggregate) => Pushable.Contains(aggregate) || PullOnly.ContainsKey(aggregate);

    public readonly record struct Target(string Aggregate, string EntityId, bool IsRoot);

    /// <summary>Map a changed row to the aggregate (and root id) whose document it belongs to.
    /// Returns null for tables that do not sync.</summary>
    public static Target? Resolve(ClinicAppDbContext db, EntityEntry entry)
    {
        switch (entry.Entity)
        {
            case Domain.Entities.Patient p: return new(Patient, Id(p.PatientId), true);
            case Domain.Entities.Booking b: return new(Booking, Id(b.BookingId), true);
            case BookingService bs: return new(Booking, Id(bs.BookingId), false);
            case Domain.Entities.Payment pay: return new(Payment, Id(pay.PaymentId), true);
            case Domain.Entities.Consultation c: return new(Consultation, Id(c.ConsultationId), true);
            case ConsultationDiagnosis d: return new(Consultation, Id(d.ConsultationId), false);
            case LabOrder l: return new(Consultation, Id(l.ConsultationId), false);
            case FollowUp f: return new(Consultation, Id(f.ConsultationId), false);
            case MedicalCertificate m: return new(Consultation, Id(m.ConsultationId), false);
            case PatientVaccination v:
                //JJQR - AI (21 Nov 2026) a vaccination not tied to a consultation travels in the patient document
                return v.ConsultationId is Guid vc ? new(Consultation, Id(vc), false) : new(Patient, Id(v.PatientId), false);
                //JJQR - END
            case PrescriptionGroup g:
                return ConsultationForBooking(db, g.BookingId) is Guid gc ? new(Consultation, Id(gc), false) : null;
            case PrescriptionLineItem li:
                {
                    var bookingId = db.PrescriptionGroups.Local.FirstOrDefault(x => x.GroupId == li.GroupId)?.BookingId
                        ?? db.PrescriptionGroups.AsNoTracking().Where(x => x.GroupId == li.GroupId).Select(x => (Guid?)x.BookingId).FirstOrDefault();
                    if (bookingId is null) return null;
                    return ConsultationForBooking(db, bookingId.Value) is Guid lc ? new(Consultation, Id(lc), false) : null;
                }
            //JJQR - AI (Nov 21, 2026) allergies are part of the patient document
            case PatientAllergy al: return new(Patient, Id(al.PatientId), false);
            //JJQR - END
            case PatientVitalReading r: return new(VitalReading, Id(r.Id), true);
            case Domain.Entities.AuditLog a: return new(AuditLog, Id(a.Id), true);
            case PrescriptionTemplateItem ti: return new("prescription_template", Id(ti.TemplateId), false);
        }
        if (PullOnlyByType.TryGetValue(entry.Metadata.ClrType, out var name))
            return new(name, KeyString(entry.Metadata, entry.Entity), true);
        return null;
    }

    private static Guid? ConsultationForBooking(ClinicAppDbContext db, Guid bookingId) =>
        db.Consultations.Local.FirstOrDefault(c => c.BookingId == bookingId)?.ConsultationId
        ?? db.Consultations.AsNoTracking().Where(c => c.BookingId == bookingId).Select(c => (Guid?)c.ConsultationId).FirstOrDefault();

    public static string Id(Guid id) => id.ToString("D");

    /// <summary>Primary key as a string: GUIDs in "D" form, enums by name, numbers invariant;
    /// composite keys joined with ':'.</summary>
    public static string KeyString(IEntityType type, object entity)
    {
        var key = type.FindPrimaryKey()!;
        return string.Join(":", key.Properties.Select(p => Format(p.PropertyInfo!.GetValue(entity))));
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        Guid g => g.ToString("D"),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    /// <summary>Inverse of <see cref="KeyString"/>; null if the string does not parse.</summary>
    public static object[]? ParseKey(IEntityType type, string keyString)
    {
        var props = type.FindPrimaryKey()!.Properties;
        var parts = keyString.Split(':');
        if (parts.Length != props.Count) return null;
        var values = new object[props.Count];
        for (var i = 0; i < props.Count; i++)
        {
            var t = Nullable.GetUnderlyingType(props[i].ClrType) ?? props[i].ClrType;
            try
            {
                values[i] = t == typeof(Guid) ? Guid.Parse(parts[i])
                    : t.IsEnum ? Enum.Parse(t, parts[i])
                    : t == typeof(string) ? parts[i]
                    : t == typeof(DateOnly) ? DateOnly.Parse(parts[i], CultureInfo.InvariantCulture)
                    : Convert.ChangeType(parts[i], t, CultureInfo.InvariantCulture);
            }
            catch { return null; }
        }
        return values;
    }
}
//JJQR - END
