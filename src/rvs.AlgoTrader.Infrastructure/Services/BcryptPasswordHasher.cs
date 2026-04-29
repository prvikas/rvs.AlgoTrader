using rvs.AlgoTrader.Application.Services;

namespace rvs.AlgoTrader.Infrastructure.Services;

/// <summary>
/// BCrypt-backed implementation of IPasswordHasher.
/// Work factor 12 — ≈250 ms on modern hardware, safe for login throughput.
/// Registered as singleton: BCrypt is stateless.
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
