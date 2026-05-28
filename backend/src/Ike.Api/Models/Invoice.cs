namespace Ike.Api.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid JobCardId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? CompanyId { get; set; }
    public Guid SiteId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "ZAR";
    public string Status { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? PaidAt { get; set; }
    /// <summary>Reminder escalation stage for collections (0 none, 1 gentle, 2 urgent, 3 final).</summary>
    public int ReminderStage { get; set; }
    public DateTime? LastReminderSentAt { get; set; }
    public DateTime? PromiseToPayBy { get; set; }
    public DateTime? CollectionEscalatedAt { get; set; }
    /// <summary>Parts on invoice must be confirmed before sending to client.</summary>
    public bool PartsConfirmed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public JobCard JobCard { get; set; } = null!;
    public Quote? Quote { get; set; }
    public Company? Company { get; set; }
    public Site Site { get; set; } = null!;
    public ICollection<InvoiceLineItem> LineItems { get; set; } = new List<InvoiceLineItem>();
}
