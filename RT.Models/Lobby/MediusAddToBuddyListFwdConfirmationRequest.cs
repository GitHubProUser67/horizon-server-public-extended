﻿using RT.Common;
using Server.Common;
using System;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassLobbyExt, MediusLobbyExtMessageIds.AddToBuddyListFwdConfirmation)]
    public class MediusAddToBuddyListFwdConfirmationRequest : BaseLobbyExtMessage, IMediusRequest
    {

        public override byte PacketType => (byte)MediusLobbyExtMessageIds.AddToBuddyListFwdConfirmation;

        public MessageId MessageID { get; set; }

        public int OriginatorAccountID;
        public string OriginatorAccountName;
        public MediusBuddyAddType AddType;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();
            reader.ReadBytes(3);

            // 
            OriginatorAccountID = reader.ReadInt32();
            OriginatorAccountName = reader.ReadString(Constants.ACCOUNTNAME_MAXLEN);
            AddType = reader.Read<MediusBuddyAddType>();
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID ?? MessageId.Empty);
            writer.Write(new byte[3]);

            // 
            writer.Write(OriginatorAccountID);
            writer.Write(OriginatorAccountName, Constants.ACCOUNTNAME_MAXLEN);
            writer.Write(AddType);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID: {MessageID} " +
                $"OriginatorAccountID: {OriginatorAccountID} " +
                $"OriginatorAccountName: {OriginatorAccountName} " +
                $"AddType: {AddType}";
        }
    }
}