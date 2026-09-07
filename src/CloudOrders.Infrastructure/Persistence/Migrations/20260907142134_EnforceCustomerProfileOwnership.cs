using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudOrders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCustomerProfileOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerProfileId",
                schema: "dbo",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubjectId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "dbo",
                table: "IdempotencyRecords",
                columns: new[] { "ActorCustomerProfileId", "IdempotencyKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "dbo",
                table: "IdempotencyRecords");

            migrationBuilder.AlterColumn<Guid>(
                name: "CustomerProfileId",
                schema: "dbo",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "TargetCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<string>(
                name: "SubjectId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorCustomerProfileId",
                schema: "dbo",
                table: "IdempotencyRecords",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                schema: "dbo",
                table: "IdempotencyRecords",
                columns: new[] { "SubjectId", "IdempotencyKey" });
        }
    }
}
