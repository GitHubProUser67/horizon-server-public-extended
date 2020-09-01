using RT.Common;
using Server.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RT.Models
{
	[MediusMessage(NetMessageTypes.MessageClassLobby, MediusLobbyMessageIds.RemoveFromBuddyList)]
    public class MediusRemoveFromBuddyListRequest : BaseLobbyMessage, IMediusRequest
    {
		public override byte PacketType => (byte)MediusLobbyMessageIds.RemoveFromBuddyList;

        public string MessageID { get; set; }

        public string SessionKey; // SESSIONKEY_MAXLEN
        public int AccountID;

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.ReadString(Constants.MESSAGEID_MAXLEN);

            // 
            SessionKey = reader.ReadString(Constants.SESSIONKEY_MAXLEN);
            reader.ReadBytes(2);
            AccountID = reader.ReadInt32();
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID, Constants.MESSAGEID_MAXLEN);

            // 
            writer.Write(SessionKey, Constants.SESSIONKEY_MAXLEN);
            writer.Write(new byte[2]);
            writer.Write(AccountID);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID} " +
             $"SessionKey:{SessionKey} " +
$"AccountID:{AccountID}";
        }
    }
}
