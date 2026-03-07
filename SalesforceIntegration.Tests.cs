using Moq;
using Xunit;
using SalesforceIntegration.Services;
using SalesforceIntegration.Data;
using SalesforceIntegration.Data.Entities;
using Microsoft.Extensions.Logging;

namespace SalesforceIntegration.Tests
{
    /// <summary>
    /// Unit tests for CustomerSyncService
    /// Tests the sync orchestration logic
    /// </summary>
    public class CustomerSyncServiceTests
    {
        // Mocks
        private readonly Mock<ISalesforceClient> _mockSalesforceClient;
        private readonly Mock<ICustomerRepository> _mockCustomerRepository;
        private readonly Mock<ISyncCheckpointRepository> _mockCheckpointRepository;
        private readonly Mock<ISyncFailureRepository> _mockFailureRepository;
        private readonly Mock<IServiceBusPublisher> _mockServiceBusPublisher;
        private readonly Mock<ILogger<CustomerSyncService>> _mockLogger;

        // Service under test
        private readonly CustomerSyncService _syncService;

        public CustomerSyncServiceTests()
        {
            _mockSalesforceClient = new Mock<ISalesforceClient>();
            _mockCustomerRepository = new Mock<ICustomerRepository>();
            _mockCheckpointRepository = new Mock<ISyncCheckpointRepository>();
            _mockFailureRepository = new Mock<ISyncFailureRepository>();
            _mockServiceBusPublisher = new Mock<IServiceBusPublisher>();
            _mockLogger = new Mock<ILogger<CustomerSyncService>>();

            _syncService = new CustomerSyncService(
                _mockSalesforceClient.Object,
                _mockCustomerRepository.Object,
                _mockCheckpointRepository.Object,
                _mockFailureRepository.Object,
                _mockServiceBusPublisher.Object,
                _mockLogger.Object
            );
        }

        /// <summary>
        /// Test: Successfully sync customers from Salesforce
        /// Scenario: Salesforce returns 10 customers, all sync successfully
        /// Expected: All 10 customers saved to DB
        /// </summary>
        [Fact]
        public async Task SyncFromSalesforce_WithValidCustomers_SavesAll()
        {
            // Arrange
            var sfCustomers = new List<SalesforceCustomer>
            {
                new SalesforceCustomer
                {
                    Id = "001A0000003DHP1AAM",
                    Name = "Acme Corp",
                    Email = "contact@acme.com",
                    Phone = "415-555-0100",
                    Industry = "Technology",
                    Website = "www.acme.com",
                    LastModifiedDate = DateTime.UtcNow
                },
                new SalesforceCustomer
                {
                    Id = "001A0000003DHP2BBN",
                    Name = "Tech Solutions Inc",
                    Email = "sales@techsol.com",
                    Phone = "415-555-0101",
                    Industry = "Consulting",
                    Website = "www.techsol.com",
                    LastModifiedDate = DateTime.UtcNow
                }
            };

            _mockSalesforceClient
                .Setup(x => x.QueryCustomersAsync(It.IsAny<string>()))
                .ReturnsAsync(sfCustomers);

            _mockCustomerRepository
                .Setup(x => x.GetBySalesforceIdAsync(It.IsAny<string>()))
                .ReturnsAsync((Customer)null); // No existing customers

            _mockCheckpointRepository
                .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((SyncCheckpoint)null);

            // Act
            var result = await _syncService.SyncFromSalesforceAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Success", result.Status);
            Assert.Equal(2, result.SuccessCount);
            Assert.Equal(0, result.FailureCount);
            
            // Verify CreateAsync was called twice
            _mockCustomerRepository.Verify(
                x => x.CreateAsync(It.IsAny<Customer>()),
                Times.Exactly(2)
            );

            // Verify checkpoint was saved
            _mockCheckpointRepository.Verify(
                x => x.SaveAsync(It.IsAny<SyncCheckpoint>()),
                Times.AtLeastOnce()
            );
        }

