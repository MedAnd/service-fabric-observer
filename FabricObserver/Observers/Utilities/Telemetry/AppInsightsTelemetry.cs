﻿// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using FabricObserver.Interfaces;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using System;
using System.Collections.Generic;
using System.Fabric.Health;
using System.Threading;
using System.Threading.Tasks;

namespace FabricObserver.Utilities.Telemetry
{
    /// <summary>
    /// Abstracts the ApplicationInsights telemetry API calls allowing
    /// other telemetry providers to be plugged in.
    /// </summary>
    public class AppInsightsTelemetry : IObserverTelemetryProvider, IDisposable
    {
        /// <summary>
        /// ApplicationInsights telemetry client.
        /// </summary>
        private readonly TelemetryClient telemetryClient = null;
        private readonly Logger logger;
        /// <summary>
        /// AiTelemetry constructor.
        /// </summary>
        public AppInsightsTelemetry(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Argument is empty", nameof(key));
            }

            this.logger = new Logger("TelemetryLog");

            this.telemetryClient = new TelemetryClient(new TelemetryConfiguration() { InstrumentationKey = key });
#if DEBUG
            // Expedites the flow of data through the pipeline.
            TelemetryConfiguration.Active.TelemetryChannel.DeveloperMode = true;
#endif
        }

        /// <summary>
        /// Gets an indicator if the telemetry is enabled or not.
        /// </summary>
        public bool IsEnabled => this.telemetryClient.IsEnabled() && ObserverManager.TelemetryEnabled;

        /// <summary>
        /// Gets or sets the key.
        /// </summary>
        public string Key
        {
            get { return this.telemetryClient?.InstrumentationKey; }
            set { this.telemetryClient.InstrumentationKey = value; }
        }

