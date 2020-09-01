using RT.Common;
using Server.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RT.Models
{
	[MediusMessage(NetMessageTypes.MessageClassLobby, MediusLobbyMessageIds.GameWorldPlayerListResponse)]
    public class MediusGameWorldPlayerListResponse : BaseLobbyMessage, IMediusResponse
    {

		public override byte PacketType => (byte)MediusLobbyMessageIds.GameWorldPlayerListResponse;

        public bool IsSuccess => StatusCode >= 0;

        public string MessageID { get; set; }

        public MediusCallbackStatus StatusCode;
        public int AccountID;
        public string AccountName; // ACCOUNTNAME_MAXLEN
        public byte[] Stats = new byte[Constants.ACCOUNTSTATS_MAXLEN];
        public MediusConnectionType ConnectionClass;
        public bool EndOfList;

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.ReadString(Constants.MESSAGEID_MAXLEN);

            // 
            reader.ReadBytes(3);
            StatusCode = reader.Read<MediusCallbackStatus>();
            AccountID = reader.ReadInt32();
            AccountName = reader.ReadString(Constants.ACCOUNTNAME_MAXLEN);
            Stats = reader.ReadBytes(Constants.ACCOUNTSTATS_MAXLEN);
            ConnectionClass = reader.Read<MediusConnectionType>();
            EndOfList = reader.ReadBoolean();
            reader.ReadBytes(3);
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID, Constants.MESSAGEID_MAXLEN);

            // 
            writer.Write(new byte[3]);
            writer.Write(StatusCode);
            writer.Write(AccountID);
            writer.Write(AccountName, Constants.ACCOUNTNAME_MAXLEN);
            writer.Write(Stats, Constants.ACCOUNTSTATS_MAXLEN);
            writer.Write(ConnectionClass);
            writer.Write(EndOfList);
            writer.Write(new byte[3]);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID} " +
             $"StatusCode:{StatusCode} " +
$"AccountID:{AccountID} " +
$"AccountName:{AccountName} " +
$"Stats:{Stats} " +
$"ConnectionClass:{ConnectionClass} " +
$"EndOfList:{EndOfList}";
        }
    }
}
