namespace InteractivesApi.Models.MailModels
{
    public class MailApp
    {
        public required string MailTo { get; init; }
        public required string MailSubject { get; init; }
        public required string MailBody { get; init; }
        public string MailFrom { get; init; } = string.Empty;

        /// <summary>Optional Reply-To address (e.g. the visitor who filled the contact form).</summary>
        public string? ReplyTo { get; init; }
    }

    /// <summary>Model bound to <c>ConfirmAccountTemplate.html</c>.</summary>
    public record ConfirmMailModel(string DisplayName, string ConfirmLink, string WebsiteLink);

    /// <summary>Model bound to <c>ResetPasswordTemplate.html</c>.</summary>
    public record ResetPasswordModel(string DisplayName, string ResetLink, string WebsiteLink);

    /// <summary>Model bound to <c>ContactNotificationTemplate.html</c> (sent to the company inbox).</summary>
    public record ContactNotificationModel(string Name, string Email, string Subject, string Message);

    /// <summary>Model bound to <c>ContactAcknowledgementTemplate.html</c> (sent back to the visitor).</summary>
    public record ContactAckModel(string Name, string Message, string WebsiteLink);

    /// <summary>
    /// Model bound to <c>CaseStatusChangedTemplate.cshtml</c> (sent to the case's client).
    /// <paramref name="FromStatusLabel"/> is null on creation, where there is no previous status,
    /// and <paramref name="Comment"/> is null when the admin left none.
    /// </summary>
    public record CaseStatusChangedModel(
        string ClientName,
        string CaseTitle,
        string CaseType,
        string? FromStatusLabel,
        string ToStatusLabel,
        string StatusColor,
        string StatusMessage,
        string? Comment,
        string CaseLink,
        string WebsiteLink
    );

    /// <summary>
    /// Model bound to <c>InvoiceGeneratedTemplate.cshtml</c> (sent to the case's client when an
    /// admin issues a devis). Amounts and dates arrive pre-formatted in fr-FR by
    /// <c>InvoiceFormat</c>, so they read exactly as on the PDF.
    /// </summary>
    public record InvoiceGeneratedModel(
        string ClientName,
        string InvoiceNumber,
        string InvoiceTitle,
        string CaseTitle,
        string CaseType,
        string IssuedAt,
        string ValidUntil,
        string TotalHt,
        string DepositPercent,
        string DepositAmount,
        string BalanceAmount,
        string? Notes,
        string CaseLink,
        string WebsiteLink
    );

    /// <summary>
    /// Une pièce encore attendue du client, telle qu'elle apparaît dans la relance.
    /// <paramref name="RejectionNote"/> n'est renseigné que sur une pièce refusée, et seulement si
    /// l'admin a motivé son refus.
    /// </summary>
    public record MissingDocumentItem(
        string Label,
        string? Description,
        bool IsRequired,
        string? DocumentTypeName,
        bool WasRejected,
        string? RejectionNote
    );

    /// <summary>
    /// Model bound to <c>CaseDocumentsReminderTemplate.cshtml</c> (sent to the case's client).
    /// <paramref name="Comment"/> is the optional free-text message the admin typed when relancing.
    /// </summary>
    public record CaseDocumentsReminderModel(
        string ClientName,
        string CaseTitle,
        string CaseType,
        IReadOnlyList<MissingDocumentItem> Documents,
        string? Comment,
        string DocumentsLink,
        string WebsiteLink
    );

}
