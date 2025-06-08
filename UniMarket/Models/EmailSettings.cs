namespace UniMarket.Models
{
    public class EmailSettings
    {
        public string DisplayName { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string SmtpServer { get; set; } = string.Empty;
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; } = string.Empty;
        public string SmtpPass { get; set; } = string.Empty;
    }
}
