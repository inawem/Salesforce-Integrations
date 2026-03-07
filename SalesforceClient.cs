using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace SalesforceIntegration.Services
{
    /// <summary>
    /// Salesforce CRM client for REST API integration
    /// Handles OAuth2 authentication and API calls
    /// </summary>
    public interface ISalesforceClient
    {
        Task AuthenticateAsync();
        Task<SalesforceCustomer> GetCustomerAsync(string customerId);
        Task<string> CreateCustomerAsync(SalesforceCustomer customer);
        Task UpdateCustomerAsync(string customerId, SalesforceCustomer customer);
        Task<List<SalesforceCustomer>> QueryCustomersAsync(string soqlQuery);
    }

    public class SalesforceClient : ISalesforceClient
    {
        private readonly HttpClient _httpClient;
        private readonly SalesforceConfig _config;
        private readonly ILogger<SalesforceClient> _logger;
        private readonly IMemoryCache _cache;

        private string _accessToken;
        private DateTime _tokenExpiry;

        private const string TOKEN_CACHE_KEY = "salesforce_access_token";
        private const string API_VERSION = "v57.0";

        public SalesforceClient(
            HttpClient httpClient,
            SalesforceConfig config,
            ILogger<SalesforceClient> logger,
            IMemoryCache cache)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _cache = cache;
        }

        /// <summary>
        /// Authenticate with Salesforce using OAuth2 Client Credentials flow
        /// </summary>
        public async Task AuthenticateAsync()
        {
            try
            {
                _logger.LogInformation("Authenticating with Salesforce using OAuth2");

                var authRequest = new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", _config.ClientId },
                    { "client_secret", _config.ClientSecret }
                };

                var content = new FormUrlEncodedContent(authRequest);
                var tokenUrl = $"{_config.InstanceUrl}/services/oauth2/token";

                var response = await _httpClient.PostAsync(tokenUrl, content);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<JsonElement>(json);

                _accessToken = tokenResponse.GetProperty("access_token").GetString();
                var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 300); // Refresh 5 minutes before expiry

                // Cache the token
                _cache.Set(TOKEN_CACHE_KEY, _accessToken, TimeSpan.FromSeconds(expiresIn - 300));

                _logger.LogInformation("Successfully authenticated with Salesforce");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to authenticate with Salesforce");
                throw;
            }
        }

        /// <summary>
        /// Get a customer (Account) from Salesforce by ID
        /// </summary>
        public async Task<SalesforceCustomer> GetCustomerAsync(string customerId)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                _logger.LogInformation("Fetching customer {CustomerId} from Salesforce", customerId);

                var url = $"{_config.InstanceUrl}/services/data/{API_VERSION}/sobjects/Account/{customerId}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                SetAuthorizationHeader(request);

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request);
                stopwatch.Stop();

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var customer = JsonSerializer.Deserialize<SalesforceCustomer>(json);

                _logger.LogInformation(
                    "Successfully retrieved customer {CustomerId} from Salesforce in {Duration}ms",
                    customerId, stopwatch.ElapsedMilliseconds);

                return customer;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Customer not found in Salesforce: {CustomerId}", customerId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching customer {CustomerId} from Salesforce", customerId);
                throw;
            }
        }

        /// <summary>
        /// Create a new customer in Salesforce
        /// </summary>
        public async Task<string> CreateCustomerAsync(SalesforceCustomer customer)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                _logger.LogInformation("Creating customer {Name} in Salesforce", customer.Name);

                var url = $"{_config.InstanceUrl}/services/data/{API_VERSION}/sobjects/Account";
                var json = JsonSerializer.Serialize(customer, new JsonSerializerOptions { WriteIndented = false });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                SetAuthorizationHeader(request);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var newId = responseObj.GetProperty("id").GetString();

                _logger.LogInformation("Successfully created customer {Name} with Salesforce ID {Id}", customer.Name, newId);
                return newId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create customer {Name} in Salesforce", customer.Name);
                throw;
            }
        }

        /// <summary>
        /// Update an existing customer in Salesforce
        /// </summary>
        public async Task UpdateCustomerAsync(string customerId, SalesforceCustomer customer)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                _logger.LogInformation("Updating customer {CustomerId} in Salesforce", customerId);

                var url = $"{_config.InstanceUrl}/services/data/{API_VERSION}/sobjects/Account/{customerId}";
                var json = JsonSerializer.Serialize(customer);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Patch, url);
                SetAuthorizationHeader(request);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                _logger.LogInformation("Successfully updated customer {CustomerId} in Salesforce", customerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update customer {CustomerId} in Salesforce", customerId);
                throw;
            }
        }

        /// <summary>
        /// Query customers using SOQL (Salesforce Object Query Language)
        /// </summary>
        public async Task<List<SalesforceCustomer>> QueryCustomersAsync(string soqlQuery)
        {
            try
            {
                await EnsureAuthenticatedAsync();
                _logger.LogInformation("Executing SOQL query");

                var allRecords = new List<SalesforceCustomer>();
                var nextRecordsUrl = string.Empty;
                var pageCount = 0;

                do
                {
                    string url;
                    if (string.IsNullOrEmpty(nextRecordsUrl))
                    {
                        var encodedQuery = System.Web.HttpUtility.UrlEncode(soqlQuery);
                        url = $"{_config.InstanceUrl}/services/data/{API_VERSION}/query?q={encodedQuery}";
                    }
                    else
                    {
                        url = nextRecordsUrl.StartsWith("http") ? nextRecordsUrl : $"{_config.InstanceUrl}{nextRecordsUrl}";
                    }

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    SetAuthorizationHeader(request);

                    var response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync();
                    var queryResponse = JsonSerializer.Deserialize<JsonElement>(json);
                    var records = queryResponse.GetProperty("records").EnumerateArray();

                    foreach (var record in records)
                    {
                        var customer = JsonSerializer.Deserialize<SalesforceCustomer>(record.GetRawText());
                        allRecords.Add(customer);
                    }

                    nextRecordsUrl = queryResponse.TryGetProperty("nextRecordsUrl", out var nextProp) 
                        ? nextProp.GetString() 
                        : string.Empty;

                    pageCount++;

                } while (!string.IsNullOrEmpty(nextRecordsUrl));

                _logger.LogInformation(
                    "SOQL query completed: Returned {Count} records across {Pages} pages",
                    allRecords.Count, pageCount);

                return allRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute SOQL query");
                throw;
            }
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return;
            }

            if (_cache.TryGetValue(TOKEN_CACHE_KEY, out string cachedToken))
            {
                _accessToken = cachedToken;
                _tokenExpiry = DateTime.UtcNow.AddMinutes(2); // Assume cached token is valid for 2 more minutes
                return;
            }

            await AuthenticateAsync();
        }

        private void SetAuthorizationHeader(HttpRequestMessage request)
        {
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        }
    }

    // ========================================================================
    // CONFIGURATION & MODELS
    // ========================================================================

    public class SalesforceConfig
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string InstanceUrl { get; set; } // e.g., https://yourinstance.salesforce.com
    }

    public class SalesforceCustomer
    {
        [System.Text.Json.Serialization.JsonPropertyName("Id")]
        public string Id { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Name")]
        public string Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("BillingStreet")]
        public string BillingStreet { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("BillingCity")]
        public string BillingCity { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("BillingState")]
        public string BillingState { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("BillingPostalCode")]
        public string BillingZip { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Phone")]
        public string Phone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Website")]
        public string Website { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Industry")]
        public string Industry { get; set; }

        // Custom field (note the __c suffix)
        [System.Text.Json.Serialization.JsonPropertyName("Email__c")]
        public string Email { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("LastModifiedDate")]
        public DateTime LastModifiedDate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("CreatedDate")]
        public DateTime CreatedDate { get; set; }
    }
}
