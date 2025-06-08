using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using UniMarket.Models;

namespace UniMarket.Services
{
    public class GmailEmailSender : IEmailSender
    {
        private readonly EmailSettings _settings;

        public GmailEmailSender(IOptions<EmailSettings> options)
        {
            _settings = options.Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            Console.WriteLine("📨 Đã vào GmailEmailSender.SendEmailAsync()");
            Console.WriteLine($"📨 Đang gửi tới: {toEmail}");

            var mail = new MailMessage
            {
                From = new MailAddress(_settings.From, _settings.DisplayName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            using var smtp = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort)
            {
                Credentials = new NetworkCredential(_settings.SmtpUser, _settings.SmtpPass),
                EnableSsl = true
            };

            try
            {
                await smtp.SendMailAsync(mail);
                Console.WriteLine("✅ Đã gửi email thành công!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi gửi mail: " + ex.Message);
                throw;
            }
        }
    }
}