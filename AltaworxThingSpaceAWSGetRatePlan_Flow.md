# AltaworxThingSpaceAWSGetRatePlan Lambda Function Flow

## Overview
This lambda function syncs Service Plans (Rate Plans) for ThingSpace by retrieving rate plan data from the ThingSpace API and storing it in the database for processing.

## 1. High-Level Flow

### Sequential Function Flow:
1. **FunctionHandler** (Entry Point)
2. **BaseFunctionHandler** (from AwsFunctionBase)
3. **ProcessServiceProviders**
4. **ClearStagingTables**
5. **ServiceProviderCommon.GetNextServiceProviderId** (External)
6. **ProcessRatePlans** (Loop for each Service Provider)
   - **ThingSpaceCommon.GetThingspaceAuthenticationInformation**
   - **ThingSpaceCommon.GetAccessToken**
   - **ThingSpaceCommon.GetSessionToken**
   - **GetLatestRatePlans**
   - **AddToDataRow** (for each rate plan)
   - **SqlBulkCopy** (from AwsFunctionBase)
7. **UpdateRatePlans**
8. **CleanUp** (from AwsFunctionBase)

---

## 2. Low-Level Flow

### 2.1 FunctionHandler (Entry Point)
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:30-46`

**Purpose**: Main entry point for the Lambda function that orchestrates the entire rate plan sync process.

**What Happens**:
- Initializes `KeySysLambdaContext` by calling `BaseFunctionHandler(context)`
- Sets up security protocols (TLS 1.3, 1.2, 1.1, TLS)
- Calls `ProcessServiceProviders(keysysContext)` to start the main processing
- Handles any exceptions by logging them with `LogInfo(keysysContext, "EXCEPTION", ex.Message)`
- Always calls `CleanUp(keysysContext)` in finally block to clean up resources

### 2.2 BaseFunctionHandler (from AwsFunctionBase)
**Location**: `AwsFunctionBase.cs:40-45`

**Purpose**: Initializes the Lambda context with necessary configuration and settings.

**What Happens**:
- Creates a new `KeySysLambdaContext` instance with the provided `ILambdaContext`
- Sets up logging, database connections, and other core services
- Returns the initialized context for use throughout the function

### 2.3 ProcessServiceProviders
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:48-62`

**Purpose**: Main orchestration method that processes rate plans for all ThingSpace service providers.

**What Happens**:
- Calls `ClearStagingTables(context)` to truncate staging tables
- Gets the first service provider ID using `ServiceProviderCommon.GetNextServiceProviderId()`
- Enters a while loop to process each service provider:
  - Calls `ProcessRatePlans(context, currentServiceProviderId)` for each provider
  - Gets the next service provider ID to continue the loop
- After processing all providers, calls `UpdateRatePlans(context)` to merge data

### 2.4 ClearStagingTables
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:150-164`

**Purpose**: Clears the staging table before processing new rate plan data.

**What Happens**:
- Opens SQL connection using `context.CentralDbConnectionString`
- Executes `TRUNCATE TABLE ThingSpaceCarrierRatePlanStaging` command
- Ensures clean state before importing new rate plan data

### 2.5 ProcessRatePlans
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:80-109`

**Purpose**: Processes rate plans for a specific service provider by calling ThingSpace API and storing data.

**What Happens**:
- **Authentication Setup**:
  - Calls `ThingSpaceCommon.GetThingspaceAuthenticationInformation()` to get auth details
  - Calls `ThingSpaceCommon.GetAccessToken(thingSpaceAuth)` to get OAuth access token
  - Calls `ThingSpaceCommon.GetSessionToken(thingSpaceAuth, accessToken)` to get session token

- **Data Retrieval**:
  - Calls `GetLatestRatePlans()` to fetch rate plans from ThingSpace API
  - If rate plans are retrieved successfully, creates a DataTable with columns:
    - RatePlanCode, RatePlanName, UsageLimitKb, ServiceProviderId, CreatedBy, CreatedDate

- **Data Processing**:
  - Loops through each rate plan and calls `AddToDataRow()` to populate DataTable rows
  - Calls `SqlBulkCopy()` to bulk insert data into `ThingSpaceCarrierRatePlanStaging` table

### 2.6 ThingSpaceCommon.GetThingspaceAuthenticationInformation
**Location**: `ThingSpaceCommon.cs:190-231`

**Purpose**: Retrieves ThingSpace authentication credentials from database for a specific service provider.

**What Happens**:
- Executes stored procedure `usp_ThingSpace_Get_AuthenticationByProviderId`
- Returns `ThingSpaceAuthentication` object containing:
  - BaseUrl, ClientId, ClientSecret, AuthTokenUrl, AuthUrl
  - Username, Password, AccountNumber, WriteIsEnabled flag

