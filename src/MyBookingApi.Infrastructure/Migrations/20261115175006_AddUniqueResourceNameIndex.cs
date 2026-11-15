using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBookingApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueResourceNameIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Resources_Name",
                table: "Resources",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Resources_Name",
                table: "Resources");
        }
    }
}
