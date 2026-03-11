# RDETSApp Azure Function - Test Use Cases

## Purpose
This document provides test scenarios for the Dubai DET API workflow Azure Function. Each test case includes the expected outcome and key validation points.

---

### 1. Token Retrieval Failure
- **Setup:** Configure invalid client id/secret in Azure Function settings.
- **Expected:** Function logs error and exits; no records processed.
- **Validation:** Error log contains "Failed to retrieve access token".

### 2. Perf Map Retrieval Failure
- **Setup:** Configure valid token, but invalid perf code.
- **Expected:** Function logs error and exits; no records processed.
- **Validation:** Error log contains "Failed to retrieve blue box details".

### 3. No Eligible Records
- **Setup:** All records have status other than 'Unprocessed'.
- **Expected:** Function completes with no processing; no errors.
- **Validation:** No status changes; logs show workflow completed.

### 4. Successful End-to-End Processing
- **Setup:** At least one record with status 'Unprocessed'; all API endpoints return valid responses.
- **Expected:** Record status transitions through all steps; barcode is stored.
- **Validation:**
  - Status changes: 'Basket_Processing' → 'Customer_Processing' → 'Purchase_Processing' → 'OrderID_Received' → 'BarCode_Processing' → 'Processed'.
  - Barcode field is populated.

### 5. Basket Creation Failure
- **Setup:** API returns error for basket creation.
- **Expected:** Status set to 'Error'; error message logged.
- **Validation:** Error log contains "Failed to create basket"; record status is 'Error'.

### 6. Customer Creation Failure
- **Setup:** API returns error for customer creation.
- **Expected:** Status set to 'Error'; error message logged.
- **Validation:** Error log contains "Failed to create customer"; record status is 'Error'.

### 7. Purchase Basket Failure
- **Setup:** API returns error for purchase basket.
- **Expected:** Status set to 'Error'; error message logged.
- **Validation:** Error log contains "Failed to purchase basket"; record status is 'Error'.

### 8. Get Order Detail Failure
- **Setup:** API returns error for order detail/barcode.
- **Expected:** Status set to 'Error'; error message logged.
- **Validation:** Error log contains "Failed to get barcode"; record status is 'Error'.

### 9. Database Connection Failure
- **Setup:** Configure invalid SQL connection string.
- **Expected:** Function logs error; no records processed.
- **Validation:** Error log contains SQL connection error.

### 10. Parallel Processing Validation
- **Setup:** Multiple 'Unprocessed' records; APIs respond quickly.
- **Expected:** All records processed in parallel; statuses updated.
- **Validation:** Processing time is minimized; all records reach 'Processed' or 'Error'.

---

## Notes
- QA should use both valid and invalid data for each step.
- Monitor logs in Azure Portal or Application Insights for validation.
- Use SQL queries to verify record status and barcode fields.
- Test timer trigger by adjusting schedule or using manual invocation.
