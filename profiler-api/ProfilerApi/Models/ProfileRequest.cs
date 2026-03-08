namespace ProfilerApi.Models;

public class ProfileRequest
{
    public string Address { get; set; } = string.Empty;
    public string Chain { get; set; } = "ethereum";
    public string Tier { get; set; } = "standard"; // "basic", "standard", "premium"
}
