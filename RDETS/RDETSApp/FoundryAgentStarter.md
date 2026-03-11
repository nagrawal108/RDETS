# Microsoft Foundry Agentic AI Agent - Starter Workflow & Template

## Agent Purpose
Monitor API errors and failed transactions, analyze root causes, and attempt automated fixes. Escalate unresolved issues.

---

## Requirements Template

**Agent Goals:**
- Detect API call errors (e.g., HTTP 4xx/5xx, timeout, invalid response).
- Identify failed transactions in database (status = 'Error').
- Analyze error type and root cause (missing data, retryable, permanent).
- Attempt automated fixes (retry, update, escalate).
- Log actions and notify for manual intervention if needed.

**Data Sources:**
- SQL Database (transaction records)
- API endpoints (Dubai DET Sales APIs)
- Application logs (optional)

**Fix Strategies:**
- Retry failed API calls (with exponential backoff)
- Update missing or invalid data
- Escalate to support if fix not possible

**Escalation Logic:**
- If fix fails after N attempts, notify support team
- Log error details and actions taken

---

## Foundry Starter Workflow (Pseudocode)

```
Agent Workflow:
1. On schedule or event trigger:
   a. Query database for records with status = 'Error'
   b. For each failed transaction:
      i. Analyze error details
      ii. Decide fix strategy (retry, update, escalate)
      iii. Attempt fix
      iv. Update status and log outcome
      v. If fix fails, escalate
2. Monitor API logs for new errors
3. Repeat
```

---

## Sample Agent Logic (Python-like pseudocode)

```
def agent_main():
    error_records = db.query("SELECT * FROM transactions WHERE status = 'Error'")
    for record in error_records:
        error_type = analyze_error(record)
        if error_type == 'retryable':
            success = retry_api_call(record)
            if success:
                db.update_status(record.id, 'Processed')
            else:
                escalate(record)
        elif error_type == 'missing_data':
            fixed = fix_data(record)
            if fixed:
                retry_api_call(record)
            else:
                escalate(record)
        else:
            escalate(record)
        log_action(record)
```

---

## Hourly Reporting Logic (Sample)

**Agent Step:**
- Every hour, query the database and generate a summary report:

```
def generate_hourly_report():
    orderid_count = db.query("SELECT COUNT(*) FROM transactions WHERE DET_OrderID IS NOT NULL")
    barcode_count = db.query("SELECT COUNT(*) FROM transactions WHERE dub_barcode IS NOT NULL")
    error_count = db.query("SELECT COUNT(*) FROM transactions WHERE status = 'Error'")
    report = f"Hourly Report:\nOrderIDs: {orderid_count}\nBarcodes: {barcode_count}\nErrors: {error_count}"
    send_report(report)  # send via email, dashboard, or log
```

**Integration:**
- Add this step to your agent workflow.
- Use Foundry’s scheduling and notification modules to automate report delivery.

---

## Example Report Output

```
Hourly Report:
OrderIDs: 120
Barcodes: 110
Errors: 5
```

---

## Customization
- Adjust queries for your schema and business logic.
- Format report as needed (CSV, HTML, dashboard, etc).
- Schedule report delivery using Foundry orchestration.

---

## Foundry Integration Notes
- Use Foundry connectors for database and API access
- Use Foundry orchestration for scheduling and triggers
- Use LLM modules for error analysis and fix decision
- Configure logging and notification modules for escalation

---

## Next Steps
- Customize workflow for your APIs and database schema
- Implement agent logic in Foundry’s orchestration environment
- Test with sample error records and monitor agent actions
