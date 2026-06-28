using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GalleryCloud.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveThumbnailCacheFromMainDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThumbnailCache");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThumbnailCache",
                columns: table => new
                {
                    PhotoId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Size = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThumbnailCache", x => new { x.PhotoId, x.Size });
                    table.ForeignKey(
                        name: "FK_ThumbnailCache_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
