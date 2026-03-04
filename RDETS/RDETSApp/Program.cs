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
            string apiUrl = "https://your-api-endpoint"; // TODO: Replace with your actual API endpoint
            string barcodeApiUrl = "https://your-barcode-api-endpoint"; // TODO: Replace with your actual barcode API endpoint

            // This workflow is designed for stateless, repeated invocation (e.g., Azure Function)
            // It processes only records that are eligible for each step, based on their status
            // Parallel processing is used to maximize throughput
            var tasks = new List<Task>();

            // Step 1: Process records with status 'Unprocessed' (OrderID step)
            // These records have not yet been sent to the OrderID API
            string orderIdQuery = "SELECT dub_key, dub_reg_cst_key, dub_country_of_residence, dub_nationality FROM YourTableName WHERE dub_registration_processing_status = 'Unprocessed'";
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(orderIdQuery, conn))
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
                        // Each record is processed in its own task for parallelism
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                using (SqlConnection innerConn = new SqlConnection(connectionString))
                                {
                                    await innerConn.OpenAsync();
                                    // Set status to OrderID_Processing before API call
                                    await UpdateStatusAsync(innerConn, record.dubKey, "OrderID_Processing");
                                    // Call API to get DET_OrderID
                                    string detOrderId = await GetDetOrderIdAsync(apiUrl, record.cstKey, record.country, record.nationality);
                                    Console.WriteLine($"DET_OrderID: {detOrderId}");
                                    // Set status to OrderID_Received and update DET_OrderID field
                                    await UpdateStatusAndOrderIdAsync(innerConn, record.dubKey, "OrderID_Received", detOrderId);
                                }
                            }
                            catch (Exception ex)
                            {
                                // On error, update status to Error and log message
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

            // Step 2: Process records with status 'OrderID_Received' (Barcode step)
            // These records have received an OrderID and are ready for barcode processing
            string barcodeQuery = "SELECT dub_key, DET_OrderID FROM YourTableName WHERE dub_registration_processing_status = 'OrderID_Received'";
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(barcodeQuery, conn))
                using (SqlDataReader reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var record = (
                            dubKey: reader["dub_key"].ToString(),
                            detOrderId: reader["DET_OrderID"].ToString()
                        );
                        // Each record is processed in its own task for parallelism
                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                using (SqlConnection innerConn = new SqlConnection(connectionString))
                                {
                                    await innerConn.OpenAsync();
                                    // Set status to BarCode_Processing before API call
                                    await UpdateStatusAsync(innerConn, record.dubKey, "BarCode_Processing");
                                    // Call API to get dub_barcode
                                    string dubBarcode = await GetDubBarcodeAsync(barcodeApiUrl, record.detOrderId);
                                    Console.WriteLine($"dub_barcode: {dubBarcode}");
                                    // Set status to Processed and update dub_barcode field
                                    await UpdateStatusAndBarcodeAsync(innerConn, record.dubKey, "Processed", dubBarcode);
                                }
                            }
                            catch (Exception ex)
                            {
                                // On error, update status to Error and log message
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

            // Wait for all parallel tasks to complete before exiting
            await Task.WhenAll(tasks);
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
