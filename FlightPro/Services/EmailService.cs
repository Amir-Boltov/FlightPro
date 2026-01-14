using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;

public class EmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string messageBody)
    {
        // 1. Read settings from appsettings.json
        var settings = _configuration.GetSection("EmailSettings");
        string mailServer = settings["MailServer"];
        int mailPort = int.Parse(settings["MailPort"]);
        string senderEmail = settings["SenderEmail"];
        string senderPassword = settings["SenderPassword"];
        string senderName = settings["SenderName"];

        // 2. Configure the SMTP Client
        var client = new SmtpClient(mailServer, mailPort)
        {
            Credentials = new NetworkCredential(senderEmail, senderPassword),
            EnableSsl = true
        };

        // 3. Create the Email Message
        var mailMessage = new MailMessage
        {
            From = new MailAddress(senderEmail, senderName),
            Subject = subject,
            Body = messageBody,
            IsBodyHtml = true // Allows you to use HTML tags in your email
        };

        mailMessage.To.Add(toEmail);

        // 4. Send
        await client.SendMailAsync(mailMessage);
    }
}