# RDETSApp

## Overview
RDETSApp is a C# (.NET 6) application that automates the processing of registration records in a SQL Server database by integrating with the Dubai DET Sales API. The workflow is status-driven, parallelized, and suitable for stateless repeated invocation (e.g., as an Azure Function).

## Dubai DET API Workflow (2026)
The application now follows the official Dubai DET API call order:

1. **Retrieve Token** (client id + secret → access token)
2. **Get Perf Map** (access token + perf code → blue box details)
3. **Create Basket** (access token + blue box → basketId)
4. **Create Customer** (access token + country of residence + nationality → customerId)
5. **Purchase Basket** (access token + basketId + customerId → orderId)
6. **Get Order Detail** (access token + orderId → barcode)

Each eligible record is processed in this sequence. All API calls and status updates are handled per record, in parallel.

## Workflow Summary
- **Step 1: Retrieve Access Token**
- **Step 2: Get Perf Map** (one time per run, for a given perf code)
- **Step 3: For each record with `dub_registration_processing_status = 'Unprocessed'`:**
  - a. Create Basket (update status to `Basket_Processing`)
  - b. Create Customer (update status to `Customer_Processing`)
  - c. Purchase Basket (update status to `Purchase_Processing` and then `OrderID_Received`)
  - d. Get Order Detail (update status to `BarCode_Processing` and then `Processed`)
  - On error, update status to `Error` and log the error message

## Status Field Values
- `Unprocessed`: Awaiting processing
- `Basket_Processing`: Basket API call in progress
- `Customer_Processing`: Customer API call in progress
- `Purchase_Processing`: Purchase API call in progress
- `OrderID_Received`: OrderID received, ready for barcode
- `BarCode_Processing`: Barcode API call in progress
- `Processed`: All API calls complete, barcode stored
- `Error`: Error occurred, see `error_message` field

## Error Handling
- Any exception during API or DB operations updates the record's status to `Error` and logs the error message in the `error_message` field.

## Azure Function Compatibility
- The workflow is stateless and can be invoked repeatedly.
- New records added to the table will be picked up automatically in the next invocation.

## Configuration
Update the following placeholders in `Program.cs`:
- `YOUR_CONNECTION_STRING`: Your SQL Server connection string
- `YOUR_CLIENT_ID` / `YOUR_CLIENT_SECRET`: Your API credentials
- `YOUR_PERF_CODE`: The performance code for the event
- `YourTableName`: Your actual table name
- `https://your-api-base-url`: Your API base URL

## Usage
1. Build the project:
  ```powershell
  dotnet build
  ```
2. Run the project:
  ```powershell
  dotnet run
  ```
3. Deploy as an Azure Function (optional):
  - Refactor the main logic into a function handler as needed.

## File Structure
- `Program.cs`: Main workflow and helper methods
- `.vscode/tasks.json`: VS Code build/run tasks
- `RDETSApp.csproj`: Project file

## Dependencies
- System.Data.SqlClient
- System.Net.Http
- System.Text.Json

## License
MIT
