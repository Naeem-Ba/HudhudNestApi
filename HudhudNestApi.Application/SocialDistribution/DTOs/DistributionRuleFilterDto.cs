using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

/// <summary>Admin-panel listing filter (spec §18: Pagination/Filtering/Sorting by province/type/transaction/account, search by name).</summary>
public sealed record DistributionRuleFilterDto(
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    Guid? SocialAccountId,
    bool? IsActive,
    bool IncludeArchived,
    string? SearchText,
    int Page = 1,
    int PageSize = 20);
