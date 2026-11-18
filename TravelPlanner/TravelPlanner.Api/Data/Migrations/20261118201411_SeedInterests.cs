using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TravelPlanner.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedInterests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Interests",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { 1, "Local dishes, markets, cafes and restaurants.", "Food" },
                    { 2, "Art, science and history museums.", "Museums" },
                    { 3, "Monuments, old towns and historic sites.", "History" },
                    { 4, "Parks, gardens, hikes and viewpoints.", "Nature" },
                    { 5, "Bars, live music and evening entertainment.", "Nightlife" },
                    { 6, "Shopping streets, boutiques and souvenirs.", "Shopping" },
                    { 7, "Beaches, coastline and waterfront walks.", "Beaches" },
                    { 8, "Galleries, street art and architecture.", "Art" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Interests",
                keyColumn: "Id",
                keyValue: 8);
        }
    }
}
