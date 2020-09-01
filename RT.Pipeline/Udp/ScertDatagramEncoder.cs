﻿using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Server.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RT.Pipeline.Udp
{
    public class ScertDatagramEncoder : MessageToMessageEncoder<ScertDatagramPacket>
    {
        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<ScertDatagramEncoder>();

        readonly int maxPacketLength;

        public ScertDatagramEncoder(int maxPacketLength)
        {
            this.maxPacketLength = maxPacketLength;
        }

        protected override void Encode(IChannelHandlerContext ctx, ScertDatagramPacket message, List<object> output)
        {
            if (message is null)
                return;

            // Serialize
            var msgs = message.Message.Serialize();

            // Condense as much as possible
            var condensedMsgs = msgs.GroupWhileAggregating(0, (sum, item) => sum + item.Length, (sum, item) => sum < maxPacketLength).SelectMany(x => x);

            // 
            foreach (var msg in condensedMsgs)
            {
                var byteBuffer = ctx.Allocator.Buffer(msg.Length);
                byteBuffer.WriteBytes(msg);
                output.Add(new DatagramPacket(byteBuffer, message.Destination));
            }
        }

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Logger.Error(exception);
        }

    }
}