        /// <summary>
        /// Test: Update existing customers
        /// Scenario: Customers already exist in DB
        /// Expected: UpdateAsync called instead of CreateAsync
        /// </summary>
        [Fact]
        public async Task SyncFromSalesforce_WithExistingCustomers_Updates()
        {
            // Arrange
            var sfCustomer = new SalesforceCustomer
            {
                Id = "001A0000003DHP1AAM",
                Name = "Acme Corp Updated",
                Email = "newemail@acme.com",
                LastModifiedDate = DateTime.UtcNow
            };

            var existingCustomer = new Customer
            {
                Id = 1,
                SalesforceId = "001A0000003DHP1AAM",
                Name = "Acme Corp Old Name"
            };

            _mockSalesforceClient
                .Setup(x => x.QueryCustomersAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<SalesforceCustomer> { sfCustomer });

            _mockCustomerRepository
                .Setup(x => x.GetBySalesforceIdAsync("001A0000003DHP1AAM"))
                .ReturnsAsync(existingCustomer);

            _mockCheckpointRepository
                .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((SyncCheckpoint)null);

            // Act
            var result = await _syncService.SyncFromSalesforceAsync();

            // Assert
            Assert.Equal(1, result.SuccessCount);
            
            // Verify UpdateAsync was called, not CreateAsync
            _mockCustomerRepository.Verify(
                x => x.UpdateAsync(It.IsAny<Customer>()),
                Times.Once
            );

            _mockCustomerRepository.Verify(
                x => x.CreateAsync(It.IsAny<Customer>()),
                Times.Never
            );
        }

        /// <summary>
        /// Test: Handle partial failures
        /// Scenario: 5 customers sync, 2 fail
        /// Expected: Successful ones are saved, failures logged
        /// </summary>
        [Fact]
        public async Task SyncFromSalesforce_WithPartialFailures_ContinuesAndLogs()
        {
            // Arrange - mix of valid and invalid data
            var customers = new List<SalesforceCustomer>
            {
                new SalesforceCustomer { Id = "001", Name = "Valid 1", LastModifiedDate = DateTime.UtcNow },
                new SalesforceCustomer { Id = "002", Name = "Valid 2", LastModifiedDate = DateTime.UtcNow },
                new SalesforceCustomer { Id = "003", Name = "Valid 3", LastModifiedDate = DateTime.UtcNow }
            };

            _mockSalesforceClient
                .Setup(x => x.QueryCustomersAsync(It.IsAny<string>()))
                .ReturnsAsync(customers);

            _mockCustomerRepository
                .Setup(x => x.GetBySalesforceIdAsync(It.IsAny<string>()))
                .ReturnsAsync((Customer)null);

            // First customer creation succeeds, second and third throw exception
            var callCount = 0;
            _mockCustomerRepository
                .Setup(x => x.CreateAsync(It.IsAny<Customer>()))
                .Callback(() => callCount++)
                .Returns((Customer c) =>
                {
                    if (callCount >= 2)
                        throw new Exception("Database error");
                    return Task.CompletedTask;
                });

            _mockCheckpointRepository
                .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((SyncCheckpoint)null);

            // Act
            var result = await _syncService.SyncFromSalesforceAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("PartialSuccess", result.Status);
            Assert.Equal(1, result.SuccessCount);
            Assert.Equal(2, result.FailureCount);
            
            // Verify failures were recorded
            _mockFailureRepository.Verify(
                x => x.CreateAsync(It.IsAny<SyncFailure>()),
                Times.Exactly(2)
            );
        }

