using Tradion.Api.Models;

namespace Tradion.Api.Helpers;

/// <summary>Final job completion client sign-off: stored as a <see cref="JobCardDocument"/> with a signature image (same idea as permit client-signature attachments).</summary>
public static class JobCardFinalSignOffHelper
{
    public const string DocumentType = "FinalClientSignOff";

    public static bool HasCapturedSignature(IEnumerable<JobCardDocument> documents) =>
        documents.Any(d =>
            string.Equals(d.DocumentType, DocumentType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(d.FilePath));
}
