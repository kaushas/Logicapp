using Microsoft.EntityFrameworkCore;
using LogicAppProcessor.Repositories.Entities;

namespace LogicAppProcessor.Repositories
{
    public class ProcessingDbContext : DbContext
    {
        public ProcessingDbContext(DbContextOptions<ProcessingDbContext> options) : base(options) { }

        public DbSet<InboxEntity> Inbox { get; set; }
        public DbSet<OutboxEntity> Outbox { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<InboxEntity>().HasIndex(i => i.MessageId).IsUnique();
            modelBuilder.Entity<OutboxEntity>().HasIndex(o => o.MessageId);
        }
    }
}
