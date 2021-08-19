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
        private RequestSocket _hbSocket;
        private readonly object _channelsLock = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();

        public ConnectionInformation ConnectionInformation { get; private set; }

        public JupyterClient(JupyterKernelSession jupyterKernelSession)
	    {
            _jupyterKernelSession = jupyterKernelSession ?? throw new ArgumentNullException(nameof(jupyterKernelSession));
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (!ChannelsAreOpen())
            {
                throw new InvalidOperationException("Channels must be open before starting the client");
            }
            
            cancellationToken.Register(() => _cancellationTokenSource.Cancel());

            var token = _cancellationTokenSource.Token;

            foreach(var entry in _sockets)
            {
                var receiver = new MessageReceiver(entry.Value);
                Task.Run(async () =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        var message = await receiver.ReceiveAsync(token);
                        _messageChannel.OnNext((entry.Key, message));
                    }
                }, token);
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
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            CloseChannels();
        }

        private void CloseChannels()
        {
            lock (_channelsLock)
            {
                if (ChannelsAreOpen())
                {
                    _shellSocket.Dispose();
                    _shellSocket = null;

                    _controlSocket.Dispose();
                    _controlSocket = null;

                    _ioPubSocket.Dispose();
                    _ioPubSocket = null;

                    _stdInSocket.Dispose();
                    _stdInSocket = null;

                    _hbSocket.Dispose();
                    _hbSocket = null;
                }
            }
        }

        public void OpenChannels()
        {
            lock (_channelsLock)
            {
                if (ChannelsAreOpen())
                {
                    throw new InvalidOperationException("Cannot open Channels when they are already open.");
                }

                _shellSocket = new DealerSocket();
                _shellSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
                _shellSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
                var shellPort = _shellSocket.BindRandomPort("tcp://localhost");

                _controlSocket = new DealerSocket();
                _controlSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
                _controlSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
                var controlPort = _controlSocket.BindRandomPort("tcp://localhost");

                _ioPubSocket = new SubscriberSocket();
                _ioPubSocket.Subscribe("");
                _ioPubSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
                _ioPubSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
                var ioPubPort = _ioPubSocket.BindRandomPort("tcp://localhost");

                _stdInSocket = new DealerSocket();
                _stdInSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
                _stdInSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
                var stdInPort = _stdInSocket.BindRandomPort("tcp://localhost");

                _hbSocket = new RequestSocket();
                _hbSocket.Options.Identity = Encoding.UTF8.GetBytes(_jupyterKernelSession.Session.Id);
                _hbSocket.Options.Linger = TimeSpan.FromMilliseconds(1000);
                var hbSocketPort = _stdInSocket.BindRandomPort("tcp://localhost");

                ConnectionInformation = new ConnectionInformation
                {
                    ControlPort = controlPort,
                    ShellPort = shellPort,
                    IOPubPort = ioPubPort,
                    StdinPort = stdInPort,
                    HBPort = hbSocketPort,
                    Transport = "tcp",
                    IP = "localhost",
                    Key = _jupyterKernelSession.Session.Key,
                    SignatureScheme = "hmac-sha256"
                };

                _signatureValidator = new SignatureValidator(_jupyterKernelSession.Session.Key, "HMACSHA256");

                _sockets = new Dictionary<JupyterChannel, NetMQSocket>
                {
                    [JupyterChannel.Shell] = _shellSocket,
                    [JupyterChannel.Control] = _controlSocket,
                    [JupyterChannel.IoPub] = _ioPubSocket,
                    [JupyterChannel.StdIn] = _stdInSocket
                };

                _senders = new Dictionary<JupyterChannel, MessageSender>
                {
                    [JupyterChannel.Shell] = new(_shellSocket, _signatureValidator),
                    [JupyterChannel.Control] = new(_controlSocket, _signatureValidator),
                    [JupyterChannel.IoPub] = new(_ioPubSocket, _signatureValidator),
                    [JupyterChannel.StdIn] = new(_stdInSocket, _signatureValidator)
                };
            }
        }

        private bool ChannelsAreOpen()
        {
            return _hbSocket is not null
                   && _shellSocket is not null
                   && _controlSocket is not null
                   && _ioPubSocket is not null
                   && _stdInSocket is not null;
        }
    }
}
