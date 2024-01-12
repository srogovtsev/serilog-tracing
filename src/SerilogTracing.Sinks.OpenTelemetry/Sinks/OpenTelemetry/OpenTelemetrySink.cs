﻿// Copyright 2022 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Net.Http;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Trace.V1;
using Serilog.Core;
using Serilog.Events;
using SerilogTracing.Sinks.OpenTelemetry.ProtocolHelpers;
using Serilog.Sinks.PeriodicBatching;

namespace SerilogTracing.Sinks.OpenTelemetry;

class OpenTelemetrySink : IBatchedLogEventSink, ILogEventSink, IDisposable
{
    readonly IFormatProvider? _formatProvider;
    readonly ResourceLogs _resourceLogsTemplate;
    readonly ResourceSpans _resourceSpansTemplate;
    readonly IExporter _exporter;
    readonly IncludedData _includedData;
    readonly bool _isLogsEnabled;
    readonly bool _isTracesEnabled;

    public OpenTelemetrySink(
        IExporter exporter,
        IFormatProvider? formatProvider,
        IReadOnlyDictionary<string, object> resourceAttributes,
        IncludedData includedData,
        bool isLogsEnabled,
        bool isTracesEnabled
        )
    {
        _exporter = exporter;
        _formatProvider = formatProvider;
        _includedData = includedData;
        _isLogsEnabled = isLogsEnabled;
        _isTracesEnabled = isTracesEnabled;

        if ((includedData & IncludedData.SpecRequiredResourceAttributes) == IncludedData.SpecRequiredResourceAttributes)
        {
            resourceAttributes = RequiredResourceAttributes.AddDefaults(resourceAttributes);
        }

        _resourceLogsTemplate = RequestTemplateFactory.CreateResourceLogs(resourceAttributes);
        _resourceSpansTemplate = RequestTemplateFactory.CreateResourceSpans(resourceAttributes);
    }

