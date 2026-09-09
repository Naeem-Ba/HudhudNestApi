using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyAttributionEventRepository
{
    void Add(PropertyAttributionEvent attributionEvent);
}
