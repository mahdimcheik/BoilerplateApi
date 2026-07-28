using BoilerPlateApi.Models.MailModels;
using BoilerPlateApi.Utilities;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;
using RazorLight;

namespace BoilerPlateApi.Services
{
    public class MailService
    {
        private const string ConfirmAccountTemplate = "ConfirmAccountTemplate.cshtml";
        private const string ResetPasswordTemplate = "ResetPasswordTemplate.cshtml";
        private const string ContactNotificationTemplate = "ContactNotificationTemplate.cshtml";
        private const string ContactAckTemplate = "ContactAcknowledgementTemplate.cshtml";

        private  string FrontUrl = EnvironmentVariables.API_FRONT_URL;
        private  string DoNotReplyEmail = EnvironmentVariables.DO_NOT_REPLY_EMAIL;
        private  string NotificationEmail = EnvironmentVariables.NOTIFICATION_EMAIL;
        private string SmtpKey = EnvironmentVariables.SMTP_KEY;
        private string SmtpLogin = EnvironmentVariables.SMTP_LOGIN;
        private string SmtpHost = EnvironmentVariables.SMTP_HOST;
        private int SmtpPort = EnvironmentVariables.SMTP_PORT;




        // Static so the engine's compiled-template cache is shared across requests
        // even though MailService itself is registered as Scoped.
        private static readonly IRazorLightEngine RazorEngine = new RazorLightEngineBuilder()
            .UseFileSystemProject(
                Path.Combine(AppContext.BaseDirectory, "Features", "Mail", "Templates")
            )
            .UseMemoryCachingProvider()
            .Build();

        private readonly ILogger<MailService> _logger;

        public MailService(ILogger<MailService> logger)
        {
            _logger = logger;
        }

        /// <summary>Sends the account-activation mail. Returns false if delivery failed.</summary>
        public async Task<bool> SendConfirmAccountAsync(
            string email,
            string displayName,
            string confirmLink,
            CancellationToken ct
        )
        {
            var model = new ConfirmMailModel(displayName, confirmLink, FrontUrl);
            var body = await RenderAsync(ConfirmAccountTemplate, model);
            if (body is null)
            {
                return false;
            }

            return await SendEmailAsync(
                new MailApp
                {
                    MailFrom = DoNotReplyEmail,
                    MailTo = email,
                    MailSubject = "Confirmation de votre compte",
                    MailBody = body,
                },
                ct
            );
        }

        /// <summary>Sends the password-reset mail. Returns false if delivery failed.</summary>
        public async Task<bool> SendResetPasswordAsync(
            string email,
            string displayName,
            string resetLink,
            CancellationToken ct
        )
        {
            var model = new ResetPasswordModel(displayName, resetLink, FrontUrl);
            var body = await RenderAsync(ResetPasswordTemplate, model);
            if (body is null)
            {
                return false;
            }

            return await SendEmailAsync(
                new MailApp
                {
                    MailFrom = DoNotReplyEmail,
                    MailTo = email,
                    MailSubject = "Récupérer votre mot de passe",
                    MailBody = body,
                },
                ct
            );
        }

        /// <summary>
        /// Notifies the company inbox of a new contact-form message. Reply-To is set to the
        /// visitor's address so a reply goes straight back to them. Returns false if delivery failed.
        /// </summary>
        public async Task<bool> SendContactNotificationAsync(
            string toInbox,
            string visitorName,
            string visitorEmail,
            string subject,
            string message,
            CancellationToken ct
        )
        {
            var model = new ContactNotificationModel(visitorName, visitorEmail, subject, message);
            var body = await RenderAsync(ContactNotificationTemplate, model);
            if (body is null)
            {
                return false;
            }

            return await SendEmailAsync(
                new MailApp
                {
                    MailFrom = NotificationEmail,
                    MailTo = toInbox,
                    ReplyTo = visitorEmail,
                    MailSubject = $"[Contact] {subject}",
                    MailBody = body,
                },
                ct
            );
        }

        /// <summary>Sends the visitor an acknowledgement of their contact message. Returns false if delivery failed.</summary>
        public async Task<bool> SendContactAckAsync(
            string visitorEmail,
            string visitorName,
            string message,
            CancellationToken ct
        )
        {
            var model = new ContactAckModel(visitorName, message, FrontUrl);
            var body = await RenderAsync(ContactAckTemplate, model);
            if (body is null)
            {
                return false;
            }

            return await SendEmailAsync(
                new MailApp
                {
                    MailFrom = DoNotReplyEmail,
                    MailTo = visitorEmail,
                    MailSubject = "Nous avons bien reçu votre message",
                    MailBody = body,
                },
                ct
            );
        }

        public async Task<bool> SendEmailAsync(MailApp mail, CancellationToken ct)
        {
            try
            {
                var message = new MimeMessage
                {
                    Subject = mail.MailSubject,
                    Body = new TextPart(TextFormat.Html) { Text = mail.MailBody },
                };
                message.From.Add(MailboxAddress.Parse(mail.MailFrom));
                message.To.Add(MailboxAddress.Parse(mail.MailTo));
                if (!string.IsNullOrWhiteSpace(mail.ReplyTo))
                {
                    message.ReplyTo.Add(MailboxAddress.Parse(mail.ReplyTo));
                }

                using var smtpClient = new SmtpClient();

                // Port 465 = TLS implicite ; sinon STARTTLS explicite (587, le défaut).
                // On évite SecureSocketOptions.Auto, qui peut retomber en clair.
                await smtpClient.ConnectAsync(
                    SmtpHost,
                    SmtpPort,
                    SmtpPort == 465
                        ? SecureSocketOptions.SslOnConnect
                        : SecureSocketOptions.StartTls,
                    ct
                );
                await smtpClient.AuthenticateAsync(SmtpLogin,SmtpKey, ct);
                await smtpClient.SendAsync(message, ct);
                await smtpClient.DisconnectAsync(true, ct);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send '{Subject}' to {To}.", mail.MailSubject, mail.MailTo);
                return false;
            }
        }

        /// <summary>
        /// Compiles and renders the Razor template (compiled result cached by RazorLight after the
        /// first render) against <paramref name="model"/>. Returns <c>null</c> if rendering failed.
        /// </summary>
        private async Task<string?> RenderAsync<TModel>(string templateName, TModel model)
        {
            try
            {
                return await RazorEngine.CompileRenderAsync(templateName, model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to render mail template {Template}.", templateName);
                return null;
            }
        }
    }

}
