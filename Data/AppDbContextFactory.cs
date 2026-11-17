
using Kartoteka.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartoteka.Server.Data;

public sealed class AppDbContextFactory
    : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Используется только для создания миграций.
        // Подключение к этой базе здесь не выполняется.
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=kartoteka_design_time;Username=design_time;Password=design_time");

        return new AppDbContext(optionsBuilder.Options);
    }
}
