﻿using RT.Common;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassDME, MediusDmeMessageIds.Ping)]
    public class TypePing : BaseDMEMessage
    {

        public override byte PacketType => (byte)MediusDmeMessageIds.Ping;

        public long TimeOfSend;
        public byte PingInstance;
        public bool RequestEcho;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            TimeOfSend = reader.ReadUInt32();
            PingInstance = reader.ReadByte();
            RequestEcho = reader.ReadBoolean();
            reader.ReadBytes(2);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            // 
            writer.Write(TimeOfSend);
            writer.Write(PingInstance);
            writer.Write(RequestEcho);
            writer.Write(new byte[2]);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"TimeOfSend: {TimeOfSend} " +
                $"PingInstance: {PingInstance} " +
                $"RequestEcho: {RequestEcho} ";
        }
    }
}
