using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClinicApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase91PatientCodeSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // §17.1 #1 — monotonic patient_code source. Starts past any existing
            // MF-#### / MF-###### codes so a fresh clinic and a migrated one both
            // land in unused territory.
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'patient_code_seq')
    CREATE SEQUENCE patient_code_seq AS bigint START WITH 100000 INCREMENT BY 1 NO CYCLE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS patient_code_seq;");
        }
    }
}
