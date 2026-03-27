using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogicAppProcessor.Repositories
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ProcessingDbContext>
    {
        public ProcessingDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ProcessingDbContext>();
            optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=ProcessingDb;Trusted_Connection=True;");

            return new ProcessingDbContext(optionsBuilder.Options);
        }
    }
}
