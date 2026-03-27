using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable enable

namespace Gateway.Data.Migrations;

/// <summary>
/// Adds Module Management Enhancement schema:
/// - AreaModuleAssignments table (new)
/// - Module shadow columns: VisibilityLevels, RegistrationType, EntryUrl, BundlePath,
///   ConfigJson, Author, License, CreatedAt, UpdatedAt
/// - SchoolModule shadow columns: Notes, IsPilot
/// </summary>
public partial class AddModuleManagementEnhancement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Module: add new columns ──
        migrationBuilder.AddColumn<string>(
            name: "VisibilityLevels",
            table: "Modules",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "school");

        migrationBuilder.AddColumn<string>(
            name: "RegistrationType",
            table: "Modules",
            type: "character varying(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "internal");

        migrationBuilder.AddColumn<string>(
            name: "EntryUrl",
            table: "Modules",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "BundlePath",
            table: "Modules",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ConfigJson",
            table: "Modules",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Author",
            table: "Modules",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "License",
            table: "Modules",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "CreatedAt",
            table: "Modules",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "NOW()");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "Modules",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "NOW()");

        // ── SchoolModule: add new columns ──
        migrationBuilder.AddColumn<string>(
            name: "Notes",
            table: "SchoolModules",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsPilot",
            table: "SchoolModules",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // ── AreaModuleAssignments: create table ──
        migrationBuilder.CreateTable(
            name: "AreaModuleAssignments",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                AreaId = table.Column<int>(type: "integer", nullable: false),
                ModuleId = table.Column<int>(type: "integer", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                AllowSchoolSelfEnable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AssignedBy = table.Column<int>(type: "integer", nullable: true),
                Notes = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AreaModuleAssignments", x => x.Id);
                table.ForeignKey(
                    name: "FK_AreaModuleAssignments_Areas_AreaId",
                    column: x => x.AreaId,
                    principalTable: "Areas",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AreaModuleAssignments_Modules_ModuleId",
                    column: x => x.ModuleId,
                    principalTable: "Modules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AreaModuleAssignments_AreaId_ModuleId",
            table: "AreaModuleAssignments",
            columns: new[] { "AreaId", "ModuleId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_AreaModuleAssignments_ModuleId",
            table: "AreaModuleAssignments",
            column: "ModuleId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AreaModuleAssignments");

        migrationBuilder.DropColumn(name: "IsPilot", table: "SchoolModules");
        migrationBuilder.DropColumn(name: "Notes", table: "SchoolModules");

        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Modules");
        migrationBuilder.DropColumn(name: "CreatedAt", table: "Modules");
        migrationBuilder.DropColumn(name: "License", table: "Modules");
        migrationBuilder.DropColumn(name: "Author", table: "Modules");
        migrationBuilder.DropColumn(name: "ConfigJson", table: "Modules");
        migrationBuilder.DropColumn(name: "BundlePath", table: "Modules");
        migrationBuilder.DropColumn(name: "EntryUrl", table: "Modules");
        migrationBuilder.DropColumn(name: "RegistrationType", table: "Modules");
        migrationBuilder.DropColumn(name: "VisibilityLevels", table: "Modules");
    }
}
