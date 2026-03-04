# RDETSApp

## Overview
RDETSApp is a C# (.NET 6) application designed to automate the processing of registration records in a SQL Server database by integrating with external APIs. The workflow is status-driven, parallelized, and suitable for stateless repeated invocation (e.g., as an Azure Function).

## Workflow Summary
- **Step 1: OrderID Processing**
  - Selects records with `dub_registration_processing_status = 'Unprocessed'`.
  - Updates status to `OrderID_Processing`.
  - Calls the OrderID API with registration details.
  - Updates status to `OrderID_Received` and stores the returned `DET_OrderID`.
  - On error, updates status to `Error` and logs the error message.

- **Step 2: Barcode Processing**
  - Selects records with `dub_registration_processing_status = 'OrderID_Received'`.
  - Updates status to `BarCode_Processing`.
  - Calls the Barcode API with the `DET_OrderID`.
  - Updates status to `Processed` and stores the returned `dub_barcode`.
  - On error, updates status to `Error` and logs the error message.

- **Parallelism**
  - Each eligible record is processed in its own task using `Task.Run` and `Task.WhenAll`.
  - Each task uses its own database connection for thread safety.

## Status Field Values
- `Unprocessed`: Awaiting OrderID API call
- `OrderID_Processing`: OrderID API call in progress
- `OrderID_Received`: OrderID received, awaiting Barcode API call
- `BarCode_Processing`: Barcode API call in progress
- `Processed`: Both API calls complete, barcode stored
- `Error`: Error occurred, see `error_message` field

## Error Handling
- Any exception during API or DB operations updates the record's status to `Error` and logs the error message in the `error_message` field.

## Azure Function Compatibility
- The workflow is stateless and can be invoked repeatedly.
- New records added to the table will be picked up automatically in the next invocation.

## Configuration
- Update the following placeholders in `Program.cs`:
  - `YOUR_CONNECTION_STRING`: Your SQL Server connection string
  - `YourTableName`: Your actual table name
  - `https://your-api-endpoint`: Your OrderID API endpoint
  - `https://your-barcode-api-endpoint`: Your Barcode API endpoint

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
