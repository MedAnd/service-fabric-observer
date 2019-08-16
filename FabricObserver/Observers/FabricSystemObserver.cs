﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using FabricObserver.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Fabric;
using System.Fabric.Health;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FabricObserver
{
    // This observer monitors all Fabric system service processes across various resource usage metrics. 
    // It will signal Warnings or Errors based on settings supplied in Settings.xml.
    // The output (a local file) is used by the API service and the HTML frontend (https://[domain:[port]]/api/ObserverManager).
    public class FabricSystemObserver : ObserverBase
    {
        private readonly List<string> processWatchList = new List<string> { "Fabric",
                                                                            "FabricApplicationGateway",
                                                                            "FabricCAS",
                                                                            "FabricDCA",
                                                                            "FabricDnsService",
                                                                            "FabricGateway",
                                                                            "FabricHost",
                                                                            "FabricIS",
                                                                            "FabricRM",
                                                                            "FabricUS" };

        // amount of time, in seconds, it took this observer to complete run run...
        private TimeSpan runtime = TimeSpan.MinValue;
        private Stopwatch stopWatch;

        // Health Report data container - For use in analysis to deterWarne health state...
        private List<FabricResourceUsageData<int>> allCpuData;
        private List<FabricResourceUsageData<long>> allMemData;
        private List<FabricResourceUsageData<float>> allAppDiskReadsData;
        private List<FabricResourceUsageData<float>> allAppDiskWritesData;

        // Windows only... (EventLog)...
        private List<EventRecord> evtRecordList = null;
        private WindowsPerfCounters perfCounters = null;
        private DiskUsage diskUsage = null;
        private bool monitorWinEventLog = false;
        private int unhealthyNodesErrorThreshold = 0;
        private int unhealthyNodesWarnThreshold = 0;

        public int CpuErrorUsageThresholdPct { get; set; } = 90;
        public int MemErrorUsageThresholdMB { get; set; } = 15000;
        public int DiskErrorIOReadsThresholdMS { get; set; } = 0;
        public int DiskErrorIOWritesThresholdMS { get; set; } = 0;
        public int TotalActivePortCount { get; set; } = 0;
        public int TotalActiveEphemeralPortCount { get; set; } = 0;
        public int PortCountWarning { get; set; } = 1000;
        public int PortCountError { get; set; } = 5000;
        public int CpuWarnUsageThresholdPct { get; set; } = 70;
        public int MemWarnUsageThresholdMB { get; set; } = 14000;
        public int DiskWarnIOReadsThresholdMS { get; set; } = 20000;
        public int DiskWarnIOWritesThresholdMS { get; set; } = 20000;
        public string ErorrOrWarningKind { get; set; } = null;

        public FabricSystemObserver() : base(ObserverConstants.FabricSystemObserverName) { }

        bool Initialize()
        {
            if (this.stopWatch == null)
            {
                this.stopWatch = new Stopwatch();
            }

            Token.ThrowIfCancellationRequested();

            this.stopWatch.Start();

            SetThresholdsFromConfiguration();

            if (this.allMemData == null)
            {
                this.allMemData = new List<FabricResourceUsageData<long>>
                { 
                    // Mem data...
                    new FabricResourceUsageData<long>("Fabric"),
                    new FabricResourceUsageData<long>("FabricApplicationGateway"),
                    new FabricResourceUsageData<long>("FabricCAS"),
                    new FabricResourceUsageData<long>("FabricDCA"),
                    new FabricResourceUsageData<long>("FabricDnsService"),
                    new FabricResourceUsageData<long>("FabricGateway"),
                    new FabricResourceUsageData<long>("FabricHost"),
                    new FabricResourceUsageData<long>("FabricIS"),
                    new FabricResourceUsageData<long>("FabricRM"),
                    new FabricResourceUsageData<long>("FabricUS")
                };
            }

            if (this.allCpuData == null)
            {
                this.allCpuData = new List<FabricResourceUsageData<int>>
                { 
                    // Cpu data...
                    new FabricResourceUsageData<int>("Fabric"),
                    new FabricResourceUsageData<int>("FabricApplicationGateway"),
                    new FabricResourceUsageData<int>("FabricCAS"),
                    new FabricResourceUsageData<int>("FabricDCA"),
                    new FabricResourceUsageData<int>("FabricDnsService"),
                    new FabricResourceUsageData<int>("FabricGateway"),
                    new FabricResourceUsageData<int>("FabricHost"),
                    new FabricResourceUsageData<int>("FabricIS"),
                    new FabricResourceUsageData<int>("FabricRM"),
                    new FabricResourceUsageData<int>("FabricUS")
                };
            }

            if (this.allAppDiskReadsData == null)
            {
                this.allAppDiskReadsData = new List<FabricResourceUsageData<float>>
                { 
                    // Disk IO Reads data...
                    new FabricResourceUsageData<float>("Fabric"),
                    new FabricResourceUsageData<float>("FabricApplicationGateway"),
                    new FabricResourceUsageData<float>("FabricCAS"),
                    new FabricResourceUsageData<float>("FabricDCA"),
                    new FabricResourceUsageData<float>("FabricDnsService"),
                    new FabricResourceUsageData<float>("FabricGateway"),
                    new FabricResourceUsageData<float>("FabricHost"),
                    new FabricResourceUsageData<float>("FabricIS"),
                    new FabricResourceUsageData<float>("FabricRM"),
                    new FabricResourceUsageData<float>("FabricUS")
                };
            }

            if (this.allAppDiskWritesData == null)
            {
                this.allAppDiskWritesData = new List<FabricResourceUsageData<float>>
                { 
                    // Disk IO Writes data...
                    new FabricResourceUsageData<float>("Fabric"),
                    new FabricResourceUsageData<float>("FabricApplicationGateway"),
                    new FabricResourceUsageData<float>("FabricCAS"),
                    new FabricResourceUsageData<float>("FabricDCA"),
                    new FabricResourceUsageData<float>("FabricDnsService"),
                    new FabricResourceUsageData<float>("FabricGateway"),
                    new FabricResourceUsageData<float>("FabricHost"),
                    new FabricResourceUsageData<float>("FabricIS"),
                    new FabricResourceUsageData<float>("FabricRM"),
                    new FabricResourceUsageData<float>("FabricUS")
                };
            }

            if (this.monitorWinEventLog)
            {
                this.evtRecordList = new List<EventRecord>();
            }

            return true;
        }

        private void SetThresholdsFromConfiguration()
        {
            /* Error thresholds */

            Token.ThrowIfCancellationRequested();

            var cpuError = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                    ObserverConstants.FabricSystemObserverErrorCpu);
            if (!string.IsNullOrEmpty(cpuError))
            {
                CpuErrorUsageThresholdPct = int.Parse(cpuError);
            }

            var memError = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                    ObserverConstants.FabricSystemObserverErrorMemory);
            if (!string.IsNullOrEmpty(memError))
            {
                MemErrorUsageThresholdMB = int.Parse(memError);
            }

            var diskIOReadsError = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                            ObserverConstants.FabricSystemObserverErrorDiskIOReads);
            if (!string.IsNullOrEmpty(diskIOReadsError))
            {
                DiskErrorIOReadsThresholdMS = int.Parse(diskIOReadsError);
            }

            var diskIOWritesError = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                             ObserverConstants.FabricSystemObserverErrorDiskIOWrites);
            if (!string.IsNullOrEmpty(diskIOWritesError))
            {
                DiskErrorIOWritesThresholdMS = int.Parse(diskIOWritesError);
            }

            var percentErrorUnhealthyNodes = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                                      ObserverConstants.FabricSystemObserverErrorPercentUnhealthyNodes);

            if (!string.IsNullOrEmpty(percentErrorUnhealthyNodes))
            {
                this.unhealthyNodesErrorThreshold = int.Parse(percentErrorUnhealthyNodes);
            }

            /* Warning thresholds */

            Token.ThrowIfCancellationRequested();

            var cpuWarn = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                   ObserverConstants.FabricSystemObserverWarnCpu);
            if (!string.IsNullOrEmpty(cpuWarn))
            {
                CpuWarnUsageThresholdPct = int.Parse(cpuWarn);
            }

            var memWarn = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                   ObserverConstants.FabricSystemObserverWarnMemory);
            if (!string.IsNullOrEmpty(memWarn))
            {
                MemWarnUsageThresholdMB = int.Parse(memWarn);
            }

            var diskIOReadsWarn = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                           ObserverConstants.FabricSystemObserverWarnDiskIOReads);
            if (!string.IsNullOrEmpty(diskIOReadsWarn))
            {
                DiskWarnIOReadsThresholdMS = int.Parse(diskIOReadsWarn);
            }

            var diskIOWritesWarn = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                            ObserverConstants.FabricSystemObserverWarnDiskIOWrites);
            if (!string.IsNullOrEmpty(diskIOWritesWarn))
            {
                DiskWarnIOWritesThresholdMS = int.Parse(diskIOWritesWarn);
            }

            var percentWarnUnhealthyNodes = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                                     ObserverConstants.FabricSystemObserverWarnPercentUnhealthyNodes);

            if (!string.IsNullOrEmpty(percentWarnUnhealthyNodes))
            {
                this.unhealthyNodesWarnThreshold = int.Parse(percentWarnUnhealthyNodes);
            }

            // Monitor Windows event log for SF and System Error/Critical events?
            var watchEvtLog = GetSettingParameterValue(ObserverConstants.FabricSystemObserverConfigurationSectionName,
                                                       ObserverConstants.FabricSystemObserverMonitorWindowsEventLog);

            if (!string.IsNullOrEmpty(watchEvtLog) && bool.TryParse(watchEvtLog, out bool watchEl))
            {
                this.monitorWinEventLog = watchEl;
            }
        }

        public override async Task ObserveAsync(CancellationToken token)
        {
            Token = token;

            if (!Initialize() || Token.IsCancellationRequested)
            {
                return;
            }

            if (FabricClientInstance.QueryManager.GetNodeListAsync().GetAwaiter().GetResult()?.Count > 3 
                && await CheckClusterHealthStateAsync(this.unhealthyNodesWarnThreshold,
                                                      this.unhealthyNodesErrorThreshold).ConfigureAwait(true) == HealthState.Error)
            {
                return;
            }

            this.perfCounters = new WindowsPerfCounters();
            this.diskUsage = new DiskUsage();

            Token.ThrowIfCancellationRequested();

            try
            {
                foreach (var proc in this.processWatchList)
                {
                    Token.ThrowIfCancellationRequested();

                    GetProcessInfo(proc);
                }
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException))
                {
                    WriteToLogWithLevel(ObserverName,
                                        "Unhandled exception in ObserveAsync. Failed to observe CPU and Memory usage of " +
                                        string.Join(",", this.processWatchList) + ": " + e.ToString(),
                                        LogLevel.Error);
                }

                throw;
            }

            try
            {
                if (this.monitorWinEventLog)
                {
                    ReadServiceFabricWindowsEventLog();
                }

                // Set TTL...
                this.stopWatch.Stop();
                this.runtime = this.stopWatch.Elapsed;
                this.stopWatch.Reset();
                await ReportAsync(token).ConfigureAwait(true);

                // No need to keep these objects in memory aross healthy iterations...
                if (!HasActiveFabricErrorOrWarning)
                {
                    // Clear out/null list objects...
                    this.allAppDiskReadsData.Clear();
                    this.allAppDiskReadsData = null;

                    this.allAppDiskWritesData.Clear();
                    this.allAppDiskWritesData = null;

                    this.allCpuData.Clear();
                    this.allCpuData = null;

                    this.allMemData.Clear();
                    this.allMemData = null;
                }

                LastRunDateTime = DateTime.Now;
            }
            finally
            {
                this.diskUsage?.Dispose();
                this.diskUsage = null;
                this.perfCounters?.Dispose();
                this.perfCounters = null;
            }
        }

        private void GetProcessInfo(string procName)
        {
            var processes = Process.GetProcessesByName(procName);
            if (processes?.Length == 0)
            {
                return;
            }

            foreach (var process in processes)
            {
                try
                {
                    Token.ThrowIfCancellationRequested();

                    // ports in use by Fabric services...
                    TotalActivePortCount += NetworkUsage.GetActivePortCount(process.Id);
                    TotalActiveEphemeralPortCount += NetworkUsage.GetActiveEphemeralPortCount(process.Id);

                    int procCount = Environment.ProcessorCount;

                    while (!process.HasExited && procCount > 0)
                    {
                        Token.ThrowIfCancellationRequested();

                        try
                        {
                            int cpu = (int)this.perfCounters.PerfCounterGetProcessorInfo("% Processor Time", "Process", process.ProcessName);

                            this.allCpuData.FirstOrDefault(x => x.Name == procName).Data.Add(cpu);

                            // Disk IO (per-process disk reads/writes per sec)
                            this.allAppDiskReadsData.FirstOrDefault(x => x.Name == procName)
                                 .Data.Add(this.diskUsage.PerfCounterGetDiskIOInfo(process.ProcessName,
                                                                                   "Process",
                                                                                   "IO Read Operations/sec"));

                            this.allAppDiskWritesData.FirstOrDefault(x => x.Name == procName)
                                 .Data.Add(this.diskUsage.PerfCounterGetDiskIOInfo(process.ProcessName,
                                                                                   "Process",
                                                                                   "IO Write Operations/sec"));
                            // Memory - Private WS for proc...
                            var workingset = this.perfCounters.PerfCounterGetProcessPrivateWorkingSetMB(process.ProcessName);
                            this.allMemData.FirstOrDefault(x => x.Name == procName).Data.Add((long)workingset);

                            --procCount;
                            Thread.Sleep(250);
                        }
                        catch (Exception e)
                        {
                            WriteToLogWithLevel(ObserverName,
                                                $"Can't observe {process} details due to {e.Message} - {e.StackTrace}",
                                                LogLevel.Warning);
                            throw;
                        }
                    }
                }
                catch (Win32Exception)
                {
                    // This will always be the case if FabricObserver.exe is not running as Admin or LocalSystem... 
                    // It's OK. Just means that the elevated process (like FabricHost.exe) won't be observed.
                    WriteToLogWithLevel(ObserverName,
                                        $"Can't observe {process} due to it's privilege level - " +
                                        "FabricObserver must be running as System or Admin for this specific task.",
                                        LogLevel.Information);
                    break;
                }
                finally
                {
                    process?.Dispose();
                }
            }
        }

        public void ReadServiceFabricWindowsEventLog()
        {
            string sfOperationalLogSource = "Microsoft-ServiceFabric/Operational";
            string sfAdminLogSource = "Microsoft-ServiceFabric/Admin";
            string systemLogSource = "System";
            string sfLeaseAdminLogSource = "Microsoft-ServiceFabric-Lease/Admin";
            string sfLeaseOperationalLogSource = "Microsoft-ServiceFabric-Lease/Operational";

            var range2Days = DateTime.UtcNow.AddDays(-1);
            var format = range2Days.ToString(
                         "yyyy-MM-ddTHH:mm:ss.fffffff00K",
                          CultureInfo.InvariantCulture);
            var datexQuery = string.Format(
                             "*[System/TimeCreated/@SystemTime >='{0}']",
                             format);

            // Critical and Errors only...
            string xQuery = "*[System/Level <= 2] and " + datexQuery;

            // SF Admin Event Store...
            var evtLogQuery = new EventLogQuery(sfAdminLogSource, PathType.LogName, xQuery);
            using (var evtLogReader = new EventLogReader(evtLogQuery))
            {
                for (var eventInstance = evtLogReader.ReadEvent();
                     eventInstance != null;
                     eventInstance = evtLogReader.ReadEvent())
                {
                    Token.ThrowIfCancellationRequested();
                    this.evtRecordList.Add(eventInstance);
                }
            }

            // SF Operational Event Store...
            evtLogQuery = new EventLogQuery(sfOperationalLogSource, PathType.LogName, xQuery);
            using (var evtLogReader = new EventLogReader(evtLogQuery))
            {
                for (var eventInstance = evtLogReader.ReadEvent();
                     eventInstance != null;
                     eventInstance = evtLogReader.ReadEvent())
                {
                    Token.ThrowIfCancellationRequested();
                    this.evtRecordList.Add(eventInstance);
                }
            }

            // SF Lease Admin Event Store...
            evtLogQuery = new EventLogQuery(sfLeaseAdminLogSource, PathType.LogName, xQuery);
            using (var evtLogReader = new EventLogReader(evtLogQuery))
            {
                for (var eventInstance = evtLogReader.ReadEvent();
                     eventInstance != null;
                     eventInstance = evtLogReader.ReadEvent())
                {
                    Token.ThrowIfCancellationRequested();
                    this.evtRecordList.Add(eventInstance);
                }
            }

            // SF Lease Operational Event Store...
            evtLogQuery = new EventLogQuery(sfLeaseOperationalLogSource, PathType.LogName, xQuery);
            using (var evtLogReader = new EventLogReader(evtLogQuery))
            {
                for (var eventInstance = evtLogReader.ReadEvent();
                     eventInstance != null;
                     eventInstance = evtLogReader.ReadEvent())
                {
                    Token.ThrowIfCancellationRequested();
                    this.evtRecordList.Add(eventInstance);
                }
            }

            // System Event Store...
            evtLogQuery = new EventLogQuery(systemLogSource, PathType.LogName, xQuery);
            using (var evtLogReader = new EventLogReader(evtLogQuery))
            {
                for (var eventInstance = evtLogReader.ReadEvent();
                     eventInstance != null;
                     eventInstance = evtLogReader.ReadEvent())
                {
                    Token.ThrowIfCancellationRequested();
                    this.evtRecordList.Add(eventInstance);
                }
            }
        }

        public override Task ReportAsync(CancellationToken token)
        {
            Token.ThrowIfCancellationRequested();
            var timeToLiveWarning = SetTimeToLiveWarning(this.runtime.Seconds);
            var portInformationReport = new Utilities.HealthReport
            {
                Observer = ObserverName,
                NodeName = NodeName,
                HealthMessage = $"Number of ports in use by Fabric services: {TotalActivePortCount}\n" +
                                $"Number of ephemeral ports in use by Fabric services: {TotalActiveEphemeralPortCount}",
                State = HealthState.Ok,
                HealthReportTimeToLive = timeToLiveWarning
            };

            // TODO: Report on port count based on thresholds PortCountWarning/Error...
            HealthReporter.ReportHealthToServiceFabric(portInformationReport);
            
            // Reset ports counters...
            TotalActivePortCount = 0;
            TotalActiveEphemeralPortCount = 0;

            // CPU
            if (CpuErrorUsageThresholdPct > 0 || CpuWarnUsageThresholdPct > 0)
            {
                ProcessResourceDataList(this.allCpuData, 
                                        "CPU", 
                                        CpuErrorUsageThresholdPct, 
                                        CpuWarnUsageThresholdPct);
            }

            // Memory
            if (MemErrorUsageThresholdMB > 0 || MemWarnUsageThresholdMB > 0)
            {
                ProcessResourceDataList(this.allMemData, 
                                        "Memory", 
                                        MemErrorUsageThresholdMB, 
                                        MemWarnUsageThresholdMB);
            }

            // Disk IO - Reads
            if (DiskErrorIOReadsThresholdMS > 0 || DiskWarnIOReadsThresholdMS > 0)
            {
                ProcessResourceDataList(this.allAppDiskReadsData, 
                                        "Disk IO Reads", 
                                        DiskErrorIOReadsThresholdMS, 
                                        DiskWarnIOReadsThresholdMS);
            }

            // Disk IO - Writes
            if (DiskErrorIOWritesThresholdMS > 0 || DiskWarnIOWritesThresholdMS > 0)
            {
                ProcessResourceDataList(this.allAppDiskWritesData, 
                                        "Disk IO Writes", 
                                        DiskErrorIOWritesThresholdMS, 
                                        DiskWarnIOWritesThresholdMS);
            }

            // Windows Event Log
            if (this.monitorWinEventLog)
            { 
                // SF Eventlog Errors?
                // Write this out to a new file, for use by the web front end log viewer...
                // Format = HTML...
                int count = this.evtRecordList.Count();
                var logPath = Path.Combine(ObserverLogger.LogFolderBasePath, "EventVwrErrors.txt");

                // Remove existing file...
                if (File.Exists(logPath))
                {
                    try
                    {
                        File.Delete(logPath);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }

                if (count >= 10)
                {
                    var sb = new StringBuilder();

                    sb.AppendLine("<br/><div><strong>" +
                                  "<a href='javascript:toggle(\"evtContainer\")'>" +
                                  "<div id=\"plus\" style=\"display: inline; font-size: 25px;\">+</div> " + count +
                                  " Error Events in ServiceFabric and System</a> " +
                                  "Event logs</strong>.<br/></div>");

                    sb.AppendLine("<div id='evtContainer' style=\"display: none;\">");

                    foreach (var evt in this.evtRecordList.Distinct())
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            // Access event properties:
                            sb.AppendLine("<div>" + evt.LogName + "</div>");
                            sb.AppendLine("<div>" + evt.LevelDisplayName + "</div>");
                            if (evt.TimeCreated.HasValue)
                            {
                                sb.AppendLine("<div>" + evt.TimeCreated.Value.ToShortDateString() + "</div>");
                            }

                            foreach (var prop in evt.Properties)
                            {
                                if (prop.Value != null && Convert.ToString(prop.Value).Length > 0)
                                {
                                    sb.AppendLine("<div>" + prop.Value + "</div>");
                                }
                            }
                        }
                        catch (EventLogException) { }
                    }
                    sb.AppendLine("</div>");

                    ObserverLogger.TryWriteLogFile(logPath, sb.ToString());
                    sb.Clear();

                }

                // Clean up...
                if (count > 0)
                {
                    this.evtRecordList.Clear();
                }
            }

            return Task.CompletedTask;
        }

        private void ProcessResourceDataList<T>(List<FabricResourceUsageData<T>> data,
                                                string propertyName,
                                                T thresholdError,
                                                T thresholdWarning)
        {
            foreach (var dataItem in data)
            {
                Token.ThrowIfCancellationRequested();

                if (dataItem.Data.Count == 0 || dataItem.AverageDataValue < 0)
                {
                    continue;
                }

                if (CsvFileLogger.EnableCsvLogging || IsTelemetryEnabled)
                {
                    var fileName = "FabricSystemServices_" + NodeName;

                    
                    // Log average data value to long-running store (CSV)...
                    string dataLogMonitorType = propertyName;


                    // Log file output...
                    string resourceProp = propertyName + " use";

                    if (propertyName == "Memory")
                    {
                        dataLogMonitorType = "Working Set (MB)";
                    }

                    if (propertyName == "CPU")
                    {
                        dataLogMonitorType = "% CPU Time";
                    }

                    if (propertyName.Contains("Disk IO"))
                    {
                        dataLogMonitorType += "/ms";
                        resourceProp = propertyName;
                    }

                    CsvFileLogger.LogData(fileName, dataItem.Name, dataLogMonitorType, "Average", Math.Round(dataItem.AverageDataValue, 2));
                    CsvFileLogger.LogData(fileName, dataItem.Name, dataLogMonitorType, "Peak", Math.Round(Convert.ToDouble(dataItem.MaxDataValue)));
                }

                ProcessResourceDataReportHealth(dataItem,
                                                propertyName,
                                                thresholdError,
                                                thresholdWarning,
                                                SetTimeToLiveWarning(this.runtime.Seconds));  
            }
        }

        private async Task<HealthState> CheckClusterHealthStateAsync(int warningThreshold, int errorThreshold)
        {
            try
            {
                var clusterHealth = await FabricClientInstance.HealthManager.GetClusterHealthAsync().ConfigureAwait(true);
                double errorNodesCount = clusterHealth.NodeHealthStates.Count(nodeHealthState => nodeHealthState.AggregatedHealthState == HealthState.Error);
                int errorNodesCountPercentage = (int)((errorNodesCount / clusterHealth.NodeHealthStates.Count) * 100);

                if (errorNodesCountPercentage >= errorThreshold)
                {
                    return HealthState.Error;
                }
                else if (errorNodesCountPercentage >= warningThreshold)
                {
                    return HealthState.Warning;
                }
            }
            catch (TimeoutException te)
            {
                ObserverLogger.LogInfo("Handled TimeoutException:\n {0}", te.ToString());

                return HealthState.Unknown;
            }
            catch (FabricException fe)
            {
                ObserverLogger.LogInfo("Handled FabricException:\n {0}", fe.ToString());

                return HealthState.Unknown;
            }
            catch (Exception e)
            {
                ObserverLogger.LogWarning("Unhandled Exception querying Cluster health:\n {0}", e.ToString());

                throw;
            }

            return HealthState.Ok;
        }
    }
}