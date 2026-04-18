using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gateway.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialtiesAndSubjectAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvatarThumbnailUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AvatarVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PersonnelContext",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonnelRefId",
                table: "Users",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferencesJson",
                table: "Users",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Schools",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedBy",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoThumbnailUrl",
                table: "Schools",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LogoVersion",
                table: "Schools",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SpecialRoleType",
                table: "PersonnelSchoolAssignments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AlterColumn<string>(
                name: "QualificationName",
                table: "PersonnelEducations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcademicStandingTypeId",
                table: "Personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AffiliationStatus",
                table: "Personnel",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "affiliated");

            migrationBuilder.AddColumn<int>(
                name: "PositionTypeId",
                table: "Personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Specialty",
                table: "Personnel",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectArea",
                table: "Personnel",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrashedAt",
                table: "Personnel",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrashedByUserId",
                table: "Personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Personnel",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UpdatedBy",
                table: "Personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NameTh",
                table: "EducationLevels",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "NameEn",
                table: "EducationLevels",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "EducationLevels",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<bool>(
                name: "AllowPersonnel",
                table: "AreaPermissionPolicies",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.CreateTable(
                name: "AuthSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    SessionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceLabel = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authz_capability_definitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Resource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameTh = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DefaultScope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsRedelegatable = table.Column<bool>(type: "boolean", nullable: false),
                    MaxDelegationDepth = table.Column<int>(type: "integer", nullable: false),
                    IsDangerous = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_capability_definitions", x => x.Id);
                    table.UniqueConstraint("AK_authz_capability_definitions_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "authz_functional_role_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NameTh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContextScope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GrantedCapabilitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                    CanBeAssignedByJson = table.Column<string>(type: "jsonb", nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_functional_role_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "authz_grant_audit_logs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetUserId = table.Column<int>(type: "integer", nullable: true),
                    CapabilityCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ScopeId = table.Column<int>(type: "integer", nullable: true),
                    GrantId = table.Column<long>(type: "bigint", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                    PrevLogHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ThisLogHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_grant_audit_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "authz_jit_elevations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CapabilityCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<int>(type: "integer", nullable: true),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_jit_elevations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authz_jit_elevations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "authz_recertification_campaigns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedBy = table.Column<int>(type: "integer", nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "active"),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_recertification_campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CacheDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CacheKeyPattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    GroupPrefix = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DbIndex = table.Column<int>(type: "integer", nullable: false),
                    SuggestedTtlMinutes = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "padm_area_personnel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    AreaId = table.Column<int>(type: "integer", nullable: false),
                    PersonnelCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TitlePrefixId = table.Column<int>(type: "integer", nullable: true),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IdCard = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    PersonnelType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PositionTypeId = table.Column<int>(type: "integer", nullable: true),
                    AcademicStandingTypeId = table.Column<int>(type: "integer", nullable: true),
                    WorkGroup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Gender = table.Column<char>(type: "character(1)", nullable: false),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LineId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Photo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AddressId = table.Column<int>(type: "integer", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_padm_area_personnel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_AcademicStandingTypes_AcademicStandingT~",
                        column: x => x.AcademicStandingTypeId,
                        principalTable: "AcademicStandingTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_Addresses_AddressId",
                        column: x => x.AddressId,
                        principalTable: "Addresses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_PositionTypes_PositionTypeId",
                        column: x => x.PositionTypeId,
                        principalTable: "PositionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_TitlePrefixes_TitlePrefixId",
                        column: x => x.TitlePrefixId,
                        principalTable: "TitlePrefixes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersonnelApprovalCycles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SchoolId = table.Column<int>(type: "integer", nullable: false),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StaffSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StaffSubmittedByUserId = table.Column<int>(type: "integer", nullable: true),
                    DeputyReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeputyReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    PrincipalApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PrincipalApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    AreaAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AreaAcceptedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReopenedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ReopenNote = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelApprovalCycles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelApprovalCycles_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonnelApprovalCycles_Schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "Schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonnelSecondments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonnelId = table.Column<int>(type: "integer", nullable: false),
                    SecondedToAreaId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AssignedModuleCodes = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonnelSecondments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonnelSecondments_Areas_SecondedToAreaId",
                        column: x => x.SecondedToAreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonnelSecondments_Personnel_PersonnelId",
                        column: x => x.PersonnelId,
                        principalTable: "Personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Specialties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameTh = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Specialties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubjectAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameTh = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubjectAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_risk_scores",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FactorsJson = table.Column<string>(type: "jsonb", nullable: false),
                    LastScoredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_risk_scores", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_risk_scores_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authz_capability_grants",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GranteeUserId = table.Column<int>(type: "integer", nullable: false),
                    CapabilityCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<int>(type: "integer", nullable: true),
                    GrantedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ParentGrantId = table.Column<long>(type: "bigint", nullable: true),
                    RedelegationDepth = table.Column<int>(type: "integer", nullable: false),
                    CanRedelegate = table.Column<bool>(type: "boolean", nullable: false),
                    RemainingDepth = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateOnly>(type: "date", nullable: true),
                    ConditionsJson = table.Column<string>(type: "jsonb", nullable: true),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OrderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RevokeReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_capability_grants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authz_capability_grants_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_capability_grants_Users_GranteeUserId",
                        column: x => x.GranteeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_capability_grants_authz_capability_definitions_Capabi~",
                        column: x => x.CapabilityCode,
                        principalTable: "authz_capability_definitions",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_capability_grants_authz_capability_grants_ParentGrant~",
                        column: x => x.ParentGrantId,
                        principalTable: "authz_capability_grants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "authz_grant_approval_requests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequesterUserId = table.Column<int>(type: "integer", nullable: false),
                    TargetUserId = table.Column<int>(type: "integer", nullable: false),
                    CapabilityDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ScopeId = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_grant_approval_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authz_grant_approval_requests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_grant_approval_requests_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_grant_approval_requests_authz_capability_definitions_~",
                        column: x => x.CapabilityDefinitionId,
                        principalTable: "authz_capability_definitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "authz_functional_assignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    FunctionalRoleTypeId = table.Column<int>(type: "integer", nullable: false),
                    ContextScopeType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContextScopeId = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    AssignedByUserId = table.Column<int>(type: "integer", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrderRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_functional_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authz_functional_assignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_functional_assignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_functional_assignments_authz_functional_role_types_Fu~",
                        column: x => x.FunctionalRoleTypeId,
                        principalTable: "authz_functional_role_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "padm_area_personnel_certifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AreaPersonnelId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IssuedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CertificateNo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttachmentPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_padm_area_personnel_certifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_certifications_padm_area_personnel_Area~",
                        column: x => x.AreaPersonnelId,
                        principalTable: "padm_area_personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "padm_area_personnel_educations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AreaPersonnelId = table.Column<int>(type: "integer", nullable: false),
                    Degree = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Major = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Institution = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    GraduatedYear = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_padm_area_personnel_educations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_padm_area_personnel_educations_padm_area_personnel_AreaPers~",
                        column: x => x.AreaPersonnelId,
                        principalTable: "padm_area_personnel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "authz_recertification_items",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CampaignId = table.Column<long>(type: "bigint", nullable: false),
                    GrantId = table.Column<long>(type: "bigint", nullable: false),
                    GranteeUserId = table.Column<int>(type: "integer", nullable: false),
                    ReviewerUserId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "pending"),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_authz_recertification_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_authz_recertification_items_authz_capability_grants_GrantId",
                        column: x => x.GrantId,
                        principalTable: "authz_capability_grants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_authz_recertification_items_authz_recertification_campaigns~",
                        column: x => x.CampaignId,
                        principalTable: "authz_recertification_campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Schools_DeletedAt",
                table: "Schools",
                column: "DeletedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_AcademicStandingTypeId",
                table: "Personnel",
                column: "AcademicStandingTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_AffiliationStatus",
                table: "Personnel",
                column: "AffiliationStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_PositionTypeId",
                table: "Personnel",
                column: "PositionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_SubjectArea",
                table: "Personnel",
                column: "SubjectArea");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_LastSeenAt",
                table: "AuthSessions",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_SessionId",
                table: "AuthSessions",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthSessions_UserId_RevokedAt",
                table: "AuthSessions",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_authz_cap_def_code",
                table: "authz_capability_definitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authz_capability_grants_CapabilityCode",
                table: "authz_capability_grants",
                column: "CapabilityCode");

            migrationBuilder.CreateIndex(
                name: "IX_authz_capability_grants_GrantedByUserId",
                table: "authz_capability_grants",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_grants_expires",
                table: "authz_capability_grants",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_authz_grants_grantee_cap_scope",
                table: "authz_capability_grants",
                columns: new[] { "GranteeUserId", "CapabilityCode", "ScopeType", "ScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_authz_grants_parent",
                table: "authz_capability_grants",
                column: "ParentGrantId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_func_assign_enddate",
                table: "authz_functional_assignments",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_authz_func_assign_user_role_scope",
                table: "authz_functional_assignments",
                columns: new[] { "UserId", "FunctionalRoleTypeId", "ContextScopeType", "ContextScopeId" });

            migrationBuilder.CreateIndex(
                name: "IX_authz_functional_assignments_AssignedByUserId",
                table: "authz_functional_assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_functional_assignments_FunctionalRoleTypeId",
                table: "authz_functional_assignments",
                column: "FunctionalRoleTypeId");

            migrationBuilder.CreateIndex(
                name: "UX_authz_func_role_code",
                table: "authz_functional_role_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authz_approval_status_requester",
                table: "authz_grant_approval_requests",
                columns: new[] { "Status", "RequesterUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_authz_grant_approval_requests_CapabilityDefinitionId",
                table: "authz_grant_approval_requests",
                column: "CapabilityDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_grant_approval_requests_RequesterUserId",
                table: "authz_grant_approval_requests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_grant_approval_requests_TargetUserId",
                table: "authz_grant_approval_requests",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_audit_actor",
                table: "authz_grant_audit_logs",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_audit_grant",
                table: "authz_grant_audit_logs",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_authz_audit_occurred",
                table: "authz_grant_audit_logs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_authz_jit_elevations_ExpiresAt",
                table: "authz_jit_elevations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_authz_jit_elevations_UserId_RevokedAt",
                table: "authz_jit_elevations",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_authz_recertification_campaigns_Status",
                table: "authz_recertification_campaigns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_authz_recertification_items_CampaignId_GrantId",
                table: "authz_recertification_items",
                columns: new[] { "CampaignId", "GrantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_authz_recertification_items_CampaignId_Status",
                table: "authz_recertification_items",
                columns: new[] { "CampaignId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_authz_recertification_items_GrantId",
                table: "authz_recertification_items",
                column: "GrantId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_AcademicStandingTypeId",
                table: "padm_area_personnel",
                column: "AcademicStandingTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_AddressId",
                table: "padm_area_personnel",
                column: "AddressId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_AreaId_IsActive",
                table: "padm_area_personnel",
                columns: new[] { "AreaId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_IdCard",
                table: "padm_area_personnel",
                column: "IdCard");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_PersonnelCode",
                table: "padm_area_personnel",
                column: "PersonnelCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_PositionTypeId",
                table: "padm_area_personnel",
                column: "PositionTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_TitlePrefixId",
                table: "padm_area_personnel",
                column: "TitlePrefixId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_UserId",
                table: "padm_area_personnel",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_WorkGroup",
                table: "padm_area_personnel",
                column: "WorkGroup");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_certifications_AreaPersonnelId",
                table: "padm_area_personnel_certifications",
                column: "AreaPersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_educations_AreaPersonnelId",
                table: "padm_area_personnel_educations",
                column: "AreaPersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelApprovalCycles_AcademicYearId",
                table: "PersonnelApprovalCycles",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelApprovalCycles_SchoolId_AcademicYearId",
                table: "PersonnelApprovalCycles",
                columns: new[] { "SchoolId", "AcademicYearId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelApprovalCycles_Status",
                table: "PersonnelApprovalCycles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSecondments_PersonnelId",
                table: "PersonnelSecondments",
                column: "PersonnelId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonnelSecondments_SecondedToAreaId",
                table: "PersonnelSecondments",
                column: "SecondedToAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_user_risk_scores_Level",
                table: "user_risk_scores",
                column: "Level");

            migrationBuilder.AddForeignKey(
                name: "FK_Personnel_AcademicStandingTypes_AcademicStandingTypeId",
                table: "Personnel",
                column: "AcademicStandingTypeId",
                principalTable: "AcademicStandingTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Personnel_PositionTypes_PositionTypeId",
                table: "Personnel",
                column: "PositionTypeId",
                principalTable: "PositionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonnelEducations_EducationLevels_EducationLevelId",
                table: "PersonnelEducations",
                column: "EducationLevelId",
                principalTable: "EducationLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Personnel_AcademicStandingTypes_AcademicStandingTypeId",
                table: "Personnel");

            migrationBuilder.DropForeignKey(
                name: "FK_Personnel_PositionTypes_PositionTypeId",
                table: "Personnel");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonnelEducations_EducationLevels_EducationLevelId",
                table: "PersonnelEducations");

            migrationBuilder.DropTable(
                name: "AuthSessions");

            migrationBuilder.DropTable(
                name: "authz_functional_assignments");

            migrationBuilder.DropTable(
                name: "authz_grant_approval_requests");

            migrationBuilder.DropTable(
                name: "authz_grant_audit_logs");

            migrationBuilder.DropTable(
                name: "authz_jit_elevations");

            migrationBuilder.DropTable(
                name: "authz_recertification_items");

            migrationBuilder.DropTable(
                name: "CacheDefinitions");

            migrationBuilder.DropTable(
                name: "padm_area_personnel_certifications");

            migrationBuilder.DropTable(
                name: "padm_area_personnel_educations");

            migrationBuilder.DropTable(
                name: "PersonnelApprovalCycles");

            migrationBuilder.DropTable(
                name: "PersonnelSecondments");

            migrationBuilder.DropTable(
                name: "Specialties");

            migrationBuilder.DropTable(
                name: "SubjectAreas");

            migrationBuilder.DropTable(
                name: "user_risk_scores");

            migrationBuilder.DropTable(
                name: "authz_functional_role_types");

            migrationBuilder.DropTable(
                name: "authz_capability_grants");

            migrationBuilder.DropTable(
                name: "authz_recertification_campaigns");

            migrationBuilder.DropTable(
                name: "padm_area_personnel");

            migrationBuilder.DropTable(
                name: "authz_capability_definitions");

            migrationBuilder.DropIndex(
                name: "IX_Schools_DeletedAt",
                table: "Schools");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_AcademicStandingTypeId",
                table: "Personnel");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_AffiliationStatus",
                table: "Personnel");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_PositionTypeId",
                table: "Personnel");

            migrationBuilder.DropIndex(
                name: "IX_Personnel_SubjectArea",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "AvatarThumbnailUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AvatarVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PersonnelContext",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PersonnelRefId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferencesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "DeletedBy",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "LogoThumbnailUrl",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "LogoVersion",
                table: "Schools");

            migrationBuilder.DropColumn(
                name: "SpecialRoleType",
                table: "PersonnelSchoolAssignments");

            migrationBuilder.DropColumn(
                name: "AcademicStandingTypeId",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "AffiliationStatus",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "PositionTypeId",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "Specialty",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "SubjectArea",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "TrashedAt",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "TrashedByUserId",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "Personnel");

            migrationBuilder.AlterColumn<string>(
                name: "QualificationName",
                table: "PersonnelEducations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "NameTh",
                table: "EducationLevels",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "NameEn",
                table: "EducationLevels",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "EducationLevels",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "AllowPersonnel",
                table: "AreaPermissionPolicies",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
