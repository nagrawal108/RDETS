using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

namespace RDETSApp.Functions
{
    public class ScheduledDetWorkflow
    {
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public ScheduledDetWorkflow(IConfiguration config, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _httpClientFactory = httpClientFactory;
        }

        [FunctionName("DetWorkflowTimer")]
        public async Task Run([TimerTrigger("0 0 * * * *")]TimerInfo myTimer, ILogger log)
        {
            // Load configuration
            string connectionString = _config["SqlConnectionString"];
            string clientId = _config["DetClientId"];
            string clientSecret = _config["DetClientSecret"];
            string apiBaseUrl = _config["DetApiBaseUrl"];
            string perfCode = _config["DetPerfCode"];

            // 1. Retrieve Access Token
            string accessToken = await RetrieveTokenAsync(apiBaseUrl, clientId, clientSecret);
            if (string.IsNullOrEmpty(accessToken))
            {
                log.LogError("Failed to retrieve access token. Exiting.");
                return;
            }

            // 2. Get Perf Map (one time, for a given perf code)
            var blueBoxDetails = await GetPerfMapAsync(apiBaseUrl, accessToken, perfCode);
            if (blueBoxDetails == null)
            {
                log.LogError("Failed to retrieve blue box details. Exiting.");
                return;
            }

            // 3. For each eligible record, process the workflow
            var tasks = new List<Task>();
            string selectQuery = "SELECT dub_key, dub_reg_cst_key, dub_country_of_residence, dub_nationality FROM YourTableName WHERE dub_registration_processing_status = 'Unprocessed'";
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(selectQuery, conn))
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var record = (
                            dubKey: reader["dub_key"].ToString(),
                            cstKey: reader["dub_reg_cst_key"].ToString(),
                            country: reader["dub_country_of_residence"].ToString(),
                            nationality: reader["dub_nationality"].ToString()
                        );
                        tasks.Add(ProcessRecordAsync(record, connectionString, apiBaseUrl, accessToken, blueBoxDetails, log));
                    }
                }
            }
            await Task.WhenAll(tasks);
        }

        private async Task ProcessRecordAsync((string dubKey, string cstKey, string country, string nationality) record, string connectionString, string apiBaseUrl, string accessToken, Dictionary<string, object> blueBoxDetails, ILogger log)
        {
            try
            {
                using (SqlConnection innerConn = new SqlConnection(connectionString))
                {
                    await innerConn.OpenAsync();
                    // a. Create Basket
                    await UpdateStatusAsync(innerConn, record.dubKey, "Basket_Processing");
                    string basketId = await CreateBasketAsync(apiBaseUrl, accessToken, blueBoxDetails);
                    if (string.IsNullOrEmpty(basketId)) throw new Exception("Failed to create basket");

                    // b. Create Customer
                    await UpdateStatusAsync(innerConn, record.dubKey, "Customer_Processing");
                    string customerId = await CreateCustomerAsync(apiBaseUrl, accessToken, record.country, record.nationality);
                    if (string.IsNullOrEmpty(customerId)) throw new Exception("Failed to create customer");

                    // c. Purchase Basket
                    await UpdateStatusAsync(innerConn, record.dubKey, "Purchase_Processing");
                    string orderId = await PurchaseBasketAsync(apiBaseUrl, accessToken, basketId, customerId);
                    if (string.IsNullOrEmpty(orderId)) throw new Exception("Failed to purchase basket");
                    await UpdateStatusAndOrderIdAsync(innerConn, record.dubKey, "OrderID_Received", orderId);

                    // d. Get Order Detail (Barcode)
                    await UpdateStatusAsync(innerConn, record.dubKey, "BarCode_Processing");
                    string barcode = await GetOrderDetailAsync(apiBaseUrl, accessToken, orderId);
                    if (string.IsNullOrEmpty(barcode)) throw new Exception("Failed to get barcode");
                    await UpdateStatusAndBarcodeAsync(innerConn, record.dubKey, "Processed", barcode);
                }
            }
            catch (Exception ex)
            {
                using (SqlConnection errorConn = new SqlConnection(connectionString))
                {
                    await errorConn.OpenAsync();
                    await UpdateErrorStatusAsync(errorConn, record.dubKey, "Error", ex.Message);
                }
                log.LogError($"Error processing record {record.dubKey}: {ex.Message}");
            }
        }

        // --- Helper methods adapted for Azure Functions ---
        private async Task<string> RetrieveTokenAsync(string apiBaseUrl, string clientId, string clientSecret)
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new { client_id = clientId, client_secret = clientSecret };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/token", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
            return result != null && result.ContainsKey("access_token") ? result["access_token"] : null;
        }

        private async Task<Dictionary<string, object>> GetPerfMapAsync(string apiBaseUrl, string accessToken, string perfCode)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var payload = new { perf_code = perfCode };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/get-perf-map", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
        }

        private async Task<string> CreateBasketAsync(string apiBaseUrl, string accessToken, Dictionary<string, object> blueBoxDetails)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var json = JsonSerializer.Serialize(blueBoxDetails);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/create-basket", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
            return result != null && result.ContainsKey("basket_id") ? result["basket_id"] : null;
        }

        private async Task<string> CreateCustomerAsync(string apiBaseUrl, string accessToken, string country, string nationality)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var payload = new { country_of_residence = country, nationality = nationality };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/create-customer", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
            return result != null && result.ContainsKey("customer_id") ? result["customer_id"] : null;
        }

        private async Task<string> PurchaseBasketAsync(string apiBaseUrl, string accessToken, string basketId, string customerId)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var payload = new { basket_id = basketId, customer_id = customerId };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/purchase-basket", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
            return result != null && result.ContainsKey("order_id") ? result["order_id"] : null;
        }

        private async Task<string> GetOrderDetailAsync(string apiBaseUrl, string accessToken, string orderId)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            var payload = new { order_id = orderId };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{apiBaseUrl}/get-order-detail", content);
            if (!response.IsSuccessStatusCode) return null;
            var responseBody = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
            return result != null && result.ContainsKey("barcode") ? result["barcode"] : null;
        }

        private async Task UpdateStatusAsync(SqlConnection conn, string dubKey, string status)
        {
            string updateQuery = "UPDATE YourTableName SET dub_registration_processing_status = @status WHERE dub_key = @dubKey";
            using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@dubKey", dubKey);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task UpdateStatusAndOrderIdAsync(SqlConnection conn, string dubKey, string status, string detOrderId)
        {
            string updateQuery = "UPDATE YourTableName SET dub_registration_processing_status = @status, DET_OrderID = @detOrderId WHERE dub_key = @dubKey";
            using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@detOrderId", detOrderId);
                updateCmd.Parameters.AddWithValue("@dubKey", dubKey);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task UpdateStatusAndBarcodeAsync(SqlConnection conn, string dubKey, string status, string dubBarcode)
        {
            string updateQuery = "UPDATE YourTableName SET dub_registration_processing_status = @status, dub_barcode = @dubBarcode WHERE dub_key = @dubKey";
            using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@dubBarcode", dubBarcode);
                updateCmd.Parameters.AddWithValue("@dubKey", dubKey);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        private async Task UpdateErrorStatusAsync(SqlConnection conn, string dubKey, string status, string errorMessage)
        {
            string updateQuery = "UPDATE YourTableName SET dub_registration_processing_status = @status, error_message = @errorMessage WHERE dub_key = @dubKey";
            using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@errorMessage", errorMessage);
                updateCmd.Parameters.AddWithValue("@dubKey", dubKey);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }
    }
}
