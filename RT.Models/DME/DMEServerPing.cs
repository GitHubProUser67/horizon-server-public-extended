﻿using RT.Common;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassDME, MediusDmeMessageIds.Ping)]
    public class DMEServerPing : BaseDMEMessage
    {

        public override byte PacketType => (byte)MediusDmeMessageIds.Ping;

        public uint Unk1;
        public byte Unk2;
        public byte Unk3;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            Unk1 = reader.ReadUInt32();
            Unk2 = reader.ReadByte();
            Unk3 = reader.ReadByte();
            reader.ReadBytes(2);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            // 
            writer.Write(Unk1);
            writer.Write(Unk2);
            writer.Write(Unk3);
            writer.Write(new byte[2]);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"Unk1: {Unk1} " +
                $"Unk2: {Unk2} " +
                $"Unk3: {Unk3} ";
        }
    }
}
