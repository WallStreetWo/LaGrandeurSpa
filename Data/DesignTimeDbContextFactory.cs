using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LaGrandeurSpa.Web.Data;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("LAGRANDEUR_DESIGNTIME_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException(
                "Set LAGRANDEUR_DESIGNTIME_CONNECTION before running EF design-time commands. " +
                "Database passwords must not be committed to source control.");
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connection)
            .Options;

        return new ApplicationDbContext(options);
    }
}
