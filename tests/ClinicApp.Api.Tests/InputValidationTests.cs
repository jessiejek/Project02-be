// JJQR - AI (Nov 20, 2026) - B-01 / B-33: the server refuses bad input (not just the UI)
using System.Net;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicApp.Api.Tests;

[Collection("api")]
public class InputValidationTests(ApiFixture api) : ApiTestBase(api)
{
    private static readonly string Long = new('x', 5000);

    private static Dictionary<string, object?> GoodPatient(Action<Dictionary<string, object?>>? tweak = null)
    {
        var d = new Dictionary<string, object?> { ["first_name"] = "Val", ["last_name"] = "Idation", ["date_of_birth"] = "1990-01-01", ["sex"] = "Female", ["is_guest"] = true };
        tweak?.Invoke(d);
        return d;
    }

    [Theory]
    [InlineData("first_name", "")]
    [InlineData("first_name", "   ")]
    [InlineData("last_name", "")]
    [InlineData("last_name", "LONG")]
    [InlineData("date_of_birth", "2999-01-01")]
    [InlineData("contact_number", "abc-not-a-phone")]
    [InlineData("address", "LONG")]
    public async Task Staff_cannot_create_a_patient_with_bad_fields(string field, string value)
    {
        using var staff = F.As(W.Staff, "Staff");
        var res = await staff.PostAsync("/api/patients", ApiFactory.Body(GoodPatient(d => d[field] = value == "LONG" ? Long : value)));
        await ShouldBe(HttpStatusCode.BadRequest, res);
        var text = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Idation", text); // no patient data echoed back in the error
    }

