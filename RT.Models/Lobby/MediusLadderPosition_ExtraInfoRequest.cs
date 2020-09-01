using RT.Common;
using Server.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RT.Models
{
	[MediusMessage(NetMessageTypes.MessageClassLobby, MediusLobbyMessageIds.LadderPosition_ExtraInfo)]
    public class MediusLadderPosition_ExtraInfoRequest : BaseLobbyMessage, IMediusRequest
    {
		public override byte PacketType => (byte)MediusLobbyMessageIds.LadderPosition_ExtraInfo;

        public string MessageID { get; set; }

        public int AccountID;
        public int LadderStatIndex;
        public MediusSortOrder SortOrder;

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.ReadString(Constants.MESSAGEID_MAXLEN);

            // 
            reader.ReadBytes(3);
            AccountID = reader.ReadInt32();
            LadderStatIndex = reader.ReadInt32();
            SortOrder = reader.Read<MediusSortOrder>();
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID, Constants.MESSAGEID_MAXLEN);

            // 
            writer.Write(new byte[3]);
            writer.Write(AccountID);
            writer.Write(LadderStatIndex);
            writer.Write(SortOrder);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID} " +
             $"AccountID:{AccountID} " +
$"LadderStatIndex:{LadderStatIndex} " +
$"SortOrder:{SortOrder}";
        }
    }
}
