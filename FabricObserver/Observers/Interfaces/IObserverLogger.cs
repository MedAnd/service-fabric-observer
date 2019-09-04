﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace FabricObserver.Interfaces
{
    public interface IObserverLogger<TLogger>
    {
        bool EnableVerboseLogging { get; set; }

        string LogFolderBasePath { get; set; }

        void LogInfo(string format, params object[] parameters);

        void LogError(string format, params object[] parameters);

        void LogTrace(string observer, string format, params object[] parameters);

        void LogWarning(string format, params object[] parameters);

        bool TryWriteLogFile(string path, string content);
    }
}
