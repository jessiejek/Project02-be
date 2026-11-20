using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClinicApp.Infrastructure;

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add</c>. Uses localhost only —
/// never the production SQL Server host.
/// </summary>
//JJQR - AI (Nov 20, 2026) B1
public sealed class ClinicAppDbContextFactory : IDesignTimeDbContextFactory<ClinicAppDbContext>
{
    public ClinicAppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs) ||
            cs.Contains("PHL8DPROG004", StringComparison.OrdinalIgnoreCase))
        {
            cs = "Server=localhost,1433;Database=ClinicAppDb_Design;User Id=sa;Password=DesignTime_Dummy1;TrustServerCertificate=True;Encrypt=True";
        }
        var options = new DbContextOptionsBuilder<ClinicAppDbContext>()
            .UseSqlServer(cs)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ClinicAppDbContext(options);
    }
}
//JJQR - END
