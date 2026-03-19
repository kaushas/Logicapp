using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using LogicAppProcessor.Repositories;

namespace LogicAppProcessor.Tests
{
    public class InboxRepositoryTests
    {
        private ProcessingDbContext CreateInMemoryDb()
        {
            var options = new DbContextOptionsBuilder<ProcessingDbContext>()
                .UseInMemoryDatabase("TestDb").Options;

            return new ProcessingDbContext(options);
        }

        private ILogger<EFInboxRepository> CreateMockLogger()
        {
            return new Mock<ILogger<EFInboxRepository>>().Object;
        }

        [Fact]
        public async Task ExistsAsync_ReturnsFalse_WhenNotPresent()
        {
            var db = CreateInMemoryDb();
            var logger = CreateMockLogger();
            var repo = new EFInboxRepository(db, logger);

            var exists = await repo.ExistsAsync("nonexistent");

            Assert.False(exists);
        }

        [Fact]
        public async Task SaveAsync_PersistsRecord()
        {
            var db = CreateInMemoryDb();
            var logger = CreateMockLogger();
            var repo = new EFInboxRepository(db, logger);

            await repo.SaveAsync(new InboxRecord { MessageId = "m1", RawPayload = "{}" });

            var exists = await repo.ExistsAsync("m1");
            Assert.True(exists);
        }
    }
}
