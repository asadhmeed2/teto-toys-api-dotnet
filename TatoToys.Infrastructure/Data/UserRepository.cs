using MySql.Data.MySqlClient;
using TatoToys.Domain.Entities;
using TatoToys.Domain.Interfaces;

namespace TatoToys.Infrastructure.Data;

public class UserRepository : IUserRepository
{
    private readonly string _connectionString;

    public UserRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        const string sql = "SELECT user_id, email, password_hash, is_active FROM TetoToys.users WHERE email = @email";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.Add("@email", MySqlDbType.VarChar).Value = email;

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new User
        {
            UserId = reader.GetGuid(reader.GetOrdinal("user_id")).ToString(),
            Email = reader.GetString(reader.GetOrdinal("email")),
            PasswordHash = reader.GetString(reader.GetOrdinal("password_hash")),
            IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
        };
    }

    public async Task CreateUserAsync(
        string userId, string email, string passwordHash,
        string firstName, string lastName, bool isAdult,
        DateTime termsAcceptedAt, string termsVersion,
        bool marketingOptIn, DateTime createdAt)
    {
        const string sql = @"
            INSERT INTO users (user_id, email, password_hash, first_name, last_name,
                               is_adult, terms_accepted_at, terms_version, marketing_opt_in, created_at)
            VALUES (@userId, @email, @passwordHash, @firstName, @lastName,
                    @isAdult, @termsAcceptedAt, @termsVersion, @marketingOptIn, @createdAt)";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@email", email);
        cmd.Parameters.AddWithValue("@passwordHash", passwordHash);
        cmd.Parameters.AddWithValue("@firstName", firstName);
        cmd.Parameters.AddWithValue("@lastName", lastName);
        cmd.Parameters.AddWithValue("@isAdult", isAdult);
        cmd.Parameters.AddWithValue("@termsAcceptedAt", termsAcceptedAt);
        cmd.Parameters.AddWithValue("@termsVersion", termsVersion);
        cmd.Parameters.AddWithValue("@marketingOptIn", marketingOptIn);
        cmd.Parameters.AddWithValue("@createdAt", createdAt);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateLastLoginAsync(string userId)
    {
        const string sql = "UPDATE users SET last_login = @now WHERE user_id = @userId";

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
