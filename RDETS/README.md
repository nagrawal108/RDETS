# RDETS C# Console App

This project automates the process of interacting with a SQL Server database and two external APIs to process Dubai event registrant records. It updates the database at each step to reflect the current processing status and stores results from the APIs.

## Workflow Overview
1. **Database Query:**
   - Reads records from the table, selecting `dub_key`, `dub_reg_cst_key`, `dub_country_of_residence`, and `dub_nationality`.
2. **OrderID API Call:**
   - Sets `dub_registration_processing_status` to `OrderID_Processing`.
   - Sends registrant details to the OrderID API and receives `DET_OrderID`.
   - Updates the table with `OrderID_Received` status and stores the received `DET_OrderID`.
3. **Barcode API Call:**
   - Sets `dub_registration_processing_status` to `BarCode_Processing`.
   - Sends the received `DET_OrderID` to the Barcode API and receives `dub_barcode`.
   - Updates the table with `Processed` status and stores the received `dub_barcode`.

## How to use
1. Update the connection string, table name, and both API endpoints in `Program.cs`.
2. Build and run the project using VS Code tasks or `dotnet run`.

## Code Structure
- `Program.cs`: Contains the main workflow and helper methods for API calls and database updates.
- Helper methods:
  - `UpdateStatusAsync`: Updates only the status field.
  - `UpdateStatusAndOrderIdAsync`: Updates status and `DET_OrderID`.
  - `UpdateStatusAndBarcodeAsync`: Updates status and `dub_barcode`.
  - `GetDetOrderIdAsync`: Calls the OrderID API.
  - `GetDubBarcodeAsync`: Calls the Barcode API.

## Table Fields Used
- `dub_key`: Primary key for the record.
- `dub_reg_cst_key`: Registrant customer key.
- `dub_country_of_residence`: Country of residence.
- `dub_nationality`: Nationality.
- `dub_registration_processing_status`: Status field updated at each step.
- `DET_OrderID`: Stores the received OrderID.
- `dub_barcode`: Stores the received barcode.

---

**Note:** Replace placeholders with your actual connection string, table name, and API endpoints. Ensure your database user has update permissions on the table.