﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using FabricObserver.Utilities;
using System;
using System.Collections.Generic;

namespace FabricObserver.Interfaces
{
    public interface IObserverBase<TServiceContext> : IObserver
    {
        string ObserverName { get; set; }
        string NodeName { get; set; }
        ObserverHealthReporter HealthReporter { get; }
        // StatefulServiceContext or StatelessServiceContext...
        TServiceContext FabricServiceContext { get; }
        Logger ObserverLogger { get; set; }
        DataTableFileLogger CsvFileLogger { get; set; }
        void WriteToLogWithLevel(string observerName, string description, LogLevel level);
        TimeSpan GetObserverRunInterval(string configSectionName, string configParamName, TimeSpan? defaultTo = null);
        string GetSettingParameterValue(string sectionName, string parameterName, string defaultValue = null);
        IDictionary<string, string> GetConfigSettingSectionParameters(string sectionName);
    }
}