using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalleryCloud.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExifFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aperture",
                table: "Photos",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExposureTime",
                table: "Photos",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FocalLength",
                table: "Photos",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FocalLength35mm",
                table: "Photos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Iso",
                table: "Photos",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aperture",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "ExposureTime",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "FocalLength",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "FocalLength35mm",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "Iso",
                table: "Photos");
        }
    }
}
