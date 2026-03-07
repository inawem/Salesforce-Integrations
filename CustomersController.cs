using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SalesforceIntegration.Services;

namespace SalesforceIntegration.Api.Controllers
{
    /// <summary>
    /// REST API controller for customer management and synchronization
    /// Requires Auth0 authentication on all endpoints
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CustomersController : ControllerBase
    {
        private readonly ICustomerSyncService _syncService;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<CustomersController> _logger;

        public CustomersController(
            ICustomerSyncService syncService,
            ICustomerRepository customerRepository,
            ILogger<CustomersController> logger)
        {
            _syncService = syncService;
            _customerRepository = customerRepository;
            _logger = logger;
        }

        /// <summary>
        /// Get all customers with pagination
        /// GET /api/customers?page=1&pageSize=100
        /// </summary>
        [HttpGet]
        [Authorize("read")]
        [ProducesResponseType(typeof(List<CustomerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            try
            {
                _logger.LogInformation("Getting customers: page {Page}, pageSize {PageSize}", page, pageSize);

                var customers = await _customerRepository.GetAllAsync(page, pageSize);
                var dtos = customers.Select(c => new CustomerDto
                {
                    Id = c.Id,
                    SalesforceId = c.SalesforceId,
                    Name = c.Name,
                    Email = c.Email,
                    Phone = c.Phone,
                    Industry = c.Industry,
                    Website = c.Website,
                    LastSyncedAt = c.LastSyncedAt,
                    CreatedAt = c.CreatedAt
                }).ToList();

                return Ok(dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customers");
                return StatusCode(500, new { error = "Failed to retrieve customers" });
            }
        }

        /// <summary>
        /// Get a single customer by ID
        /// GET /api/customers/123
        /// </summary>
        [HttpGet("{id}")]
        [Authorize("read")]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(int id)
        {
            try
            {
                var customer = await _customerRepository.GetByIdAsync(id);
                if (customer == null)
                {
                    return NotFound(new { error = "Customer not found" });
                }

                var dto = new CustomerDto
                {
                    Id = customer.Id,
                    SalesforceId = customer.SalesforceId,
                    Name = customer.Name,
                    Email = customer.Email,
                    Phone = customer.Phone,
                    Industry = customer.Industry,
                    Website = customer.Website,
                    LastSyncedAt = customer.LastSyncedAt,
                    CreatedAt = customer.CreatedAt
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting customer {CustomerId}", id);
                return StatusCode(500, new { error = "Failed to retrieve customer" });
            }
        }

        /// <summary>
        /// Create a new customer
        /// POST /api/customers
        /// </summary>
        [HttpPost]
        [Authorize("write")]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create([FromBody] CreateCustomerDto dto)
        {
            try
            {
                _logger.LogInformation("Creating customer: {Name}", dto.Name);

                var customer = new SalesforceIntegration.Data.Entities.Customer
                {
                    Name = dto.Name,
                    Email = dto.Email,
                    Phone = dto.Phone,
                    Industry = dto.Industry,
                    Website = dto.Website
                };

                var created = await _customerRepository.CreateAsync(customer);

                var responseDto = new CustomerDto
                {
                    Id = created.Id,
                    SalesforceId = created.SalesforceId,
                    Name = created.Name,
                    Email = created.Email,
                    Phone = created.Phone,
                    Industry = created.Industry,
                    Website = created.Website,
                    CreatedAt = created.CreatedAt
                };

                return CreatedAtAction(nameof(GetById), new { id = created.Id }, responseDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating customer");
                return StatusCode(500, new { error = "Failed to create customer" });
            }
        }

        /// <summary>
        /// Update an existing customer
        /// PUT /api/customers/123
        /// </summary>
        [HttpPut("{id}")]
        [Authorize("write")]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerDto dto)
        {
            try
            {
                _logger.LogInformation("Updating customer {CustomerId}", id);

                var customer = await _customerRepository.GetByIdAsync(id);
                if (customer == null)
                {
                    return NotFound(new { error = "Customer not found" });
                }

                customer.Name = dto.Name ?? customer.Name;
                customer.Email = dto.Email ?? customer.Email;
                customer.Phone = dto.Phone ?? customer.Phone;
                customer.Industry = dto.Industry ?? customer.Industry;
                customer.Website = dto.Website ?? customer.Website;

                var updated = await _customerRepository.UpdateAsync(customer);

                var responseDto = new CustomerDto
                {
                    Id = updated.Id,
                    SalesforceId = updated.SalesforceId,
                    Name = updated.Name,
                    Email = updated.Email,
                    Phone = updated.Phone,
                    Industry = updated.Industry,
                    Website = updated.Website,
                    LastSyncedAt = updated.LastSyncedAt,
                    CreatedAt = updated.CreatedAt
                };

                return Ok(responseDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating customer {CustomerId}", id);
                return StatusCode(500, new { error = "Failed to update customer" });
            }
        }

        /// <summary>
        /// Delete a customer (soft delete)
        /// DELETE /api/customers/123
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize("write")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                _logger.LogInformation("Deleting customer {CustomerId}", id);

                var result = await _customerRepository.DeleteAsync(id);
                if (!result)
                {
                    return NotFound(new { error = "Customer not found" });
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting customer {CustomerId}", id);
                return StatusCode(500, new { error = "Failed to delete customer" });
            }
        }
    }

    /// <summary>
    /// Sync operations controller
    /// Manages synchronization between Salesforce and local database
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SyncController : ControllerBase
    {
        private readonly ICustomerSyncService _syncService;
        private readonly ILogger<SyncController> _logger;

        public SyncController(ICustomerSyncService syncService, ILogger<SyncController> logger)
        {
            _syncService = syncService;
            _logger = logger;
        }

        /// <summary>
        /// Sync customers from Salesforce to local database
        /// POST /api/sync/from-salesforce?since=2024-02-20T00:00:00Z
        /// </summary>
        [HttpPost("from-salesforce")]
        [Authorize("write")]
        [ProducesResponseType(typeof(SyncResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> SyncFromSalesforce([FromQuery] DateTime? since = null)
        {
            try
            {
                _logger.LogInformation("Starting sync from Salesforce since {Since}", since ?? DateTime.UtcNow.AddHours(-1));

                var result = await _syncService.SyncFromSalesforceAsync(since);

                var dto = new SyncResultDto
                {
                    SyncType = result.SyncType,
                    Status = result.Status,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    StartedAt = result.StartedAt,
                    CompletedAt = result.CompletedAt,
                    Duration = result.Duration.TotalSeconds,
                    Failures = result.Failures.Select(f => new { f.ExternalId, f.ErrorMessage }).ToList()
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing from Salesforce");
                return StatusCode(500, new { error = "Sync failed", detail = ex.Message });
            }
        }

        /// <summary>
        /// Sync customers from local database to Salesforce
        /// POST /api/sync/to-salesforce
        /// </summary>
        [HttpPost("to-salesforce")]
        [Authorize("write")]
        [ProducesResponseType(typeof(SyncResultDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> SyncToSalesforce()
        {
            try
            {
                _logger.LogInformation("Starting sync to Salesforce");

                var result = await _syncService.SyncToSalesforceAsync();

                var dto = new SyncResultDto
                {
                    SyncType = result.SyncType,
                    Status = result.Status,
                    SuccessCount = result.SuccessCount,
                    FailureCount = result.FailureCount,
                    StartedAt = result.StartedAt,
                    CompletedAt = result.CompletedAt,
                    Duration = result.Duration.TotalSeconds,
                    Failures = result.Failures.Select(f => new { f.ExternalId, f.ErrorMessage }).ToList()
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing to Salesforce");
                return StatusCode(500, new { error = "Sync failed", detail = ex.Message });
            }
        }

        /// <summary>
        /// Get current sync status and statistics
        /// GET /api/sync/status
        /// </summary>
        [HttpGet("status")]
        [Authorize("read")]
        [ProducesResponseType(typeof(SyncStatusDto), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await _syncService.GetSyncStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting sync status");
                return StatusCode(500, new { error = "Failed to get sync status" });
            }
        }

        /// <summary>
        /// Retry a failed sync operation
        /// POST /api/sync/retry/123
        /// </summary>
        [HttpPost("retry/{failureId}")]
        [Authorize("write")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> RetrySync(int failureId)
        {
            try
            {
                _logger.LogInformation("Retrying sync failure {FailureId}", failureId);

                var result = await _syncService.RetryFailedSyncAsync(failureId);
                if (result)
                {
                    return Ok(new { message = "Retry successful" });
                }

                return BadRequest(new { error = "Failed to retry sync" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying sync {FailureId}", failureId);
                return StatusCode(500, new { error = "Retry failed" });
            }
        }
    }

    // ========================================================================
    // DTOs
    // ========================================================================

    public class CustomerDto
    {
        public int Id { get; set; }
        public string SalesforceId { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Industry { get; set; }
        public string Website { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateCustomerDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Industry { get; set; }
        public string Website { get; set; }
    }

    public class UpdateCustomerDto
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Industry { get; set; }
        public string Website { get; set; }
    }

    public class SyncResultDto
    {
        public string SyncType { get; set; }
        public string Status { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public double Duration { get; set; }
        public List<object> Failures { get; set; }
    }

    public class SyncStatusDto
    {
        public int TotalCustomers { get; set; }
        public int UnresolvedFailures { get; set; }
        public DateTime? LastSyncFromSalesforce { get; set; }
        public DateTime? LastSyncToSalesforce { get; set; }
        public int LastFromSalesforceCount { get; set; }
        public int LastToSalesforceCount { get; set; }
    }
}
