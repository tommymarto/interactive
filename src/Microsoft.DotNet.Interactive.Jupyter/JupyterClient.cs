// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive.Jupyter.ZMQ;
using NetMQ;
using NetMQ.Sockets;

namespace Microsoft.DotNet.Interactive.Jupyter
{
    public class JupyterClient : IObservable<(JupyterChannel channel, ZMQ.Message message)>, IDisposable
    {
        private readonly JupyterKernelSession _jupyterKernelSession;
        private SignatureValidator _signatureValidator;
        private DealerSocket _shellSocket;
        private DealerSocket _controlSocket;
        private SubscriberSocket _ioPubSocket;
        private DealerSocket _stdInSocket;
        private Dictionary<JupyterChannel, NetMQSocket> _sockets;
        private Dictionary<JupyterChannel, MessageSender> _senders;
        private readonly Subject<(JupyterChannel channel, ZMQ.Message message)> _messageChannel = new();

        public JupyterClient(JupyterKernelSession jupyterKernelSession)
	    {
            _jupyterKernelSession = jupyterKernelSession ?? throw new ArgumentNullException(nameof(jupyterKernelSession));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _signatureValidator = new SignatureValidator(_jupyterKernelSession.Session.Key, "HMACSHA256");

            _shellSocket = new DealerSocket();
            _shellSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
            _shellSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
            _shellSocket.Connect($"tcp://localhost:{_jupyterKernelSession.ConnectionInformation.ShellPort}");

            _controlSocket = new DealerSocket();
            _controlSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
            _controlSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
            _controlSocket.Connect($"tcp://localhost:{_jupyterKernelSession.ConnectionInformation.ShellPort}");

            _ioPubSocket = new SubscriberSocket();
            _ioPubSocket.Subscribe("");
            _ioPubSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
            _ioPubSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
            _ioPubSocket.Connect($"tcp://localhost:{_jupyterKernelSession.ConnectionInformation.ShellPort}");

            _stdInSocket = new DealerSocket();
            _stdInSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
            _stdInSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
            _stdInSocket.Connect($"tcp://localhost:{_jupyterKernelSession.ConnectionInformation.ShellPort}");

            _sockets = new Dictionary<JupyterChannel, NetMQSocket>
            {
                [JupyterChannel.Shell] = _shellSocket,
                [JupyterChannel.Control] = _controlSocket,
                [JupyterChannel.IoPub] = _ioPubSocket,
                [JupyterChannel.StdIn] = _stdInSocket
            };

            _senders = new Dictionary<JupyterChannel, MessageSender>
            {
                [JupyterChannel.Shell] = new MessageSender(_shellSocket, _signatureValidator),
                [JupyterChannel.Control] = new MessageSender(_controlSocket, _signatureValidator),
                [JupyterChannel.IoPub] = new MessageSender(_ioPubSocket, _signatureValidator),
                [JupyterChannel.StdIn] = new MessageSender(_stdInSocket, _signatureValidator)
            };

            foreach(var entry in _sockets)
            {
                var receiver = new MessageReceiver(entry.Value);
                Task.Run(() =>
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var message = receiver.Receive();
                        _messageChannel.OnNext((entry.Key, message));
                    }
                }, cancellationToken);
            }

            return Task.CompletedTask;
        }

        public void Send(JupyterChannel channel, ZMQ.Message message)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _senders[channel].Send(message);
        }

        public ZMQ.Message CreateMessage<T>(T message) where T : Protocol.Message
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return ZMQ.Message.Create(message, _jupyterKernelSession.Session);
        }

        public ZMQ.Message CreateMessage<T>(T message, string commandToken) where T : Protocol.Message
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (string.IsNullOrWhiteSpace(commandToken))
            {
                throw new ArgumentException($"'{nameof(commandToken)}' cannot be null or whitespace.", nameof(commandToken));
            }

            var metaData = new Dictionary<string, object>
            {
                ["commandToken"] = commandToken
            };

            return ZMQ.Message.Create(message, _jupyterKernelSession.Session, metaData: metaData);
        }

        public IDisposable Subscribe(IObserver<(JupyterChannel channel, ZMQ.Message message)> observer)
        {
            return _messageChannel.Subscribe(observer);
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }

    public enum JupyterChannel { Shell, Control, IoPub, StdIn }
}
