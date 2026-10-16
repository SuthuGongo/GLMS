namespace GLMS.Core.Domain;

/// <summary>A TechMove customer. One client has many contracts (1..*).</summary>
public class Client
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // "Contact Details"
    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public ICollection<Contract> Contracts { get; set; } = new List<Contract>();
}
