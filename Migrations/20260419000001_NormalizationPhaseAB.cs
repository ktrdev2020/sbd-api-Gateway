using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gateway.Migrations
{
    /// <inheritdoc />
    public partial class NormalizationPhaseAB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Phase A1: Personnel.PersonnelType text → PersonnelTypeId NOT NULL ──

            // Fill PersonnelTypeId for all legacy rows using Code match
            migrationBuilder.Sql("""
                UPDATE "Personnel" p
                SET "PersonnelTypeId" = pt."Id"
                FROM "PersonnelTypes" pt
                WHERE p."PersonnelType" = pt."Code"
                  AND p."PersonnelTypeId" IS NULL;
                """);

            // Make PersonnelTypeId NOT NULL
            migrationBuilder.AlterColumn<int>(
                name: "PersonnelTypeId",
                table: "Personnel",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // Drop legacy text column + its index
            migrationBuilder.DropIndex(
                name: "IX_Personnel_SubjectArea",
                table: "Personnel");

            migrationBuilder.DropColumn(
                name: "PersonnelType",
                table: "Personnel");

            // ── Phase A2: Personnel.SubjectArea text → SubjectAreaId FK ──

            migrationBuilder.AddColumn<int>(
                name: "SubjectAreaId",
                table: "Personnel",
                type: "integer",
                nullable: true);

            // Migrate existing text → FK
            migrationBuilder.Sql("""
                UPDATE "Personnel" p
                SET "SubjectAreaId" = sa."Id"
                FROM "SubjectAreas" sa
                WHERE p."SubjectArea" = sa."NameTh"
                  AND p."SubjectArea" IS NOT NULL;
                """);

            // Drop text column (after data migrated)
            migrationBuilder.DropColumn(
                name: "SubjectArea",
                table: "Personnel");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_SubjectAreaId",
                table: "Personnel",
                column: "SubjectAreaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Personnel_SubjectAreas_SubjectAreaId",
                table: "Personnel",
                column: "SubjectAreaId",
                principalTable: "SubjectAreas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Phase A3: Personnel.Specialty text → SpecialtyId FK ──

            migrationBuilder.AddColumn<int>(
                name: "SpecialtyId",
                table: "Personnel",
                type: "integer",
                nullable: true);

            // Migrate existing text → FK
            migrationBuilder.Sql("""
                UPDATE "Personnel" p
                SET "SpecialtyId" = s."Id"
                FROM "Specialties" s
                WHERE p."Specialty" = s."NameTh"
                  AND p."Specialty" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "Specialty",
                table: "Personnel");

            migrationBuilder.CreateIndex(
                name: "IX_Personnel_SpecialtyId",
                table: "Personnel",
                column: "SpecialtyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Personnel_Specialties_SpecialtyId",
                table: "Personnel",
                column: "SpecialtyId",
                principalTable: "Specialties",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Phase A4: padm_area_personnel.PersonnelType text → PersonnelTypeId FK ──
            // Table is currently empty (0 rows) — migration is trivial

            migrationBuilder.AddColumn<int>(
                name: "PersonnelTypeId",
                table: "padm_area_personnel",
                type: "integer",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "PersonnelType",
                table: "padm_area_personnel");

            migrationBuilder.CreateIndex(
                name: "IX_padm_area_personnel_PersonnelTypeId",
                table: "padm_area_personnel",
                column: "PersonnelTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_padm_area_personnel_PersonnelTypes_PersonnelTypeId",
                table: "padm_area_personnel",
                column: "PersonnelTypeId",
                principalTable: "PersonnelTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ── Phase B1: Schools.Phone2 → DROP ──

            migrationBuilder.DropColumn(
                name: "Phone2",
                table: "Schools");

            // ── Phase B2: Schools.SchoolLevel (numeric code 1–18) → SchoolLevelId FK ──

            migrationBuilder.CreateTable(
                name: "SchoolLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    NameTh = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchoolLevels", x => x.Id);
                    table.UniqueConstraint("AK_SchoolLevels_Code", x => x.Code);
                });

            // Seed school levels (Ministry standard numeric codes 1–18)
            migrationBuilder.Sql("""
                INSERT INTO "SchoolLevels" ("Code","NameTh","SortOrder") VALUES
                  ('1',  'ขนาดโรงเรียน ระดับ 1',  1),
                  ('2',  'ขนาดโรงเรียน ระดับ 2',  2),
                  ('3',  'ขนาดโรงเรียน ระดับ 3',  3),
                  ('4',  'ขนาดโรงเรียน ระดับ 4',  4),
                  ('5',  'ขนาดโรงเรียน ระดับ 5',  5),
                  ('6',  'ขนาดโรงเรียน ระดับ 6',  6),
                  ('7',  'ขนาดโรงเรียน ระดับ 7',  7),
                  ('8',  'ขนาดโรงเรียน ระดับ 8',  8),
                  ('9',  'ขนาดโรงเรียน ระดับ 9',  9),
                  ('10', 'ขนาดโรงเรียน ระดับ 10', 10),
                  ('11', 'ขนาดโรงเรียน ระดับ 11', 11),
                  ('12', 'ขนาดโรงเรียน ระดับ 12', 12),
                  ('13', 'ขนาดโรงเรียน ระดับ 13', 13),
                  ('14', 'ขนาดโรงเรียน ระดับ 14', 14),
                  ('15', 'ขนาดโรงเรียน ระดับ 15', 15),
                  ('16', 'ขนาดโรงเรียน ระดับ 16', 16),
                  ('17', 'ขนาดโรงเรียน ระดับ 17', 17),
                  ('18', 'ขนาดโรงเรียน ระดับ 18', 18);
                """);

            migrationBuilder.AddColumn<int>(
                name: "SchoolLevelId",
                table: "Schools",
                type: "integer",
                nullable: true);

            // Migrate numeric code in SchoolLevel text → SchoolLevelId
            // SPLIT_PART handles dirty value like '4 บ้านมัดกา' → takes '4'
            migrationBuilder.Sql("""
                UPDATE "Schools" s
                SET "SchoolLevelId" = sl."Id"
                FROM "SchoolLevels" sl
                WHERE TRIM(SPLIT_PART(s."SchoolLevel", ' ', 1)) = sl."Code"
                  AND s."SchoolLevel" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "SchoolLevel",
                table: "Schools");

            migrationBuilder.CreateIndex(
                name: "IX_Schools_SchoolLevelId",
                table: "Schools",
                column: "SchoolLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Schools_SchoolLevels_SchoolLevelId",
                table: "Schools",
                column: "SchoolLevelId",
                principalTable: "SchoolLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── Phase C: StudentProfiles → StudentParents table ──

            migrationBuilder.CreateTable(
                name: "StudentParents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StudentProfileId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Relation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false,
                        defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentParents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentParents_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentParents_StudentProfileId",
                table: "StudentParents",
                column: "StudentProfileId");

            // Migrate existing inline parent data
            migrationBuilder.Sql("""
                INSERT INTO "StudentParents" ("StudentProfileId","Name","Phone","Relation","IsPrimary","CreatedAt")
                SELECT "Id", "ParentName", "ParentPhone", "ParentRelation", true, NOW()
                FROM "StudentProfiles"
                WHERE "ParentName" IS NOT NULL AND TRIM("ParentName") <> '';
                """);

            migrationBuilder.DropColumn(name: "ParentName", table: "StudentProfiles");
            migrationBuilder.DropColumn(name: "ParentPhone", table: "StudentProfiles");
            migrationBuilder.DropColumn(name: "ParentRelation", table: "StudentProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // StudentParents → restore inline fields
            migrationBuilder.AddColumn<string>(name: "ParentName", table: "StudentProfiles", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ParentPhone", table: "StudentProfiles", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ParentRelation", table: "StudentProfiles", type: "text", nullable: true);
            migrationBuilder.Sql("""
                UPDATE "StudentProfiles" sp
                SET "ParentName" = par."Name",
                    "ParentPhone" = par."Phone",
                    "ParentRelation" = par."Relation"
                FROM "StudentParents" par
                WHERE par."StudentProfileId" = sp."Id" AND par."IsPrimary" = true;
                """);
            migrationBuilder.DropTable(name: "StudentParents");

            // Schools SchoolLevel
            migrationBuilder.DropForeignKey(name: "FK_Schools_SchoolLevels_SchoolLevelId", table: "Schools");
            migrationBuilder.DropIndex(name: "IX_Schools_SchoolLevelId", table: "Schools");
            migrationBuilder.AddColumn<string>(name: "SchoolLevel", table: "Schools", type: "text", nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Schools" s SET "SchoolLevel" = sl."Code"
                FROM "SchoolLevels" sl WHERE s."SchoolLevelId" = sl."Id";
                """);
            migrationBuilder.DropColumn(name: "SchoolLevelId", table: "Schools");
            migrationBuilder.DropTable(name: "SchoolLevels");

            // Schools Phone2
            migrationBuilder.AddColumn<string>(name: "Phone2", table: "Schools", type: "text", nullable: true);

            // padm PersonnelType
            migrationBuilder.DropForeignKey(name: "FK_padm_area_personnel_PersonnelTypes_PersonnelTypeId", table: "padm_area_personnel");
            migrationBuilder.DropIndex(name: "IX_padm_area_personnel_PersonnelTypeId", table: "padm_area_personnel");
            migrationBuilder.AddColumn<string>(name: "PersonnelType", table: "padm_area_personnel",
                type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "");
            migrationBuilder.DropColumn(name: "PersonnelTypeId", table: "padm_area_personnel");

            // Personnel Specialty
            migrationBuilder.DropForeignKey(name: "FK_Personnel_Specialties_SpecialtyId", table: "Personnel");
            migrationBuilder.DropIndex(name: "IX_Personnel_SpecialtyId", table: "Personnel");
            migrationBuilder.AddColumn<string>(name: "Specialty", table: "Personnel",
                type: "character varying(200)", maxLength: 200, nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Personnel" p SET "Specialty" = s."NameTh"
                FROM "Specialties" s WHERE p."SpecialtyId" = s."Id";
                """);
            migrationBuilder.DropColumn(name: "SpecialtyId", table: "Personnel");

            // Personnel SubjectArea
            migrationBuilder.DropForeignKey(name: "FK_Personnel_SubjectAreas_SubjectAreaId", table: "Personnel");
            migrationBuilder.DropIndex(name: "IX_Personnel_SubjectAreaId", table: "Personnel");
            migrationBuilder.AddColumn<string>(name: "SubjectArea", table: "Personnel",
                type: "character varying(100)", maxLength: 100, nullable: true);
            migrationBuilder.Sql("""
                UPDATE "Personnel" p SET "SubjectArea" = sa."NameTh"
                FROM "SubjectAreas" sa WHERE p."SubjectAreaId" = sa."Id";
                """);
            migrationBuilder.DropColumn(name: "SubjectAreaId", table: "Personnel");
            migrationBuilder.CreateIndex(name: "IX_Personnel_SubjectArea", table: "Personnel", column: "SubjectArea");

            // Personnel PersonnelType
            migrationBuilder.AddColumn<string>(name: "PersonnelType", table: "Personnel",
                type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "");
            migrationBuilder.Sql("""
                UPDATE "Personnel" p SET "PersonnelType" = pt."Code"
                FROM "PersonnelTypes" pt WHERE p."PersonnelTypeId" = pt."Id";
                """);
            migrationBuilder.AlterColumn<int>(name: "PersonnelTypeId", table: "Personnel",
                type: "integer", nullable: true,
                oldClrType: typeof(int), oldType: "integer", oldNullable: false);
        }
    }
}
