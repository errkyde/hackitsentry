using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace HITSight.Server.Services;

public class PlatformJwtService
{
    private readonly string _key;
    private readonly bool _platformEnabled;
    private const string Issuer = "HITSightPlatform";
    private const string Audience = "HITSightPlatform";

    public PlatformJwtService(IConfiguration config)
    {
        _key = config["Platform:JwtKey"] ?? "";
        _platformEnabled = !string.IsNullOrEmpty(config["Platform:ConnectionString"]);
    }

    public bool IsConfigured => _platformEnabled && !string.IsNullOrEmpty(_key);

    public string GenerateTempToken(string username)
        => Generate(username, "temp", TimeSpan.FromMinutes(10));

    public string GenerateFullToken(string username)
        => Generate(username, "full", TimeSpan.FromHours(24));

    private string Generate(string username, string phase, TimeSpan expiry)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim("phase", phase),
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.Add(expiry),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
