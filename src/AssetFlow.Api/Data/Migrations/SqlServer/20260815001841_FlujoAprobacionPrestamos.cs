using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetFlow.Api.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class FlujoAprobacionPrestamos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "LoanDate",
                table: "Loans",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "Loans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DecidedByUserId",
                table: "Loans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "Loans",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAt",
                table: "Loans",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnDecidedAt",
                table: "Loans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnDecidedByUserId",
                table: "Loans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnDecisionNote",
                table: "Loans",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnRequestedAt",
                table: "Loans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    ActorUsername = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvalidatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loans_DecidedByUserId",
                table: "Loans",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_ReturnDecidedByUserId",
                table: "Loans",
                column: "ReturnDecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_Status_UserId",
                table: "Loans",
                columns: new[] { "Status", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_ActorUserId",
                table: "AuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_EntityType_EntityId",
                table: "AuditEntries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                table: "PasswordResetTokens",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Users_DecidedByUserId",
                table: "Loans",
                column: "DecidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Users_ReturnDecidedByUserId",
                table: "Loans",
                column: "ReturnDecidedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Users_DecidedByUserId",
                table: "Loans");

            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Users_ReturnDecidedByUserId",
                table: "Loans");

            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_Loans_DecidedByUserId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_ReturnDecidedByUserId",
                table: "Loans");

            migrationBuilder.DropIndex(
                name: "IX_Loans_Status_UserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DecidedByUserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "RequestedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnDecidedAt",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnDecidedByUserId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnDecisionNote",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "ReturnRequestedAt",
                table: "Loans");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "LoanDate",
                table: "Loans",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
