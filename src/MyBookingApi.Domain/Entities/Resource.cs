using MyBookingApi.Domain.Common;

namespace MyBookingApi.Domain.Entities;

public class Resource : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool IsAvailable { get; private set; } = true;

    protected Resource() { }

    public Resource(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public void UpdateDetails(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public void SetAvailability(bool isAvailable)
    {
        IsAvailable = isAvailable;
    }
}