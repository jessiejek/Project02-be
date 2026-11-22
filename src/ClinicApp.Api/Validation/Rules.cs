using ClinicApp.Api.Validation;
// JJQR - AI (Nov 20, 2026) - B-01: the field rules each controller shares
using ClinicApp.Domain;
using ClinicApp.Api.Controllers;
using ClinicApp.Domain.Entities;

namespace ClinicApp.Api.Validation;

public static class Rules
{
    /// <summary>Patient create / update. Required: first + last name. Dates of birth cannot be in the future (Manila today).</summary>
    public static Check Patient(Patient p) => new Check()
        .Required("first_name", "First name", p.FirstName, 100)
        .Optional("middle_name", "Middle name", p.MiddleName, 100)
        .Required("last_name", "Last name", p.LastName, 100)
        .NotFuture("date_of_birth", "Date of birth", p.DateOfBirth, ClinicClock.Today)
        .NotBefore("date_of_birth", "Date of birth", p.DateOfBirth)
        .Optional("civil_status", "Civil status", p.CivilStatus, 30)
        .Optional("address", "Address", p.Address, 300)
        .Optional("city", "City", p.City, 100)
        .Optional("zip_code", "Zip code", p.ZipCode, 20)
        .Phone("contact_number", "Contact number", p.ContactNumber)
        .Email("email", "Email", string.IsNullOrWhiteSpace(p.Email) ? null : p.Email)
        .Optional("emergency_contact_name", "Emergency contact name", p.EmergencyContactName, 150)
        .Phone("emergency_contact_number", "Emergency contact number", p.EmergencyContactNumber)
        .Optional("emergency_contact_relationship", "Emergency contact relationship", p.EmergencyContactRelationship, 100)
        .Optional("blood_type", "Blood type", p.BloodType, 10)
        .Optional("philhealth_number", "PhilHealth number", p.PhilhealthNumber, 30)
        .Optional("senior_id_number", "Senior ID number", p.SeniorIdNumber, 50)
        .Optional("pwd_id_number", "PWD ID number", p.PwdIdNumber, 50)
        .Optional("hmo_provider", "HMO provider", p.HmoProvider, 100)
        .Optional("hmo_card_number", "HMO card number", p.HmoCardNumber, 50)
        .Optional("guardian_name", "Guardian name", p.GuardianName, 150)
        .Optional("guardian_relationship", "Guardian relationship", p.GuardianRelationship, 100);

    public static Check Announcement(Announcement a) => new Check()
        .Required("title", "Title", a.Title, 200)
        .Required("body", "Message", a.Body, 2000);

    public static Check VitalReadings(IEnumerable<VitalReadingInput> readings)
    {
        var c = new Check();
        foreach (var r in readings) c.Optional("value", "A vital value", r.Value, 200);
        return c;
    }

    public static Check Vaccination(string? name, string? manufacturer, short? dose, string? route, string? site, string? lot, string? notes) => new Check()
        .Required("vaccine_name", "Vaccine name", name, 150)
        .Optional("manufacturer", "Manufacturer", manufacturer, 100)
        .Range("dose_number", "Dose number", dose ?? 1, 1, 50)
        .Optional("route", "Route", route, 100)
        .Optional("site", "Site", site, 100)
        .Optional("lot_number", "Lot number", lot, 100)
        .Optional("notes", "Notes", notes, 2000);

    //JJQR - AI (Nov 22, 2026) - QA high #6: a prescription line needs a dosage and a real quantity. Quantity -5, "abc", 0, an empty dosage and a negative or absurd duration all used to save and print.
    public static Check RxItem(RxItemInput i, int number)
    {
        var c = new Check();
        var n = $"Medicine {number}";
        c.Required($"items[{number}].generic_name", $"{n}: medicine name", i.GenericName, 200)
         .Required($"items[{number}].dosage", $"{n}: dosage", i.Dosage, 100)
         .Optional($"items[{number}].instruction", $"{n}: instructions", i.Instruction, 500)
         .Optional($"items[{number}].indication", $"{n}: indication", i.Indication, 200)
         .Optional($"items[{number}].route", $"{n}: route", i.Route, 50);
        var q = (i.Quantity ?? "").Trim();
        if (q.Length == 0) c.Fail($"items[{number}].quantity", $"{n}: quantity is required.");
        else if (q.Length > 40) c.Fail($"items[{number}].quantity", $"{n}: quantity is too long.");
        else
        {
            var m = System.Text.RegularExpressions.Regex.Match(q, @"-?\d+(?:\.\d+)?");
            if (!m.Success) c.Fail($"items[{number}].quantity", $"{n}: quantity must include a number, for example 30 or 1 box.");
            else if (m.Value.StartsWith('-') || decimal.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) <= 0)
                c.Fail($"items[{number}].quantity", $"{n}: quantity must be more than 0.");
        }
        if (i.DurationValue is { } d && (d < 1 || d > 365)) c.Fail($"items[{number}].duration", $"{n}: duration must be between 1 and 365.");
        return c;
    }
    //JJQR - END

    public static Check LabOrder(LabOrderInput o) => new Check()
        .Required("test_name", "Test name", o.TestName, 200)
        .Optional("clinical_indication", "Clinical indication", o.ClinicalIndication, 1000)
        .Optional("specimen_type", "Specimen type", o.SpecimenType, 100)
        .Optional("notes", "Notes", o.Notes, 2000);

    // JJQR - AI (Nov 20, 2026) - B-31: a doctor profile cannot carry a negative fee or a nonsense slot length
    public static Check Doctor(Doctor d) => new Check()
        .Required("specialization", "Specialty", d.Specialization, 100)
        .Money("consultation_fee", "Consultation fee", d.ConsultationFee)
        .Range("slot_duration_minutes", "Slot duration", d.SlotDurationMinutes, 5, 240)
        .Range("slot_capacity", "Slot capacity", d.SlotCapacity, 1, 50)
        .Range("daily_patient_limit", "Daily patient limit", d.DailyPatientLimit ?? 1, 1, 500)
        .Optional("bio", "Bio", d.Bio, 2000)
        .Optional("license_number", "License number", d.LicenseNumber, 50)
        .Optional("ptr_number", "PTR number", d.PtrNumber, 50)
        .Optional("s2_number", "S2 number", d.S2Number, 50);
    // JJQR - END

    public static Check Staff(StaffAccount s) => new Check()
        .Required("full_name", "Full name", s.FullName, 200)
        .Phone("contact_number", "Contact number", s.ContactNumber);
}
// JJQR - END
