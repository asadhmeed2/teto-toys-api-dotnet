using TatoToys.Domain.Entities;

namespace TatoToys.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);
    Task CreateUserAsync(
        string userId, string email, string passwordHash,
        string firstName, string lastName, bool isAdult,
        DateTime termsAcceptedAt, string termsVersion,
        bool marketingOptIn, DateTime createdAt);
    Task UpdateLastLoginAsync(string userId);
}
