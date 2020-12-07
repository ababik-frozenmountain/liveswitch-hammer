﻿using FM.LiveSwitch;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FM.LiveSwitch.Hammer
{
    class ClusterTestIteration
    {
        public ClusterTestOptions Options { get; private set; }

        public ClusterTestIteration(ClusterTestOptions options)
        {
            Options = options;
        }

        public async Task Run(CancellationToken cancellationToken)
        {
            try
            {
                await RegisterClients(cancellationToken).ConfigureAwait(false);
                try
                {
                    await JoinChannel(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await StartTracks(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            await OpenConnections(cancellationToken).ConfigureAwait(false);
                            await VerifyConnections(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            await CloseConnections().ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        await StopTracks().ConfigureAwait(false);
                    }
                }
                finally
                {
                    await LeaveChannel().ConfigureAwait(false);
                }
            }
            finally
            {
                await UnregisterClients().ConfigureAwait(false);
            }
        }

        #region Register and Unregister Clients

        private Client _Client1;
        private Client _Client2;

        private async Task RegisterClients(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("  Registering clients...");

            _Client1 = new Client(Options.GatewayUrl, Options.ApplicationId, Options.User1, Options.Device1, null, null, Options.Region1);
            _Client2 = new Client(Options.GatewayUrl, Options.ApplicationId, Options.User2, Options.Device2, null, null, Options.Region2);

            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                Task.WhenAll(
                    _Client1.Register(Token.GenerateClientRegisterToken(_Client1, new ChannelClaim[0], Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                    _Client2.Register(Token.GenerateClientRegisterToken(_Client2, new ChannelClaim[0], Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
                )
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ClientRegisterException("One or more clients could not be registered.", result.Exception);
            }
        }

        private Task UnregisterClients()
        {
            Console.Error.WriteLine("  Unregistering clients...");

            return Task.WhenAll(
                _Client1.Unregister().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _Client2.Unregister().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            );
        }

        #endregion

        #region Join and Leave Channels

        private string _ChannelId;

        private Channel _Channel1;
        private Channel _Channel2;

        private async Task JoinChannel(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("  Joining channel...");

            _ChannelId = Utility.GenerateId();

            var channelTasks = Task.WhenAll(
                _Client1.Join(Token.GenerateClientJoinToken(_Client1, _ChannelId, Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _Client2.Join(Token.GenerateClientJoinToken(_Client2, _ChannelId, Options.SharedSecret)).AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            );
            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                channelTasks
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ChannelJoinException("One or more channels could not be joined.", result.Exception);
            }

            _Channel1 = channelTasks.Result[0];
            _Channel2 = channelTasks.Result[1];
        }

        private Task LeaveChannel()
        {
            Console.Error.WriteLine("  Leaving channel...");

            return Task.WhenAll(
                _Client1.Leave(_ChannelId).AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _Client2.Leave(_ChannelId).AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            );
        }

        #endregion

        #region Start and Stop Tracks

        private TaskCompletionSource<bool> _AudioVerify1;
        private TaskCompletionSource<bool> _AudioVerify2;
        private TaskCompletionSource<bool> _VideoVerify1;
        private TaskCompletionSource<bool> _VideoVerify2;

        private AudioTrack _LocalAudioTrack1;
        private AudioTrack _LocalAudioTrack2;
        private VideoTrack _LocalVideoTrack1;
        private VideoTrack _LocalVideoTrack2;
        private AudioTrack _RemoteAudioTrack1;
        private AudioTrack _RemoteAudioTrack2;
        private VideoTrack _RemoteVideoTrack1;
        private VideoTrack _RemoteVideoTrack2;

        private async Task StartTracks(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("  Starting tracks...");

            _AudioVerify1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _AudioVerify2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _VideoVerify1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _VideoVerify2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _LocalAudioTrack1 = CreateLocalAudioTrack();
            _LocalAudioTrack2 = CreateLocalAudioTrack();
            _LocalVideoTrack1 = CreateLocalVideoTrack();
            _LocalVideoTrack2 = CreateLocalVideoTrack();
            _RemoteAudioTrack1 = CreateRemoteAudioTrack(_AudioVerify1);
            _RemoteAudioTrack2 = CreateRemoteAudioTrack(_AudioVerify2);
            _RemoteVideoTrack1 = CreateRemoteVideoTrack(_VideoVerify1);
            _RemoteVideoTrack2 = CreateRemoteVideoTrack(_VideoVerify2);

            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                Task.WhenAll(
                    _LocalAudioTrack1.Source.Start().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                    _LocalAudioTrack2.Source.Start().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                    _LocalVideoTrack1.Source.Start().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                    _LocalVideoTrack2.Source.Start().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
                )
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new TrackStartException("One or more tracks could not be started.", result.Exception);
            }
        }

        private async Task StopTracks()
        {
            Console.Error.WriteLine("  Stopping tracks...");

            await Task.WhenAll(
                _LocalAudioTrack1.Source.Stop().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _LocalAudioTrack2.Source.Stop().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _LocalVideoTrack1.Source.Stop().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _LocalVideoTrack2.Source.Stop().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            ).ConfigureAwait(false);

            _LocalAudioTrack1.Destroy();
            _LocalAudioTrack2.Destroy();
            _LocalVideoTrack1.Destroy();
            _LocalVideoTrack2.Destroy();
            _RemoteAudioTrack1.Destroy();
            _RemoteAudioTrack2.Destroy();
            _RemoteVideoTrack1.Destroy();
            _RemoteVideoTrack2.Destroy();
        }

        #endregion

        #region Open and Close Connections

        private int _AudioSendCount1;
        private int _AudioSendCount2;
        private int _AudioReceiveCount1;
        private int _AudioReceiveCount2;
        private int _VideoSendCount1;
        private int _VideoSendCount2;
        private int _VideoReceiveCount1;
        private int _VideoReceiveCount2;

        private McuConnection _Connection1;
        private McuConnection _Connection2;

        private async Task OpenConnections(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("  Opening connections...");

            var audioStream1 = new AudioStream(_LocalAudioTrack1, _RemoteAudioTrack1);
            var audioStream2 = new AudioStream(_LocalAudioTrack2, _RemoteAudioTrack2);
            var videoStream1 = new VideoStream(_LocalVideoTrack1, _RemoteVideoTrack1);
            var videoStream2 = new VideoStream(_LocalVideoTrack2, _RemoteVideoTrack2);

            audioStream1.OnProcessFrame += (f) => _AudioSendCount1++;
            audioStream2.OnProcessFrame += (f) => _AudioSendCount2++;
            audioStream1.OnRaiseFrame += (f) => _AudioReceiveCount1++;
            audioStream2.OnRaiseFrame += (f) => _AudioReceiveCount2++;
            videoStream1.OnProcessFrame += (f) => _VideoSendCount1++;
            videoStream2.OnProcessFrame += (f) => _VideoSendCount2++;
            videoStream1.OnRaiseFrame += (f) => _VideoReceiveCount1++;
            videoStream2.OnRaiseFrame += (f) => _VideoReceiveCount2++;

            _Connection1 = _Channel1.CreateMcuConnection(audioStream1, videoStream1);
            _Connection2 = _Channel2.CreateMcuConnection(audioStream2, videoStream2);

            var result = await Task.WhenAny(
                Task.Delay(Timeout.Infinite, cancellationToken),
                Task.WhenAll(
                    _Connection1.Open().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                    _Connection2.Open().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
                )
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // check faulted
            if (result.IsFaulted)
            {
                throw new ConnectionOpenException("One or more connections could not be opened.", result.Exception);
            }
        }

        private Task CloseConnections()
        {
            Console.Error.WriteLine("  Closing connections...");

            return Task.WhenAll(
                _Connection1.Close().AsTask(TaskCreationOptions.RunContinuationsAsynchronously),
                _Connection2.Close().AsTask(TaskCreationOptions.RunContinuationsAsynchronously)
            );
        }

        private async Task VerifyConnections(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine("  Verifying media...");

            await Task.WhenAny(
                Task.Delay(Options.MediaTimeout * 1000, cancellationToken),
                Task.WhenAll(
                    _AudioVerify1.Task,
                    _AudioVerify2.Task,
                    _VideoVerify1.Task,
                    _VideoVerify2.Task
                )
            ).ConfigureAwait(false);

            // check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            if (!_AudioVerify1.Task.IsCompleted || _AudioVerify1.Task.IsFaulted)
            {
                throw new MediaStreamFailedException(StreamType.Audio, $"Audio stream #1 failed. Sent: {_AudioSendCount1} frames, but received {_AudioReceiveCount1} frames.");
            }

            if (!_AudioVerify2.Task.IsCompleted || _AudioVerify2.Task.IsFaulted)
            {
                throw new MediaStreamFailedException(StreamType.Audio, $"Audio stream #2 failed. Sent: {_AudioSendCount2} frames, but received {_AudioReceiveCount2} frames.");
            }

            if (!_VideoVerify1.Task.IsCompleted || _VideoVerify1.Task.IsFaulted)
            {
                throw new MediaStreamFailedException(StreamType.Video, $"Video stream #1 failed. Sent: {_VideoSendCount1} frames, but received {_VideoReceiveCount1} frames.");
            }

            if (!_VideoVerify2.Task.IsCompleted || _VideoVerify2.Task.IsFaulted)
            {
                throw new MediaStreamFailedException(StreamType.Video, $"Video stream #2 failed. Sent: {_VideoSendCount2} frames, but received {_VideoReceiveCount2} frames.");
            }
        }

        #endregion

        #region Create Tracks

        private AudioTrack CreateLocalAudioTrack()
        {
            var source = new FakeAudioSource(Opus.Format.DefaultConfig);
            var encoder = new Opus.Encoder();
            var packetizer = new Opus.Packetizer();
            return new AudioTrack(source).Next(encoder).Next(packetizer);
        }

        private AudioTrack CreateRemoteAudioTrack(TaskCompletionSource<bool> verify)
        {
            var depacketizer = new Opus.Depacketizer();
            var decoder = new Opus.Decoder();
            var sink = new NullAudioSink();
            sink.OnProcessFrame += (frame) =>
            {
                var dataBuffer = frame.LastBuffer.DataBuffer;

                var samples = new List<int>();
                for (var i = 0; i < dataBuffer.Length; i += sizeof(short))
                {
                    samples.Add(dataBuffer.Read16Signed(i));
                }

                var max = samples.Max();
                var min = samples.Min();
                if (min < -1 && max > 1) // not silent
                {
                    verify.TrySetResult(true);
                }
            };
            return new AudioTrack(depacketizer).Next(decoder).Next(sink);
        }

        private VideoTrack CreateLocalVideoTrack()
        {
            var source = new FakeVideoSource(new VideoConfig(320, 240, 30), VideoFormat.I420);
            var encoder = new Vp8.Encoder();
            var packetizer = new Vp8.Packetizer();
            return new VideoTrack(source).Next(encoder).Next(packetizer);
        }

        private VideoTrack CreateRemoteVideoTrack(TaskCompletionSource<bool> verify)
        {
            var depacketizer = new Vp8.Depacketizer();
            var decoder = new Vp8.Decoder();
            var sink = new NullVideoSink();
            sink.OnProcessFrame += (frame) =>
            {
                var buffer = frame.LastBuffer;

                var point = new Point(buffer.Width / 2, buffer.Height / 2);

                int r, g, b;
                if (buffer.IsRgbType)
                {
                    var index = point.Y * buffer.Width + point.X;
                    r = buffer.GetRValue(index);
                    g = buffer.GetGValue(index);
                    b = buffer.GetBValue(index);
                }
                else
                {
                    var yIndex = point.Y * buffer.Width + point.X;
                    var uvIndex = point.Y / 2 * buffer.Width / 2 + point.X / 2;

                    var y = buffer.GetYValue(yIndex);
                    var u = buffer.GetUValue(uvIndex);
                    var v = buffer.GetVValue(uvIndex);

                    // Rec.601
                    var kr = 0.299;
                    var kg = 0.587;
                    var kb = 0.114;

                    r = Math.Max(0, Math.Min(255, (int)(y + 2 * (v - 128) * (1 - kr))));
                    g = Math.Max(0, Math.Min(255, (int)(y - 2 * (u - 128) * (1 - kb) * kb / kg - 2 * (v - 128) * (1 - kr) * kr / kg)));
                    b = Math.Max(0, Math.Min(255, (int)(y + 2 * (u - 128) * (1 - kb))));
                }

                var max = new[] { r, g, b }.Max();
                if (max > 15) // not black
                {
                    verify.TrySetResult(true);
                }
            };
            return new VideoTrack(depacketizer).Next(decoder).Next(sink);
        }

        #endregion
    }
}
