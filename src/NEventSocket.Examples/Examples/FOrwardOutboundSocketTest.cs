﻿namespace NEventSocket.Examples.Examples
{
    using System;
    using System.Reactive.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    using ColoredConsole;

    using Net.CommandLine;

    using NEventSocket.FreeSwitch;
    using NEventSocket.Util;

    public class ForwardOutboundSocketTest : ICommandLineTask, IDisposable
    {
        private OutboundListener listener;

        private OutboundListener listener2;

        public Task Run(CancellationToken cancellationToken)
        {
            this.listener = new OutboundListener(8084);
            this.listener2 = new OutboundListener(8085);

            /* this example demonstrates forwarding an outbound connection from one listener to another.
               FS Dialplan is configured to hit localhost:8084
               By parking the call then using sched_api to schedule a transfer, we can disconnect the socket on localhost:8084
               and allow  transfer the call to localhost:8085 without hanging up the call when the first socket disconnects. */

            this.listener.Connections.Subscribe(
                async connection =>
                {
                    try
                    {
                        await connection.Connect();
                        await connection.ExecuteApplication(connection.ChannelData.UUID, "pre_answer");

                        //let's pretend we did a database or service registry lookup to find the socket server we want to route to - localhost:8085 in this example

                        await connection.Api("uuid_park", connection.ChannelData.UUID);
                        await connection.Api("sched_api +0.01 none uuid_transfer {0} 'socket:127.0.0.1:8085 async full' inline".Fmt(connection.ChannelData.UUID));
                        await connection.Exit();
                    }
                    catch (OperationCanceledException)
                    {
                        
                    }
                });

            this.listener2.Connections.Subscribe(
                async connection =>
                {
                    await connection.Connect();
                    Console.WriteLine("New Socket connected");

                    connection.Events.Where(x => x.UUID == connection.ChannelData.UUID && x.EventName == EventName.ChannelHangup)
                        .Take(1)
                        .Subscribe(
                            async e =>
                            {
                                ColorConsole.WriteLine(
                                    "Hangup Detected on A-Leg ".Red(),
                                    e.Headers[HeaderNames.CallerUniqueId],
                                    " ",
                                    e.Headers[HeaderNames.HangupCause]);

                                await connection.Exit();
                            });


                    connection.Events.Where(x => x.UUID == connection.ChannelData.UUID && x.EventName == EventName.Dtmf)
                       .Subscribe(
                           async e =>
                           {
                               ColorConsole.WriteLine(
                                   "DTMF Detected on A-Leg ".Red(),
                                   e.Headers[HeaderNames.CallerUniqueId].Yellow(),
                                   " ",
                                   e.Headers[HeaderNames.DtmfDigit].Red());

                               await connection.Play(connection.ChannelData.UUID, "$${base_dir}/sounds/en/us/callie/misc/8000/misc-freeswitch_is_state_of_the_art.wav");
                           });

                    var uuid = connection.ChannelData.Headers[HeaderNames.UniqueId];

                    await connection.SubscribeEvents(EventName.Dtmf, EventName.ChannelHangup);

                    await connection.Linger();
                    await connection.ExecuteApplication(uuid, "answer", null, true, false);

                    var result =
                        await connection.Play(uuid, "$${base_dir}/sounds/en/us/callie/misc/8000/misc-freeswitch_is_state_of_the_art.wav");
                });

            this.listener.Start();

            this.listener2.Start();

            Console.WriteLine("Listener started on 8084 and 8085. Press [Enter] to exit");
            Console.ReadLine();

            return Task.FromResult(0);
        }

        public void Dispose()
        {
            this.listener.Dispose();
            this.listener2.Dispose();
        }
    }
}