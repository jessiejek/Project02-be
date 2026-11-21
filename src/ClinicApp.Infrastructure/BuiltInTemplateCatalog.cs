using ClinicApp.Domain.Enums;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Default diagnoses and SOAP phrases that used to live only in <see cref="DevDataSeeder"/>.
/// Prod GET/restore uses the same list; keys are stable (the historical seed Guids).
/// </summary>
//JJQR - AI (Nov 20, 2026) A2
public static class BuiltInTemplateKind
{
    public const string Soap = "soap";
    public const string Rx = "rx";
    public const string Cert = "cert";
    public const string Diagnosis = "diagnosis";
    public const string Phrase = "phrase";
    public const string Favorite = "favorite";

    public static readonly string[] Copyable = [Soap, Rx, Cert, Diagnosis, Phrase];

    public static string? Normalize(string? kind)
    {
        var k = kind?.Trim().ToLowerInvariant();
        return k is Soap or Rx or Cert or Diagnosis or Phrase or Favorite ? k : null;
    }

    public static bool IsCopyable(string? kind) =>
        kind is Soap or Rx or Cert or Diagnosis or Phrase;
}

public sealed record BuiltInDiagnosisDef(string Key, string Label, string Body);
public sealed record BuiltInPhraseDef(string Key, SoapField Field, string Label, string Body);
//JJQR - AI (Nov 21, 2026) - SOAP and certificate templates now live in CODE (they used to exist only in the Development seeder, so a production database had none)
public sealed record BuiltInSoapDef(string Key, string Title, string ChiefComplaint, string Subjective, string Objective, string Assessment, string Plan);
public sealed record BuiltInCertDef(string Key, string Title, string? DiagnosisText, string Recommendations, string PurposeException);
//JJQR - END

public static class BuiltInTemplateCatalog
{
    private static Guid G(string s) => Guid.Parse(s);
    private static string K(string s) => G(s).ToString();

    public static readonly BuiltInDiagnosisDef[] Diagnoses =
    [
        new(K("d1111111-1111-4111-8111-111111111101"), "J06.9 — Acute URI",
            "Acute upper respiratory infection, likely viral; no signs of lower tract involvement."),
        new(K("d1111111-1111-4111-8111-111111111102"), "I10 — Essential hypertension",
            "Essential (primary) hypertension; blood pressure at goal on current regimen."),
        new(K("d1111111-1111-4111-8111-111111111103"), "E11.9 — Type 2 DM",
            "Type 2 diabetes mellitus without complications; glycemic control per recent labs."),
        //JJQR - AI (Nov 20, 2026) B2 — A09 (US unspecified) -> WHO A09.0 (short label kept so the remapped existing row is adopted, not duplicated)
        new(K("d1111111-1111-4111-8111-111111111104"), "A09.0 — Infectious gastroenteritis",
            "Acute infectious gastroenteritis, presumed viral; mild dehydration."),
        new(K("d1111111-1111-4111-8111-111111111105"), "N39.0 — UTI",
            "Urinary tract infection, site not specified; uncomplicated cystitis."),
        new(K("d1111111-1111-4111-8111-111111111106"), "Z00.0 — General adult exam",
            "General adult medical examination without abnormal findings."),
        new(K("d1111111-1111-4111-8111-111111111107"), "R50.9 — Fever",
            "Fever, unspecified; no clear focus on initial evaluation."),
        new(K("d1111111-1111-4111-8111-111111111108"), "Z34.9 — Supervision of pregnancy",
            "Supervision of normal pregnancy; routine prenatal care."),
        new(K("d1111111-1111-4111-8111-111111111109"), "J02.9 — Acute pharyngitis",
            "Acute pharyngitis, unspecified; likely viral etiology."),
        new(K("d1111111-1111-4111-8111-111111111110"), "K21.9 — GERD",
            "Gastro-esophageal reflux disease without esophagitis; symptoms controlled."),
    ];

