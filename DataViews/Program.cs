﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using OSIsoft.Data;
using OSIsoft.Data.Reflection;
using OSIsoft.DataViews;
using OSIsoft.DataViews.Contracts;
using OSIsoft.DataViews.Data;
using OSIsoft.DataViews.Resolved;
using OSIsoft.Identity;

namespace DataViews
{
    public static class Program
    {
        private static IConfiguration _configuration;
        private static Exception _toThrow;

        public static void Main()
        {
            MainAsync().GetAwaiter().GetResult();
        }

        public static async Task<bool> MainAsync(bool test = false)
        {
            ISdsMetadataService metadataService = null;
            IDataViewService dataviewService = null;

            #region settings

            // Sample Data Information
            string sampleTypeId1 = "Time_SampleType1";
            string sampleTypeId2 = "Time_SampleType2";
            string sampleStreamId1 = "dvTank2";
            string sampleStreamName1 = "Tank2";
            string sampleStreamDesc1 = "A stream to hold sample Pressure and Temperature events";
            string sampleStreamId2 = "dvTank100";
            string sampleStreamName2 = "Tank100";
            string sampleStreamDesc2 = "A stream to hold sample Pressure and Ambient Temperature events";
            string sampleFieldToConsolidateTo = "Temperature";
            string sampleFieldToConsolidate = "AmbientTemperature";
            string uomColumn1 = "Pressure";
            string uomColumn2 = "Temperature";
            string summaryField = "Pressure";
            SdsSummaryType summaryType1 = SdsSummaryType.Mean;
            SdsSummaryType summaryType2 = SdsSummaryType.Total;

            // Data View Information
            string sampleDataViewId = "DataView_Sample_DotNet";
            string sampleDataViewName = "DataView_Sample_Name_DotNet";
            string sampleDataViewDescription = "A Sample Description that describes that this Data View is just used for our sample.";
            string sampleQueryId = "stream";
            string sampleQueryString = "dvTank*";
            TimeSpan sampleRange = new (1, 0, 0); // range of one hour
            TimeSpan sampleInterval = new (0, 20, 0); // timespan of twenty minutes
            #endregion // settings

            try
            {
                #region configurationSettings

                _configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json")
                    .Build();

                string tenantId = _configuration["TenantId"];
                string namespaceId = _configuration["NamespaceId"];
                string resource = _configuration["Resource"];
                string clientId = _configuration["ClientId"];
                string clientSecret = _configuration["ClientSecret"];
                string apiVersion = _configuration["ApiVersion"];

                (_configuration as ConfigurationRoot).Dispose();
                Uri uriResource = new (resource);
                #endregion // configurationSettings

                // Step 1 - Authenticate Against ADH
                #region step1
                Console.WriteLine("Step 1: Authenticate Against ADH");

                SdsService sdsService = new (new Uri(resource), new AuthenticationHandler(uriResource, clientId, clientSecret));
                metadataService = sdsService.GetMetadataService(tenantId, namespaceId);
                ISdsDataService dataService = sdsService.GetDataService(tenantId, namespaceId);
                ISdsTableService tableService = sdsService.GetTableService(tenantId, namespaceId);

                VerbosityHeaderHandler verbosityHandler = new (true); // Creating a named variable since we access it later to change the verbosity

                DataViewServiceFactory dataviewServiceFactory = new (new Uri(resource), new AuthenticationHandler(uriResource, clientId, clientSecret), verbosityHandler);
                dataviewService = dataviewServiceFactory.GetDataViewService(tenantId, namespaceId);
                #endregion // step1

                // Step 2 - Create Types, Streams, and Data
                #region step2
                Console.WriteLine("Step 2: Create types, streams, and data");

                // create both sample types
                SdsType sampleType1 = SdsTypeBuilder.CreateSdsType<SampleType1>();
                sampleType1.Id = sampleTypeId1;
                sampleType1 = await metadataService.GetOrCreateTypeAsync(sampleType1).ConfigureAwait(false);

                SdsType sampleType2 = SdsTypeBuilder.CreateSdsType<SampleType2>();
                sampleType2.Id = sampleTypeId2;
                sampleType2 = await metadataService.GetOrCreateTypeAsync(sampleType2).ConfigureAwait(false);

                // create streams
                SdsStream sampleStream1 = new ()
                {
                    Id = sampleStreamId1,
                    Name = sampleStreamName1,
                    TypeId = sampleTypeId1,
                    Description = sampleStreamDesc1,
                };
                sampleStream1 = await metadataService.GetOrCreateStreamAsync(sampleStream1).ConfigureAwait(false);

                SdsStream sampleStream2 = new ()
                {
                    Id = sampleStreamId2,
                    Name = sampleStreamName2,
                    TypeId = sampleTypeId2,
                    Description = sampleStreamDesc2,
                };
                sampleStream2 = await metadataService.GetOrCreateStreamAsync(sampleStream2).ConfigureAwait(false);

                // create data
                DateTime sampleEndTime = DateTime.Now;
                DateTime sampleStartTime = sampleEndTime.AddSeconds(-sampleRange.TotalSeconds);

                List<SampleType1> sampleValues1 = new ();
                List<SampleType2> sampleValues2 = new ();

                Random rand = new ();
                double pressureUpperLimit = 100;
                double pressureLowerLimit = 0;
                double tempUpperLimit = 70;
                double tempLowerLimit = 50;
                int dataFrequency = 120; // does not need to match data view sampling interval

                for (double offsetSeconds = 0; offsetSeconds <= sampleRange.TotalSeconds; offsetSeconds += dataFrequency)
                {
                    SampleType1 val1 = new ()
                    {
                        Pressure = (rand.NextDouble() * (pressureUpperLimit - pressureLowerLimit)) + pressureLowerLimit,
                        Temperature = (rand.NextDouble() * (tempUpperLimit - tempLowerLimit)) + tempLowerLimit,
                        Time = sampleStartTime.AddSeconds(offsetSeconds),
                    };

                    SampleType2 val2 = new ()
                    {
                        Pressure = (rand.NextDouble() * (pressureUpperLimit - pressureLowerLimit)) + pressureLowerLimit,
                        AmbientTemperature = (rand.NextDouble() * (tempUpperLimit - tempLowerLimit)) + tempLowerLimit,
                        Time = sampleStartTime.AddSeconds(offsetSeconds),
                    };

                    sampleValues1.Add(val1);
                    sampleValues2.Add(val2);
                }

                // upload data
                await dataService.InsertValuesAsync(sampleStreamId1, sampleValues1).ConfigureAwait(false);
                await dataService.InsertValuesAsync(sampleStreamId2, sampleValues2).ConfigureAwait(false);

                #endregion //step2

                // Step 3 - Create a Data View 
                #region step3
                Console.WriteLine("Step 3: Create a Data View");
                DataView dataView = new ()
                {
                    Id = sampleDataViewId,
                    Name = sampleDataViewName,
                    Description = sampleDataViewDescription,
                };
                dataView = await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);
                #endregion //step3

                // Step 4 - Retrieve the Data View
                #region step4
                Console.WriteLine("Step 4: Retrieve the Data View");

                dataView = await dataviewService.GetDataViewAsync(sampleDataViewId).ConfigureAwait(false);
                Console.WriteLine();
                Console.WriteLine($"Retrieved Data View:");
                Console.WriteLine($"ID: {dataView.Id}, Name: {dataView.Name}, Description: {dataView.Description}");
                Console.WriteLine();
                #endregion //step4

                // Step 5 - Add a Query for Data Items
                #region step5
                Console.WriteLine("Step 5: Add a Query for Data Items");

                Query query = new ()
                {
                    Id = sampleQueryId,
                    Value = sampleQueryString,
                    Kind = DataItemResourceType.Stream,
                };

                dataView.Queries.Add(query);

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);
                #endregion //step5

