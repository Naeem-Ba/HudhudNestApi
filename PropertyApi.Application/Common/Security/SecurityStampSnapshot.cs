namespace PropertyApi.Application.Common.Security;

public sealed record SecurityStampSnapshot(
    string SecurityStamp,
    bool IsDeleted);

