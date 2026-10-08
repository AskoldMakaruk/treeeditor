using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreeEditor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "version",
                table: "elements",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "tree_revision",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tree_revision", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "tree_revision",
                columns: new[] { "id", "revision" },
                values: new object[] { 1, 1L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tree_revision");

            migrationBuilder.DropColumn(
                name: "version",
                table: "elements");
        }
    }
}
