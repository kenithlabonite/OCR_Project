using MimeKit;
using MailKit.Net.Smtp;

namespace Project_OLMS.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlMessage);
    }

    public class EmailService : IEmailService
    {
        public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
        {
            var emailMessage = new MimeMessage();

            // 1. Configure Sender (Change this to your email address)
            emailMessage.From.Add(new MailboxAddress("Project OLMS Admin", "kenithlabonite21@gmail.com"));

            // 2. Configure Recipient
            emailMessage.To.Add(new MailboxAddress("", toEmail));

            // 3. Configure Subject and Body Content
            emailMessage.Subject = subject;
            var bodyBuilder = new BodyBuilder { HtmlBody = htmlMessage };
            emailMessage.Body = bodyBuilder.ToMessageBody();

            // 4. Establish SMTP Connection and Dispatch
            using var client = new SmtpClient();
            try
            {
                // Example using Gmail SMTP configurations.
                await client.ConnectAsync("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls);

                // Remember to use a generated Google "App Password" here, not your account login password.
                await client.AuthenticateAsync("kenithlabonite21@gmail.com", "wvvndyjxtoaqxrog");

                await client.SendAsync(emailMessage);
            }
            finally
            {
                await client.DisconnectAsync(true);
                client.Dispose();
            }
        }
    }
}