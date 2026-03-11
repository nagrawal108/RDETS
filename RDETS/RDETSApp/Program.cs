using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RDETSApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string connectionString = "YOUR_CONNECTION_STRING"; // TODO: Replace with your actual DB connection string
            string clientId = "YOUR_CLIENT_ID"; // TODO: Replace with your actual client id
            string clientSecret = "YOUR_CLIENT_SECRET"; // TODO: Replace with your actual client secret
            string apiBaseUrl = "https://your-api-base-url"; // TODO: Replace with your actual API base url

            // --- Dubai DET API Integration Workflow ---
            // 1. Retrieve Access Token using client id and secret
            string accessToken = await RetrieveTokenAsync(apiBaseUrl, clientId, clientSecret);
            if (string.IsNullOrEmpty(accessToken))
            {
                Console.WriteLine("Failed to retrieve access token. Exiting.");
                return;
            }

            // 2. Get Perf Map (one time, for a given perf code) to obtain blue box details
            string perfCode = "YOUR_PERF_CODE"; // TODO: Replace with your actual perf code
            var blueBoxDetails = await GetPerfMapAsync(apiBaseUrl, accessToken, perfCode);
            if (blueBoxDetails == null)
            {
                Console.WriteLine("Failed to retrieve blue box details. Exiting.");
                return;
            }

            // 3. For each eligible record, process the following sequence:
            //    a. Create Basket (using blue box details)
            //    b. Create Customer (using country of residence and nationality)
            //    c. Purchase Basket (using basketId and customerId)
            //    d. Get Order Detail (using orderId) to obtain barcode
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
                        tasks.Add(Task.Run(async () =>
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
                            }
                        }));
                    }
                }
            }
            await Task.WhenAll(tasks);
                // 2.1.1 Retrieve Token (client id + secret => access token)
                static async Task<string> RetrieveTokenAsync(string apiBaseUrl, string clientId, string clientSecret)
                {
                    using (var client = new HttpClient())
                    {
                        var payload = new { client_id = clientId, client_secret = clientSecret };
                        var json = JsonSerializer.Serialize(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync($"{apiBaseUrl}/token", content);
                        if (!response.IsSuccessStatusCode) return null;
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
                        return result != null && result.ContainsKey("access_token") ? result["access_token"] : null;
                    }
                }

                // 2.1.2 Get Perf Map (access token + perf code => blue box details)
                static async Task<Dictionary<string, object>> GetPerfMapAsync(string apiBaseUrl, string accessToken, string perfCode)
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                        var payload = new { perf_code = perfCode };
                        var json = JsonSerializer.Serialize(payload);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync($"{apiBaseUrl}/get-perf-map", content);
                        if (!response.IsSuccessStatusCode) return null;
                        var responseBody = await response.Content.ReadAsStringAsync();
                        return JsonSerializer.Deserialize<Dictionary<string, object>>(responseBody);
                    }
                }

                // 2.1.6 Create Basket (access token + blue box => basketId)
                static async Task<string> CreateBasketAsync(string apiBaseUrl, string accessToken, Dictionary<string, object> blueBoxDetails)
                {
                    using (var client = new HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
                        var json = JsonSerializer.Serialize(blueBoxDetails);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await client.PostAsync($"{apiBaseUrl}/create-basket", content);
                        if (!response.IsSuccessStatusCode) return null;
                        var responseBody = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
                        return result != null && result.ContainsKey("basket_id") ? result["basket_id"] : null;
                    }
                }

                // 2.1.15 Create Customer (access token + country of residence + nationality => customerId)
                static async Task<string> CreateCustomerAsync(string apiBaseUrl, string accessToken, string country, string nationality)
                {
                    using (var client = new HttpClient())
                    {
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
                }

                // 2.1.11 Purchase Basket (access token + basketId + customerId => orderId)
                static async Task<string> PurchaseBasketAsync(string apiBaseUrl, string accessToken, string basketId, string customerId)
                {
                    using (var client = new HttpClient())
                    {
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
                }

                // 2.1.12 Get Order Detail (access token + orderId => barcode)
                static async Task<string> GetOrderDetailAsync(string apiBaseUrl, string accessToken, string orderId)
                {
                    using (var client = new HttpClient())
                    {
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
                }
        }

        // Helper methods below are direct members of Program class
        static async Task UpdateStatusAndBarcodeAsync(SqlConnection conn, string dubKey, string status, string dubBarcode)
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

        // Helper method: update only the status field
        // Used to set status transitions (e.g., OrderID_Processing, BarCode_Processing)
        static async Task UpdateStatusAsync(SqlConnection conn, string dubKey, string status)
        {
            string updateQuery = "UPDATE YourTableName SET dub_registration_processing_status = @status WHERE dub_key = @dubKey";
            using (SqlCommand updateCmd = new SqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@status", status);
                updateCmd.Parameters.AddWithValue("@dubKey", dubKey);
                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        // Helper method: update status and DET_OrderID fields
        static async Task UpdateStatusAndOrderIdAsync(SqlConnection conn, string dubKey, string status, string detOrderId)
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

        // ...existing code...

        // Helper method: call API to get DET_OrderID
        // Sends registration details to external API and returns DET_OrderID
        static async Task<string> GetDetOrderIdAsync(string apiUrl, string cstKey, string country, string nationality)
        {
            using (var client = new HttpClient())
            {
                var payload = new
                {
                    dub_reg_cst_key = cstKey,
                    dub_country_of_residence = country,
                    dub_nationality = nationality
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(apiUrl, content);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                // Assuming the API returns { "DET_OrderID": "value" }
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
                return result != null && result.ContainsKey("DET_OrderID") ? result["DET_OrderID"] : "Not found";
            }
        }

        // Helper method: call API to get dub_barcode
        // Sends DET_OrderID to external API and returns dub_barcode
        static async Task<string> GetDubBarcodeAsync(string barcodeApiUrl, string detOrderId)
        {
            using (var client = new HttpClient())
            {
                var payload = new
                {
                    DET_OrderID = detOrderId
                };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync(barcodeApiUrl, content);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();

                // Assuming the API returns { "dub_barcode": "value" }
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseBody);
                return result != null && result.ContainsKey("dub_barcode") ? result["dub_barcode"] : "Not found";
            }
        }

        // Helper method: update status and error message
        // Used to log errors and set status to Error
        static async Task UpdateErrorStatusAsync(SqlConnection conn, string dubKey, string status, string errorMessage)
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

        // Helper method: update status and error message
        static async Task UpdateErrorStatusAsync(SqlConnection conn, string dubKey, string status, string errorMessage)
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
