using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientUniqueUserIndexTests : AuthTestBase
{
    public PatientUniqueUserIndexTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task DuplicatePatientUserId_IsRejectedByDatabase()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new User
        {
            Email = $"dupe-{suffix}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        var firstCode = $"DUP-A-{suffix}";
        var secondCode = $"DUP-B-{suffix}";

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            Db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO patients (user_id, patient_code, full_name, status)
                VALUES ({user.Id}, {firstCode}, 'Hồ sơ A', 'ACTIVE'),
                       ({user.Id}, {secondCode}, 'Hồ sơ B', 'ACTIVE');
                """));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);

        await Db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM patients WHERE patient_code IN ({firstCode}, {secondCode});");
        var createdUser = await Db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
        if (createdUser is not null)
            Db.Users.Remove(createdUser);
        await Db.SaveChangesAsync();
    }
}
