# AltaworxThingSpaceAWSGetRatePlan Lambda Analysis

## Overview
The `AltaworxThingSpaceAWSGetRatePlan` Lambda function is designed to synchronize rate plans from the ThingSpace API to the central database. This document analyzes the function's behavior, triggers, error handling, and data processing logic.

## Key Questions & Answers

### 1. What triggers this Lambda—manual start or scheduled job?

**Answer: Both manual and scheduled execution are supported**

- **Manual Trigger**: The Lambda can be invoked manually through AWS Console, CLI, or API calls
- **Scheduled Trigger**: Typically configured using AWS EventBridge (CloudWatch Events) with cron or rate expressions
- **Function Signature**: Uses `ILambdaContext` parameter, indicating it can accept various trigger types
- **No Event Parameters**: The `FunctionHandler` doesn't expect specific event data, making it suitable for scheduled execution

### 2. What is the schedule/frequency for rate plan sync?

**Answer: Schedule is configured externally via EventBridge**

- **Configuration Location**: The schedule is not defined in the Lambda code itself but configured in AWS EventBridge Scheduler
- **Typical Patterns**: 
  - Daily sync: `cron(0 12 * * ? *)` (daily at noon UTC)
  - Hourly sync: `rate(1 hour)`
  - Custom intervals based on business requirements
- **Environment Variables**: The provided configuration shows `BatchSize: 250`, suggesting the system processes rate plans in batches
- **No Built-in Scheduling**: The Lambda relies on external triggers for timing

### 3. What happens if API authentication fails—is retry attempted or logged only?

**Answer: Logged only, no automatic retry mechanism**

#### Authentication Flow:
1. **Access Token Retrieval** (`GetAccessToken`)
   - If fails: Returns `null`
   - No retry logic implemented
   
2. **Session Token Retrieval** (`GetSessionToken`)
   - If fails: Returns `null`
   - No retry logic implemented

3. **Rate Plan API Call** (`GetLatestRatePlans`)
   - Success: Processes and returns rate plans
   - Failure: Logs error and returns `null`

#### Error Handling Details:
```csharp
// Line 123-134 in AltaworxThingSpaceAWSGetRatePlan.cs
if (response.IsSuccessStatusCode)
{
    string responseBody = response.Content.ReadAsStringAsync().Result;
    return JsonConvert.DeserializeObject<List<ThingSpaceServicePlan>>(responseBody);
}
else
{
    string responseBody = response.Content.ReadAsStringAsync().Result;
    LogInfo(context, "Response Error", responseBody);
    return null;
}
```

#### Exception Handling:
```csharp
// Line 40-43 in AltaworxThingSpaceAWSGetRatePlan.cs
catch (Exception ex)
{
    LogInfo(keysysContext, "EXCEPTION", ex.Message);
}
```

**Key Points:**
- **No Retry Logic**: Authentication failures result in immediate termination
- **Logging Only**: Errors are logged but not automatically retried
- **Graceful Degradation**: Function continues processing other service providers if one fails
- **Manual Intervention Required**: Failed authentications require manual investigation

### 4. How are new vs updated rate plans differentiated in `usp_ThingSpace_Carrier_Rate_Plan_Process`?

**Answer: Uses SQL MERGE statement to handle both scenarios automatically**

#### Stored Procedure Logic:

```sql
MERGE [dbo].[JasperCarrierRatePlan] AS target  
USING (  
    SELECT DISTINCT  
        [RatePlanCode]  
        ,MAX([RatePlanName]) AS [RatePlanName]  
        ,MAX([UsageLimitKb]) AS [UsageLimitKb]  
        ,MAX([CreatedBy]) AS [CreatedBy]  
        ,MAX([CreatedDate]) AS [CreatedDate]  
        ,[ServiceProviderId]  
    FROM [dbo].[ThingSpaceCarrierRatePlanStaging]  
    GROUP BY [RatePlanCode], [ServiceProviderId]  
) AS source ON target.[RatePlanCode] = source.[RatePlanCode] 
                AND target.[ServiceProviderId] = source.[ServiceProviderId]
```

#### Differentiation Logic:

**WHEN MATCHED (Updates):**
- **Criteria**: Existing record found with same `RatePlanCode` AND `ServiceProviderId`
- **Action**: Updates existing record
- **Fields Updated**:
  - `PlanMB = source.UsageLimitKb / 1024`
  - `ModifiedBy = source.CreatedBy`
  - `ModifiedDate = source.CreatedDate`

**WHEN NOT MATCHED (Inserts):**
- **Criteria**: No existing record with same `RatePlanCode` AND `ServiceProviderId`
- **Action**: Inserts new record
- **Fields Inserted**:
  - `RatePlanCode`
  - `BaseRate = 0` (default)
  - `PlanMB = source.UsageLimitKb / 1024`
  - `OverageRateCost = 0` (default)
  - `3GSurcharge = 0` (default)
  - `CreatedBy = source.CreatedBy`
  - `CreatedDate = source.CreatedDate`
  - `IsDeleted = 0`
  - `RateChargeAmt = 0.0`
  - `IsActive = 1`
  - `RatePlanShortName = source.RatePlanCode`
  - `JasperRatePlanId = NULL`
  - `ServiceProviderId`

## Process Flow Summary

### Step 1: Initialization
- Clear staging tables (`ThingSpaceCarrierRatePlanStaging`)
- Get list of service providers with ThingSpace integration

### Step 2: Per Service Provider Processing
- Retrieve authentication information
- Get access token from ThingSpace API
- Get session token using access token
- Fetch latest rate plans from API
- Convert to DataTable format
- Bulk insert into staging table

### Step 3: Data Consolidation
- Execute `usp_ThingSpace_Carrier_Rate_Plan_Process`
- Merge staging data into production table
- Handle both new and updated rate plans automatically

### Step 4: Cleanup
- Clean up resources and connections
- Log completion status

## Configuration Details

From the provided environment variables:
- **BatchSize**: 250 records per batch
- **Database**: AltaWorxCentral_TEST (Test environment)
- **Environment**: Test
- **Connection Timeout**: 90 seconds

## Recommendations

1. **Add Retry Logic**: Implement exponential backoff for API authentication failures
2. **Monitoring**: Add CloudWatch alarms for authentication failures
3. **Dead Letter Queue**: Configure DLQ for failed executions
4. **Batch Processing**: Consider implementing pagination for large rate plan sets
5. **Error Notifications**: Send alerts when authentication consistently fails

## Technical Notes

- **Lambda Runtime**: .NET with System.Text.Json serialization
- **Database**: SQL Server with bulk copy operations
- **API Security**: Uses TLS 1.1+ protocols
- **Authentication**: Two-step process (access token + session token)
- **Data Processing**: Bulk operations for performance optimization