    public static readonly BuiltInPhraseDef[] Phrases =
    [
        new(K("e1111111-1111-4111-8111-111111111101"), SoapField.ChiefComplaint, "Cough and colds", "Cough, runny nose, and sore throat for several days."),
        new(K("e1111111-1111-4111-8111-111111111102"), SoapField.ChiefComplaint, "Fever", "Intermittent fever with body malaise for 2 days."),
        new(K("e1111111-1111-4111-8111-111111111103"), SoapField.Subjective, "Denies red flags", "Denies dyspnea, chest pain, hemoptysis, or altered sensorium."),
        new(K("e1111111-1111-4111-8111-111111111104"), SoapField.Subjective, "No sick contacts", "No known sick contacts; no recent travel."),
        new(K("e1111111-1111-4111-8111-111111111105"), SoapField.Subjective, "Symptom onset", "Symptoms began gradually about 3 days ago."),
        new(K("e1111111-1111-4111-8111-111111111106"), SoapField.Subjective, "Hydration OK", "Able to eat and drink; urine output normal."),
        new(K("e1111111-1111-4111-8111-111111111107"), SoapField.Objective, "Lungs clear", "Lungs clear to auscultation bilaterally; no wheezes or crackles."),
        new(K("e1111111-1111-4111-8111-111111111108"), SoapField.Objective, "Throat exam", "Oropharynx mildly erythematous; no tonsillar exudates."),
        new(K("e1111111-1111-4111-8111-111111111109"), SoapField.Objective, "Stable vitals", "Alert, not in distress. Vital signs as recorded."),
        new(K("e1111111-1111-4111-8111-111111111110"), SoapField.Objective, "No lymphadenopathy", "No significant cervical lymphadenopathy."),
        new(K("e1111111-1111-4111-8111-111111111111"), SoapField.Assessment, "Viral URI", "Viral upper respiratory infection, likely self-limited."),
        new(K("e1111111-1111-4111-8111-111111111112"), SoapField.Assessment, "HTN controlled", "Essential hypertension, controlled on current therapy."),
        new(K("e1111111-1111-4111-8111-111111111113"), SoapField.Plan, "Symptomatic care", "Symptomatic care: fluids, rest, paracetamol PRN."),
        new(K("e1111111-1111-4111-8111-111111111114"), SoapField.Plan, "Return precautions", "Return if fever persists >3 days, worsening breathlessness, or new red flags."),
        new(K("e1111111-1111-4111-8111-111111111115"), SoapField.Plan, "Continue meds", "Continue present medications; routine follow-up as scheduled."),
        //JJQR - AI (Nov 21, 2026) - more everyday phrases for adult and pediatric family medicine
        new(K("e1111111-1111-4111-8111-111111111116"), SoapField.ChiefComplaint, "Cough", "Cough."),
        new(K("e1111111-1111-4111-8111-111111111117"), SoapField.ChiefComplaint, "Sore throat", "Sore throat."),
        new(K("e1111111-1111-4111-8111-111111111118"), SoapField.ChiefComplaint, "Diarrhea / vomiting", "Diarrhea and/or vomiting."),
        new(K("e1111111-1111-4111-8111-111111111119"), SoapField.ChiefComplaint, "Abdominal pain", "Abdominal pain."),
        new(K("e1111111-1111-4111-8111-111111111120"), SoapField.ChiefComplaint, "Urinary symptoms", "Painful or frequent urination."),
        new(K("e1111111-1111-4111-8111-111111111121"), SoapField.ChiefComplaint, "Skin rash", "Skin rash and itching."),
        new(K("e1111111-1111-4111-8111-111111111122"), SoapField.ChiefComplaint, "Headache", "Headache."),
        new(K("e1111111-1111-4111-8111-111111111123"), SoapField.ChiefComplaint, "Low back pain", "Low back pain."),
        new(K("e1111111-1111-4111-8111-111111111124"), SoapField.ChiefComplaint, "Ear pain", "Ear pain."),
        new(K("e1111111-1111-4111-8111-111111111125"), SoapField.ChiefComplaint, "Wheezing / breathing", "Wheezing or difficulty of breathing."),
        new(K("e1111111-1111-4111-8111-111111111126"), SoapField.ChiefComplaint, "Follow-up visit", "Follow-up visit."),
        new(K("e1111111-1111-4111-8111-111111111127"), SoapField.ChiefComplaint, "Routine check-up", "Routine check-up."),
        new(K("e1111111-1111-4111-8111-111111111128"), SoapField.Subjective, "Denies fever", "Denies fever or chills."),
        new(K("e1111111-1111-4111-8111-111111111129"), SoapField.Subjective, "Denies breathing difficulty", "Denies difficulty of breathing or chest pain."),
        new(K("e1111111-1111-4111-8111-111111111130"), SoapField.Subjective, "Denies vomiting / bloody stool", "Denies persistent vomiting or blood in the stool."),
        new(K("e1111111-1111-4111-8111-111111111131"), SoapField.Subjective, "Denies urinary red flags", "Denies flank pain, fever, or blood in the urine."),
        new(K("e1111111-1111-4111-8111-111111111132"), SoapField.Subjective, "Takes maintenance medicines", "Taking maintenance medicines as prescribed."),
        new(K("e1111111-1111-4111-8111-111111111133"), SoapField.Subjective, "No new medicines", "No new medicines started."),
        new(K("e1111111-1111-4111-8111-111111111134"), SoapField.Subjective, "Reduced appetite", "Reduced appetite; still able to drink fluids."),
        new(K("e1111111-1111-4111-8111-111111111135"), SoapField.Objective, "Heart", "Heart: regular rate and rhythm, no murmurs."),
        new(K("e1111111-1111-4111-8111-111111111136"), SoapField.Objective, "Abdomen soft", "Abdomen: soft, non-tender, no guarding or masses."),
        new(K("e1111111-1111-4111-8111-111111111137"), SoapField.Objective, "Ears", "Ears: canals clear, tympanic membranes intact without redness or bulging."),
        new(K("e1111111-1111-4111-8111-111111111138"), SoapField.Objective, "Skin clear", "Skin: no rash, lesions, or erythema."),
        new(K("e1111111-1111-4111-8111-111111111139"), SoapField.Objective, "Back", "Back: no midline tenderness; full range of motion."),
        new(K("e1111111-1111-4111-8111-111111111140"), SoapField.Objective, "Wheezing", "Lungs: wheezing heard bilaterally; no crackles."),
        new(K("e1111111-1111-4111-8111-111111111141"), SoapField.Objective, "Crackles", "Lungs: crackles heard over the affected area."),
        new(K("e1111111-1111-4111-8111-111111111142"), SoapField.Objective, "Tonsils inflamed", "Throat: tonsils enlarged and erythematous with exudates."),
        new(K("e1111111-1111-4111-8111-111111111143"), SoapField.Assessment, "Pneumonia", "Community-acquired pneumonia."),
        new(K("e1111111-1111-4111-8111-111111111144"), SoapField.Assessment, "Tonsillopharyngitis", "Acute tonsillopharyngitis."),
        new(K("e1111111-1111-4111-8111-111111111145"), SoapField.Assessment, "Skin infection", "Skin and soft-tissue infection."),
        new(K("e1111111-1111-4111-8111-111111111146"), SoapField.Assessment, "UTI", "Urinary tract infection."),
        new(K("e1111111-1111-4111-8111-111111111147"), SoapField.Assessment, "Gastroenteritis", "Acute gastroenteritis."),
        new(K("e1111111-1111-4111-8111-111111111148"), SoapField.Assessment, "Asthma", "Asthma."),
        new(K("e1111111-1111-4111-8111-111111111149"), SoapField.Assessment, "Allergic rhinitis", "Allergic rhinitis."),
        new(K("e1111111-1111-4111-8111-111111111150"), SoapField.Plan, "Fluids and rest", "Increase oral fluids and rest."),
        new(K("e1111111-1111-4111-8111-111111111151"), SoapField.Plan, "Complete the antibiotic", "Complete the full course of the prescribed antibiotic."),
        new(K("e1111111-1111-4111-8111-111111111152"), SoapField.Plan, "Pain / fever reliever", "Paracetamol for fever or pain as needed."),
        new(K("e1111111-1111-4111-8111-111111111153"), SoapField.Plan, "Breathing return precautions", "Return immediately for difficulty of breathing, chest pain, persistent high fever, or inability to drink."),
        new(K("e1111111-1111-4111-8111-111111111154"), SoapField.Plan, "ORS and light diet", "ORS after each loose stool; continue light feeding."),
        new(K("e1111111-1111-4111-8111-111111111155"), SoapField.Plan, "Skin care", "Keep the area clean and dry; avoid scratching; wash hands."),
        new(K("e1111111-1111-4111-8111-111111111156"), SoapField.Plan, "Healthy lifestyle", "Healthy diet, regular exercise, avoid smoking and excessive alcohol."),
        new(K("e1111111-1111-4111-8111-111111111157"), SoapField.Plan, "Follow-up in 1 week", "Follow-up in 1 week, or sooner if worse."),
        new(K("e1111111-1111-4111-8111-111111111158"), SoapField.Plan, "Laboratory tests", "Laboratory tests as advised."),
        //JJQR - END
    ];

