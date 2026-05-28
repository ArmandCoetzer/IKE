using Ike.Api.Helpers;
using Ike.Api.Models;
using Ike.Api.Services;
using Xunit;

namespace Ike.Api.Tests;

public class BusinessRulesRegressionTests
{
    private readonly IStatusTransitionService _transitions = new StatusTransitionService();

    [Fact]
    public void QuoteStatus_Rejects_BackwardTransition_SentToDraft()
    {
        var ok = _transitions.TryTransitionQuote(QuoteStatus.Sent, QuoteStatus.Draft, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void QuoteStatus_Allows_DraftToSent()
    {
        var ok = _transitions.TryTransitionQuote(QuoteStatus.Draft, QuoteStatus.Sent, out var next, out _);
        Assert.True(ok);
        Assert.Equal(QuoteStatus.Sent, next);
    }

    [Fact]
    public void QuoteStatus_Allows_SentToAccepted()
    {
        var ok = _transitions.TryTransitionQuote(QuoteStatus.Sent, QuoteStatus.Accepted, out var next, out _);
        Assert.True(ok);
        Assert.Equal(QuoteStatus.Accepted, next);
    }

    [Fact]
    public void PurchaseOrderStatus_Rejects_OrderedToDraft()
    {
        var ok = _transitions.TryTransitionPurchaseOrder(PurchaseOrderStatus.Ordered, PurchaseOrderStatus.Draft, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void SupplierQuoteRequest_Rejects_CancelledToOrdered()
    {
        var ok = _transitions.TryTransitionSupplierQuoteRequest(SupplierQuoteRequestStatus.Cancelled, SupplierQuoteRequestStatus.Ordered, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ServiceRequest_Rejects_ClosedToOpen()
    {
        var ok = _transitions.TryTransitionServiceRequest(ServiceRequestStatus.Closed, ServiceRequestStatus.Open, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void FinalClientSignOffHelper_Detects_CapturedSignature_ByDocumentTypeAndFile()
    {
        var docs = new List<JobCardDocument>
        {
            new() { DocumentType = "SitePhoto", FilePath = "uploads/job-documents/abc.jpg" },
            new() { DocumentType = JobCardFinalSignOffHelper.DocumentType, FilePath = "uploads/job-documents/sign.png" }
        };

        Assert.True(JobCardFinalSignOffHelper.HasCapturedSignature(docs));
    }

    [Fact]
    public void PaperPermitModeHelper_Hides_HistoricalPermits()
    {
        var permits = new List<JobPermit>
        {
            new() { HiddenFromUiForHistory = false, PermitNumber = 1 },
            new() { HiddenFromUiForHistory = true, PermitNumber = 2 }
        };

        var visible = PaperPermitModeHelper.VisiblePermits(permits).ToList();
        Assert.Single(visible);
        Assert.Equal(1, visible[0].PermitNumber);
    }

    [Fact]
    public void PermitStatus_ExpiredLike_Recognizes_OnlyExpired()
    {
        Assert.True(PermitStatus.IsExpiredLike(PermitStatus.Expired));
        Assert.False(PermitStatus.IsExpiredLike(PermitStatus.Active));
    }
}
