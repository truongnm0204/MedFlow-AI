using Microsoft.EntityFrameworkCore;

namespace MedFlow.Infrastructure.Persistence;

public class MedFlowDbContext : DbContext
{
    public MedFlowDbContext(DbContextOptions<MedFlowDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedFlowDbContext).Assembly);
    }
}