    //JJQR - AI (Nov 21, 2026) - SOAP templates. Subjective and Objective are LABELLED PROMPTS the doctor completes (never pre-filled findings), Assessment is the working diagnosis, Plan is
    // general advice + return precautions with no doses. Keys 101-108 keep the ids of the older Development-seeded templates so existing copies are recognised, not duplicated.
    public static readonly BuiltInSoapDef[] SoapTemplates =
    [
        new(K("a1111111-1111-4111-8111-111111111101"), "Upper respiratory infection",
            "Cough, colds, and sore throat.",
            "Onset and duration:\nCough / nasal discharge / sore throat:\nFever:\nDifficulty of breathing or chest pain:\nSick contacts:",
            "General appearance:\nVital signs: as recorded in Vital Signs.\nThroat / tonsils:\nNose / ears:\nNeck nodes:\nChest / lungs:",
            "Acute upper respiratory infection.",
            "Increase oral fluids and rest.\nParacetamol for fever or discomfort as needed.\nReturn for fever lasting more than 3 days, difficulty of breathing, chest pain, or if not improving."),
        new(K("a1111111-1111-4111-8111-111111111102"), "Hypertension follow-up",
            "Follow-up for hypertension.",
            "Taking maintenance medicines as prescribed:\nHome blood pressure readings:\nHeadache / dizziness / chest pain / palpitations / vision changes:\nDiet, salt intake, exercise, smoking:",
            "Blood pressure and pulse: as recorded in Vital Signs.\nHeart:\nLungs:\nLeg swelling:",
            "Essential hypertension.",
            "Continue or adjust medicines per prescription.\nLow-salt diet, regular exercise, weight control; avoid smoking and excessive alcohol.\nKeep a home blood pressure log.\nFollow-up as advised; return sooner for severe headache, chest pain, weakness, or vision changes."),
        new(K("a1111111-1111-4111-8111-111111111103"), "Type 2 diabetes follow-up",
            "Follow-up for diabetes.",
            "Taking medicines and following the diet:\nSymptoms of high or low blood sugar (frequent urination, thirst, weight loss, sweating, shakiness):\nHome glucose readings:\nFoot problems, numbness, vision changes:",
            "Vital signs and weight: as recorded in Vital Signs.\nFoot examination:\nHeart and lungs:",
            "Type 2 diabetes mellitus.",
            "Continue or adjust medicines per prescription.\nBalanced meals and portion control; regular exercise.\nBlood sugar and HbA1c monitoring as advised.\nAnnual eye and foot screening.\nReturn for symptoms of very high or low blood sugar, foot wounds, or vision changes."),
        new(K("a1111111-1111-4111-8111-111111111104"), "Acute gastroenteritis",
            "Diarrhea and vomiting.",
            "Onset; number of loose stools and vomiting episodes:\nBlood or mucus in the stool:\nFever / abdominal pain:\nAble to drink fluids; urine output:\nPossible food or water source; sick contacts:",
            "General appearance and alertness:\nVital signs: as recorded in Vital Signs.\nHydration (mucous membranes, skin turgor):\nAbdomen:",
            "Acute gastroenteritis.",
            "Oral rehydration solution (ORS) in small, frequent sips.\nContinue feeding as tolerated; light diet.\nParacetamol for fever or discomfort as needed.\nReturn for blood in the stool, persistent vomiting, signs of dehydration (very dry mouth, little or no urine, drowsiness), or high fever."),
        new(K("a1111111-1111-4111-8111-111111111105"), "Uncomplicated UTI",
            "Painful or frequent urination.",
            "Onset and duration:\nBurning, frequency, urgency:\nFlank pain / fever / vomiting:\nBlood in urine; discharge:\nPregnancy (if applicable), recent catheter use, previous UTIs:",
            "Temperature and vital signs: as recorded in Vital Signs.\nSuprapubic tenderness:\nCostovertebral angle tenderness:",
            "Urinary tract infection.",
            "Increase oral fluids.\nMedicines per prescription; complete the full course.\nUrinalysis / urine culture as indicated.\nReturn for fever, flank pain, vomiting, blood in the urine, or no improvement in 48 hours."),
        new(K("a1111111-1111-4111-8111-111111111106"), "Annual physical / check-up",
            "Annual check-up.",
            "Current complaints:\nPast medical, surgical, and family history:\nMedicines and allergies:\nDiet, exercise, sleep, smoking, alcohol:\nImmunization status:",
            "Vital signs, weight, height / BMI: as recorded in Vital Signs.\nGeneral survey:\nHEENT:\nHeart:\nLungs:\nAbdomen:\nExtremities / skin:",
            "Routine health examination.",
            "Screening laboratory tests and immunizations as advised.\nLifestyle counseling: diet, activity, sleep; avoid smoking.\nReturn for interim concerns or the next annual check-up."),
        new(K("a1111111-1111-4111-8111-111111111107"), "Fever work-up",
            "Fever.",
            "Onset, duration, and pattern of fever:\nAssociated symptoms (cough, sore throat, ear pain, urinary symptoms, diarrhea, rash, body or joint pain, headache):\nWarning signs (persistent vomiting, severe abdominal pain, bleeding):\nSick contacts, travel, mosquito exposure:",
            "Temperature and vital signs: as recorded in Vital Signs.\nGeneral appearance and hydration:\nThroat / ears / chest:\nSkin / rash:\nAbdomen:",
            "Febrile illness.",
            "Paracetamol for fever as needed; adequate fluids and rest.\nLaboratory tests as clinically indicated.\nReturn promptly for fever lasting more than 2-3 days, persistent vomiting, severe abdominal pain, bleeding, difficulty of breathing, drowsiness, or inability to drink."),
        new(K("a1111111-1111-4111-8111-111111111108"), "Prenatal check-up",
            "Routine prenatal check-up.",
            "Age of gestation / last menstrual period:\nFetal movement:\nVaginal bleeding, leaking of fluid, abdominal pain or contractions:\nHeadache, blurred vision, swelling of the face or hands:\nCurrent medicines and vitamins:",
            "Blood pressure and weight: as recorded in Vital Signs.\nFundal height:\nFetal heart tones:\nLeg swelling:",
            "Pregnancy; routine prenatal care.",
            "Continue prenatal vitamins as prescribed.\nLaboratory tests and ultrasound as advised.\nReturn immediately for vaginal bleeding, leaking of fluid, decreased fetal movement, severe headache, vision changes, or regular painful contractions.\nNext prenatal visit as scheduled."),
        new(K("a1111111-1111-4111-8111-111111111109"), "Community-acquired pneumonia",
            "Cough and fever.",
            "Onset and duration:\nCough / sputum (color, blood):\nFever:\nDifficulty of breathing / chest pain:\nPertinent negatives:\nOther illnesses and current medicines:",
            "General appearance:\nVital signs and oxygen saturation: as recorded in Vital Signs.\nRespiratory rate and work of breathing:\nChest / lung examination:",
            "Community-acquired pneumonia.",
            "Medicines per prescription; complete the full course.\nIncrease oral fluids, rest, and fever control.\nChest X-ray and laboratory tests as indicated.\nReassess if not improving in 48-72 hours.\nReturn immediately for worsening difficulty of breathing, persistent high fever, chest pain, confusion, or inability to drink."),
        new(K("a1111111-1111-4111-8111-111111111110"), "Pediatric pneumonia",
            "Cough and fast breathing.",
            "Onset and duration of cough / fever:\nFast or difficult breathing; chest indrawing:\nFeeding and ability to drink; vomiting:\nVaccination status; sick contacts:",
            "Weight and vital signs: as recorded in Vital Signs.\nRespiratory rate and work of breathing:\nChest indrawing / grunting:\nLung examination:\nHydration and alertness:",
            "Pneumonia.",
            "Medicines per prescription (dose by weight); complete the full course.\nContinue breastfeeding / feeding; increase fluids.\nParacetamol for fever as needed.\nReturn immediately for fast or difficult breathing, chest indrawing, inability to drink or feed, persistent vomiting, convulsions, or unusual sleepiness.\nFollow-up in 2 days."),
        new(K("a1111111-1111-4111-8111-111111111111"), "Skin and soft-tissue infection",
            "Skin sores / boil / swelling.",
            "Onset and duration:\nSite, pain, itching, discharge:\nFever:\nPrevious episodes; diabetes or other conditions; recent injury or insect bite:\nSimilar lesions in household contacts:",
            "Lesion (site, size, redness, warmth, pus, crusting):\nSurrounding skin / spreading redness:\nRegional lymph nodes:\nTemperature: as recorded in Vital Signs.",
            "Skin and soft-tissue infection.",
            "Keep the area clean and dry; do not squeeze or scratch.\nMedicines per prescription (topical / oral); complete the course.\nWash hands; do not share towels or linens.\nReturn for spreading redness, increasing pain, fever, pus, or no improvement in 3 days."),
        new(K("a1111111-1111-4111-8111-111111111112"), "Dyslipidemia follow-up",
            "Follow-up for high cholesterol.",
            "Taking medicines and following the diet:\nMuscle pain or other side effects:\nFamily history of heart disease; smoking; diabetes; hypertension:",
            "Blood pressure, weight, and BMI: as recorded in Vital Signs.\nHeart:\nWaist circumference:",
            "Dyslipidemia.",
            "Continue or adjust medicines per prescription.\nLow-fat, low-sugar diet; regular exercise; weight control.\nRepeat lipid profile as advised.\nReturn for chest pain, muscle weakness, or severe muscle pain."),
        new(K("a1111111-1111-4111-8111-111111111113"), "Dyspepsia / GERD",
            "Heartburn / upper abdominal discomfort.",
            "Onset, frequency, and relation to meals:\nHeartburn, sour taste, bloating, nausea:\nAlarm symptoms (vomiting, black stools, difficulty swallowing, weight loss):\nPain relievers (NSAIDs), smoking, alcohol, coffee:",
            "Vital signs: as recorded in Vital Signs.\nAbdomen (epigastric tenderness, masses):",
            "Dyspepsia / gastro-esophageal reflux.",
            "Medicines per prescription.\nSmaller meals; avoid late-night meals; limit coffee, alcohol, spicy and fatty food; avoid NSAIDs unless advised.\nReturn for vomiting, black or bloody stools, difficulty swallowing, weight loss, or persistent symptoms."),
        new(K("a1111111-1111-4111-8111-111111111114"), "Low back pain",
            "Low back pain.",
            "Onset, duration, and cause (injury, lifting):\nPain description; spread to the legs:\nNumbness, weakness, bladder or bowel changes:\nFever, weight loss, history of cancer:",
            "Vital signs: as recorded in Vital Signs.\nSpine tenderness / muscle spasm:\nRange of motion:\nStraight leg raise:\nLeg strength, sensation, reflexes:",
            "Low back pain.",
            "Stay active; avoid heavy lifting and prolonged bed rest.\nPain relievers per prescription.\nWarm compress; posture and back-care advice.\nReturn for leg weakness or numbness, loss of bladder or bowel control, fever, or worsening pain."),
        new(K("a1111111-1111-4111-8111-111111111115"), "Headache",
            "Headache.",
            "Onset, duration, location, and character:\nSeverity, triggers, relieving factors:\nNausea, light or sound sensitivity, aura:\nFever, neck stiffness, vomiting, weakness, vision changes, recent head injury:\nMedicines used:",
            "Blood pressure and vital signs: as recorded in Vital Signs.\nNeurologic examination:\nNeck stiffness:\nSinus tenderness / eyes / ears / throat:",
            "Headache.",
            "Rest in a quiet place; adequate fluids and sleep; avoid known triggers.\nPain relievers per prescription; avoid overuse.\nReturn immediately for sudden severe headache, weakness, confusion, vision changes, stiff neck with fever, or head injury."),
        new(K("a1111111-1111-4111-8111-111111111116"), "Allergic rhinitis",
            "Sneezing and runny or stuffy nose.",
            "Onset, duration, and seasonal or trigger pattern:\nSneezing, itching of the nose or eyes, congestion, watery discharge:\nAsthma / eczema / family history of allergy:\nMedicines tried:",
            "Vital signs: as recorded in Vital Signs.\nNasal mucosa and discharge:\nEyes:\nThroat:\nChest / lungs:",
            "Allergic rhinitis.",
            "Avoid known triggers (dust, smoke, pets); keep the room clean.\nMedicines per prescription.\nSaline nasal rinse as advised.\nReturn for fever, facial pain, difficulty of breathing, or wheezing."),
        new(K("a1111111-1111-4111-8111-111111111117"), "Acute tonsillopharyngitis",
            "Sore throat.",
            "Onset and duration:\nPain on swallowing; fever:\nCough or colds:\nRash; sick contacts:",
            "Temperature and vital signs: as recorded in Vital Signs.\nThroat / tonsils (redness, exudate, swelling):\nNeck lymph nodes:",
            "Acute tonsillopharyngitis.",
            "Increase warm fluids and rest; salt-water gargle.\nParacetamol for pain or fever as needed.\nMedicines per prescription if indicated.\nReturn for difficulty swallowing or breathing, drooling, high fever, or no improvement in 3 days."),
        new(K("a1111111-1111-4111-8111-111111111118"), "Otitis media",
            "Ear pain.",
            "Onset and duration; which ear:\nFever; irritability; pulling at the ear; ear discharge:\nRecent colds; difficulty hearing:",
            "Temperature and vital signs: as recorded in Vital Signs.\nEar canal and eardrum (redness, bulging, discharge):\nNose / throat:\nTenderness behind the ear:",
            "Otitis media.",
            "Paracetamol or ibuprofen for pain as needed.\nMedicines per prescription (dose by weight for children).\nKeep the ear dry.\nReturn for persistent high fever, severe pain, swelling behind the ear, ear discharge, or no improvement in 2-3 days."),
        new(K("a1111111-1111-4111-8111-111111111119"), "Dermatitis / skin rash",
            "Skin rash and itching.",
            "Onset and duration; where it started and spread:\nItching or pain:\nNew soaps, detergents, foods, medicines, plants; others with a similar rash:\nHistory of eczema, asthma, or allergies:",
            "Rash (type, color, distribution, scaling, weeping, scratch marks):\nVital signs: as recorded in Vital Signs.\nSigns of secondary infection:",
            "Dermatitis.",
            "Avoid known triggers and irritants; gentle skin care and moisturizer.\nMedicines per prescription.\nAvoid scratching; keep nails short.\nReturn for a spreading rash, fever, pus, or no improvement in 1 week."),
        new(K("a1111111-1111-4111-8111-111111111120"), "Asthma follow-up",
            "Cough, wheezing, or difficulty of breathing.",
            "Frequency of cough, wheeze, or night symptoms:\nTriggers (dust, smoke, weather, exercise, infection):\nReliever use and controller adherence:\nHospital or emergency visits since the last check:",
            "Vital signs and oxygen saturation: as recorded in Vital Signs.\nRespiratory rate and work of breathing:\nLung examination (wheeze, air entry):",
            "Asthma.",
            "Medicines per prescription; review inhaler technique.\nAvoid known triggers.\nFollow-up as advised.\nReturn immediately for severe difficulty of breathing, inability to speak in full sentences, blue lips, or no relief from the reliever medicine."),
    ];