        /// <summary>
        /// Calls AI to track the availability.
        /// </summary>
        /// <param name="serviceName">Service name.</param>
        /// <param name="instance">Instance identifier.</param>
        /// <param name="testName">Availability test name.</param>
        /// <param name="captured">The time when the availability was captured.</param>
        /// <param name="duration">The time taken for the availability test to run.</param>
        /// <param name="location">Name of the location the availability test was run from.</param>
        /// <param name="success">True if the availability test ran successfully.</param>
        /// <param name="message">Error message on availability test run failure.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task ReportAvailabilityAsync(Uri serviceName,
                                            string instance,
                                            string testName,
                                            DateTimeOffset captured,
                                            TimeSpan duration,
                                            string location,
                                            bool success,
                                            CancellationToken cancellationToken,
                                            string message = null)
        {
            if (!IsEnabled || cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(1);
            }

            AvailabilityTelemetry at = new AvailabilityTelemetry(testName, captured, duration, location, success, message);

            at.Properties.Add("Service", serviceName?.OriginalString);
            at.Properties.Add("Instance", instance);

            this.telemetryClient.TrackAvailability(at);
            

            return Task.FromResult(0);
        }

        /// <summary>
        /// Calls AI to report health.
        /// </summary>
        /// <param name="applicationName">Application name.</param>
        /// <param name="serviceName">Service name.</param>
        /// <param name="instance">Instance identifier.</param>
        /// <param name="source">Name of the health source.</param>
        /// <param name="property">Name of the health property.</param>
        /// <param name="state">HealthState.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task ReportHealthAsync(string applicationName,
                                      string serviceName,
                                      string instance,
                                      string source,
                                      string property,
                                      HealthState state,
                                      CancellationToken cancellationToken)
        {
            if (!IsEnabled || cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(1);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                SeverityLevel sev = (HealthState.Error == state) ? SeverityLevel.Error
                                    : (HealthState.Warning == state) ? SeverityLevel.Warning : SeverityLevel.Information;

                var tt = new TraceTelemetry($"{applicationName}: Service Fabric Health report - {Enum.GetName(typeof(HealthState), state)} -> {source}:{property}", sev);
                tt.Context.Cloud.RoleName = serviceName;
                tt.Context.Cloud.RoleInstance = instance;
                this.telemetryClient.TrackTrace(tt);
            }
            catch (Exception e)
            {
                this.logger.LogWarning($"Unhandled exception in TelemetryClient.ReportHealthAsync:\n{e.ToString()}");
                throw;
            }

            return Task.FromResult(0);
        }

        /// <summary>
        /// Calls AI to report a metric.
        /// </summary>
        /// <param name="name">Name of the metric.</param>
        /// <param name="value">Value of the property.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task<bool> ReportMetricAsync<T>(string name, T value, CancellationToken cancellationToken)
        {
            if (!IsEnabled || cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            var metricTelemetry = new MetricTelemetry
            {
                Name = name,
                Sum = Convert.ToDouble(value)
            };

            this.telemetryClient.TrackMetric(metricTelemetry);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Calls AI to report a metric.
        /// </summary>
        /// <param name="name">Name of the metric.</param>
        /// <param name="value">Value of the property.</param>
        /// <param name="properties">IDictionary&lt;string&gt;,&lt;string&gt; containing name/value pairs of additional properties.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task ReportMetricAsync(string name, long value, IDictionary<string, string> properties, CancellationToken cancellationToken)
        {
            if (!IsEnabled || cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(1);
            }

            this.telemetryClient.GetMetric(name).TrackValue(value, string.Join(";", properties));
            

            return Task.FromResult(0);
        }

        /// <summary>
        /// Calls AI to report a metric.
        /// </summary>
        /// <param name="role">Name of the service.</param>
        /// <param name="partition">Guid of the partition.</param>
        /// <param name="name">Name of the metric.</param>
        /// <param name="value">Value if the metric.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task ReportMetricAsync(string role, Guid partition, string name, long value, CancellationToken cancellationToken)
        {
            return ReportMetricAsync(role, partition.ToString(), name, value, 1, value, value, value, 0.0, null, cancellationToken);
        }

        /// <summary>
        /// Calls AI to report a metric.
        /// </summary>
        /// <param name="role">Name of the service.</param>
        /// <param name="id">Replica or Instance identifier.</param>
        /// <param name="name">Name of the metric.</param>
        /// <param name="value">Value if the metric.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public async Task ReportMetricAsync(string role, long id, string name, long value, CancellationToken cancellationToken)
        {
            await ReportMetricAsync(role, id.ToString(), name, value, 1, value, value, value, 0.0, null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls AI to report a metric.
        /// </summary>
        /// <param name="roleName">Name of the role. Usually the service name.</param>
        /// <param name="instance">Instance identifier.</param>
        /// <param name="name">Name of the metric.</param>
        /// <param name="value">Value if the metric.</param>
        /// <param name="count">Number of samples for this metric.</param>
        /// <param name="min">Minimum value of the samples.</param>
        /// <param name="max">Maximum value of the samples.</param>
        /// <param name="sum">Sum of all of the samples.</param>
        /// <param name="deviation">Standard deviation of the sample set.</param>
        /// <param name="properties">IDictionary&lt;string&gt;,&lt;string&gt; containing name/value pairs of additional properties.</param>
        /// <param name="cancellationToken">CancellationToken instance.</param>
        public Task ReportMetricAsync(string roleName,
                                      string instance,
                                      string name,
                                      long value,
                                      int count,
                                      long min,
                                      long max,
                                      long sum,
                                      double deviation,
                                      IDictionary<string, string> properties,
                                      CancellationToken cancellationToken)
        {
            if (!IsEnabled || cancellationToken.IsCancellationRequested)
            {
                return Task.FromResult(false);
            }

            MetricTelemetry mt = new MetricTelemetry(name, value)
            {
                Count = count,
                Min = min,
                Max = max,
                StandardDeviation = deviation,
            };

            mt.Context.Cloud.RoleName = roleName;
            mt.Context.Cloud.RoleInstance = instance;

            // Set the properties.
            if (null != properties)
            {
                foreach (KeyValuePair<string, string> prop in properties)
                {
                    mt.Properties.Add(prop);
                }
            }

            // Track the telemetry.
            this.telemetryClient.TrackMetric(mt);

            return Task.FromResult(0);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.logger?.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~AppInsightsTelemetry()
        // {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}