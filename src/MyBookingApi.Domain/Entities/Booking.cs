using MyBookingApi.Domain.Common;
using MyBookingApi.Domain.Enums;

namespace MyBookingApi.Domain.Entities;



public class Booking : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid ResourceId { get; private set; }
    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }
    public BookingStatus Status { get; private set; }

    // EF Core will automatically populate these properties based on the foreign key relationships
    public User? User { get; private set; }
    public Resource? Resource { get; private set; }

    protected Booking() { }

    public Booking(Guid userId, Guid resourceId, DateTime startTime, DateTime endTime)
    {
        if (startTime >= endTime)
            throw new ArgumentException("Start time must be earlier than end time.");

        UserId = userId;
        ResourceId = resourceId;
        StartTime = startTime;
        EndTime = endTime;
        Status = BookingStatus.Pending;
    }

    // Domain rule: Check and execute booking cancellation
    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
            throw new InvalidOperationException("Booking is already cancelled.");

        if (DateTime.UtcNow >= StartTime)
            throw new InvalidOperationException("Cannot cancel a booking that has already started or passed.");

        Status = BookingStatus.Cancelled;
    }

    public void Confirm()
    {
        if (Status == BookingStatus.Pending)
        {
            Status = BookingStatus.Confirmed;
        }
    }
}