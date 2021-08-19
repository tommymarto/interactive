// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Interactive.Jupyter
{
    public class JupyterKernelSession : IDisposable
    {
        public JupyterKernelSession(string name, Session session)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace.", nameof(name));
            }
            Name = name;
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }

        public string Name { get; }
        public Session Session { get; }
        public ConnectionInformation ConnectionInformation { get; private set; }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var connectionFile = Path.Combine(
                Path.GetTempPath(),
                "jupyter",
                "runtime",
                $"{Session.Id}.json");

            var connectionInformationTask = GetConnectionInformationAsync(connectionFile, cancellationToken);

            // TODO: start process

            ConnectionInformation = await connectionInformationTask;

            throw new NotImplementedException();
        }

        private async Task<ConnectionInformation> GetConnectionInformationAsync(string connectionFile, CancellationToken cancellationToken)
        {
            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();
            var timeout = TimeSpan.FromSeconds(10);
            while (!File.Exists(connectionFile) && stopWatch.Elapsed < timeout)
            {
                await Task.Delay(250);
            }
            await Task.Delay(500);
            var content = await File.ReadAllTextAsync(connectionFile, cancellationToken);
            return JsonSerializer.Deserialize<ConnectionInformation>(content);
        }
    }
}