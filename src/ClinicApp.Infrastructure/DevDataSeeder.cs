using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Development-only. Creates the five login accounts from the Project02-fe
/// <c>account-list.txt</c> — <c>users</c> + <c>profiles</c> only, with the
/// <b>same ids and roles as the Supabase project</b> and a shared password
/// (<see cref="DevPassword"/>). Also idempotently seeds clinical templates:
/// system <c>soap_templates</c>, medicines, prescription and medical certificate
/// templates, per-doctor diagnosis templates, and SOAP phrases.
///
/// The domain rows (<c>staff_accounts</c>, <c>doctors</c>, <c>patients</c>, …)
/// are NOT seeded here — they are mirrored from Supabase with their real primary
/// keys by <c>Project02-fe/scripts/dev/import-from-supabase.mjs</c> →
/// <see cref="ClinicApp.Api"/> <c>POST /api/dev/import</c>. Run that once after
/// first startup. Until then login still works (the session falls back to the
/// email as the display name).
///
/// Idempotent: skips any user whose email already exists; seeds clinical
/// templates when missing. Never runs outside
/// the Development environment (guarded in Program.cs).
/// </summary>
public static class DevDataSeeder
{
    public const string DevPassword = "ClinicDev123!";

    // JJQR - AI (Nov 20, 2026) - B-32
    public const string SpareStaffEmail = "spare-staff@clinic.test";
    private static readonly Guid SpareStaffId = Guid.Parse("c2d6b0a4-7f0e-4c1e-9a55-3b8f0d1e7a42");
    // JJQR - END

    private static readonly (Guid Id, string Email, UserRole Role)[] Accounts =
    [
        (Guid.Parse("409a0fd7-c371-4c4c-89b3-31f9d3b18366"), "admin@clinic.test",       UserRole.Admin),
        (Guid.Parse("aa4f8351-410c-4b5d-842f-e684de014276"), "staff@clinic.test",       UserRole.Staff),
        (Guid.Parse("60310394-4c74-4370-9cd3-47b22c4cbfb8"), "doctor@clinic.test",      UserRole.Doctor),
        (Guid.Parse("857757af-9545-485b-8981-5a2043f54fe9"), "phase5doctor@clinic.test", UserRole.Doctor),
        (Guid.Parse("f3c7d22e-9f67-4511-ae1a-73b30b2fc4e6"), "patient@clinic.test",     UserRole.Patient),
        // JJQR - AI (Nov 20, 2026) - B-32: a spare front-desk login the e2e suite can deactivate/reactivate without disturbing staff@clinic.test
        (SpareStaffId,                                       SpareStaffEmail,           UserRole.Staff),
        // JJQR - END
    ];

    public static async Task SeedAsync(ClinicAppDbContext db, PasswordHasherService hasher, ILogger logger, CancellationToken ct = default, bool stableClinicalIds = true)
    {
        // JJQR - AI (Nov 21, 2026) - well-known visit IDs are for the shared API/dev database only; nodes pass false
        Guid ClinicalId(string stable) => stableClinicalIds ? Guid.Parse(stable) : Guid.NewGuid();
        // JJQR - END

        var now = DateTimeOffset.UtcNow;
        var created = 0;

        foreach (var (id, email, role) in Accounts)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;

            var user = new User { Id = id, Email = email, EmailConfirmed = true, MustSetPassword = false, CreatedAt = now };
            user.PasswordHash = hasher.Hash(user, DevPassword);
            db.Users.Add(user);
            db.Profiles.Add(new Profile { Id = id, Role = role, CreatedAt = now });
            created++;
        }

        // JJQR - AI (Nov 20, 2026) - B-32: the spare login gets its staff_accounts row (the five originals are mirrored from Supabase)
        if (!await db.StaffAccounts.AnyAsync(a => a.UserId == SpareStaffId, ct) && (db.Users.Local.Any(u => u.Id == SpareStaffId) || await db.Users.AnyAsync(u => u.Id == SpareStaffId, ct)))
        {
            db.StaffAccounts.Add(new StaffAccount
            {
                StaffId = SpareStaffId, UserId = SpareStaffId, FullName = "Spare Staff (dev)", Email = SpareStaffEmail,
                Role = StaffRole.Staff, Status = StaffStatus.Active, InvitedAt = now, CreatedAt = now, UpdatedAt = now,
            });
            created++;
        }
        // JJQR - END

