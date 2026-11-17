using Microsoft.EntityFrameworkCore;

namespace Xumora.Infrastructure.Shared.Persistence;

public class XumoraDbContext : DbContext
{
    public XumoraDbContext(DbContextOptions<XumoraDbContext> options)
        : base(options)
    {
    }
}