    //JJQR - AI (Nov 21, 2026) - certificate templates: neutral wording (no assumed fever / diagnosis). A null diagnosis lets the certificate take the visit's own diagnosis / Assessment.
    public static readonly BuiltInCertDef[] CertTemplates =
    [
        new(K("c1111111-1111-4111-8111-111111111101"), "Fit to work", null,
            "May resume usual work duties.",
            "Fit to work certification."),
        new(K("c1111111-1111-4111-8111-111111111102"), "Rest days (3 days)", null,
            "Advised home rest for three (3) calendar days.",
            "Medical leave / sick leave."),
        new(K("c1111111-1111-4111-8111-111111111103"), "School excuse", null,
            "Excused from school during the illness; may return when well enough to attend.",
            "School absence excuse."),
        new(K("c1111111-1111-4111-8111-111111111104"), "Medical clearance", null,
            "Cleared for the requested activity per employer or school requirements.",
            "Pre-employment or pre-enrollment medical clearance."),
        new(K("c1111111-1111-4111-8111-111111111105"), "Prenatal excuse / ANC leave", "Routine prenatal check-up.",
            "Advised rest and attendance at scheduled antenatal care (ANC) visits.",
            "School or work absence for antenatal care (ANC)."),
        new(K("c1111111-1111-4111-8111-111111111106"), "Maternity rest", "Pregnancy-related condition requiring reduced activity.",
            "Advised home rest and reduced activity.",
            "Maternity leave / pregnancy-related medical rest."),
    ];
    //JJQR - END
}
//JJQR - END
