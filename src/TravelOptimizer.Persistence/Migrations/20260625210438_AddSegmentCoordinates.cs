using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelOptimizer.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSegmentCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "FromLat",
                table: "PredictionSegments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FromLng",
                table: "PredictionSegments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ToLat",
                table: "PredictionSegments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ToLng",
                table: "PredictionSegments",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromLat",
                table: "PredictionSegments");

            migrationBuilder.DropColumn(
                name: "FromLng",
                table: "PredictionSegments");

            migrationBuilder.DropColumn(
                name: "ToLat",
                table: "PredictionSegments");

            migrationBuilder.DropColumn(
                name: "ToLng",
                table: "PredictionSegments");
        }
    }
}
