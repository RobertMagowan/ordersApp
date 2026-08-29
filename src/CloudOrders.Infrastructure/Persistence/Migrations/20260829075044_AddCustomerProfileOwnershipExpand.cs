using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudOrders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerProfileOwnershipExpand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerProfileId",
                schema: "dbo",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerReference = table.Column<string>(type: "varchar(64)", nullable: false, collation: "Latin1_General_100_BIN2"),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_100_BIN2"),
                    ObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true, collation: "Latin1_General_100_CI_AS_SC"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.Id);
                    table.UniqueConstraint("AK_CustomerProfiles_CustomerReference", x => x.CustomerReference);
                    table.UniqueConstraint("AK_CustomerProfiles_Issuer_ObjectId", x => new { x.Issuer, x.ObjectId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerProfileId",
                schema: "dbo",
                table: "Orders",
                column: "CustomerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                column: "ActorCustomerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                column: "TargetCustomerProfileId");

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyRecords_CustomerProfiles_ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                column: "ActorCustomerProfileId",
                principalSchema: "dbo",
                principalTable: "CustomerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_IdempotencyRecords_CustomerProfiles_TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                column: "TargetCustomerProfileId",
                principalSchema: "dbo",
                principalTable: "CustomerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_CustomerProfiles_CustomerProfileId",
                schema: "dbo",
                table: "Orders",
                column: "CustomerProfileId",
                principalSchema: "dbo",
                principalTable: "CustomerProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyRecords_CustomerProfiles_ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_IdempotencyRecords_CustomerProfiles_TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_Orders_CustomerProfiles_CustomerProfileId",
                schema: "dbo",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "CustomerProfiles",
                schema: "dbo");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CustomerProfileId",
                schema: "dbo",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_IdempotencyRecords_TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "CustomerProfileId",
                schema: "dbo",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords");
        }
    }
}
