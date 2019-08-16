﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using FabricObserver.Interfaces;
using NLog;
using NLog.Targets;
using NLog.Time;
using System;
using System.IO;

namespace FabricObserver.Utilities
{
    // CSV file logger for long-running monitoring data (memory/cpu/disk/network usage data)...
    public class DataTableFileLogger : IDataTableFileLogger<ILogger>
    {
        internal static ILogger logger = null;
        public bool EnableCsvLogging { get; set; } = false;
        public string DataLogFolderPath { get; set; } = null;

        public void ConfigureLogger(string filename)
        {
            // default log directory...
            string windrive = Environment.SystemDirectory.Substring(0, 2);
            string logPath = windrive + "\\observer_logs\\fabric_observer_data";

            // log directory supplied in config... Set in ObserverManager.
            if (!string.IsNullOrEmpty(DataLogFolderPath))
            {
                logPath = DataLogFolderPath;
            }

            var csvPath = Path.Combine(logPath, filename + ".csv");

            if (logger == null)
            {
                logger = LogManager.GetCurrentClassLogger();
            }

            TimeSource.Current = new AccurateUtcTimeSource();
            var dataLog = (FileTarget)LogManager.Configuration.FindTargetByName("AvgTargetDataStore");
            dataLog.FileName = csvPath;
            dataLog.AutoFlush = true;
            dataLog.ConcurrentWrites = true;

            LogManager.ReconfigExistingLoggers();
        }

        /* NLog: For writing out to local CSV file...
           Format:
            <column name="date" layout="${longdate}" />
            <column name="target" layout="${event-properties:target}" />
            <column name="metric" layout="${event-properties:metric}" />
            <column name="stat" layout="${event-properties:stat}" />
            <column name="value" layout="${event-properties:value}" />
        */
        public void LogData(string fileName,
                            string target,
                            string metric,
                            string stat,
                            double value)
        {
            // If you provided an IObserverTelemetry impl, then this will, for example, 
            // send traces up to App Insights (Azure). See the App.config for settings example.
            if (!EnableCsvLogging)
            {
                if (!ObserverManager.TelemetryEnabled)
                {
                    return;
                }

                if (logger == null)
                {
                    logger = LogManager.GetCurrentClassLogger();
                }

                logger.Info($"{target}/{metric}/{stat}: {value}");

                return;
            }

            // Else, reconfigure logger to write to file on disk...
            ConfigureLogger(fileName);

            logger.Info("{target}{metric}{stat}{value}",
                         target, metric, stat, value);
        }

        internal static void ShutDown()
        {
            LogManager.Shutdown();
        }

        internal static void Flush()
        {
            LogManager.Flush();
        }
    }
}
