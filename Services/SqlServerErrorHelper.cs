using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LaGrandeurSpa.Web.Services;

public static class SqlServerErrorHelper
{
    public static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number is 2601 or 2627)
                return true;
        }

        return false;
    }
}
