using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace OutfitFinder.Infrastructure.Data.Migrations.Outfit
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AiProviderSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Deployment = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    Endpoint = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    EncryptedKey = table.Column<string>(type: "text", nullable: false),
                    KeyLast4 = table.Column<string>(type: "varchar(4)", maxLength: 4, nullable: true),
                    WebSearchEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Actor = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Consents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    VisitorId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Document = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Version = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    AcceptedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Consents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    FriendlyName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MeasurementProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    VisitorId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    MeasurementsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasurementProfiles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModerationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ImageHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    VisitorId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModerationEvents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutfitSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    VisitorId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    PendingBlobPath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    BlurredBlobPath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ImageHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    IpHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Stage = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    Provider = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    Source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ClaimedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<Guid>(type: "char(36)", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutfitSearches", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ProblemReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    OutfitSearchId = table.Column<Guid>(type: "char(36)", nullable: true),
                    VisitorId = table.Column<Guid>(type: "char(36)", nullable: true),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ContactEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ContentUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    Reason = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Details = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemReports", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Visitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visitors", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutfitItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OutfitSearchId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    BrandHints = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    BoxX = table.Column<double>(type: "double", nullable: false),
                    BoxY = table.Column<double>(type: "double", nullable: false),
                    BoxW = table.Column<double>(type: "double", nullable: false),
                    BoxH = table.Column<double>(type: "double", nullable: false),
                    Confidence = table.Column<double>(type: "double", nullable: false),
                    IsUserAdjusted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MakeItJson = table.Column<string>(type: "text", nullable: true),
                    Makeable = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutfitItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutfitItems_OutfitSearches_OutfitSearchId",
                        column: x => x.OutfitSearchId,
                        principalTable: "OutfitSearches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ItemMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OutfitItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Store = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: true),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    ImageUrl = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    MatchType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    LinkVerified = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Feedback = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemMatches_OutfitItems_OutfitItemId",
                        column: x => x.OutfitItemId,
                        principalTable: "OutfitItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PatternJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OutfitItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Block = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false),
                    MeasurementsSnapshot = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PdfBlobPath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    A0PdfBlobPath = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatternJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatternJobs_OutfitItems_OutfitItemId",
                        column: x => x.OutfitItemId,
                        principalTable: "OutfitItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MaterialLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    PatternJobId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Description = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: false),
                    Unit = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(12,4)", precision: 12, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLines_PatternJobs_PatternJobId",
                        column: x => x.PatternJobId,
                        principalTable: "PatternJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_CreatedUtc",
                table: "AuditEntries",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Consents_UserId",
                table: "Consents",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemMatches_OutfitItemId",
                table: "ItemMatches",
                column: "OutfitItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLines_PatternJobId",
                table: "MaterialLines",
                column: "PatternJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ModerationEvents_CreatedUtc",
                table: "ModerationEvents",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitItems_OutfitSearchId",
                table: "OutfitItems",
                column: "OutfitSearchId");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitSearches_CreatedUtc",
                table: "OutfitSearches",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitSearches_ImageHash",
                table: "OutfitSearches",
                column: "ImageHash");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitSearches_Status_CreatedUtc",
                table: "OutfitSearches",
                columns: new[] { "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutfitSearches_UserId",
                table: "OutfitSearches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitSearches_VisitorId",
                table: "OutfitSearches",
                column: "VisitorId");

            migrationBuilder.CreateIndex(
                name: "IX_PatternJobs_OutfitItemId",
                table: "PatternJobs",
                column: "OutfitItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Visitors_UserId",
                table: "Visitors",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiProviderSettings");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "Consents");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "ItemMatches");

            migrationBuilder.DropTable(
                name: "MaterialLines");

            migrationBuilder.DropTable(
                name: "MeasurementProfiles");

            migrationBuilder.DropTable(
                name: "ModerationEvents");

            migrationBuilder.DropTable(
                name: "ProblemReports");

            migrationBuilder.DropTable(
                name: "Visitors");

            migrationBuilder.DropTable(
                name: "PatternJobs");

            migrationBuilder.DropTable(
                name: "OutfitItems");

            migrationBuilder.DropTable(
                name: "OutfitSearches");
        }
    }
}
