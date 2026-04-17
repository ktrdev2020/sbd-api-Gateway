using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AreaPermissionPolicySchoolScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop old area-level unique index
            migrationBuilder.DropIndex(
                name: "IX_AreaPermissionPolicies_AreaId_PermissionCode",
                table: "AreaPermissionPolicies");

            // 2. Add nullable SchoolId column
            migrationBuilder.AddColumn<int>(
                name: "SchoolId",
                table: "AreaPermissionPolicies",
                type: "integer",
                nullable: true);

            // 3. FK index for SchoolId lookups
            migrationBuilder.CreateIndex(
                name: "IX_AreaPermissionPolicies_SchoolId",
                table: "AreaPermissionPolicies",
                column: "SchoolId");

            // 4. FK constraint to Schools
            migrationBuilder.AddForeignKey(
                name: "FK_AreaPermissionPolicies_Schools_SchoolId",
                table: "AreaPermissionPolicies",
                column: "SchoolId",
                principalTable: "Schools",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // 5. Partial unique index — area-wide policies (SchoolId IS NULL)
            migrationBuilder.CreateIndex(
                name: "IX_AreaPermissionPolicies_Area_Global",
                table: "AreaPermissionPolicies",
                columns: new[] { "AreaId", "PermissionCode" },
                unique: true,
                filter: "\"SchoolId\" IS NULL");

            // 6. Partial unique index — school-specific policies (SchoolId IS NOT NULL)
            migrationBuilder.CreateIndex(
                name: "IX_AreaPermissionPolicies_Area_School",
                table: "AreaPermissionPolicies",
                columns: new[] { "AreaId", "SchoolId", "PermissionCode" },
                unique: true,
                filter: "\"SchoolId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AreaPermissionPolicies_Area_School",
                table: "AreaPermissionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AreaPermissionPolicies_Area_Global",
                table: "AreaPermissionPolicies");

            migrationBuilder.DropForeignKey(
                name: "FK_AreaPermissionPolicies_Schools_SchoolId",
                table: "AreaPermissionPolicies");

            migrationBuilder.DropIndex(
                name: "IX_AreaPermissionPolicies_SchoolId",
                table: "AreaPermissionPolicies");

            migrationBuilder.DropColumn(
                name: "SchoolId",
                table: "AreaPermissionPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_AreaPermissionPolicies_AreaId_PermissionCode",
                table: "AreaPermissionPolicies",
                columns: new[] { "AreaId", "PermissionCode" },
                unique: true);
        }
    }
}
