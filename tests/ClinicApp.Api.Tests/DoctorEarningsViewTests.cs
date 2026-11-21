using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClinicApp.Api.Tests;

//JJQR - AI (Nov 21, 2026) - v_doctor_earnings exists on SQLite and matches the SQL Server definition (month key, completed-only, collected vs waived)
[Collection("api")]
public class DoctorEarningsViewTests(ApiFixture api) : ApiTestBase(api)
{
    [Fact]
    public async Task Earnings_view_groups_completed_visits_per_doctor_and_month()
    {
        var doctor = Guid.NewGuid();
        await WithDb<int>(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            var uid = Guid.NewGuid();
            db.Users.Add(new User { Id = uid, Email = $"earn-{uid:N}@test.local", PasswordHash = "x", EmailConfirmed = true, CreatedAt = now });
            db.Profiles.Add(new Profile { Id = uid, Role = UserRole.Doctor, CreatedAt = now });
            db.StaffAccounts.Add(new StaffAccount { StaffId = doctor, UserId = uid, FullName = "Earn Doctor", Email = $"earn-{doctor:N}@test.local", Role = StaffRole.Doctor, Status = StaffStatus.Active, InvitedAt = now, CreatedAt = now, UpdatedAt = now });
            db.Doctors.Add(new Doctor { DoctorId = doctor, Specialization = "GP", ConsultationFee = 450, SlotDurationMinutes = 15, SlotCapacity = 1, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();

            // (date, status, fee, payment status, received)
            var rows = new (DateOnly Date, BookingStatus Status, decimal Fee, PaymentStatus Pay, decimal? Received)[]
            {
                (new DateOnly(2026, 10, 5), BookingStatus.Completed, 450, PaymentStatus.Paid, 500),   // collected = amount_received
                (new DateOnly(2026, 10, 20), BookingStatus.Completed, 300, PaymentStatus.Paid, null), // collected = amount
                (new DateOnly(2026, 10, 21), BookingStatus.Completed, 200, PaymentStatus.Waived, null),
                (new DateOnly(2026, 10, 22), BookingStatus.Cancelled, 999, PaymentStatus.Unpaid, null), // not completed: ignored
                (new DateOnly(2026, 11, 2), BookingStatus.Completed, 450, PaymentStatus.Unpaid, null),
            };
            foreach (var r in rows)
            {
                var patient = Guid.NewGuid();
                db.Patients.Add(new Patient { PatientId = patient, PatientCode = "EV-" + patient.ToString("N")[..10], FirstName = "E", LastName = "V", DateOfBirth = new DateOnly(1990, 1, 1), Sex = SexType.Female, Email = $"{patient:N}@test.local", ContactNumber = "0917", CreatedAt = now, UpdatedAt = now });
                var booking = Guid.NewGuid();
                db.Bookings.Add(new Booking { BookingId = booking, PatientId = patient, DoctorId = doctor, AppointmentDate = r.Date, Status = r.Status, PaymentMode = PaymentMode.PayAtClinic, QueueNumber = "P-" + booking.ToString("N")[..8], VisitType = VisitType.New, ConsultationFeeSnapshot = r.Fee, TotalFee = r.Fee, AmountDue = 0, CreatedAt = now, UpdatedAt = now });
                db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = booking, Amount = r.Fee, Status = r.Pay, AmountReceived = r.Received, CreatedAt = now, UpdatedAt = now });
            }
            await db.SaveChangesAsync();
            return 0;
        });

        var view = await WithDb(db => db.VDoctorEarnings.AsNoTracking().Where(e => e.DoctorId == doctor).OrderBy(e => e.Period).ToListAsync());

        Assert.Equal(2, view.Count);
        var oct = view[0];
        Assert.Equal("2026-10", oct.Period);
        Assert.Equal(3, oct.CompletedVisits);
        Assert.Equal(950m, oct.GrossBilled);
        Assert.Equal(800m, oct.Collected);
        Assert.Equal(200m, oct.Waived);
        var nov = view[1];
        Assert.Equal("2026-11", nov.Period);
        Assert.Equal(1, nov.CompletedVisits);
        Assert.Equal(450m, nov.GrossBilled);
        Assert.Equal(0m, nov.Collected);
        Assert.Equal(0m, nov.Waived);
    }
}
//JJQR - END
