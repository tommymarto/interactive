// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Microsoft.DotNet.Interactive.Formatting;
using Microsoft.DotNet.Interactive.Jupyter.Protocol;

namespace Microsoft.DotNet.Interactive.Jupyter
{
    public class JupyterKernelProxy : Kernel, IKernelCommandHandler<SubmitCode>
    {
        private readonly JupyterKernelSession _kernelSession;
        private readonly object _clientLock = new();
        private JupyterClient _client = null;
        private readonly Dictionary<string, CommandForwardingContext> _inflightCommands = new();

        public JupyterKernelProxy(JupyterKernelSession kernel) : base(kernel.Name)
        {
            _kernelSession = kernel;
        }

        private void HandleResponse((JupyterChannel channel, ZMQ.Message message) e)
        {
            var token = e.message.MetaData["commandToken"] as string;
            var forwardingContext = _inflightCommands[token];
            switch (e.message.Content)
            {
                case ExecuteReplyOk:
                    _inflightCommands.Remove(token);
                    forwardingContext.CompletionSource.TrySetResult();
                    break;
                case ExecuteReplyError reply:
                    _inflightCommands.Remove(token);
                    forwardingContext.CompletionSource.TrySetException(new Exception(reply.EValue));
                    break;
                default:
                    TranslateAndForward(e.channel, e.message, forwardingContext.Context, forwardingContext.Command);
                    break;
            }
        }

        private IReadOnlyCollection<FormattedValue> ToFormattedValues(IReadOnlyDictionary<string, object> data)
        {
            return data.Select(entry => new FormattedValue(entry.Key, entry.Value.ToString())).ToArray();
        }

        private void TranslateAndForward(JupyterChannel channel, ZMQ.Message message, KernelInvocationContext context, KernelCommand command)
        {
            switch(message.Content)
            {
                case DisplayData displayData:
                    var displayedValueProduced = new DisplayedValueProduced(null, command, formattedValues: ToFormattedValues(displayData.Data));
                    context.Publish(displayedValueProduced);
                    break;
                case UpdateDisplayData updateDisplayData:
                    var valueId = GetValueId(updateDisplayData.Transient);
                    var displayedValueUpdated = new DisplayedValueUpdated(null, valueId, command, formattedValues: ToFormattedValues(updateDisplayData.Data));
                    context.Publish(displayedValueUpdated);
                    break;
                case ExecuteResult executeResult:
                    var returnValueProduced = new ReturnValueProduced(null, command, formattedValues: ToFormattedValues(executeResult.Data));
                    context.Publish(returnValueProduced);
                    break;
                case Stream stdout when stdout.Name == Stream.StandardOutput:
                    var standardOutputValueProduced = new StandardOutputValueProduced(command, new[] { new FormattedValue(PlainTextFormatter.MimeType, stdout.Text ?? string.Empty) });
                    context.Publish(standardOutputValueProduced);
                    break;
                case Stream stderr when stderr.Name == Stream.StandardError:
                    var standardErrorValueProduced = new StandardErrorValueProduced(command, new[] { new FormattedValue(PlainTextFormatter.MimeType, stderr.Text ?? string.Empty) });
                    context.Publish(standardErrorValueProduced);
                    break;
                case ExecuteInput when command is not SubmitCode:
                    throw new InvalidOperationException($"received {message.Header.MessageType} while current command is {command.GetType().Name}.");
                case ExecuteInput when command is SubmitCode:
                    break;
                default:
                    throw new InvalidOperationException($"{message.Header.MessageType} is not supported.");
            }
        }

        private string GetValueId(IReadOnlyDictionary<string, object> transient)
        {
            if(transient.TryGetValue("display_id", out var displayId))
            {
                return displayId is null ? Guid.NewGuid().ToString() : displayId.ToString();
            }
            return Guid.NewGuid().ToString();
        }

        public Task HandleAsync(SubmitCode command, KernelInvocationContext context)
        {
            EnsureClient(context.CancellationToken);

            var request = new ExecuteRequest(command.Code);
            var requestMessage = _client.CreateMessage(request, command.GetToken());
            TaskCompletionSource taskCompletionSource = new();
            _inflightCommands[command.GetToken()] = new(context, command, taskCompletionSource);
            _client.Send(JupyterChannel.Shell, requestMessage);
            return taskCompletionSource.Task;
        }

        private void EnsureClient(System.Threading.CancellationToken cancellationToken)
        {
            lock (_clientLock)
            {
                if(_client is null)
                {
                    var client = new JupyterClient(_kernelSession);
                    _client = client;
                    RegisterForDisposal(_client.Subscribe(HandleResponse));
                    RegisterForDisposal(_client);
                    RegisterForDisposal(_kernelSession);
                    _client.OpenChannels();
                    _kernelSession.StartAsync(_client.ConnectionInformation, cancellationToken).Wait(cancellationToken);
                    _client.StartAsync(cancellationToken);
                }
            }
        }

        private record CommandForwardingContext(KernelInvocationContext Context, KernelCommand Command, TaskCompletionSource CompletionSource);
    }
}