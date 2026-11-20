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

    public static async Task SeedAsync(ClinicAppDbContext db, PasswordHasherService hasher, ILogger logger, CancellationToken ct = default)
    {
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
                "DevDataSeeder: created {Count} login account(s), password '{Password}'. " +
                "Run scripts/dev/import-from-supabase.mjs to populate domain rows.",
                created, DevPassword);
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

        var systemSoapTemplates = new SoapTemplate[]
        {
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111101"),
                Title = "Upper respiratory infection",
                ChiefComplaint = "Cough, colds, and sore throat for several days.",
                Subjective = "Patient reports runny nose, mild sore throat, and occasional dry cough. No high fever at home; appetite fair. No known sick contacts recently.",
                Objective = "Alert, not in distress. Throat mildly erythematous without exudates. Lungs clear to auscultation. No cervical lymphadenopathy.",
                Assessment = "Upper respiratory tract infection, likely viral.",
                Plan = "Symptomatic care: increased fluids, rest, paracetamol PRN for fever or pain. Saline gargle for throat comfort. Return if fever persists beyond 3 days, worsening breathlessness, or chest pain.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111102"),
                Title = "Hypertension follow-up",
                ChiefComplaint = "Follow-up for high blood pressure.",
                Subjective = "On current antihypertensive regimen; reports good adherence. Occasional mild headaches; no chest pain, palpitations, or visual changes.",
                Objective = "BP within clinic target range on repeat measurement. Heart regular rate and rhythm. No pedal edema.",
                Assessment = "Essential hypertension, controlled on current therapy.",
                Plan = "Continue present medications. Low-sodium diet and regular activity advised. Home BP log encouraged. Follow-up in 3 months or sooner if symptoms worsen.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111103"),
                Title = "Type 2 diabetes follow-up",
                ChiefComplaint = "Routine diabetes check-up.",
                Subjective = "Known type 2 diabetes on oral agents. No polyuria, polydipsia, or unexplained weight loss. Diet compliance moderate.",
                Objective = "Well appearing. Feet inspected without ulcers; monofilament sensation grossly intact. Recent labs reviewed when available.",
                Assessment = "Type 2 diabetes mellitus; glycemic control to be guided by recent HbA1c and fasting glucose.",
                Plan = "Continue medications as prescribed. Reinforce balanced meals and portion control. Annual eye and foot screening reminders. Repeat HbA1c before next visit.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111104"),
                Title = "Acute gastroenteritis",
                ChiefComplaint = "Diarrhea and vomiting since yesterday.",
                Subjective = "Multiple loose stools and two episodes of vomiting. Mild abdominal cramping. Able to sip oral fluids; urine output slightly reduced.",
                Objective = "Mild dry mucous membranes; capillary refill normal. Abdomen soft, mild diffuse tenderness, no guarding. No blood in stool reported.",
                Assessment = "Acute gastroenteritis, likely viral; mild dehydration.",
                Plan = "Oral rehydration solution in small frequent sips. Light diet when tolerated. Paracetamol for cramps or fever PRN. Seek care for bloody stools, persistent vomiting, or signs of severe dehydration.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111105"),
                Title = "Uncomplicated UTI",
                ChiefComplaint = "Painful urination and urinary frequency.",
                Subjective = "Dysuria and urgency for two days. No flank pain or fever. Not pregnant; no recent catheter use.",
                Objective = "Afebrile; no costovertebral angle tenderness. Abdomen soft, non-tender.",
                Assessment = "Uncomplicated urinary tract infection.",
                Plan = "Empiric antibiotic course per local guideline for uncomplicated cystitis. Increase fluid intake. Complete full course. Return if fever, flank pain, or no improvement in 48 hours.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111106"),
                Title = "Annual physical / check-up",
                ChiefComplaint = "Annual wellness visit.",
                Subjective = "No acute complaints. Reviews sleep, diet, and exercise. Immunizations updated per record when applicable.",
                Objective = "General survey unremarkable. Vital signs within age-appropriate limits. Basic PE including HEENT, heart, lungs, and abdomen documented.",
                Assessment = "Health maintenance visit; no acute disease identified today.",
                Plan = "Age-appropriate screening labs and vitals trend discussed. Lifestyle counseling on diet, activity, and sun protection. Return for interim concerns or next annual review.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111107"),
                Title = "Fever work-up",
                ChiefComplaint = "Fever for two days.",
                Subjective = "Intermittent fever with chills and body malaise. No clear focus yet; mild headache. No rash or travel history of note.",
                Objective = "Febrile in clinic; otherwise stable vitals. Throat, ears, lungs, and skin examined for focus. No meningeal signs.",
                Assessment = "Undifferentiated febrile illness; viral syndrome most likely pending course.",
                Plan = "Paracetamol for fever and comfort. Adequate hydration and rest. Watch for localized symptoms or persistent fever beyond 3 days. CBC or other tests if no improvement or if red flags develop.",
            },
            new()
            {
                Id = Guid.Parse("a1111111-1111-4111-8111-111111111108"),
                Title = "Prenatal check-up",
                ChiefComplaint = "Routine prenatal visit.",
                Subjective = "Pregnant patient in stated trimester; fetal movement felt. No bleeding, leaking fluid, or severe headache. Taking prescribed vitamins.",
                Objective = "Fundal height and fetal heart rate appropriate for gestational age when checked. BP and weight recorded. No pedal edema.",
                Assessment = "Intrauterine pregnancy; routine prenatal care.",
                Plan = "Continue prenatal vitamins and iron if indicated. Discuss warning signs: bleeding, decreased fetal movement, severe headache, or visual changes. Schedule next prenatal visit per protocol.",
            },
        };

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
                (G("22222222-0000-0000-0000-000000000001"), "500mg", "20", "PO 1 tab q6h PRN fever/pain x 5 days"),
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
            [(G("22222222-0000-0000-0000-000000000001"), "500mg", "20", "PO 1 tab q6h PRN fever x 5 days")]),
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
        foreach (var (id, title, diagnosis, recs, purpose) in new (Guid Id, string Title, string Diagnosis, string Recs, string Purpose)[]
        {
            (G("c1111111-1111-4111-8111-111111111101"), "Fit to work",
                "Recovered from minor illness; clinically stable.",
                "May resume usual work duties.",
                "Fit to work certification."),
            (G("c1111111-1111-4111-8111-111111111102"), "Rest days (3 days)",
                "Acute febrile illness requiring rest.",
                "Advised home rest for three (3) calendar days.",
                "Medical leave / sick leave."),
            (G("c1111111-1111-4111-8111-111111111103"), "School excuse",
                "Acute illness; advised to stay home.",
                "Excused from school until symptoms improve and afebrile for 24 hours.",
                "School absence excuse."),
            (G("c1111111-1111-4111-8111-111111111104"), "Medical clearance",
                "No active contagious disease identified on examination.",
                "Cleared for requested activity per employer or school requirements.",
                "Pre-employment or pre-enrollment medical clearance."),
            (G("c1111111-1111-4111-8111-111111111105"), "Prenatal excuse / ANC leave",
                "Routine prenatal checkup; patient advised to attend antenatal care.",
                "Advised rest and attendance at scheduled ANC visits as indicated.",
                "School or work absence for antenatal care (ANC)."),
            (G("c1111111-1111-4111-8111-111111111106"), "Maternity rest",
                "Pregnancy-related condition requiring reduced activity.",
                "Advised home rest for the prescribed number of calendar days.",
                "Maternity leave / pregnancy-related medical rest."),
        })
        {
            if (await db.MedicalCertificateTemplates.AnyAsync(t => t.Id == id, ct)) continue;
            db.MedicalCertificateTemplates.Add(new MedicalCertificateTemplate
            {
                Id = id,
                DoctorId = ownerDoctorId,
                Title = title,
                IsSystemTemplate = true,
                DiagnosisText = diagnosis,
                Recommendations = recs,
                PurposeException = purpose,
                CreatedAt = now,
                UpdatedAt = now,
            });
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

        var pedPatientId = G("f1111111-1111-4111-8111-111111111201");
        var obPatientId = G("f1111111-1111-4111-8111-111111111202");
        var eldPatientId = G("f1111111-1111-4111-8111-111111111203");

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

        var allergiesCreated = 0;
        var nkdaId = G("f3111111-1111-4111-8111-111111111201");
        if (!await db.PatientAllergies.AnyAsync(a => a.Id == nkdaId, ct))
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

        var penicillinAllergyId = G("f3111111-1111-4111-8111-111111111203");
        if (!await db.PatientAllergies.AnyAsync(a => a.Id == penicillinAllergyId, ct))
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
            (G("f4111111-1111-4111-8111-111111111301"), "HBA1C", 8.2m, "%", 30),
            (G("f4111111-1111-4111-8111-111111111302"), "CREATININE", 1.40m, "mg/dL", 14),
            (G("f4111111-1111-4111-8111-111111111303"), "LDL", 142m, "mg/dL", 30),
            (G("f4111111-1111-4111-8111-111111111304"), "HDL", 38m, "mg/dL", 30),
            (G("f4111111-1111-4111-8111-111111111305"), "TOTAL_CHOL", 210m, "mg/dL", 30),
            (G("f4111111-1111-4111-8111-111111111306"), "TRIGLYCERIDES", 180m, "mg/dL", 30),
            (G("f4111111-1111-4111-8111-111111111307"), "HBA1C", 7.6m, "%", 120),
            (G("f4111111-1111-4111-8111-111111111308"), "CREATININE", 1.25m, "mg/dL", 120),
        })
        {
            if (await db.PatientLabValues.AnyAsync(v => v.Id == labId, ct)) continue;
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
            (G("f2111111-1111-4111-8111-111111111201"), pedPatientId, new TimeOnly(9, 0), new TimeOnly(9, 30), "P-01"),
            (G("f2111111-1111-4111-8111-111111111202"), obPatientId, new TimeOnly(10, 0), new TimeOnly(10, 30), "P-02"),
            (G("f2111111-1111-4111-8111-111111111203"), eldPatientId, new TimeOnly(11, 0), new TimeOnly(11, 30), "P-03"),
        })
        {
            if (!seedDemoBookings || await db.Bookings.AnyAsync(b => b.BookingId == bookingId, ct)) continue; //JJQR - AI (Nov 19, 2026)
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
    }
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
