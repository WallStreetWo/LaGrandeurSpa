namespace LaGrandeurSpa.Web.Services;

public sealed class SmtpSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseStartTls { get; set; } = true;

    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public string FromName { get; set; } = "La Grandeur Spa";
    public string FromEmail { get; set; } = "";
}
