﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace FabricObserver.Utilities
{
    internal class DiskUsage : IDisposable
    {
        internal string Drive { get; private set; }

        private WindowsPerfCounters winPerfCounters;
        private bool isDisposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiskUsage"/> class.
        /// </summary>
        public DiskUsage()
        {
            var driveLetter = Environment.CurrentDirectory.Substring(0, 2);
            this.Drive = driveLetter;
            this.winPerfCounters = new WindowsPerfCounters();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DiskUsage"/> class.
        /// </summary>
        /// <param name="driveLetter">Drive letter...</param>
        public DiskUsage(string driveLetter)
        {
            this.Drive = driveLetter;
            this.winPerfCounters = new WindowsPerfCounters();
        }

        /// <summary>
        /// Gets the percent used space (as an integer value) of the current drive where this code is running from...
        /// Or from whatever drive letter you supplied to DiskUsage(string driveLetter) ctor...
        /// </summary>
        internal int PercentUsedSpace => this.GetCurrentDiskSpaceUsedPercent(this.Drive);

        internal int GetCurrentDiskSpaceUsedPercent(string drive)
        {
            if (string.IsNullOrEmpty(drive))
            {
                return -1; // Don't throw here...
            }

            var driveInfo = new DriveInfo(drive);
            long availableMB = driveInfo.AvailableFreeSpace / 1024 / 1024;
            long totalMB = driveInfo.TotalSize / 1024 / 1024;
            double usedPct = ((double)(totalMB - availableMB)) / totalMB;

            return (int)(usedPct * 100);
        }

        internal List<Tuple<string, int>> GetCurrentDiskSpaceUsedPercentAllDrives()
        {
            DriveInfo[] allDrives = DriveInfo.GetDrives();

            var tuples = new List<Tuple<string, int>>();

            for (int i = 0; i < allDrives.Length; i++)
            {
                if (!allDrives[i].IsReady)
                {
                    continue;
                }

                var drivename = allDrives[i].Name;
                var pctUsed = this.GetCurrentDiskSpaceUsedPercent(drivename);
                tuples.Add(Tuple.Create(drivename.Substring(0, 1), pctUsed));
            }

            return tuples;
        }

        internal double GetAvailabeDiskSpace(string driveLetter, SizeUnit sizeUnit = SizeUnit.Bytes)
        {
            var driveInfo = new DriveInfo(driveLetter);
            long available = driveInfo.AvailableFreeSpace;

            return ConvertToSizeUnits(available, sizeUnit);
        }

        internal double GetUsedDiskSpace(string driveLetter, SizeUnit sizeUnit = SizeUnit.Bytes)
        {
            var driveInfo = new DriveInfo(driveLetter);
            long used = driveInfo.TotalSize - driveInfo.AvailableFreeSpace;

            return ConvertToSizeUnits(used, sizeUnit);
        }

        internal double GetTotalDiskSpace(string driveLetter, SizeUnit sizeUnit = SizeUnit.Bytes)
        {
            var driveInfo = new DriveInfo(driveLetter);
            long total = driveInfo.TotalSize;

            return ConvertToSizeUnits(total, sizeUnit);
        }

        private static double ConvertToSizeUnits(double amount, SizeUnit sizeUnit)
        {
            switch (sizeUnit)
            {
                case SizeUnit.Bytes:
                    return amount;

                case SizeUnit.Kilobytes:
                    return amount / 1024;

                case SizeUnit.Megabytes:
                    return amount / 1024 / 1024;

                case SizeUnit.Gigabytes:
                    return amount / 1024 / 1024 / 1024;

                case SizeUnit.Terabytes:
                    return amount / 1024 / 1024 / 1024 / 1024;

                default:
                    return amount;
            }
        }

        internal float PerfCounterGetDiskIOInfo(
            string instance,
            string category,
            string countername)
        {
            if (countername.Contains("Read"))
            {
                return this.winPerfCounters.PerfCounterGetIOReadInfo(instance, category, countername);
            }
            else
            {
                return this.winPerfCounters.PerfCounterGetIOWriteInfo(instance, category, countername);
            }
        }

        internal float GetAverageDiskQueueLength(string instance)
        {
            return this.winPerfCounters.PerfCounterGetAverageDiskQueueLength(instance);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    if (this.winPerfCounters != null)
                    {
                        this.winPerfCounters.Dispose();
                        this.winPerfCounters = null;
                    }
                }

                this.isDisposed = true;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            this.Dispose(true);
        }
    }

    public enum SizeUnit
    {
        Bytes,
        Kilobytes,
        Megabytes,
        Gigabytes,
        Terabytes,
    }
}