    /// <summary>
    /// Frees any resources allocated by the IExporter.
    /// </summary>
    public void Dispose()
    {
        (_exporter as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Transforms and sends the given batch of LogEvent objects
    /// to an OTLP endpoint.
    /// </summary>
    public Task EmitBatchAsync(IEnumerable<LogEvent> batch)
    {
        if (!_isLogsEnabled && !_isTracesEnabled)
        {
            return Task.CompletedTask;
        }
        
        var resourceLogs = _resourceLogsTemplate.Clone();
        var resourceSpans = _resourceSpansTemplate.Clone();
        
        var logsAnonymousScope = (ScopeLogs?)null;
        var traceAnonymousScope = (ScopeSpans?)null;
        var logsNamedScopes = (Dictionary<string, ScopeLogs>?)null;
        var spansNamedScopes = (Dictionary<string, ScopeSpans>?)null;

        foreach (var logEvent in batch)
        {
            if (IsSpan(logEvent) && _isTracesEnabled)
            {
                var (span, scopeName) = OtlpEventBuilder.ToSpan(logEvent, _formatProvider, _includedData);
                if (scopeName == null)
                {
                    if (traceAnonymousScope == null)
                    {
                        traceAnonymousScope = RequestTemplateFactory.CreateScopeSpans(null);
                        resourceSpans.ScopeSpans.Add(traceAnonymousScope);
                    }
                
                    traceAnonymousScope.Spans.Add(span);
                }
                else
                {
                    spansNamedScopes ??= new Dictionary<string, ScopeSpans>();
                    if (!spansNamedScopes.TryGetValue(scopeName, out var namedScope))
                    {
                        namedScope = RequestTemplateFactory.CreateScopeSpans(scopeName);
                        spansNamedScopes.Add(scopeName, namedScope);
                        resourceSpans.ScopeSpans.Add(namedScope);
                    }
                
                    namedScope.Spans.Add(span);
                }
            }
            else if (_isLogsEnabled)
            {
                var (logRecord, scopeName) = OtlpEventBuilder.ToLogRecord(logEvent, _formatProvider, _includedData);
                if (scopeName == null)
                {
                    if (logsAnonymousScope == null)
                    {
                        logsAnonymousScope = RequestTemplateFactory.CreateScopeLogs(null);
                        resourceLogs.ScopeLogs.Add(logsAnonymousScope);
                    }
                
                    logsAnonymousScope.LogRecords.Add(logRecord);
                }
                else
                {
                    logsNamedScopes ??= new Dictionary<string, ScopeLogs>();
                    if (!logsNamedScopes.TryGetValue(scopeName, out var namedScope))
                    {
                        namedScope = RequestTemplateFactory.CreateScopeLogs(scopeName);
                        logsNamedScopes.Add(scopeName, namedScope);
                        resourceLogs.ScopeLogs.Add(namedScope);
                    }
                
                    namedScope.LogRecords.Add(logRecord);
                }
            }
        }

        var exportTasks = new List<Task>();

        if (_isLogsEnabled && resourceLogs.ScopeLogs.Any(sl => sl.LogRecords.Any()))
        {
            var logsRequest = new ExportLogsServiceRequest();
            logsRequest.ResourceLogs.Add(resourceLogs);
            exportTasks.Add(_exporter.ExportAsync(logsRequest));
        }

        if (_isTracesEnabled && resourceSpans.ScopeSpans.Any(ss => ss.Spans.Any()))
        {
            var spansRequest = new ExportTraceServiceRequest();
            spansRequest.ResourceSpans.Add(resourceSpans);
            exportTasks.Add(_exporter.ExportAsync(spansRequest));
        }

        return Task.WhenAll(exportTasks);
    }

    /// <summary>
    /// Transforms and sends the given LogEvent
    /// to an OTLP endpoint.
    /// </summary>
    public void Emit(LogEvent logEvent)
    {
        if (!_isLogsEnabled && !_isTracesEnabled)
        {
            return;
        }
        
        if (IsSpan(logEvent) && _isTracesEnabled)
        {
            EmitSpan(logEvent);
        }
        else if (_isLogsEnabled)
        {
           EmitLog(logEvent);
        }
    }

    void EmitSpan(LogEvent logEvent)
    {
        var (span, scopeName) = OtlpEventBuilder.ToSpan(logEvent, _formatProvider, _includedData);
        var scopeSpans = RequestTemplateFactory.CreateScopeSpans(scopeName);
        scopeSpans.Spans.Add(span);
        var resourceSpans = _resourceSpansTemplate.Clone();
        resourceSpans.ScopeSpans.Add(scopeSpans);
        var request = new ExportTraceServiceRequest();
        request.ResourceSpans.Add(resourceSpans);
        _exporter.Export(request);
    }

    void EmitLog(LogEvent logEvent)
    {
        var (logRecord, scopeName) = OtlpEventBuilder.ToLogRecord(logEvent, _formatProvider, _includedData);
        var scopeLogs = RequestTemplateFactory.CreateScopeLogs(scopeName);
        scopeLogs.LogRecords.Add(logRecord);
        var resourceLogs = _resourceLogsTemplate.Clone();
        resourceLogs.ScopeLogs.Add(scopeLogs);
        var request = new ExportLogsServiceRequest();
        request.ResourceLogs.Add(resourceLogs);
        _exporter.Export(request);
    }

    static bool IsSpan(LogEvent logEvent)
    {
        return logEvent is { TraceId: not null, SpanId: not null } &&
               logEvent.Properties.TryGetValue(SerilogTracing.Core.Constants.SpanStartTimestampPropertyName, out var sst) &&
               sst is ScalarValue { Value: DateTime };
    }

    /// <summary>
    /// A no-op for an empty batch.
    /// </summary>
    public Task OnEmptyBatchAsync()
    {
        return Task.CompletedTask;
    }
}
