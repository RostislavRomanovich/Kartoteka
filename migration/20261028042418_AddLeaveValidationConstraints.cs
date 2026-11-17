using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kartoteka.Server.migration
{
    /// <inheritdoc />
    public partial class AddLeaveValidationConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmployeeLeaveEntitlements_EmployeeId",
                table: "EmployeeLeaveEntitlements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LeaveBlackoutPeriod_DateRange",
                table: "LeaveBlackoutPeriods",
                sql: "\"EndDate\" >= \"StartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeaveRecord_ActualDateRange",
                table: "EmployeeLeaveRecords",
                sql: "\"ActualEnd\" IS NULL OR \"ActualStart\" IS NULL OR \"ActualEnd\" >= \"ActualStart\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeaveRecord_Days",
                table: "EmployeeLeaveRecords",
                sql: "\"PlannedDays\" >= 0 AND (\"ActualDays\" IS NULL OR \"ActualDays\" >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeaveRecord_PlannedDateRange",
                table: "EmployeeLeaveRecords",
                sql: "\"PlannedEnd\" >= \"PlannedStart\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeavePreference_Month",
                table: "EmployeeLeavePreferences",
                sql: "\"Month\" >= 1 AND \"Month\" <= 12");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeavePreference_Priority",
                table: "EmployeeLeavePreferences",
                sql: "\"Priority\" >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveEntitlements_EmployeeId_CalendarYear",
                table: "EmployeeLeaveEntitlements",
                columns: new[] { "EmployeeId", "CalendarYear" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EmployeeLeaveEntitlement_Days",
                table: "EmployeeLeaveEntitlements",
                sql: "\"MainDays\" >= 0 AND \"VeteranDays\" >= 0 AND \"AdditionalDays\" >= 0 AND \"CarryoverDays\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LeaveBlackoutPeriod_DateRange",
                table: "LeaveBlackoutPeriods");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeaveRecord_ActualDateRange",
                table: "EmployeeLeaveRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeaveRecord_Days",
                table: "EmployeeLeaveRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeaveRecord_PlannedDateRange",
                table: "EmployeeLeaveRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeavePreference_Month",
                table: "EmployeeLeavePreferences");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeavePreference_Priority",
                table: "EmployeeLeavePreferences");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeLeaveEntitlements_EmployeeId_CalendarYear",
                table: "EmployeeLeaveEntitlements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EmployeeLeaveEntitlement_Days",
                table: "EmployeeLeaveEntitlements");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeLeaveEntitlements_EmployeeId",
                table: "EmployeeLeaveEntitlements",
                column: "EmployeeId");
        }
    }
}
