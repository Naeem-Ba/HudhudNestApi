using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RebindBusinessUserRelationshipsToUserAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_Users_UserId",
                table: "Favorites");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Users_ReceiverId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Users_SenderId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Users_RecipientId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Users_AgentId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_Users_OwnerId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyReviews_Users_ReviewerId",
                table: "PropertyReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitRequests_Users_RequesterId",
                table: "VisitRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_UserAccounts_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_UserAccounts_UserId",
                table: "Favorites",
                column: "UserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_UserAccounts_ReceiverId",
                table: "Messages",
                column: "ReceiverId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_UserAccounts_SenderId",
                table: "Messages",
                column: "SenderId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_UserAccounts_RecipientId",
                table: "Notifications",
                column: "RecipientId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_UserAccounts_AgentId",
                table: "Properties",
                column: "AgentId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_UserAccounts_OwnerId",
                table: "Properties",
                column: "OwnerId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyReviews_UserAccounts_ReviewerId",
                table: "PropertyReviews",
                column: "ReviewerId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitRequests_UserAccounts_RequesterId",
                table: "VisitRequests",
                column: "RequesterId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_UserAccounts_UserId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Favorites_UserAccounts_UserId",
                table: "Favorites");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_UserAccounts_ReceiverId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_UserAccounts_SenderId",
                table: "Messages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_UserAccounts_RecipientId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_UserAccounts_AgentId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_Properties_UserAccounts_OwnerId",
                table: "Properties");

            migrationBuilder.DropForeignKey(
                name: "FK_PropertyReviews_UserAccounts_ReviewerId",
                table: "PropertyReviews");

            migrationBuilder.DropForeignKey(
                name: "FK_VisitRequests_UserAccounts_RequesterId",
                table: "VisitRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_UserId",
                table: "AuditLogs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Favorites_Users_UserId",
                table: "Favorites",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Users_ReceiverId",
                table: "Messages",
                column: "ReceiverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Users_SenderId",
                table: "Messages",
                column: "SenderId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Users_RecipientId",
                table: "Notifications",
                column: "RecipientId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Users_AgentId",
                table: "Properties",
                column: "AgentId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Properties_Users_OwnerId",
                table: "Properties",
                column: "OwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PropertyReviews_Users_ReviewerId",
                table: "PropertyReviews",
                column: "ReviewerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VisitRequests_Users_RequesterId",
                table: "VisitRequests",
                column: "RequesterId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
