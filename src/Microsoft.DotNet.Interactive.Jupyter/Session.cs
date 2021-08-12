// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Jupyter.Protocol;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Interactive.Jupyter
{
    public class Session
    {
        private const string _protocolVersion = "5.0";
        private Random _rnd;

        public string Id { get; }

        public string Key { get; set; }

        public string Username { get; }

        public static string ProtocolVersion => _protocolVersion;

        public Session(string username = "username")
        {
            _rnd = new Random();
            Id = Guid.NewGuid().ToString();
            Username = username;
        }
    }

    public class JupyterKernelProxy : Kernel, IKernelCommandHandler<SubmitCode>
    {
        private readonly JupyterKernel _kernel;
        private JupyterClient _client = null;
        private Dictionary<string, TaskCompletionSource> _pendingRequests = new();

        public JupyterKernelProxy(JupyterKernel kernel) : base(kernel.Name)
        {
            _kernel = kernel;
            _client.Subscribe(HandleResponse);
        }

        private void HandleResponse((JupyterChannel channel, ZMQ.Message message) e)
        {
            //PublishEvent(new DisplayedValueProduced()
            var token = e.message.MetaData["commandToken"] as string;
            var completionSource = _pendingRequests[token];
            switch (e.message.Content)
            {
                case ExecuteReplyOk:
                    _pendingRequests.Remove(token);
                    completionSource.TrySetResult();
                    break;
                case ExecuteReplyError reply:
                    _pendingRequests.Remove(token);
                    completionSource.TrySetException(new Exception(reply.EValue));
                    break;
                default:
                    throw new InvalidOperationException($"{e.message.Header.MessageType} is not supported.");
            }
        }

        public Task HandleAsync(SubmitCode command, KernelInvocationContext context)
        {
            var request = new ExecuteRequest(command.Code);
            var requestMessage = _client.CreateMessage(request, command.GetToken());
            TaskCompletionSource taskCompletionSource = new();
            _pendingRequests[command.GetToken()] = taskCompletionSource;
            _client.Send(JupyterChannel.Shell, requestMessage);
            return taskCompletionSource.Task;
        }
    }

    public class JupyterKernel
    {
        public JupyterKernel(string name, Session session, ConnectionInformation connectionInformation)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"'{nameof(name)}' cannot be null or whitespace.", nameof(name));
            }
            Name = name;
            Session = session ?? throw new ArgumentNullException(nameof(session));
            ConnectionInformation = connectionInformation ?? throw new ArgumentNullException(nameof(connectionInformation));
        }

        public string Name { get; }
        public Session Session { get; }
        public ConnectionInformation ConnectionInformation { get; }
    }
}
