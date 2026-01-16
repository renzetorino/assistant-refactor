using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.Api.Services;
using dataAccess.Entities;
using dataAccess.Planning.Insights;
using dataAccess.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace dataAccess.Api.Tests
{
    /// <summary>
    /// Tests for InsightRefreshWorker background service.
    /// </summary>
    public class InsightRefreshWorkerTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _appContext;
        private readonly AiDbContext _aiContext;
        private readonly Mock<ILogger<InsightRefreshWorker>> _logger;
        private const int TEST_BUSINESS_ID_1 = 1;
        private const int TEST_BUSINESS_ID_2 = 2;

        public InsightRefreshWorkerTests()
        {
            // Setup in-memory databases
            var appOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var aiOptions = new DbContextOptionsBuilder<AiDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _appContext = new AppDbContext(appOptions);
            _aiContext = new AiDbContext(aiOptions);

            _logger = new Mock<ILogger<InsightRefreshWorker>>();

            // Setup DI container
            var services = new ServiceCollection();
            services.AddScoped(_ => _appContext);
            services.AddScoped(_ => _aiContext);
            services.AddScoped<InsightScanner>();
            services.AddScoped<InsightCoordinator>();
            services.AddScoped<BusinessMentorFormatter>();
            services.AddSingleton<ILogger<InsightScanner>>(Mock.Of<ILogger<InsightScanner>>());
            services.AddSingleton<ILogger<InsightCoordinator>>(Mock.Of<ILogger<InsightCoordinator>>());
            services.AddSingleton<ILogger<BusinessMentorFormatter>>(Mock.Of<ILogger<BusinessMentorFormatter>>());
            
            _serviceProvider = services.BuildServiceProvider();

            SeedTestData();
        }

        public void Dispose()
        {
            _appContext.Database.EnsureDeleted();
            _aiContext.Database.EnsureDeleted();
            _appContext.Dispose();
            _aiContext.Dispose();
            _serviceProvider.Dispose();
        }

        [Fact]
        public async Task Worker_StartsAndStops_Successfully()
        {
            // Arrange
            var options = Options.Create(new InsightRefreshOptions
            {
                Enabled = false, // Disabled to avoid actual execution
                RefreshIntervalMinutes = 1,
                InitialDelayMinutes = 0
            });

            var worker = new InsightRefreshWorker(_logger.Object, _serviceProvider, options);
            var cts = new CancellationTokenSource();

            // Act
            var task = worker.StartAsync(cts.Token);
            await Task.Delay(100); // Give it time to start
            await worker.StopAsync(cts.Token);

            // Assert
            Assert.True(task.IsCompleted);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("starting")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Worker_RespectsEnabledFlag()
        {
            // Arrange
            var options = Options.Create(new InsightRefreshOptions
            {
                Enabled = false,
                RefreshIntervalMinutes = 1
            });

            var worker = new InsightRefreshWorker(_logger.Object, _serviceProvider, options);
            var cts = new CancellationTokenSource();

            // Act
            await worker.StartAsync(cts.Token);
            await Task.Delay(200);
            await worker.StopAsync(cts.Token);

            // Assert
            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("disabled")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task Worker_ProcessesMultipleBusinesses()
        {
            // Arrange
            var options = Options.Create(new InsightRefreshOptions
            {
                Enabled = true,
                RefreshIntervalMinutes = 60, // Long interval to only run once
                InitialDelayMinutes = 0,
                BatchSize = 5,
                DelayBetweenBusinessesMs = 0,
                DelayBetweenBatchesMs = 0
            });

            var worker = new InsightRefreshWorker(_logger.Object, _serviceProvider, options);
            var cts = new CancellationTokenSource();

            // Act
            var workerTask = worker.StartAsync(cts.Token);
            await Task.Delay(2000); // Wait for one cycle
            cts.Cancel();
            
            try
            {
                await worker.StopAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }

            // Assert - Check that insights were created
            var insights = await _aiContext.AiInsights
                .Where(i => i.SourceEvent == "background_refresh")
                .ToListAsync();

            Assert.NotEmpty(insights);
            _logger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Found") && v.ToString()!.Contains("businesses")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);
        }

        [Fact]
        public void Options_HasCorrectDefaults()
        {
            // Arrange & Act
            var options = new InsightRefreshOptions();

            // Assert
            Assert.True(options.Enabled);
            Assert.Equal(30, options.RefreshIntervalMinutes);
            Assert.Equal(2, options.InitialDelayMinutes);
            Assert.Equal(10, options.BatchSize);
            Assert.Equal(500, options.DelayBetweenBusinessesMs);
            Assert.Equal(2000, options.DelayBetweenBatchesMs);
        }

        [Fact]
        public async Task Worker_HandlesErrors_Gracefully()
        {
            // Arrange - Create a scenario that will cause errors
            var corruptContext = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);

            var corruptServices = new ServiceCollection();
            corruptServices.AddScoped(_ => corruptContext);
            corruptServices.AddScoped(_ => _aiContext);
            corruptServices.AddScoped<InsightScanner>();
            corruptServices.AddScoped<InsightCoordinator>();
            corruptServices.AddScoped<BusinessMentorFormatter>();
            corruptServices.AddSingleton<ILogger<InsightScanner>>(Mock.Of<ILogger<InsightScanner>>());
            corruptServices.AddSingleton<ILogger<InsightCoordinator>>(Mock.Of<ILogger<InsightCoordinator>>());
            corruptServices.AddSingleton<ILogger<BusinessMentorFormatter>>(Mock.Of<ILogger<BusinessMentorFormatter>>());

            var corruptProvider = corruptServices.BuildServiceProvider();

            var options = Options.Create(new InsightRefreshOptions
            {
                Enabled = true,
                RefreshIntervalMinutes = 60,
                InitialDelayMinutes = 0,
                BatchSize = 5
            });

            var worker = new InsightRefreshWorker(_logger.Object, corruptProvider, options);
            var cts = new CancellationTokenSource();

            // Act
            await worker.StartAsync(cts.Token);
            await Task.Delay(1500);
            cts.Cancel();

            try
            {
                await worker.StopAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

            // Assert - Worker should log errors but not crash
            _logger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.AtLeastOnce);

            corruptProvider.Dispose();
            corruptContext.Dispose();
        }

        private void SeedTestData()
        {
            // Add suppliers for both businesses to make them discoverable
            _appContext.Suppliers.AddRange(
                new Supplier
                {
                    SupplierId = 1,
                    BusinessId = TEST_BUSINESS_ID_1,
                    SupplierName = "Test Supplier 1",
                    ContactPerson = "Person 1",
                    PhoneNumber = "123-456-7890",
                    Address = "Address 1"
                },
                new Supplier
                {
                    SupplierId = 2,
                    BusinessId = TEST_BUSINESS_ID_2,
                    SupplierName = "Test Supplier 2",
                    ContactPerson = "Person 2",
                    PhoneNumber = "098-765-4321",
                    Address = "Address 2"
                }
            );

            // Add products
            _appContext.Products.AddRange(
                new Product
                {
                    ProductId = 1,
                    BusinessId = TEST_BUSINESS_ID_1,
                    SupplierId = 1,
                    ProductName = "Laptop",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new Product
                {
                    ProductId = 2,
                    BusinessId = TEST_BUSINESS_ID_2,
                    SupplierId = 2,
                    ProductName = "Pizza",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            );

            // Add product categories with stock info
            _appContext.ProductCategories.AddRange(
                new ProductCategory
                {
                    ProductCategoryId = 1,
                    ProductId = 1,
                    Price = 1000,
                    Cost = 800,
                    CurrentStock = 2,
                    ReorderPoint = 5,
                    UpdatedStock = DateTime.UtcNow
                },
                new ProductCategory
                {
                    ProductCategoryId = 2,
                    ProductId = 2,
                    Price = 15,
                    Cost = 10,
                    CurrentStock = 1,
                    ReorderPoint = 10,
                    UpdatedStock = DateTime.UtcNow
                }
            );

            _appContext.SaveChanges();
        }
    }
}