                // Step 6 - View Items Found by the Query
                #region step6
                Console.WriteLine("Step 6: View Items Found by the Query");

                ResolvedItems<DataItem> resolvedDataItems = await dataviewService.GetDataItemsAsync(dataView.Id, query.Id).ConfigureAwait(false);
                ResolvedItems<DataItem> ineligibleDataItems = await dataviewService.GetIneligibleDataItemsAsync(dataView.Id, query.Id).ConfigureAwait(false);

                Console.WriteLine();
                Console.WriteLine($"Resolved data items for query {query.Id}:");
                foreach (DataItem dataItem in resolvedDataItems.Items)
                {
                    Console.WriteLine($"Name: {dataItem.Name}; ID: {dataItem.Id}");
                }

                Console.WriteLine();

                Console.WriteLine($"Ineligible data items for query {query.Id}:");
                foreach (DataItem dataItem in ineligibleDataItems.Items)
                {
                    Console.WriteLine($"Name: {dataItem.Name}; ID: {dataItem.Id}");
                }

                Console.WriteLine();

                #endregion //step6

                // Step 7 - View Fields Available to Include in the Data View
                #region step7
                Console.WriteLine("Step 7: View Fields Available to Include in the Data View");

                ResolvedItems<FieldSet> availableFields = await dataviewService.GetAvailableFieldSetsAsync(dataView.Id).ConfigureAwait(false);