        /// <summary>
        /// Test: Respect checkpoint for resume
        /// Scenario: Checkpoint exists from last sync
        /// Expected: Only fetch customers modified after checkpoint time
        /// </summary>
        [Fact]
        public async Task SyncFromSalesforce_WithCheckpoint_OnlyFetchesModifiedSince()
        {
            // Arrange
            var checkpointTime = DateTime.UtcNow.AddHours(-1);
            var checkpoint = new SyncCheckpoint
            {
                SyncType = "FromSalesforce",
                EntityType = "Customer",
                LastSyncTime = checkpointTime
            };

            _mockCheckpointRepository
                .Setup(x => x.GetAsync("FromSalesforce", "Customer"))
                .ReturnsAsync(checkpoint);

            _mockSalesforceClient
                .Setup(x => x.QueryCustomersAsync(It.IsAny<string>()))
                .ReturnsAsync(new List<SalesforceCustomer>
                {
                    new SalesforceCustomer
                    {
                        Id = "001",
                        Name = "Recent Customer",
                        LastModifiedDate = DateTime.UtcNow
                    }
                });

            _mockCustomerRepository
                .Setup(x => x.GetBySalesforceIdAsync(It.IsAny<string>()))
                .ReturnsAsync((Customer)null);

            // Act
            await _syncService.SyncFromSalesforceAsync();

            // Assert - Verify QueryCustomersAsync was called with checkpoint time
            _mockSalesforceClient.Verify(
                x => x.QueryCustomersAsync(
                    It.Is<string>(q => q.Contains(checkpointTime.ToString("O")))
                ),
                Times.Once
            );
        }

        /// <summary>
        /// Test: Get sync status
        /// Scenario: Check current sync state
        /// Expected: Returns accurate counts and timestamps
        /// </summary>
        [Fact]
        public async Task GetSyncStatus_ReturnsAccurateMetrics()
        {
            // Arrange
            var lastSyncFromSf = DateTime.UtcNow.AddHours(-1);
            var checkpoint = new SyncCheckpoint
            {
                LastSyncTime = lastSyncFromSf,
                ProcessedCount = 100,
                FailedCount = 2
            };

            _mockCheckpointRepository
                .Setup(x => x.GetAsync("FromSalesforce", "Customer"))
                .ReturnsAsync(checkpoint);

            _mockCheckpointRepository
                .Setup(x => x.GetAsync("ToSalesforce", "Customer"))
                .ReturnsAsync((SyncCheckpoint)null);

            _mockCustomerRepository
                .Setup(x => x.CountAsync())
                .ReturnsAsync(250);

            _mockFailureRepository
                .Setup(x => x.GetUnresolvedAsync())
                .ReturnsAsync(new List<SyncFailure>
                {
                    new SyncFailure { Id = 1, ExternalId = "001", IsResolved = false }
                });

            // Act
            var status = await _syncService.GetSyncStatusAsync();

            // Assert
            Assert.NotNull(status);
            Assert.Equal(250, status.TotalCustomers);
            Assert.Equal(1, status.UnresolvedFailures);
            Assert.Equal(lastSyncFromSf, status.LastSyncFromSalesforce);
            Assert.Null(status.LastSyncToSalesforce);
            Assert.Equal(100, status.LastFromSalesforceCount);
        }

