namespace Ike.Api.Models;

public class Supplier
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? Phone { get; set; }
    public string? ContactPerson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Company? Company { get; set; }
    public ICollection<PartSupplier> Parts { get; set; } = new List<PartSupplier>();
}
