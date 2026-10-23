using Microsoft.EntityFrameworkCore;

namespace Labaik.FakeGateway;

public sealed class GatewayDbContext(DbContextOptions<GatewayDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.HasDefaultSchema("fake_gateway");
}