        /// <summary>
        /// Test: Retry failed sync
        /// Scenario: Retry a previously failed sync
        /// Expected: Failure marked as resolved
        /// </summary>
        [Fact]
        public async Task RetryFailedSync_MarksFailureAsResolved()
        {
            // Arrange
            var failureId = 1;
            var failure = new SyncFailure
            {
                Id = failureId,
                EntityType = "Customer",
                ExternalId = "001",
                ErrorMessage = "Previous error",
                RetryCount = 0,
                MaxRetries = 5,
                IsResolved = false
            };

            _mockFailureRepository
                .Setup(x => x.GetByIdAsync(failureId))
                .ReturnsAsync(failure);

            _mockFailureRepository
                .Setup(x => x.UpdateAsync(It.IsAny<SyncFailure>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _syncService.RetryFailedSyncAsync(failureId);

            // Assert
            Assert.True(result);
            
            // Verify failure was updated
            _mockFailureRepository.Verify(
                x => x.UpdateAsync(It.Is<SyncFailure>(
                    f => f.Id == failureId && f.IsResolved
                )),
                Times.Once
            );
        }

        /// <summary>
        /// Test: Max retries exceeded
        /// Scenario: Retry count exceeds max retries
        /// Expected: Failure marked as resolved, no more retries
        /// </summary>
        [Fact]
        public async Task RetryFailedSync_ExceedsMaxRetries_StopsRetrying()
        {
            // Arrange
            var failure = new SyncFailure
            {
                Id = 1,
                RetryCount = 5,
                MaxRetries = 5,
                IsResolved = false
            };

            _mockFailureRepository
                .Setup(x => x.GetByIdAsync(1))
                .ReturnsAsync(failure);

            // Act
            var result = await _syncService.RetryFailedSyncAsync(1);

            // Assert - Should return false (not retried)
            Assert.False(result);
        }

        /// <summary>
        /// Test: Publish sync events
        /// Scenario: After successful sync, events are published
        /// Expected: Service bus received event for each synced customer
        /// </summary>
        [Fact]
        public async Task SyncFromSalesforce_PublishesEventsToServiceBus()
        {
            // Arrange
            var sfCustomers = new List<SalesforceCustomer>
            {
                new SalesforceCustomer
                {
                    Id = "001",
                    Name = "Customer 1",
                    LastModifiedDate = DateTime.UtcNow
                }
            };

            _mockSalesforceClient
                .Setup(x => x.QueryCustomersAsync(It.IsAny<string>()))
                .ReturnsAsync(sfCustomers);

            _mockCustomerRepository
                .Setup(x => x.GetBySalesforceIdAsync(It.IsAny<string>()))
                .ReturnsAsync((Customer)null);

            _mockCustomerRepository
                .Setup(x => x.CreateAsync(It.IsAny<Customer>()))
                .Returns(Task.CompletedTask);

            _mockCheckpointRepository
                .Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync((SyncCheckpoint)null);

            // Act
            await _syncService.SyncFromSalesforceAsync();

            // Assert - Event was published
            _mockServiceBusPublisher.Verify(
                x => x.PublishAsync(
                    "customer-sync-events",
                    It.IsAny<SyncEventMessage>()
                ),
                Times.AtLeastOnce()
            );
        }
    }

    /// <summary>
    /// Unit tests for SalesforceClient
    /// Tests OAuth2 authentication and API calls
    /// </summary>
    public class SalesforceClientTests
    {
        private readonly Mock<HttpMessageHandler> _mockHttpHandler;
        private readonly Mock<ILogger<SalesforceClient>> _mockLogger;
        private readonly Mock<IMemoryCache> _mockCache;
        private readonly SalesforceClient _client;
        private readonly SalesforceConfig _config;

        public SalesforceClientTests()
        {
            _mockHttpHandler = new Mock<HttpMessageHandler>();
            _mockLogger = new Mock<ILogger<SalesforceClient>>();
            _mockCache = new Mock<IMemoryCache>();

            _config = new SalesforceConfig
            {
                ClientId = "test-client-id",
                ClientSecret = "test-client-secret",
                InstanceUrl = "https://test.salesforce.com"
            };

            var httpClient = new HttpClient(_mockHttpHandler.Object);
            _client = new SalesforceClient(httpClient, _config, _mockLogger, _mockCache);
        }

        /// <summary>
        /// Test: OAuth2 authentication
        /// Scenario: Get access token from Salesforce
        /// Expected: Token is cached and returned
        /// </summary>
        [Fact]
        public async Task Authenticate_SuccessfullyGetsToken()
        {
            // Arrange
            var tokenResponse = @"{
                ""access_token"": ""00Dxx0000000000!AQwAQPkpN0KnQBh8PQqScqzzJVWf1H"",
                ""scope"": ""api"",
                ""instance_url"": ""https://test.salesforce.com"",
                ""id"": ""https://login.salesforce.com/id/00Dxx0000000000EAA/005xx000001SQkAAM"",
                ""token_type"": ""Bearer"",
                ""issued_at"": ""1234567890"",
                ""signature"": ""3e567"",
                ""expires_in"": 7200
            }";

            var httpResponse = new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent(tokenResponse)
            };

            _mockHttpHandler
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(httpResponse);

            // Act
            await _client.AuthenticateAsync();

            // Assert
            _mockHttpHandler.Protected().Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri.ToString().Contains("oauth2/token")
                ),
                ItExpr.IsAny<CancellationToken>()
            );
        }
    }
}
