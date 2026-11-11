using MyBookingApi.Domain.Entities;

namespace MyBookingApi.Application.Common.Interfaces;

public interface IBookingRepository
{
    Task<Booking?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> IsOverlappingAsync(Guid resourceId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);
    void Add(Booking booking);
}