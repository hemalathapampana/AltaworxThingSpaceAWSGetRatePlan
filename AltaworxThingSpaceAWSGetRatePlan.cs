using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Altaworx.AWS.Core;
using Amazon.Lambda.Core;
using Newtonsoft.Json;
using System.Net;
using Altaworx.AWS.Core.Models;
using Amop.Core.Models;
using Altaworx.ThingSpace.Core;
using Altaworx.ThingSpace.Core.Models;
using Amop.Core.Logger;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace AltaworxThingSpaceAWSGetRatePlan
{
    public class Function : AwsFunctionBase
    {
        /// <summary>
        /// Syncs Service Plans for ThingSpace
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public void FunctionHandler(ILambdaContext context)
        {
            KeySysLambdaContext keysysContext = null;
            try
            {
                keysysContext = BaseFunctionHandler(context);

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                ProcessServiceProviders(keysysContext);
            }
            catch (Exception ex)
            {
                LogInfo(keysysContext, "EXCEPTION", ex.Message);
            }

            CleanUp(keysysContext);
        }

        private void ProcessServiceProviders(KeySysLambdaContext context)
        {
            ClearStagingTables(context);

            int currentServiceProviderId = ServiceProviderCommon.GetNextServiceProviderId(context.CentralDbConnectionString, IntegrationType.ThingSpace, 0);
            while (currentServiceProviderId > 0)
            {
                ProcessRatePlans(context, currentServiceProviderId);

                currentServiceProviderId = ServiceProviderCommon.GetNextServiceProviderId(context.CentralDbConnectionString, IntegrationType.ThingSpace, currentServiceProviderId);
            }

            // merge rate plans
            UpdateRatePlans(context);
        }

        private void UpdateRatePlans(KeySysLambdaContext context)
        {
            using (var con = new SqlConnection(context.CentralDbConnectionString))
            {
                using (var cmd = new SqlCommand("usp_ThingSpace_Carrier_Rate_Plan_Process", con)
                {
                    CommandType = CommandType.StoredProcedure
                })
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    con.Close();
                }
            }
        }

        private void ProcessRatePlans(KeySysLambdaContext context, int serviceProviderId)
        {
            ThingSpaceAuthentication thingSpaceAuth = ThingSpaceCommon.GetThingspaceAuthenticationInformation(context.CentralDbConnectionString, serviceProviderId);
            var accessToken = ThingSpaceCommon.GetAccessToken(thingSpaceAuth);
            var sessionToken = ThingSpaceCommon.GetSessionToken(thingSpaceAuth, accessToken);

            List<ThingSpaceServicePlan> ratePlans = GetLatestRatePlans(context, thingSpaceAuth, accessToken, sessionToken);
            if (ratePlans != null && ratePlans.Count > 0)
            {
                // convert to data table
                DataTable table = new DataTable();
                table.Columns.Add("RatePlanCode");
                table.Columns.Add("RatePlanName");
                table.Columns.Add("UsageLimitKb", typeof(Int64));
                table.Columns.Add("ServiceProviderId", typeof(int));
                table.Columns.Add("CreatedBy");
                table.Columns.Add("CreatedDate");

                foreach (var ratePlan in ratePlans)
                {
                    var dr = AddToDataRow(table, ratePlan, serviceProviderId);
                    table.Rows.Add(dr);
                }

                // bulk load
                LogInfo(context, "STATUS", "Rate Plan SQL Bulk Copy Start");
                SqlBulkCopy(context, context.CentralDbConnectionString, table, "ThingSpaceCarrierRatePlanStaging");
            }
            ;
        }

        private List<ThingSpaceServicePlan> GetLatestRatePlans(KeySysLambdaContext context, ThingSpaceAuthentication thingSpaceAuth, ThingSpaceTokenResponse accessToken, ThingSpaceLoginResponse sessionToken)
        {
            using (HttpClient client = new HttpClient(new LambdaLoggingHandler()))
            {
                var baseUrl = thingSpaceAuth.BaseUrl.TrimEnd('/');
                client.BaseAddress = new Uri(baseUrl + $"/api/m2m/v1/plans/{thingSpaceAuth.AccountNumber}");
                LogInfo(context, "Endpoint", client.BaseAddress);
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken.Access_Token);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                client.DefaultRequestHeaders.Add("VZ-M2M-Token", sessionToken.sessionToken);

                HttpResponseMessage response = client.GetAsync(client.BaseAddress).Result;
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
            }
        }

        private DataRow AddToDataRow(DataTable table, ThingSpaceServicePlan ratePlan, int serviceProviderId)
        {
            var dr = table.NewRow();
            dr[0] = ratePlan.code;
            dr[1] = ratePlan.name;
            dr[2] = ratePlan.sizeKb;
            dr[3] = serviceProviderId;
            dr[4] = "AWS Lambda - Get Rate Plan Service";
            dr[5] = DateTime.UtcNow;
            return dr;
        }

        private void ClearStagingTables(KeySysLambdaContext context)
        {
            using (var con = new SqlConnection(context.CentralDbConnectionString))
            {
                using (var cmd = new SqlCommand("TRUNCATE TABLE ThingSpaceCarrierRatePlanStaging", con)
                {
                    CommandType = CommandType.Text
                })
                {
                    con.Open();
                    cmd.ExecuteNonQuery();
                    con.Close();
                }
            }
        }
    }
}
