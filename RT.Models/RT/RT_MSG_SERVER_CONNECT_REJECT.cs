﻿using RT.Common;
using Server.Common;

namespace RT.Models
{
    [ScertMessage(RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_REJECT)]
    public class RT_MSG_SERVER_CONNECT_REJECT : BaseScertMessage
    {
        public override RT_MSG_TYPE Id => RT_MSG_TYPE.RT_MSG_SERVER_CONNECT_REJECT;

        //
        public RT_MSG_CONNECT_REJECT_REASON rejectReason;
        public ushort Unk1;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            rejectReason = reader.Read<RT_MSG_CONNECT_REJECT_REASON>();
            Unk1 = reader.ReadUInt16();
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            writer.Write(rejectReason);
            writer.Write(Unk1);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"rejectReason: {rejectReason} " +
                $"Unk1: {Unk1}";
        }
    }
}