namespace PropertyApi.Domain.Users.Constants;

public static class RoleNames
{
    public const string User = "User";
    public const string Agent = "Agent";
    public const string Admin = "Admin";

    public static readonly string[] All =
    [
        User,
        Agent,
        Admin
    ];
}