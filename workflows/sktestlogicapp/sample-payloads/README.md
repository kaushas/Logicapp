# D365 FinOps Sample Payloads

This directory contains sample JSON payloads that simulate messages from D365 Finance and Operations.

## Message Types

### 1. Customer Messages
**Queue:** `sbq-d365finops-customer-upserts`  
**Output Topic:** `sbt-d365-finops-CustomerChanged`  
**Template:** `customerToCanonical.liquid`

**Files:**
- `d365-customer-create.json` - New customer creation
- `d365-customer-update.json` - Existing customer update

### 2. Vendor Messages
**Queue:** `sbq-d365finops-vendor-upserts`  
**Output Topic:** `sbt-d365-finops-VendorChanged`  
**Template:** `vendorToCanonical.liquid`

**Files:**
- `d365-vendor-create.json` - New vendor creation

### 3. Item Messages
**Queue:** `sbq-d365finops-item-upserts`  
**Output Topic:** `sbt-d365Ffinops-ItemChanged`  
**Template:** `itemToCanonical.liquid` *(needs to be created)*

**Files:**
- `d365-item-create.json` - New item/product creation

### 4. Item Unit Conversion Messages
**Queue:** `sbq-d365finops-itemunitconversion-upserts`  
**Output Topic:** `sbt-d365-finops-UnitOfMeasureChanged`  
**Template:** `itemUnitConversionToCanonical.liquid` *(needs to be created)*

**Files:**
- `d365-itemunitconversion-create.json` - Unit of measure conversion

### 5. Customer Address Messages
**Queue:** `sbq-d365finops-customeraddress-upserts`  
**Output Topic:** `sbt-d365-finops-BusinessAddressChanged`  
**Template:** `customerAddressToCanonical.liquid`

**Files:**
- `d365-customeraddress-create.json` - Customer address creation

### 6. Vendor Address Messages
**Queue:** `sbq-d365finops-vendoraddress-upserts`  
**Output Topic:** `sbt-d365-finops-BusinessAddressChanged`  
**Template:** `vendorAddressToCanonical.liquid`

**Files:**
- `d365-vendoraddress-create.json` - Vendor address creation

## Common Fields

All messages include:
- `correlationId`: For end-to-end tracing
- `messageId`: Unique message identifier
- `eventType`: Type of event (e.g., CustomerCreated, VendorUpdated)
- `sourceSystem`: Always "D365FinOps"
- `sourceEntity`: D365 table name (CustTable, VendTable, etc.)
- `operationType`: Insert, Update, or Delete
- `modifiedDateTime`: Timestamp of the change
- `modifiedBy`: User who made the change

## Testing Workflow

### Local Testing with HTTP Trigger

```powershell
# Test customer creation
$customerJson = Get-Content "sample-payloads/d365-customer-create.json" -Raw
Invoke-RestMethod -Uri "http://localhost:7071/api/process-message" `
  -Method POST `
  -Body $customerJson `
  -ContentType "application/json" `
  -Headers @{
    "x-correlation-id" = "test-corr-001"
    "x-source-topic" = "customerToCanonical"
  }
```

### Azure Service Bus Testing

```powershell
# Send to customer queue
az servicebus queue message send `
  --resource-group <rg-name> `
  --namespace-name <sb-namespace> `
  --queue-name sbq-d365finops-customer-upserts `
  --body (Get-Content "sample-payloads/d365-customer-create.json" -Raw)

# Send to vendor queue
az servicebus queue message send `
  --resource-group <rg-name> `
  --namespace-name <sb-namespace> `
  --queue-name sbq-d365finops-vendor-upserts `
  --body (Get-Content "sample-payloads/d365-vendor-create.json" -Raw)

# Send to item queue
az servicebus queue message send `
  --resource-group <rg-name> `
  --namespace-name <sb-namespace> `
  --queue-name sbq-d365finops-item-upserts `
  --body (Get-Content "sample-payloads/d365-item-create.json" -Raw)
```

## Expected Transformation

The Liquid templates transform these payloads into canonical event format:

```json
{
  "eventType": "CustomerChanged",
  "subject": "customer/d365",
  "source": "D365FinOps",
  "eventTime": "2026-03-25T14:30:00Z",
  "schemaVersion": "1.0",
  "contentType": "application/json",
  "correlationId": "d365-cust-001-20260325-143000",
  "data": { ... original payload ... }
}
```

## Queue → Topic Mapping

| D365 Queue | Canonical EventType | Output Topic |
|------------|---------------------|--------------|
| sbq-d365finops-customer-upserts | CustomerChanged | sbt-d365-finops-CustomerChanged |
| sbq-d365finops-vendor-upserts | VendorChanged | sbt-d365-finops-VendorChanged |
| sbq-d365finops-item-upserts | ItemChanged | sbt-d365Ffinops-ItemChanged |
| sbq-d365finops-itemunitconversion-upserts | UnitOfMeasureChanged | sbt-d365-finops-UnitOfMeasureChanged |
| sbq-d365finops-customeraddress-upserts | BusinessAddressChanged | sbt-d365-finops-BusinessAddressChanged |
| sbq-d365finops-vendoraddress-upserts | BusinessAddressChanged | sbt-d365-finops-BusinessAddressChanged |

