using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Common.Interfaces;

public interface IResourceRepository
{
    Task<Resource?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<Resource>> GetAllAsync(CancellationToken cancellationToken = default);
    void Add(Resource resource);
}