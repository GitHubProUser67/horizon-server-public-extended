using RT.Common;
using Server.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RT.Models
{
	[MediusMessage(NetMessageTypes.MessageClassLobbyExt, MediusLobbyExtMessageIds.GetGameListFilterResponse)]
    public class MediusGetGameListFilterResponse : BaseLobbyExtMessage, IMediusResponse
    {
		public override byte PacketType => (byte)MediusLobbyExtMessageIds.GetGameListFilterResponse;

        public bool IsSuccess => StatusCode >= 0;

        public MessageId MessageID { get; set; }

        public MediusCallbackStatus StatusCode;
        public uint FilterID;
        public MediusGameListFilterField FilterField;
        public uint Mask;
        public MediusComparisonOperator ComparisonOperator;
        public int BaselineValue;
        public bool EndOfList;

        public override void Deserialize(BinaryReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();

            // 
            reader.ReadBytes(3);
            StatusCode = reader.Read<MediusCallbackStatus>();
            FilterID = reader.ReadUInt32();
            FilterField = reader.Read<MediusGameListFilterField>();
            Mask = reader.ReadUInt32();
            ComparisonOperator = reader.Read<MediusComparisonOperator>();
            BaselineValue = reader.ReadInt32();
            EndOfList = reader.ReadBoolean();
            reader.ReadBytes(3);
        }

        public override void Serialize(BinaryWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID);

            // 
            writer.Write(new byte[3]);
            writer.Write(StatusCode);
            writer.Write(FilterID);
            writer.Write(FilterField);
            writer.Write(Mask);
            writer.Write(ComparisonOperator);
            writer.Write(BaselineValue);
            writer.Write(EndOfList);
            writer.Write(new byte[3]);
        }


        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID:{MessageID} " +
             $"StatusCode:{StatusCode} " +
$"FilterID:{FilterID} " +
$"FilterField:{FilterField} " +
$"Mask:{Mask} " +
$"ComparisonOperator:{ComparisonOperator} " +
$"BaselineValue:{BaselineValue} " +
$"EndOfList:{EndOfList}";
        }
    }
}
