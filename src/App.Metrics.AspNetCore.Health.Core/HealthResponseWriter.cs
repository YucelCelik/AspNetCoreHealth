﻿// <copyright file="HealthResponseWriter.cs" company="Allan Hardy">
// Copyright (c) Allan Hardy. All rights reserved.
// </copyright>

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.Health;
using App.Metrics.Health.Formatters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace App.Metrics.AspNetCore.Health.Core
{
    public class HealthResponseWriter : IHealthResponseWriter
    {
        private readonly HealthOptions _healthOptions;

        public HealthResponseWriter(
            IOptions<HealthOptions> healthOptionsAccessor,
            IOptions<HealthAspNetCoreOptions> healthMiddlewareOptionsAccessor)
        {
            if (healthOptionsAccessor == null)
            {
                throw new ArgumentNullException(nameof(healthOptionsAccessor));
            }

            if (healthMiddlewareOptionsAccessor == null)
            {
                throw new ArgumentNullException(nameof(healthMiddlewareOptionsAccessor));
            }

            _healthOptions = healthOptionsAccessor.Value;
        }

        public Task WriteAsync(HttpContext context, HealthStatus healthStatus, CancellationToken token = default(CancellationToken))
        {
            var acceptHeaderMediaType = context.Request.GetTypedHeaders();
            var formatter = default(IHealthOutputFormatter);

            context.SetNoCacheHeaders();

            if (acceptHeaderMediaType.Accept != null)
            {
                foreach (var accept in acceptHeaderMediaType.Accept)
                {
                    formatter = ResolveFormatter(_healthOptions.OutputFormatters, accept);
                }
            }

            if (formatter == default(IHealthOutputFormatter))
            {
                if (_healthOptions.DefaultOutputFormatter == default(IHealthOutputFormatter))
                {
                    context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                    return Task.CompletedTask;
                }

                formatter = _healthOptions.DefaultOutputFormatter;
            }

            SetResponseStatusCode(context, healthStatus);

            context.Response.Headers[HeaderNames.ContentType] = new[] { formatter.MediaType.ContentType };

            return formatter.WriteAsync(context.Response.Body, healthStatus, token);
        }

        private static IHealthOutputFormatter ResolveFormatter(HealthFormatterCollection formatters, MediaTypeHeaderValue acceptHeader)
        {
            var versionAndFormatTokens = acceptHeader.SubType.Value.Split('-');

            if (acceptHeader.Type.Value.IsMissing()
                || acceptHeader.SubType.Value.IsMissing()
                || versionAndFormatTokens.Length != 2)
            {
                return default(IHealthOutputFormatter);
            }

            var versionAndFormat = versionAndFormatTokens[1].Split('+');

            if (versionAndFormat.Length != 2)
            {
                return default(IHealthOutputFormatter);
            }

            var mediaTypeValue = new HealthMediaTypeValue(
                acceptHeader.Type.Value,
                versionAndFormatTokens[0],
                versionAndFormat[0],
                versionAndFormat[1]);

            return formatters.GetType(mediaTypeValue);
        }

        private static void SetResponseStatusCode(HttpContext context, HealthStatus healthStatus)
        {
            var responseStatusCode = HttpStatusCode.OK;

            if (healthStatus.Status.IsUnhealthy())
            {
                responseStatusCode = HttpStatusCode.ServiceUnavailable;
            }

            if (healthStatus.Status.IsDegraded())
            {
                responseStatusCode = HttpStatusCode.OK;
                context.Response.Headers[HeaderNames.Warning] = new[] { "Warning: 100 'Degraded'" };
            }

            context.Response.StatusCode = (int)responseStatusCode;
        }
    }
}