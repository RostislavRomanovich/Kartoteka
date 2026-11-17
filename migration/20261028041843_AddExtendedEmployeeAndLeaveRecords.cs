using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Kartoteka.Server.migration
{
    /// <inheritdoc />
    public partial class AddExtendedEmployeeAndLeaveRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeConfidentialHealthRecords",
                columns: table => new
                {
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    PsychiatricFitnessCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PsychiatricAssessmentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RestrictedNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeConfidentialHealthRecords", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_EmployeeConfidentialHealthRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeExtendedData",
                columns: table => new
                {
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    Inn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    Snils = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    PekNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DactyloscopyPerformed = table.Column<bool>(type: "boolean", nullable: true),
                    DactyloscopyDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BloodGroup = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    RhFactor = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    DnaTestPerformed = table.Column<bool>(type: "boolean", nullable: true),
                    DnaTestDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UmoPassedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UmoExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FitnessCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HeightCm = table.Column<decimal>(type: "numeric", nullable: true),
                    WeightKg = table.Column<decimal>(type: "numeric", nullable: true),
                    ClothingSize = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ShoeSize = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DrivingLicenseSeries = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DrivingLicenseNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    DrivingLicenseIssuedBy = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    DrivingLicenseIssuedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeExtendedData", x => x.EmployeeId);
                    table.ForeignKey(
                        name: "FK_EmployeeExtendedData_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLeaveEntitlements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    CalendarYear = table.Column<int>(type: "integer", nullable: false),
                    MainDays = table.Column<int>(type: "integer", nullable: false),
                    VeteranDays = table.Column<int>(type: "integer", nullable: false),
                    AdditionalDays = table.Column<int>(type: "integer", nullable: false),
                    CarryoverDays = table.Column<int>(type: "integer", nullable: false),
                    ManualAdjustmentDays = table.Column<int>(type: "integer", nullable: false),
                    BasisNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLeaveEntitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveEntitlements_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLeavePreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    CalendarYear = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLeavePreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLeavePreferences_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeLeaveRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<long>(type: "bigint", nullable: false),
                    LeaveType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedDays = table.Column<int>(type: "integer", nullable: false),
                    ActualStart = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    ActualDays = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeLeaveRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeLeaveRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaveBlackoutPeriods",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OrganizationalUnitId = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBlackoutPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveBlackoutPeriods_OrganizationalUnits_OrganizationalUnit~",
                        column: x => x.OrganizationalUnitId,
                        principalTable: "OrganizationalUnits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveEntitlements_EmployeeId",
                table: "EmployeeLeaveEntitlements",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeavePreferences_EmployeeId",
                table: "EmployeeLeavePreferences",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveRecords_EmployeeId",
                table: "EmployeeLeaveRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBlackoutPeriods_OrganizationalUnitId",
                table: "LeaveBlackoutPeriods",
                column: "OrganizationalUnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeConfidentialHealthRecords");

            migrationBuilder.DropTable(
                name: "EmployeeExtendedData");

            migrationBuilder.DropTable(
                name: "EmployeeLeaveEntitlements");

            migrationBuilder.DropTable(
                name: "EmployeeLeavePreferences");

            migrationBuilder.DropTable(
                name: "EmployeeLeaveRecords");

            migrationBuilder.DropTable(
                name: "LeaveBlackoutPeriods");
        }
    }
}
