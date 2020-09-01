using RT.Common;
using Server.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RT.Models
{
	[MediusMessage(NetMessageTypes.MessageClassLobbyExt, MediusLobbyExtMessageIds.ChannelList_ExtraInfo)]
    public class MediusChannelList_ExtraInfoRequest : BaseLobbyExtMessage, IMediusRequest
    {

		public override byte PacketType => (byte)MediusLobbyExtMessageIds.ChannelList_ExtraInfo;

        public MessageId MessageID { get; set; }

        public ushort PageID;
        public ushort PageSize;

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();

            // 
            reader.ReadBytes(1);
            PageID = reader.ReadUInt16();
            PageSize = reader.ReadUInt16();
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID);

            // 
            writer.Write(new byte[1]);
            writer.Write(PageID);
            writer.Write(PageSize);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID} " +
             $"PageID:{PageID} " +
$"PageSize:{PageSize}";
        }
    }
}