        if (created > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "DevDataSeeder: created {Count} login account(s). " +
                "Run scripts/dev/import-from-supabase.mjs to populate domain rows.",
                created);
        }

        //JJQR - AI (Nov 19, 2026) - empty-database start: the dev doctor logins had no staff_accounts / doctors rows, so every later "first doctor" lookup
        // fell back to a made-up GUID and the demo bookings insert crashed with an FK error. Seed the two doctor rows when (and only when) the database has no doctors at all,
        // so a database imported from Supabase / restored from a backup is never touched.
        if (!await db.Doctors.AnyAsync(ct))
        {
            var doctorAccounts = new (Guid UserId, string Email, string Name)[]
            {
                (Accounts[2].Id, Accounts[2].Email, "Dr. Demo Doctor"),
                (Accounts[3].Id, Accounts[3].Email, "Dr. Phase5 Doctor"),
            };
            foreach (var (userId, email, name) in doctorAccounts)
            {
                if (await db.StaffAccounts.AnyAsync(a => a.StaffId == userId || a.UserId == userId, ct)) continue;
                db.StaffAccounts.Add(new StaffAccount
                {
                    StaffId = userId,
                    UserId = userId,
                    FullName = name,
                    Email = email,
                    Role = StaffRole.Doctor,
                    Status = StaffStatus.Active,
                    InvitedAt = now,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                db.Doctors.Add(new Doctor
                {
                    DoctorId = userId,
                    Specialization = "Family Medicine",
                    ConsultationFee = 500,
                    SlotDurationMinutes = 30,
                    SlotCapacity = 1,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("DevDataSeeder: database had no doctors - seeded {Count} demo doctor row(s).", doctorAccounts.Length);
        }
        //JJQR - END

        //JJQR - AI (Nov 21, 2026) - empty-database demo (the desktop "clinic node" in demo mode): only the doctors used to get their records, so the admin and front-desk logins had no
        // staff_accounts row (empty profile pages). Fill in whatever is MISSING; an imported database already has all of it. (The desktop node has no patient portal.)
        {
            var extraStaff = new (Guid UserId, string Email, string Name, StaffRole Role)[]
            {
                (Accounts[0].Id, Accounts[0].Email, "Test Admin", StaffRole.Admin),
                (Accounts[1].Id, Accounts[1].Email, "Test Staff", StaffRole.Staff),
            };
            var added = 0;
            foreach (var (userId, email, name, role) in extraStaff)
            {
                if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) continue;
                if (await db.StaffAccounts.AnyAsync(a => a.StaffId == userId || a.UserId == userId, ct)) continue;
                db.StaffAccounts.Add(new StaffAccount
                {
                    StaffId = userId, UserId = userId, FullName = name, Email = email, Role = role,
                    Status = StaffStatus.Active, InvitedAt = now, CreatedAt = now, UpdatedAt = now,
                });
                added++;
            }
            if (added > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("DevDataSeeder: filled in {Count} missing demo record(s) (admin / staff accounts).", added);
            }
        }
        //JJQR - END

        //JJQR - AI (Nov 20, 2026) A3 — demo doctor logins that still say "General Medicine" become Family Medicine (imported/custom specialties are left alone)
        {
            var demoStaffIds = await (
                from sa in db.StaffAccounts
                join u in db.Users on sa.UserId equals u.Id
                where u.Email == "doctor@clinic.test" || u.Email == "phase5doctor@clinic.test"
                select sa.StaffId).ToListAsync(ct);
            var demoDocs = await db.Doctors.Where(d => demoStaffIds.Contains(d.DoctorId) && d.Specialization == "General Medicine").ToListAsync(ct);
            foreach (var d in demoDocs)
                d.Specialization = "Family Medicine";
            if (demoDocs.Count > 0)
                await db.SaveChangesAsync(ct);
        }
        //JJQR - END

        //JJQR - AI (Oct 31, 2026)
        var doctorId = await db.Doctors.Select(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (doctorId == Guid.Empty)
            doctorId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        //JJQR - AI (Nov 21, 2026) - SOAP templates now come from BuiltInTemplateCatalog (code), the same in every environment; nothing is seeded here any more
        var systemSoapTemplates = Array.Empty<SoapTemplate>();

        var templatesCreated = 0;
        foreach (var template in systemSoapTemplates)
        {
            if (await db.SoapTemplates.AnyAsync(t => t.Id == template.Id, ct)) continue;

            template.DoctorId = doctorId;
            template.IsSystemTemplate = true;
            template.CreatedAt = now;
            template.UpdatedAt = now;
            db.SoapTemplates.Add(template);
            templatesCreated++;
        }

        if (templatesCreated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("DevDataSeeder: created {Count} system SOAP template(s).", templatesCreated);
        }
        //JJQR - END

        //JJQR - AI (Oct 31, 2026)
        static Guid G(string s) => Guid.Parse(s);

        var ownerDoctorId = await db.Doctors.Select(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (ownerDoctorId == Guid.Empty)
            ownerDoctorId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var staffDoctorIds = await (
            from sa in db.StaffAccounts
            join u in db.Users on sa.UserId equals u.Id
            where u.Email == "doctor@clinic.test" || u.Email == "phase5doctor@clinic.test"
            select sa.StaffId).Distinct().ToListAsync(ct);

        var doctorIds = staffDoctorIds
            .Union(await db.Doctors.Select(d => d.DoctorId).ToListAsync(ct))
            .Distinct()
            .ToList();
        if (doctorIds.Count == 0)
            doctorIds = [ownerDoctorId];

        var medicinesCreated = 0;
        foreach (var (medId, genericName) in new (Guid Id, string Name)[]
        {
            (G("22222222-0000-0000-0000-000000000016"), "NITROFURANTOIN 100MG CAP"),
            (G("22222222-0000-0000-0000-000000000017"), "ORAL REHYDRATION SALTS (ORS) SACHET"),
            (G("22222222-0000-0000-0000-000000000018"), "FERROUS SULFATE + FOLIC ACID TAB"),
            (G("22222222-0000-0000-0000-000000000019"), "CALCIUM CARBONATE 500MG TAB"),
        })
        {
            if (await db.Medicines.AnyAsync(m => m.MedicineId == medId, ct)) continue;
            db.Medicines.Add(new Medicine { MedicineId = medId, GenericName = genericName, CreatedAt = now });
            medicinesCreated++;
        }

        var med = new Dictionary<Guid, string>
        {
            [G("22222222-0000-0000-0000-000000000001")] = "PARACETAMOL 500MG TAB",
            [G("22222222-0000-0000-0000-000000000004")] = "LOPERAMIDE 2MG CAP",
            [G("22222222-0000-0000-0000-000000000006")] = "CETIRIZINE 10MG TAB",
            [G("22222222-0000-0000-0000-000000000008")] = "METFORMIN 500MG TAB",
            [G("22222222-0000-0000-0000-000000000010")] = "ASCORBIC ACID + MULTIVITAMINS 500MG TAB",
            [G("22222222-0000-0000-0000-000000000012")] = "AMLODIPINE 5MG TAB",
            [G("22222222-0000-0000-0000-000000000016")] = "NITROFURANTOIN 100MG CAP",
            [G("22222222-0000-0000-0000-000000000017")] = "ORAL REHYDRATION SALTS (ORS) SACHET",
            [G("22222222-0000-0000-0000-000000000018")] = "FERROUS SULFATE + FOLIC ACID TAB",
            [G("22222222-0000-0000-0000-000000000019")] = "CALCIUM CARBONATE 500MG TAB",
        };

        var rxTemplatesCreated = 0;
        var rxItemsCreated = 0;
        var rxItemSeq = 101;
        foreach (var (templateId, title, lines) in new (Guid TemplateId, string Title, (Guid MedId, string Dosage, string Qty, string Instruction)[] Lines)[]
        {
            (G("b1111111-1111-4111-8111-111111111101"), "Upper respiratory infection",
            [
                (G("22222222-0000-0000-0000-000000000001"), "500mg", "20", "Take 1 tablet by mouth every 6 hours as needed for fever or pain, for 5 days."),
                (G("22222222-0000-0000-0000-000000000006"), "10mg", "10", "PO 1 tab OD x 10 days"),
            ]),
            (G("b1111111-1111-4111-8111-111111111102"), "Hypertension follow-up",
            [(G("22222222-0000-0000-0000-000000000012"), "5mg", "30", "PO 1 tab OD x 30 days")]),
            (G("b1111111-1111-4111-8111-111111111103"), "Type 2 diabetes follow-up",
            [(G("22222222-0000-0000-0000-000000000008"), "500mg", "60", "PO 1 tab BID with meals x 30 days")]),
            (G("b1111111-1111-4111-8111-111111111104"), "Acute gastroenteritis",
            [
                (G("22222222-0000-0000-0000-000000000017"), "1 sachet", "10", "Dissolve in 200mL water; sip frequently x 3 days"),
                (G("22222222-0000-0000-0000-000000000004"), "2mg", "8", "PO 1 cap after each loose stool; max 8/day x 2 days"),
            ]),
            (G("b1111111-1111-4111-8111-111111111105"), "Uncomplicated UTI",
            [(G("22222222-0000-0000-0000-000000000016"), "100mg", "14", "PO 1 cap BID x 7 days")]),
            (G("b1111111-1111-4111-8111-111111111106"), "Annual physical / check-up",
            [(G("22222222-0000-0000-0000-000000000010"), "500mg", "30", "PO 1 tab OD x 30 days")]),
            (G("b1111111-1111-4111-8111-111111111107"), "Fever work-up",
            [(G("22222222-0000-0000-0000-000000000001"), "500mg", "20", "Take 1 tablet by mouth every 6 hours as needed for fever, for 5 days.")]),
            (G("b1111111-1111-4111-8111-111111111108"), "Prenatal check-up",
            [
                (G("22222222-0000-0000-0000-000000000018"), "—", "30", "PO 1 tab OD x 30 days"),
                (G("22222222-0000-0000-0000-000000000019"), "500mg", "60", "PO 1 tab BID x 30 days"),
            ]),
        })
        {
            if (!await db.PrescriptionTemplates.AnyAsync(t => t.TemplateId == templateId, ct))
            {
                db.PrescriptionTemplates.Add(new PrescriptionTemplate
                {
                    TemplateId = templateId,
                    DoctorId = ownerDoctorId,
                    Title = title,
                    IsSystemTemplate = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                rxTemplatesCreated++;
            }

            foreach (var line in lines)
            {
                var itemId = G($"b2111111-1111-4111-8111-111111111{rxItemSeq++:D3}");
                if (await db.PrescriptionTemplateItems.AnyAsync(i => i.Id == itemId, ct)) continue;

                db.PrescriptionTemplateItems.Add(new PrescriptionTemplateItem
                {
                    Id = itemId,
                    TemplateId = templateId,
                    MedicineId = line.MedId,
                    GenericName = med[line.MedId],
                    Dosage = line.Dosage,
                    Quantity = line.Qty,
                    Instruction = line.Instruction,
                    IsControlledSubstance = false,
                    //JJQR - AI (Nov 20, 2026) - Rx pad fields (meal timing / times / as needed) derived from the sig, same rules as the FE sig parser
                    AsNeeded = RxPadFromSig(line.Instruction).AsNeeded,
                    Timing = RxPadFromSig(line.Instruction).Timing,
                    MealRelation = RxPadFromSig(line.Instruction).Meal,
                    //JJQR - END
                    CreatedAt = now,
                });
                rxItemsCreated++;
            }
        }

        var mcCreated = 0;
        //JJQR - AI (Nov 21, 2026) - certificate templates now come from BuiltInTemplateCatalog (code), the same in every environment; nothing is seeded here any more
        foreach (var (id, title, diagnosis, recs, purpose) in Array.Empty<(Guid Id, string Title, string Diagnosis, string Recs, string Purpose)>())
        {
            if (await db.MedicalCertificateTemplates.AnyAsync(t => t.Id == id, ct)) continue;
            mcCreated++;
        }

        //JJQR - AI (Nov 20, 2026) A2 — diagnoses/phrases come from BuiltInTemplateCatalog via the same copier GET uses (adopts existing same-title rows)
        var dxBefore = await db.DoctorDiagnosisTemplates.CountAsync(ct);
        var phrasesBefore = await db.SoapPhrases.CountAsync(ct);
        var copier = new BuiltInTemplateCopier(db);
        foreach (var docId in doctorIds)
        {
            await copier.EnsureCopiesAsync(docId, BuiltInTemplateKind.Diagnosis, ct);
            await copier.EnsureCopiesAsync(docId, BuiltInTemplateKind.Phrase, ct);
        }
        var dxCreated = await db.DoctorDiagnosisTemplates.CountAsync(ct) - dxBefore;
        var phrasesCreated = await db.SoapPhrases.CountAsync(ct) - phrasesBefore;
        //JJQR - END

        if (medicinesCreated + rxTemplatesCreated + rxItemsCreated + mcCreated + dxCreated + phrasesCreated > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DevDataSeeder: clinical templates — medicines {Medicines}, rx templates {RxTemplates}, rx items {RxItems}, medical certs {Mc}, diagnoses {Dx}, SOAP phrases {Phrases}.",
            medicinesCreated, rxTemplatesCreated, rxItemsCreated, mcCreated, dxCreated, phrasesCreated);

        //JJQR - AI (Nov 20, 2026) B1 — ICD-10 catalog is owned by Icd10Seeder (WHO ICD-10 2019); do not hand-type codes here.

        var vitalDefs = new (Guid TemplateId, string FormKey, string Description, string Unit, string Icon)[]
        {
            (G("11111111-0000-0000-0000-000000000010"), "presentation", "Presentation", "", "baby"),
            (G("11111111-0000-0000-0000-000000000011"), "fetal_movement", "Fetal Movement", "", "baby"),
            (G("11111111-0000-0000-0000-000000000012"), "head_circumference", "Head Circumference (cm)", "cm", "ruler"),
            (G("11111111-0000-0000-0000-000000000013"), "length", "Length (cm)", "cm", "ruler"),
        };

        var vitalsCreated = 0;
        foreach (var (templateId, formKey, description, unit, icon) in vitalDefs)
        {
            if (await db.VitalFieldTemplates.AnyAsync(v => v.FormKey == formKey, ct)) continue;
            db.VitalFieldTemplates.Add(new VitalFieldTemplate
            {
                TemplateId = templateId,
                FormKey = formKey,
                Description = description,
                Unit = unit,
                Icon = icon,
                IsDefault = false,
                CreatedAt = now,
            });
            vitalsCreated++;
        }

        if (vitalsCreated > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DevDataSeeder: lookup seeds — vital field templates {Vitals} (ICD-10 owned by Icd10Seeder).",
            vitalsCreated);
        //JJQR - END

        //JJQR - AI (Oct 31, 2026) — demo patients, bookings, labs (mirrors seed-review-fixes.sql)
        var manilaToday = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(8)).DateTime);

        var demoDoctorId = await db.Doctors.Select(d => d.DoctorId).FirstOrDefaultAsync(ct);
        //JJQR - AI (Nov 19, 2026) - never insert bookings pointing at a doctor that does not exist (the old made-up fallback GUID broke fresh databases)
        var seedDemoBookings = demoDoctorId != Guid.Empty;
        //JJQR - END

        var pedPatientId = ClinicalId("f1111111-1111-4111-8111-111111111201");
        var obPatientId = ClinicalId("f1111111-1111-4111-8111-111111111202");
        var eldPatientId = ClinicalId("f1111111-1111-4111-8111-111111111203");

        var patientsCreated = 0;
        if (!await db.Patients.AnyAsync(p => p.PatientId == pedPatientId || p.PatientCode == "MF-PEDS02", ct))
        {
            db.Patients.Add(new Patient
            {
                PatientId = pedPatientId,
                PatientCode = "MF-PEDS02",
                FirstName = "Ana",
                LastName = "Santos",
                DateOfBirth = manilaToday.AddMonths(-24),
                Sex = SexType.Female,
                Email = "ana.peds@clinic.test",
                EmergencyContactName = "Maria Santos",
                EmergencyContactRelationship = "Mother",
                GuardianName = "Maria Santos",
                GuardianRelationship = "Mother",
                IsGuest = false,
                IsEmailVerified = true,
                ConsentVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            patientsCreated++;
        }

        if (!await db.Patients.AnyAsync(p => p.PatientId == obPatientId || p.PatientCode == "MF-OB024", ct))
        {
            db.Patients.Add(new Patient
            {
                PatientId = obPatientId,
                PatientCode = "MF-OB024",
                FirstName = "Carla",
                LastName = "Reyes",
                DateOfBirth = manilaToday.AddYears(-28),
                Sex = SexType.Female,
                Email = "carla.ob@clinic.test",
                IsPregnant = true,
                LmpDate = manilaToday.AddDays(-168),
                Gravida = 2,
                Para = 1,
                RhFactor = "Positive",
                IsGuest = false,
                IsEmailVerified = true,
                ConsentVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            patientsCreated++;
        }

        if (!await db.Patients.AnyAsync(p => p.PatientId == eldPatientId || p.PatientCode == "MF-ELD70", ct))
        {
            db.Patients.Add(new Patient
            {
                PatientId = eldPatientId,
                PatientCode = "MF-ELD70",
                FirstName = "Ramon",
                LastName = "Dela Cruz",
                DateOfBirth = manilaToday.AddYears(-70),
                Sex = SexType.Male,
                Email = "ramon.eld@clinic.test",
                LastCreatinineMgDl = 1.40m,
                LastEgfr = 48,
                LastRenalLabsDate = manilaToday.AddDays(-14),
                IsGuest = false,
                IsEmailVerified = true,
                ConsentVersion = 1,
                CreatedAt = now,
                UpdatedAt = now,
            });
            patientsCreated++;
        }

        // Prefer an already-seeded row (matched by code) so unstable IDs stay idempotent.
        async Task<Guid> ExistingPatientAsync(string code, Guid fallback)
        {
            var local = db.Patients.Local.FirstOrDefault(p => p.PatientCode == code);
            if (local is not null) return local.PatientId;
            var id = await db.Patients.Where(p => p.PatientCode == code).Select(p => p.PatientId).FirstOrDefaultAsync(ct);
            return id == Guid.Empty ? fallback : id;
        }
        pedPatientId = await ExistingPatientAsync("MF-PEDS02", pedPatientId);
        obPatientId = await ExistingPatientAsync("MF-OB024", obPatientId);
        eldPatientId = await ExistingPatientAsync("MF-ELD70", eldPatientId);

        var allergiesCreated = 0;
        var nkdaId = ClinicalId("f3111111-1111-4111-8111-111111111201");
        if (!await db.PatientAllergies.AnyAsync(a => a.Id == nkdaId || (a.PatientId == pedPatientId && a.IsNkda), ct))
        {
            db.PatientAllergies.Add(new PatientAllergy
            {
                Id = nkdaId,
                PatientId = pedPatientId,
                Allergen = "NKDA",
                IsNkda = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            allergiesCreated++;
        }

        var penicillinAllergyId = ClinicalId("f3111111-1111-4111-8111-111111111203");
        if (!await db.PatientAllergies.AnyAsync(a => a.Id == penicillinAllergyId || (a.PatientId == eldPatientId && a.Allergen == "AMOXICILLIN"), ct))
        {
            db.PatientAllergies.Add(new PatientAllergy
            {
                Id = penicillinAllergyId,
                PatientId = eldPatientId,
                Allergen = "AMOXICILLIN",
                Reaction = "rash",
                Severity = "Moderate",
                IsNkda = false,
                CreatedAt = now,
                UpdatedAt = now,
            });
            allergiesCreated++;
        }

        var labValuesCreated = 0;
        foreach (var (labId, testCode, value, unit, daysAgo) in new (Guid Id, string Code, decimal Val, string Unit, int DaysAgo)[]
        {
            (ClinicalId("f4111111-1111-4111-8111-111111111301"), "HBA1C", 8.2m, "%", 30),
            (ClinicalId("f4111111-1111-4111-8111-111111111302"), "CREATININE", 1.40m, "mg/dL", 14),
            (ClinicalId("f4111111-1111-4111-8111-111111111303"), "LDL", 142m, "mg/dL", 30),
            (ClinicalId("f4111111-1111-4111-8111-111111111304"), "HDL", 38m, "mg/dL", 30),
            (ClinicalId("f4111111-1111-4111-8111-111111111305"), "TOTAL_CHOL", 210m, "mg/dL", 30),
            (ClinicalId("f4111111-1111-4111-8111-111111111306"), "TRIGLYCERIDES", 180m, "mg/dL", 30),
            (ClinicalId("f4111111-1111-4111-8111-111111111307"), "HBA1C", 7.6m, "%", 120),
            (ClinicalId("f4111111-1111-4111-8111-111111111308"), "CREATININE", 1.25m, "mg/dL", 120),
        })
        {
            if (await db.PatientLabValues.AnyAsync(v => v.Id == labId || (v.PatientId == eldPatientId && v.TestCode == testCode && v.ObservedAt == manilaToday.AddDays(-daysAgo)), ct)) continue;
            db.PatientLabValues.Add(new PatientLabValue
            {
                Id = labId,
                PatientId = eldPatientId,
                TestCode = testCode,
                Value = value,
                Unit = unit,
                ObservedAt = manilaToday.AddDays(-daysAgo),
                CreatedAt = now,
            });
            labValuesCreated++;
        }

        var bookingsCreated = 0;
        foreach (var (bookingId, patientId, slotStart, slotEnd, queue) in new (Guid BookingId, Guid PatientId, TimeOnly Start, TimeOnly End, string Queue)[]
        {
            (ClinicalId("f2111111-1111-4111-8111-111111111201"), pedPatientId, new TimeOnly(9, 0), new TimeOnly(9, 30), "P-01"),
            (ClinicalId("f2111111-1111-4111-8111-111111111202"), obPatientId, new TimeOnly(10, 0), new TimeOnly(10, 30), "P-02"),
            (ClinicalId("f2111111-1111-4111-8111-111111111203"), eldPatientId, new TimeOnly(11, 0), new TimeOnly(11, 30), "P-03"),
        })
        {
            if (!seedDemoBookings || await db.Bookings.AnyAsync(b => b.BookingId == bookingId || (b.PatientId == patientId && b.AppointmentDate == manilaToday), ct)) continue; //JJQR - AI (Nov 19, 2026)
            db.Bookings.Add(new Booking
            {
                BookingId = bookingId,
                PatientId = patientId,
                DoctorId = demoDoctorId,
                AppointmentDate = manilaToday,
                SlotStartTime = slotStart,
                SlotEndTime = slotEnd,
                Status = BookingStatus.CheckedIn,
                PaymentMode = PaymentMode.PayAtClinic,
                VisitType = VisitType.FollowUp,
                ConsultationFeeSnapshot = 500,
                TotalFee = 500,
                AmountDue = 500,
                IsWalkIn = true,
                QueueNumber = queue,
                CreatedAt = now,
                UpdatedAt = now,
            });
            bookingsCreated++;
        }

        var labCatalogCreated = 0;
        foreach (var (catalogId, name, sortOrder) in new (Guid Id, string Name, int Sort)[]
        {
            (G("33333333-0000-0000-0000-000000000014"), "BLOOD TYPE + Rh", 14),
            (G("33333333-0000-0000-0000-000000000015"), "HBsAg", 15),
            (G("33333333-0000-0000-0000-000000000016"), "HIV", 16),
            (G("33333333-0000-0000-0000-000000000017"), "SYPHILIS", 17),
            (G("33333333-0000-0000-0000-000000000018"), "75g OGTT", 18),
            (G("33333333-0000-0000-0000-000000000019"), "GBS CULTURE", 19),
        })
        {
            if (await db.LabTestCatalog.AnyAsync(t => t.LabTestId == catalogId, ct)) continue;
            db.LabTestCatalog.Add(new LabTestCatalog
            {
                LabTestId = catalogId,
                Name = name,
                IsDefault = false,
                SortOrder = sortOrder,
                CreatedAt = now,
            });
            labCatalogCreated++;
        }

        if (patientsCreated + allergiesCreated + labValuesCreated + bookingsCreated + labCatalogCreated > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "DevDataSeeder: review-fixes demo — patients {Patients}, allergies {Allergies}, lab values {LabValues}, bookings {Bookings}, ANC lab catalog {LabCatalog}.",
            patientsCreated, allergiesCreated, labValuesCreated, bookingsCreated, labCatalogCreated);
        //JJQR - END

        // JJQR - AI (Nov 21, 2026) - services, payments, full-week schedule, announcements, day status, favorites
        await SeedDemoClinicExtrasAsync(db, logger, now, manilaToday, ct, stableClinicalIds);
        // JJQR - END
    }

    // JJQR - AI (Nov 21, 2026) - fill in the demo clinic day so Today, payments, and the catalog are not empty
    static readonly Guid DemoConsultServiceId = Guid.Parse("0a111111-0000-4000-8000-000000000001");
    static readonly Guid DemoFollowUpServiceId = Guid.Parse("0a111111-0000-4000-8000-000000000002");
    static readonly Guid DemoMedCertServiceId = Guid.Parse("0a111111-0000-4000-8000-000000000003");

    static async Task SeedDemoClinicExtrasAsync(ClinicAppDbContext db, ILogger logger, DateTimeOffset now, DateOnly manilaToday, CancellationToken ct, bool stableClinicalIds = true)
    {
        var doctorIds = await (
            from sa in db.StaffAccounts
            join u in db.Users on sa.UserId equals u.Id
            where u.Email == "doctor@clinic.test" || u.Email == "phase5doctor@clinic.test"
            select sa.StaffId).ToListAsync(ct);
        if (doctorIds.Count == 0)
            doctorIds = await db.Doctors.Select(d => d.DoctorId).Take(2).ToListAsync(ct);

        var servicesCreated = 0;
        var catalog = new (Guid Id, string Name, ServiceCategory Category, decimal Price)[]
        {
            (DemoConsultServiceId, "Consultation", ServiceCategory.Consultation, 500m),
            (DemoFollowUpServiceId, "Follow-up consultation", ServiceCategory.Consultation, 350m),
            (DemoMedCertServiceId, "Medical certificate", ServiceCategory.Procedure, 50m),
        };
        foreach (var (id, name, category, price) in catalog)
        {
            if (await db.Services.AnyAsync(s => s.ServiceId == id || s.Name == name, ct)) continue;
            db.Services.Add(new Service
            {
                ServiceId = id,
                Name = name,
                Category = category,
                Price = price,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
            });
            servicesCreated++;
        }
        if (servicesCreated > 0) await db.SaveChangesAsync(ct);

        var serviceIds = await db.Services.Where(s =>
            s.ServiceId == DemoConsultServiceId || s.ServiceId == DemoFollowUpServiceId || s.ServiceId == DemoMedCertServiceId
            || s.Name == "Consultation" || s.Name == "Follow-up consultation" || s.Name == "Medical certificate")
            .Select(s => s.ServiceId).ToListAsync(ct);

        var linksCreated = 0;
        foreach (var doctorId in doctorIds)
        {
            foreach (var serviceId in serviceIds)
            {
                if (await db.DoctorServices.AnyAsync(l => l.DoctorId == doctorId && l.ServiceId == serviceId, ct)) continue;
                db.DoctorServices.Add(new DoctorService
                {
                    DoctorId = doctorId,
                    ServiceId = serviceId,
                    DurationMinutes = 30,
                    CreatedAt = now,
                });
                linksCreated++;
            }
        }

        var schedulesCreated = 0;
        foreach (var doctorId in doctorIds)
        {
            var have = (await db.DoctorSchedules.Where(s => s.DoctorId == doctorId).Select(s => s.DayOfWeek).ToListAsync(ct)).ToHashSet();
            for (short day = 0; day <= 6; day++)
            {
                if (have.Contains(day)) continue;
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.NewGuid(),
                    DoctorId = doctorId,
                    DayOfWeek = day,
                    IsActive = true,
                    StartTime = new TimeOnly(8, 0),
                    EndTime = new TimeOnly(17, 0),
                    CreatedAt = now,
                    UpdatedAt = now,
                });
                schedulesCreated++;
            }
        }

        var paymentsCreated = 0;
        var todays = await db.Bookings.Where(b => b.AppointmentDate == manilaToday).Select(b => new { b.BookingId, b.AmountDue, b.TotalFee }).ToListAsync(ct);
        foreach (var b in todays)
        {
            if (await db.Payments.AnyAsync(p => p.BookingId == b.BookingId, ct)) continue;
            var amount = b.AmountDue != 0 ? b.AmountDue : (b.TotalFee != 0 ? b.TotalFee : 500m);
            db.Payments.Add(new Payment
            {
                PaymentId = Guid.NewGuid(),
                BookingId = b.BookingId,
                Amount = amount,
                Status = PaymentStatus.Unpaid,
                CreatedAt = now,
                UpdatedAt = now,
            });
            paymentsCreated++;
        }

        var announcementsCreated = 0;
        var postedBy = Accounts[0].Id;
        var announcements = new (Guid Id, string Title, string Body)[]
        {
            (Guid.Parse("0a111111-0000-4000-8000-000000000101"),
                "Welcome to the clinic node",
                "This is the desktop clinic workstation. Sign in as a doctor, staff member, or administrator."),
            (Guid.Parse("0a111111-0000-4000-8000-000000000102"),
                "Walk-ins are welcome today",
                "Front desk can check walk-in patients into the queue. Please confirm payment after the consult."),
        };
        foreach (var (id, title, body) in announcements)
        {
            if (await db.Announcements.AnyAsync(a => a.Id == id || a.Title == title, ct)) continue;
            db.Announcements.Add(new Announcement
            {
                Id = id,
                Title = title,
                Body = body,
                IsActive = true,
                PostedByUserId = await db.Users.AnyAsync(u => u.Id == postedBy, ct) ? postedBy : null,
                CreatedAt = now,
                UpdatedAt = now,
            });
            announcementsCreated++;
        }

        var dayStatusesCreated = 0;
        foreach (var doctorId in doctorIds)
        {
            if (await db.DoctorDayStatuses.AnyAsync(s => s.DoctorId == doctorId && s.StatusDate == manilaToday, ct)) continue;
            db.DoctorDayStatuses.Add(new DoctorDayStatus
            {
                Id = Guid.NewGuid(),
                DoctorId = doctorId,
                StatusDate = manilaToday,
                Status = DoctorDayStatusEnum.Available,
                CreatedAt = now,
                UpdatedAt = now,
            });
            dayStatusesCreated++;
        }

        var favoritesCreated = 0;
        var meds = await db.Medicines.AsNoTracking().OrderBy(m => m.GenericName).Take(3).ToListAsync(ct);
        if (meds.Count == 0)
        {
            var medId = Guid.Parse("0a111111-0000-4000-8000-000000000201");
            if (!await db.Medicines.AnyAsync(m => m.MedicineId == medId, ct))
            {
                db.Medicines.Add(new Medicine { MedicineId = medId, GenericName = "Salbutamol", CreatedAt = now });
                await db.SaveChangesAsync(ct);
            }
            meds = await db.Medicines.AsNoTracking().Where(m => m.MedicineId == medId).ToListAsync(ct);
        }
        foreach (var doctorId in doctorIds)
        {
            var sort = 0;
            foreach (var med in meds)
            {
                if (await db.DoctorFavoriteMedicines.AnyAsync(f => f.DoctorId == doctorId && f.MedicineId == med.MedicineId, ct)) continue;
                db.DoctorFavoriteMedicines.Add(new DoctorFavoriteMedicine
                {
                    Id = Guid.NewGuid(),
                    DoctorId = doctorId,
                    MedicineId = med.MedicineId,
                    GenericName = med.GenericName,
                    Dosage = "100 mcg",
                    Quantity = "1 inhaler",
                    Instruction = "2 puffs as needed",
                    SortOrder = sort++,
                    CreatedAt = now,
                });
                favoritesCreated++;
            }
        }

        if (servicesCreated + linksCreated + schedulesCreated + paymentsCreated + announcementsCreated + dayStatusesCreated + favoritesCreated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "DevDataSeeder: demo extras — services {Services}, doctor-service links {Links}, schedule rows {Schedules}, payments {Payments}, announcements {Announcements}, day statuses {DayStatuses}, favorites {Favorites}.",
                servicesCreated, linksCreated, schedulesCreated, paymentsCreated, announcementsCreated, dayStatusesCreated, favoritesCreated);
        }
    }
    // JJQR - END

    //JJQR - AI (Nov 20, 2026) - deterministic sig -> Rx pad fields for seeded Rx templates (mirrors Project02-fe src/lib/rx-sig-parse.ts for the sigs used here)
    private static (bool AsNeeded, string? Timing, string? Meal) RxPadFromSig(string sig)
    {
        var t = sig.ToLowerInvariant();
        bool Has(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(t, pattern);
        var asNeeded = Has(@"\bprn\b|\bas needed\b|after each loose stool|\bstat\b");
        string? meal = Has(@"\bbefore (meals?|food|breakfast|lunch|dinner)\b|\bempty stomach\b") ? "Before"
            : Has(@"\bafter (meals?|food|eating)\b|\bwith (meals?|food)\b") ? "After" : null;
        string? timing = null;
        if (!asNeeded)
        {
            if (Has(@"\bqid\b|\bq6h\b|\bq4h\b")) timing = "Breakfast, Lunch, Dinner, Bedtime";
            else if (Has(@"\btid\b|\bq8h\b|\bthree times\b")) timing = "Breakfast, Lunch, Dinner";
            else if (Has(@"\bbid\b|\bq12h\b|\btwice daily\b")) timing = "Breakfast, Dinner";
            else if (Has(@"\b(od|qd|once daily|once a day|daily)\b")) timing = Has(@"bedtime|at night|\bhs\b") ? "Bedtime" : "Breakfast";
        }
        return (asNeeded, timing, meal);
    }
    //JJQR - END
}
