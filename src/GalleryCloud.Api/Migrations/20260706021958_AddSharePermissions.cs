using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalleryCloud.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSharePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowDownload",
                table: "Shares",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowMetadata",
                table: "Shares",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowDownload",
                table: "Shares");

            migrationBuilder.DropColumn(
                name: "AllowMetadata",
                table: "Shares");
        }
    }
}