    [Fact]
    public async Task A_valid_patient_is_created_and_a_bad_update_changes_nothing()
    {
        using var staff = F.As(W.Staff, "Staff");
        var created = await staff.PostAsync("/api/patients", ApiFactory.Body(GoodPatient()));
        await ShouldBe(HttpStatusCode.Created, created);
        var p = await JsonOf(created);
        var id = p.GetProperty("patient_id").GetGuid();
        var bad = GoodPatient(d => { d["first_name"] = ""; d["patient_id"] = id; });
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PutAsync($"/api/patients/{id}", ApiFactory.Body(bad)));
        var fresh = await JsonOf(await staff.GetAsync($"/api/patients/{id}"));
        Assert.Equal("Val", fresh.GetProperty("first_name").GetString());
        var future = GoodPatient(d => { d["date_of_birth"] = "2999-01-01"; d["patient_id"] = id; });
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PutAsync($"/api/patients/{id}", ApiFactory.Body(future)));
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/patients/{id}", ApiFactory.Body(GoodPatient(d => { d["patient_id"] = id; d["first_name"] = "Valid"; }))));
    }

    [Theory]
    [InlineData("", "m")]
    [InlineData("t", "")]
    [InlineData("t", "LONG")]
    [InlineData("LONG", "m")]
    public async Task Announcements_need_a_title_and_a_reasonable_message(string title, string body)
    {
        using var admin = F.As(W.Admin, "Admin");
        string V(string s) => s == "LONG" ? Long : s;
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PostAsync("/api/announcements", ApiFactory.Body(new { title = V(title), body = V(body), is_active = true })));
        var ok = await admin.PostAsync("/api/announcements", ApiFactory.Body(new { title = "Hello", body = "World", is_active = false }));
        await ShouldBe(HttpStatusCode.OK, ok);
        var id = (await JsonOf(ok)).GetProperty("id").GetGuid();
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PutAsync($"/api/announcements/{id}", ApiFactory.Body(new { title = "", body = "World", is_active = true })));
    }

    [Fact]
    public async Task Staff_profile_refuses_a_blank_or_huge_name_and_a_non_phone()
    {
        var s = await TestHelpers.NewStaffLoginAsync(F, UserRole.Staff);
        using var me = F.ClientFor(s.UserId, "Staff");
        object Body(string name, string? phone) => new { staff_id = s.StaffId, full_name = name, contact_number = phone, role = "Staff", status = "Active" };
        await ShouldBe(HttpStatusCode.BadRequest, await me.PutAsync($"/api/staff-accounts/{s.StaffId}", ApiFactory.Body(Body("", null))));
        await ShouldBe(HttpStatusCode.BadRequest, await me.PutAsync($"/api/staff-accounts/{s.StaffId}", ApiFactory.Body(Body(new string('N', 300), null))));
        await ShouldBe(HttpStatusCode.BadRequest, await me.PutAsync($"/api/staff-accounts/{s.StaffId}", ApiFactory.Body(Body("Ok Name", "not-a-number"))));
        await ShouldBe(HttpStatusCode.OK, await me.PutAsync($"/api/staff-accounts/{s.StaffId}", ApiFactory.Body(Body("Ok Name", "0917 123 4567"))));
    }

    [Fact]
    public async Task Vitals_refuse_a_huge_value_and_an_unknown_template()
    {
        using var staff = F.As(W.Staff, "Staff");
        var (templateId, bookingId) = (Guid.NewGuid(), Guid.NewGuid());
        await WithDb(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.VitalFieldTemplates.Add(new VitalFieldTemplate { TemplateId = templateId, Description = "T " + templateId.ToString()[..4], FormKey = "t_" + templateId.ToString("N"), Unit = "C", Icon = "x", CreatedAt = now });
            db.Bookings.Add(new Booking { BookingId = bookingId, PatientId = W.PatientA.RowId, DoctorId = W.DoctorA.RowId, AppointmentDate = ClinicApp.Domain.ClinicClock.Today, Status = BookingStatus.CheckedIn, PaymentMode = PaymentMode.PayAtClinic, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
            return 0;
        });
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PutAsync($"/api/vitals/by-booking/{bookingId}", ApiFactory.Body(new[] { new { template_id = templateId, value = Long } })));
        await ShouldBe(HttpStatusCode.BadRequest, await staff.PutAsync($"/api/vitals/by-booking/{bookingId}", ApiFactory.Body(new[] { new { template_id = Guid.NewGuid(), value = "120/80" } })));
        await ShouldBe(HttpStatusCode.OK, await staff.PutAsync($"/api/vitals/by-booking/{bookingId}", ApiFactory.Body(new[] { new { template_id = templateId, value = "36.8" } })));
    }

    [Fact]
    public async Task Vaccinations_and_lab_orders_refuse_blank_oversized_and_negative_values()
    {
        using var doc = F.As(W.DoctorA, "Doctor");
        var vax = $"/api/patient-vaccinations/by-consultation/{W.ConsultationA}";
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync(vax, ApiFactory.Body(new[] { new { vaccine_name = "" } })));
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync(vax, ApiFactory.Body(new[] { new { vaccine_name = Long } })));
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync(vax, ApiFactory.Body(new[] { new { vaccine_name = "MMR", dose_number = -3 } })));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync(vax, ApiFactory.Body(new[] { new { vaccine_name = "MMR", dose_number = 1 } })));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync(vax, ApiFactory.Body(new object[0])));

        var post = new Func<object, Task<HttpResponseMessage>>(extra => doc.PostAsync("/api/patient-vaccinations", ApiFactory.Body(extra)));
        await ShouldBe(HttpStatusCode.BadRequest, await post(new { patient_id = W.PatientA.RowId, vaccine_name = "", status = "Administered", source = "AdministeredInClinic" }));
        await ShouldBe(HttpStatusCode.BadRequest, await post(new { patient_id = W.PatientA.RowId, vaccine_name = "MMR", dose_number = -3, status = "Administered", source = "AdministeredInClinic" }));

        var lab = $"/api/lab-orders/by-consultation/{W.ConsultationA}";
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync(lab, ApiFactory.Body(new[] { new { test_name = "" } })));
        await ShouldBe(HttpStatusCode.BadRequest, await doc.PutAsync(lab, ApiFactory.Body(new[] { new { test_name = Long } })));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync(lab, ApiFactory.Body(new[] { new { test_name = "CBC" } })));
        await ShouldBe(HttpStatusCode.OK, await doc.PutAsync(lab, ApiFactory.Body(new object[0])));
    }

    [Theory]
    [InlineData("consultation_fee", -50)]
    [InlineData("slot_duration_minutes", 0)]
    [InlineData("slot_capacity", 0)]
    public async Task A_doctor_profile_cannot_get_a_negative_fee_or_a_zero_slot(string field, int value)
    {
        using var admin = F.As(W.Admin, "Admin");
        var d = await JsonOf(await admin.GetAsync($"/api/doctors/{W.DoctorB.RowId}"));
        var body = new Dictionary<string, object?>
        {
            ["doctor_id"] = W.DoctorB.RowId, ["specialization"] = d.GetProperty("specialization").GetString(), ["consultation_fee"] = 300,
            ["slot_duration_minutes"] = 30, ["slot_capacity"] = 1,
        };
        body[field] = value;
        await ShouldBe(HttpStatusCode.BadRequest, await admin.PutAsync($"/api/doctors/{W.DoctorB.RowId}", ApiFactory.Body(body)));
        body[field] = field == "consultation_fee" ? 0 : field == "slot_duration_minutes" ? 20 : 2;
        await ShouldBe(HttpStatusCode.OK, await admin.PutAsync($"/api/doctors/{W.DoctorB.RowId}", ApiFactory.Body(body)));
    }
}
// JJQR - END
