using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Application.ShortStay.Queries.GetActiveAccommodationTypes;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.Tests.ShortStay.Queries;

public sealed class GetActiveAccommodationTypesQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsTypesOrderedBySortOrder()
    {
        var villa = AccommodationType.Create("villa", "فيلا", "Villa", "Tourism", sortOrder: 2);
        var chalet = AccommodationType.Create("chalet", "شاليه", "Chalet", "Tourism", sortOrder: 1);
        var repo = new StubRepository([villa, chalet]);
        var handler = new GetActiveAccommodationTypesQueryHandler(repo);

        var result = await handler.Handle(new GetActiveAccommodationTypesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("chalet", result[0].Code);
        Assert.Equal("villa", result[1].Code);
    }

    private sealed class StubRepository : IAccommodationTypeRepository
    {
        private readonly IReadOnlyList<AccommodationType> _types;
        public StubRepository(IReadOnlyList<AccommodationType> types) => _types = types;
        public Task<AccommodationType?> GetByIdAsync(int id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AccommodationType>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult(_types);
    }
}
