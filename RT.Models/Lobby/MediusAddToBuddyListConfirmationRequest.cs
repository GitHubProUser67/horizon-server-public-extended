﻿using RT.Common;
using RT.Models.Misc;
using Server.Common;
using System;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassLobbyExt, MediusLobbyExtMessageIds.AddToBuddyListConfirmation)]
    public class MediusAddToBuddyListConfirmationRequest : BaseLobbyExtMessage, IMediusAddToBuddyListConfirmationRequest
    {

        public override byte PacketType => (byte)MediusLobbyExtMessageIds.AddToBuddyListConfirmation;

        public MessageId MessageID { get; set; }

        public string SessionKey; // SESSIONKEY_MAXLEN
        public int TargetAccountID { get; set; }
        public MediusBuddyAddType AddType { get; set; }

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();
            SessionKey = reader.ReadString(Constants.SESSIONKEY_MAXLEN);
            reader.ReadBytes(2);

            // 
            TargetAccountID = reader.ReadInt32();
            AddType = reader.Read<MediusBuddyAddType>();
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID ?? MessageId.Empty);
            writer.Write(SessionKey, Constants.SESSIONKEY_MAXLEN);
            writer.Write(new byte[2]);

            // 
            writer.Write(TargetAccountID);
            writer.Write(AddType);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID: {MessageID} " +
                $"SessionKey: {SessionKey} " +
                $"TargetAccountID: {TargetAccountID} " +
                $"AddType: {AddType} ";
        }
    }
}