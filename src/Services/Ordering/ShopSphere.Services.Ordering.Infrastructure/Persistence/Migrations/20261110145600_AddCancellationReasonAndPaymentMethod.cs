using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopSphere.Services.Ordering.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCancellationReasonAndPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethod",
                table: "OrderStateData",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Orders",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethod",
                table: "OrderStateData");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Orders");
        }
    }
}