                Console.WriteLine();
                Console.WriteLine($"Available fields for data view {dataView.Name}:");
                foreach (FieldSet fieldset in availableFields.Items)
                {
                    Console.WriteLine($"  QueryId: {fieldset.QueryId}");
                    Console.WriteLine($"  Data Fields: ");
                    foreach (Field datafield in fieldset.DataFields)
                    {
                        Console.Write($"    Label: {datafield.Label}");
                        Console.Write($", Source: {datafield.Source}");
                        foreach (string key in datafield.Keys)
                        {
                            Console.Write($", Key: {key}");
                        }

                        Console.Write('\n');
                    }
                }

                Console.WriteLine();
                #endregion //step7

                // Step 8 - Include Some of the Available Fields
                #region step8
                Console.WriteLine("Step 8: Include Some of the Available Fields");

                foreach (FieldSet field in availableFields.Items)
                {
                    dataView.DataFieldSets.Add(field);
                }

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);

                #endregion //step8

                // Step 9 - Group the Data View
                #region step9
                Console.WriteLine("Step 9: Group the Data View");

                dataView.GroupingFields.Add(new Field
                {
                    Source = FieldSource.Id,
                    Label = "{IdentifyingValue} {Key}",
                });

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);

                #endregion //step9

                // Step 10 - Identify Data Items
                #region step10
                Console.WriteLine("Step 10: Identify Data Items");

                foreach (FieldSet thisFieldSet in dataView.DataFieldSets.ToList())
                {
                    thisFieldSet.IdentifyingField = new Field
                    {
                        Source = FieldSource.Id,
                        Label = "{IdentifyingValue} {Key}",
                    };
                }

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);

                #endregion //step10

                // Step 11 - Consolidate Data Fields
                #region step11
                Console.WriteLine("Step 11: Consolidate Data Fields");

                FieldSet fieldSet = dataView.DataFieldSets.Single(a => a.QueryId == sampleQueryId);
                fieldSet.DataFields.Remove(fieldSet.DataFields.Single(a => a.Keys.Contains(sampleFieldToConsolidate)));

                Field consolidatingField = fieldSet.DataFields.Single(a => a.Keys.Contains(sampleFieldToConsolidateTo));
                consolidatingField.Keys.Add(sampleFieldToConsolidate);

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);

                #endregion //step11

                // Step 12 - Add Units of Measure Column
                #region step12
                Console.WriteLine("Step 12: Add Units of Measure Column");

                // Find the data fields for which we want to add a unit of measure column
                Field uomField1 = fieldSet.DataFields.Single(a => a.Keys.Contains(uomColumn1));
                Field uomField2 = fieldSet.DataFields.Single(a => a.Keys.Contains(uomColumn2));

                // Add the unit of measure column for these two data fields
                uomField1.IncludeUom = true;
                uomField2.IncludeUom = true;

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);
                #endregion //step 12

                // Step 13 - Add Summary Columns
                #region step13
                Console.WriteLine("Step 13: Add Summaries Columns");

                // Find the data field for which we want to add summary columns
                Field fieldToSummarize = fieldSet.DataFields.Single(a => a.Keys.Contains(summaryField));

                // Make two copies of the field to be summarized
                Field summaryField1 = fieldToSummarize.Clone();
                Field summaryField2 = fieldToSummarize.Clone();

                // Set the summary properties on the new fields and add them to the FieldSet
                summaryField1.SummaryDirection = SummaryDirection.Forward;
                summaryField1.SummaryType = summaryType1;

                summaryField2.SummaryDirection = SummaryDirection.Forward;
                summaryField2.SummaryType = summaryType2;

                fieldSet.DataFields.Add(summaryField1);
                fieldSet.DataFields.Add(summaryField2);

                await dataviewService.CreateOrUpdateDataViewAsync(dataView).ConfigureAwait(false);

                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime, sampleInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, sampleStartTime, sampleEndTime).ConfigureAwait(false);
                #endregion //step 13

                // Step 14 - Demonstrate accept-verbosity header usage
                #region step14
                Console.WriteLine("Step 14: Demonstrate accept-verbosity header usage");

                Console.WriteLine("Writing null values to the streams");

                // Keep the times in the future, guaranteeing no overlaps with existing data
                DateTime nullDataStartTime = DateTime.Now.AddHours(1);
                DateTime nullDataEndTime = nullDataStartTime.AddHours(1);
                TimeSpan nullDataInterval = new (1, 0, 0);

                // The first value is only a pressure, keeping temperature as null. Vice versa for the second
                List<SampleType1> defaultValues1 = new ()
                {
                    new SampleType1 // Temperature is null
                    {
                        Time = nullDataStartTime,
                        Pressure = 100,
                    },
                    new SampleType1 // Pressure is null
                    {
                        Time = nullDataEndTime,
                        Temperature = 50,
                    },
                };

                List<SampleType2> defaultValues2 = new ()
                {
                    new SampleType2 // AmbientTemperature is null
                    {
                        Time = nullDataStartTime,
                        Pressure = 100,
                    },
                    new SampleType2 // Pressure is null
                    {
                        Time = nullDataEndTime,
                        AmbientTemperature = 50,
                    },
                };

                await dataService.InsertValuesAsync(sampleStreamId1, defaultValues1).ConfigureAwait(false);
                await dataService.InsertValuesAsync(sampleStreamId2, defaultValues2).ConfigureAwait(false);

                Console.WriteLine("Data View results will include null values if the accept-verbosity header is not set to non-verbose.");
                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, nullDataStartTime, nullDataEndTime, nullDataInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, nullDataStartTime, nullDataEndTime).ConfigureAwait(false);

                Console.WriteLine("Changing the verbosity setting to non-verbose");
                verbosityHandler.Verbose = false;

                Console.WriteLine("Data View results will not include null values if the accept-verbosity header is set to non-verbose.");
                await OutputDataViewInterpolatedData(dataviewService, dataView.Id, nullDataStartTime, nullDataEndTime, nullDataInterval).ConfigureAwait(false);
                await OutputDataViewStoredData(dataviewService, dataView.Id, nullDataStartTime, nullDataEndTime).ConfigureAwait(false);

                #endregion //step 14
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                _toThrow = ex;
                throw;
            }
            finally
            {
                // Step 15 - Delete Sample Objects from ADH
                #region step15

                if (dataviewService != null)
                {
                    // Delete the data view
                    await RunInTryCatch(dataviewService.DeleteDataViewAsync, sampleDataViewId).ConfigureAwait(false);

                    Thread.Sleep(10); // slight rest here for consistency

                    // Check Delete
                    await RunInTryCatchExpectException(dataviewService.GetDataViewAsync, sampleDataViewId).ConfigureAwait(false);
                }

                if (metadataService != null)
                {
                    // Delete everything
                    Console.WriteLine("Step 14: Delete Sample Objects from ADH");
                    await RunInTryCatch(metadataService.DeleteStreamAsync, sampleStreamId1).ConfigureAwait(false);
                    await RunInTryCatch(metadataService.DeleteStreamAsync, sampleStreamId2).ConfigureAwait(false);
                    await RunInTryCatch(metadataService.DeleteTypeAsync, sampleTypeId1).ConfigureAwait(false);
                    await RunInTryCatch(metadataService.DeleteTypeAsync, sampleTypeId2).ConfigureAwait(false);

                    Thread.Sleep(10); // slight rest here for consistency

                    // Check Deletes
                    await RunInTryCatchExpectException(metadataService.GetStreamAsync, sampleStreamId1).ConfigureAwait(false);
                    await RunInTryCatchExpectException(metadataService.GetStreamAsync, sampleStreamId2).ConfigureAwait(false);
                    await RunInTryCatchExpectException(metadataService.GetTypeAsync, sampleTypeId1).ConfigureAwait(false);
                    await RunInTryCatchExpectException(metadataService.GetTypeAsync, sampleTypeId2).ConfigureAwait(false);
                }

                #endregion //step15
            }

            if (test && _toThrow != null)
                throw _toThrow;

            return _toThrow == null;
        }

        /// <summary>
        /// Use this to run a method that you don't want to stop the program if there is an exception
        /// </summary>
        /// <param name="methodToRun">The method to run.</param>
        /// <param name="value">The value to put into the method to run</param>
        private static async Task RunInTryCatch(Func<string, Task> methodToRun, string value)
        {
            try
            {
                await methodToRun(value).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Got error in {methodToRun.Method.Name} with value {value} but continued on: {ex.Message}");
                if (_toThrow == null)
                {
                    _toThrow = ex;
                }
            }
        }

        /// <summary>
        /// Use this to run a method that you don't want to stop the program if there is an exception, and you expect an exception
        /// </summary>
        /// <param name="methodToRun">The method to run.</param>
        /// <param name="value">The value to put into the method to run</param>
        private static async Task RunInTryCatchExpectException(Func<string, Task> methodToRun, string value)
        {
            try
            {
                await methodToRun(value).ConfigureAwait(false);

                Console.WriteLine($"Got error.  Expected {methodToRun.Method.Name} with value {value} to throw an error but it did not.");
            }
            catch
            {
            }
        }

        /// <summary>
        /// Helper function to output to the console interpolated data for a data view
        /// </summary>
        /// <param name="dataviewService">The active IDataViewService object being used</param>
        /// <param name="dataViewId">The ID of the Data View to be outputted</param>
        /// <param name="startTime">The start index of the output range</param>
        /// <param name="endTime">The end index of the output range</param>
        /// <param name="interval">The time step interval to use for the output range</param>
        /// <returns>a Task object to await the data call</returns>
        private static async Task OutputDataViewInterpolatedData(IDataViewService dataviewService, string dataViewId, DateTime startTime, DateTime endTime, TimeSpan interval)
        {
            IAsyncEnumerable<string> values = dataviewService.GetDataInterpolatedAsync(
                    dataViewId,
                    OutputFormat.Default,
                    startTime.ToUniversalTime().ToString(CultureInfo.InvariantCulture),
                    endTime.ToUniversalTime().ToString(CultureInfo.InvariantCulture),
                    interval.ToString(),
                    null,
                    CacheBehavior.Refresh,
                    default);

            await foreach (string value in values)
            {
                Console.WriteLine(value);
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Helper function to output to the console stored data for a data view
        /// </summary>
        /// <param name="dataviewService">The active IDataViewService object being used</param>
        /// <param name="dataViewId">The ID of the Data View to be outputted</param>
        /// <param name="startTime">The start index of the output range</param>
        /// <param name="endTime">The end index of the output range</param>
        /// <returns>a Task object to await the data call</returns>
        private static async Task OutputDataViewStoredData(IDataViewService dataviewService, string dataViewId, DateTime startTime, DateTime endTime)
        {
            IAsyncEnumerable<string> values = dataviewService.GetDataStoredAsync(
                    dataViewId,
                    OutputFormat.Default,
                    startTime.ToUniversalTime().ToString(CultureInfo.InvariantCulture),
                    endTime.ToUniversalTime().ToString(CultureInfo.InvariantCulture),
                    null,
                    CacheBehavior.Refresh,
                    default);

            await foreach (string value in values)
            {
                Console.WriteLine(value);
            }

            Console.WriteLine();
        }
    }
}
