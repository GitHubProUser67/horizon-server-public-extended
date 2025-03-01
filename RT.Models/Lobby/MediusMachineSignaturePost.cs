using RT.Common;
using Server.Common;
using System;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassLobby, MediusLobbyMessageIds.MachineSignaturePost)]
    public class MediusMachineSignaturePost : BaseLobbyMessage
    {

        public override byte PacketType => (byte)MediusLobbyMessageIds.MachineSignaturePost;

        /// <summary>
        /// Message ID
        /// </summary>
        public MessageId MessageID { get; set; }
        /// <summary>
        /// Session Key
        /// </summary>
        public string SessionKey;
        /// <summary>
        /// DNAS Machine Signature
        /// </summary>
        public byte[] MachineSignature = new byte[Constants.MACHINESIGNATURE_MAXLEN];

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            //
            MessageID = reader.Read<MessageId>();

            //
            SessionKey = reader.ReadString(Constants.SESSIONKEY_MAXLEN);
            MachineSignature = reader.ReadBytes(Constants.MACHINESIGNATURE_MAXLEN);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            //
            writer.Write(MessageID ?? MessageId.Empty);

            // 
            writer.Write(SessionKey, Constants.SESSIONKEY_MAXLEN);
            writer.Write(MachineSignature, Constants.DNASSIGNATURE_MAXLEN);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID: {MessageID} " +
                $"SessionKey: {SessionKey} " +
                $"MachineSignature: {BitConverter.ToString(MachineSignature)}";
        }
    }
}
