using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyShareEventRepository
{
    void Add(PropertyShareEvent shareEvent);
}
