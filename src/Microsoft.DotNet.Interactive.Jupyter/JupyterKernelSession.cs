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

        public void Dispose()
        {
            throw new NotImplementedException();
        }
        public async Task StartAsync(ConnectionInformation connectionInformation, CancellationToken cancellationToken)
        {
            var connectionFile = Path.Combine(
                Path.GetTempPath(),
                "jupyter",
                "runtime",
                $"{Session.Id}.json");

            await File.WriteAllTextAsync(connectionFile, JsonSerializer.Serialize(connectionInformation), cancellationToken);

            throw new NotImplementedException();
        }
    }
}