### 2.7 ThingSpaceCommon.GetAccessToken
**Location**: `ThingSpaceCommon.cs:33-59`

**Purpose**: Obtains OAuth access token from ThingSpace API using client credentials.

**What Happens**:
- Sets up HttpClient with ThingSpace base URL
- Creates Basic Authentication header using Base64-encoded ClientId:ClientSecret
- Sends POST request to `AuthTokenUrl` with `grant_type=client_credentials`
- Returns `ThingSpaceTokenResponse` containing access token and expiration details

### 2.8 ThingSpaceCommon.GetSessionToken
**Location**: `ThingSpaceCommon.cs:134-158`

**Purpose**: Obtains session token using username/password authentication.

**What Happens**:
- Sets up HttpClient with Bearer token from access token
- Base64 decodes the stored password
- Sends POST request to `AuthUrl` with JSON payload containing username and password
- Returns `ThingSpaceLoginResponse` containing session token

### 2.9 GetLatestRatePlans
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:111-136`

**Purpose**: Retrieves rate plans from ThingSpace API for the specific account.

**What Happens**:
- Sets up HttpClient with ThingSpace API endpoint: `/api/m2m/v1/plans/{accountNumber}`
- Adds required headers:
  - Authorization: Bearer {accessToken}
  - Accept: application/json
  - VZ-M2M-Token: {sessionToken}
- Sends GET request to retrieve rate plans
- Deserializes response into `List<ThingSpaceServicePlan>`
- Logs endpoint and any response errors

### 2.10 AddToDataRow
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:138-148`

**Purpose**: Converts a ThingSpace service plan object into a DataTable row.

**What Happens**:
- Creates new DataRow from the DataTable
- Maps ThingSpace service plan fields to DataTable columns:
  - `ratePlan.code` → RatePlanCode
  - `ratePlan.name` → RatePlanName
  - `ratePlan.sizeKb` → UsageLimitKb
  - `serviceProviderId` → ServiceProviderId
  - "AWS Lambda - Get Rate Plan Service" → CreatedBy
  - `DateTime.UtcNow` → CreatedDate

### 2.11 SqlBulkCopy (from AwsFunctionBase)
**Location**: `AwsFunctionBase.cs:286-330`

**Purpose**: Performs high-performance bulk insert of rate plan data into staging table.

**What Happens**:
- Opens SQL connection using provided connection string
- Creates `SqlBulkCopy` instance with destination table `ThingSpaceCarrierRatePlanStaging`
- Sets timeout and batch size from configuration constants
- Executes `WriteToServer(table)` to bulk insert all DataTable rows
- Handles SQL exceptions, invalid operation exceptions, and general exceptions

### 2.12 UpdateRatePlans
**Location**: `AltaworxThingSpaceAWSGetRatePlan.cs:64-78`

**Purpose**: Processes and merges staged rate plan data into final tables.

**What Happens**:
- Opens SQL connection using `context.CentralDbConnectionString`
- Executes stored procedure `usp_ThingSpace_Carrier_Rate_Plan_Process`
- This stored procedure handles the business logic for:
  - Comparing staged data with existing rate plans
  - Inserting new rate plans
  - Updating modified rate plans
  - Handling data validation and cleanup

### 2.13 CleanUp (from AwsFunctionBase)
**Location**: `AwsFunctionBase.cs:53-56`

**Purpose**: Performs cleanup operations to release resources and finalize logging.

**What Happens**:
- Calls `context.CleanUp()` on the KeySysLambdaContext
- Ensures proper disposal of database connections, HTTP clients, and other resources
- Finalizes logging entries and flushes log buffers

---

## 3. Data Flow Summary

1. **Input**: Lambda execution context
2. **Authentication**: Retrieves ThingSpace API credentials from database
3. **API Calls**: 
   - Gets OAuth access token
   - Gets session token
   - Retrieves rate plans from ThingSpace API
4. **Data Processing**: 
   - Transforms API response into database-compatible format
   - Bulk inserts into staging table
5. **Data Merge**: Stored procedure processes staged data into final tables
6. **Output**: Updated rate plan data in database

## 4. Key Dependencies

- **AwsFunctionBase**: Provides base functionality for logging, database operations, and cleanup
- **ThingSpaceCommon**: Handles all ThingSpace API authentication and communication
- **ServiceProviderCommon**: Manages service provider iteration logic
- **Database Stored Procedures**: 
  - `usp_ThingSpace_Get_AuthenticationByProviderId`
  - `usp_ThingSpace_Carrier_Rate_Plan_Process`

## 5. Error Handling

- All major operations are wrapped in try-catch blocks
- SQL exceptions are specifically handled with detailed logging
- HTTP client errors are logged with response details
- Context cleanup is guaranteed through finally blocks
- Failed API calls return null and are handled gracefully