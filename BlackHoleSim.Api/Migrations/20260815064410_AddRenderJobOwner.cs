using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlackHoleSim.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRenderJobOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "RenderJobs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RenderJobs_OwnerId_CreatedAt",
                table: "RenderJobs",
                columns: new[] { "OwnerId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RenderJobs_OwnerId_CreatedAt",
                table: "RenderJobs");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "RenderJobs");
        }
    }
}
