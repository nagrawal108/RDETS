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
            string query = "SELECT dub_key, dub_reg_cst_key, dub_country_of_residence, dub_nationality FROM YourTableName"; // TODO: Replace with your actual table name
            string apiUrl = "https://your-api-endpoint"; // TODO: Replace with your actual API endpoint
            string barcodeApiUrl = "https://your-barcode-api-endpoint"; // TODO: Replace with your actual barcode API endpoint

            // Collect all records to process
            var recordList = new List<(string dubKey, string cstKey, string country, string nationality)>();
            // Read all records from the database first (sequential)
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        recordList.Add((
                            reader["dub_key"].ToString(),
                            reader["dub_reg_cst_key"].ToString(),
                            reader["dub_country_of_residence"].ToString(),
                            reader["dub_nationality"].ToString()
                        ));
                    }
                }
            }

            // Process all records in parallel using Task.Run and Task.WhenAll
            // Each record is handled in its own task, with its own DB connection and API calls
            // This allows multiple records to be processed concurrently, improving throughput
            var tasks = new List<Task>();
            foreach (var record in recordList)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // Each task uses its own SqlConnection to avoid conflicts
                    using (SqlConnection conn = new SqlConnection(connectionString))
                    {
                        await conn.OpenAsync();
                        // Step 1: Set status to OrderID_Processing
                        await UpdateStatusAsync(conn, record.dubKey, "OrderID_Processing");

                        // Step 2: Call API to get DET_OrderID
                        string detOrderId = await GetDetOrderIdAsync(apiUrl, record.cstKey, record.country, record.nationality);
                        Console.WriteLine($"DET_OrderID: {detOrderId}");

                        // Step 3: Set status to OrderID_Received and update DET_OrderID field
                        await UpdateStatusAndOrderIdAsync(conn, record.dubKey, "OrderID_Received", detOrderId);

                        // Step 4: Set status to BarCode_Processing
                        await UpdateStatusAsync(conn, record.dubKey, "BarCode_Processing");

                        // Step 5: Call API to get dub_barcode
                        string dubBarcode = await GetDubBarcodeAsync(barcodeApiUrl, detOrderId);
                        Console.WriteLine($"dub_barcode: {dubBarcode}");

                        // Step 6: Set status to Processed and update dub_barcode field
                        await UpdateStatusAndBarcodeAsync(conn, record.dubKey, "Processed", dubBarcode);
                    }
                }));
            }
            // Wait for all parallel tasks to complete
            await Task.WhenAll(tasks);
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
                    }
                }
            }
        }

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
    }
}
