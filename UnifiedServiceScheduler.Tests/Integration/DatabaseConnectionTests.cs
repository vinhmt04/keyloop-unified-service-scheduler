using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Testcontainers.PostgreSql;
using UnifiedServiceScheduler.Data;

namespace UnifiedServiceScheduler.Tests.Integration;

public class DatabaseConnectionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgreSqlContainer = new PostgreSqlBuilder()
        .WithDatabase("unified_service_scheduler_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    [Fact]
    public async Task DatabaseConnection_ShouldConnect_WhenContainerIsRunning()
    {
        // Arrange
        var connectionString = _postgreSqlContainer.GetConnectionString();
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddDbContext<ServiceSchedulerDbContext>(options =>
            options.UseNpgsql(connectionString));
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Act & Assert
        var canConnect = await context.Database.CanConnectAsync();
        
        Assert.True(canConnect, "Should be able to connect to PostgreSQL test container");
    }

    [Fact]
    public async Task DatabaseContext_ShouldCreateDatabase_WhenEnsureCreatedCalled()
    {
        // Arrange
        var connectionString = _postgreSqlContainer.GetConnectionString();
        
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        serviceCollection.AddDbContext<ServiceSchedulerDbContext>(options =>
            options.UseNpgsql(connectionString));
        
        var serviceProvider = serviceCollection.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ServiceSchedulerDbContext>();

        // Act
        var created = await context.Database.EnsureCreatedAsync();
        var canConnect = await context.Database.CanConnectAsync();

        // Assert
        Assert.True(created, "Database should be created successfully");
        Assert.True(canConnect, "Should be able to connect after database creation");
    }

    public async Task InitializeAsync()
    {
        await _postgreSqlContainer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgreSqlContainer.DisposeAsync();
    }
}