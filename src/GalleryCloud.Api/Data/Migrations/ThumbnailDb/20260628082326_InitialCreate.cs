using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalleryCloud.Api.Data.Migrations.ThumbnailDb
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThumbnailCache",
                columns: table => new
                {
                    PhotoId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Size = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThumbnailCache", x => new { x.PhotoId, x.Size });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThumbnailCache");
        }
    }
}
