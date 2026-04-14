namespace RedisSmartDemo.Api.Models;

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public UserPreferences Preferences { get; set; } = new();
}

public class UserPreferences
{
    public string Theme { get; set; } = "light";
    public string Language { get; set; } = "en";
}