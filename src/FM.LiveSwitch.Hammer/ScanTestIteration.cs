﻿using System;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace FM.LiveSwitch.Hammer
{
    class ScanTestIteration
    {
        public ScanTestOptions Options { get; private set; }

        public ConcurrentDictionary<string, X509Certificate2> ServerCertificates { get; private set; }

        public ScanTestIteration(ScanTestOptions options)
        {
            Options = options;
            ServerCertificates = new ConcurrentDictionary<string, X509Certificate2>();
        }

        public async Task Run(string mediaServerId, CancellationToken cancellationToken)
        {
            try
            {
                TcpSocket.ClientSslAuthenticate = CheckRemoteCertificate;
                try
                {
                    await RegisterClient(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await JoinChannel(cancellationToken).ConfigureAwait(false);

                        // test a variety of scenarios
                        foreach (var mode in new[]
                        {
                            ScanTestMode.Host,
                            ScanTestMode.Stun,
                            ScanTestMode.TurnUdp,
                            ScanTestMode.TurnTcp,
                            ScanTestMode.Turns
                        })
                        {
                            if (Options.ShouldTest(mode))
                            {
                                try
                                {
                                    await OpenConnection(mediaServerId, mode, cancellationToken).ConfigureAwait(false);
                                }
                                finally
                                {
                                    await CloseConnection().ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    finally
                    {
                        await LeaveChannel().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await UnregisterClient().ConfigureAwait(false);
                }
            }
            finally
            {
                TcpSocket.ClientSslAuthenticate = null;
            }
        }

        #region Register and Unregister Clients

        private Client _Client;

        private async Task RegisterClient(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("Registering client...");

            _Client = new Client(Options.GatewayUrl, Options.ApplicationId)
            {
                Tag = Options.Tag
            };

            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                _Client.Register(Token.GenerateClientRegisterToken(_Client, new ChannelClaim[0], Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ClientRegisterException("One or more clients could not be registered.", result.Exception);
            }
        }

        private Task UnregisterClient()
        {
            Console.Error.WriteLine("Unregistering client...");

            return _Client.Unregister().AsTask(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion

        #region Join and Leave Channels

        private string _ChannelId;

        private Channel _Channel;

        private async Task JoinChannel(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("Joining channel...");

            _ChannelId = Utility.GenerateId();

            var channelTask = _Client.Join(Token.GenerateClientJoinToken(_Client, _ChannelId, Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously);
            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                channelTask
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ChannelJoinException("One or more channels could not be joined.", result.Exception);
            }

            _Channel = channelTask.Result;
        }

        private Task LeaveChannel()
        {
            Console.Error.WriteLine("Leaving channel...");

            return _Client.Leave(_ChannelId).AsTask(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        #endregion

        #region Open and Close Connections

        private McuConnection _Connection;

        private async Task OpenConnection(string mediaServerId, ScanTestMode mode, CancellationToken cancellationToken)
        {
            Console.Error.WriteLine($"Opening connection ({mode.ToDisplayString()})...");

            _Connection = _Channel.CreateMcuConnection(new AudioStream(new AudioTrack(new NullAudioSource(new Opus.Format { IsPacketized = true }))));

            _Connection.PreferredMediaServerId = mediaServerId;

            if (mode == ScanTestMode.Host)
            {
                _Connection.IceGatherPolicy = IceGatherPolicy.All;
            }
            else if (mode == ScanTestMode.Stun)
            {
                _Connection.IceGatherPolicy = IceGatherPolicy.NoHost;
            }
            else
            {
                _Connection.IceGatherPolicy = IceGatherPolicy.Relay;
            }
            Console.Error.WriteLine($"ICE gather policy: {_Connection.IceGatherPolicy}");

            _Connection.OnAutomaticIceServers += (connection, automaticIceServers) =>
            {
                FilterIceServers(automaticIceServers, mode);

                foreach (var automaticIceServer in automaticIceServers.Values)
                {
                    Console.Error.WriteLine($"ICE server: {automaticIceServer.Url}");
                }
            };

            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                _Connection.Open().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ConnectionOpenException("Connection could not be opened.", result.Exception);
            }

            if (_Connection.MediaServerId != mediaServerId)
            {
                throw new MediaServerMismatchException($"Connection connected to Media Server {_Connection.MediaServerId} instead of preferred Media Server {mediaServerId}.");
            }
        }

        private Task CloseConnection()
        {
            Console.Error.WriteLine("Closing connection...");

            return _Connection.Close().AsTask(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void FilterIceServers(IceServerCollection iceServers, ScanTestMode mode)
        {
            foreach (var iceServer in iceServers.Values)
            {
                if (!mode.IsCompatible(iceServer))
                {
                    iceServers.Remove(iceServer);
                }
            }
        }

        #endregion

        #region Check Remote Certificate

        private void CheckRemoteCertificate(SslStream sslStream, string targetHost, SslProtocols enabledSslProtocols)
        {
            sslStream.AuthenticateAsClient(targetHost, null, enabledSslProtocols, false);

            if (sslStream.RemoteCertificate is X509Certificate2 remoteCertificate && ServerCertificates.TryAdd(targetHost, remoteCertificate))
            {
                Console.Error.WriteLine(string.Join(Environment.NewLine, new[]
                {
                    $"TLS certificate subject: {remoteCertificate.Subject}",
                    $"TLS certificate issuer: {remoteCertificate.Issuer}",
                    $"TLS certificate issued: {remoteCertificate.NotBefore:yyyy-MM-ddTHH\\:mm\\:ss}",
                    $"TLS certificate expiry: {remoteCertificate.NotAfter:yyyy-MM-ddTHH\\:mm\\:ss}",
                    $"TLS certificate thumbprint: {remoteCertificate.Thumbprint}"
                }));
            }
        }

        #endregion
    }
}
