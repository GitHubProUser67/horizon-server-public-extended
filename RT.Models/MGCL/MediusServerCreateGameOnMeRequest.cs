using RT.Common;
using Server.Common;

namespace RT.Models
{
    [MediusMessage(NetMessageClass.MessageClassLobbyReport, MediusMGCLMessageIds.ServerCreateGameOnMeRequest)]
    public class MediusServerCreateGameOnMeRequest : BaseMGCLMessage, IMediusRequest
    {

        public override byte PacketType => (byte)MediusMGCLMessageIds.ServerCreateGameOnMeRequest;

        public MessageId MessageID { get; set; }
        public string GameName; // MGCL_GAMENAME_MAXLEN
        public byte[] GameStats = new byte[Constants.MGCL_GAMESTATS_MAXLEN];
        public string GamePassword; // MGCL_GAMEPASSWORD_MAXLEN
        public int ApplicationID;
        public int MaxClients;
        public int MinClients;
        public int GameLevel;
        public int PlayerSkillLevel;
        public int RulesSet;
        public int GenericField1;
        public int GenericField2;
        public int GenericField3;
        public int GenericField4;
        public int GenericField5;
        public int GenericField6;
        public int GenericField7;
        public int GenericField8;
        public MGCL_GAME_HOST_TYPE GameHostType;
        public NetAddressList AddressList;
        public int WorldID;
        public int AccountID;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            // 
            base.Deserialize(reader);

            // 
            MessageID = reader.Read<MessageId>();

            if (reader.MediusVersion == 113)
            {
                GameName = reader.ReadString(Constants.MGCL_GAMENAME_MAXLEN1);
            }
            else
            {
                GameName = reader.ReadString(Constants.MGCL_GAMENAME_MAXLEN);
            }
            GameStats = reader.ReadBytes(Constants.MGCL_GAMESTATS_MAXLEN);

            if (reader.MediusVersion == 113)
            {
                GamePassword = reader.ReadString(Constants.MGCL_GAMEPASSWORD_MAXLEN1);
            }
            else
            {
                GamePassword = reader.ReadString(Constants.MGCL_GAMEPASSWORD_MAXLEN);
            }
            reader.ReadBytes(3);
            ApplicationID = reader.ReadInt32();
            MaxClients = reader.ReadInt32();
            MinClients = reader.ReadInt32();
            GameLevel = reader.ReadInt32();
            PlayerSkillLevel = reader.ReadInt32();
            RulesSet = reader.ReadInt32();
            GenericField1 = reader.ReadInt32();
            GenericField2 = reader.ReadInt32();
            GenericField3 = reader.ReadInt32();
            GenericField4 = reader.ReadInt32();
            GenericField5 = reader.ReadInt32();
            GenericField6 = reader.ReadInt32();
            GenericField7 = reader.ReadInt32();
            GenericField8 = reader.ReadInt32();
            GameHostType = reader.Read<MGCL_GAME_HOST_TYPE>();
            //reader.ReadBytes(3);
            AddressList = reader.Read<NetAddressList>();
            WorldID = reader.ReadInt32();
            AccountID = reader.ReadInt32();
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            // 
            base.Serialize(writer);

            // 
            writer.Write(MessageID ?? MessageId.Empty);
            writer.Write(GameName, Constants.MGCL_GAMENAME_MAXLEN);
            writer.Write(GameStats, Constants.MGCL_GAMESTATS_MAXLEN);
            writer.Write(GamePassword, Constants.MGCL_GAMEPASSWORD_MAXLEN);
            writer.Write(new byte[3]);
            writer.Write(ApplicationID);
            writer.Write(MaxClients);
            writer.Write(MinClients);
            writer.Write(GameLevel);
            writer.Write(PlayerSkillLevel);
            writer.Write(RulesSet);
            writer.Write(GenericField1);
            writer.Write(GenericField2);
            writer.Write(GenericField3);
            writer.Write(GenericField4);
            writer.Write(GenericField5);
            writer.Write(GenericField6);
            writer.Write(GenericField7);
            writer.Write(GenericField8);
            writer.Write(GameHostType);
            //writer.Write(new byte[3]);
            writer.Write(AddressList);
            writer.Write(WorldID);
            writer.Write(AccountID);
        }

        public override string ToString()
        {
            return base.ToString() + " " +
                $"MessageID: {MessageID} " +
                $"GameName: {GameName} " +
                $"GameStats: {GameStats} " +
                $"GamePassword: {GamePassword} " +
                $"ApplicationID: {ApplicationID} " +
                $"MaxClients: {MaxClients} " +
                $"MinClients: {MinClients} " +
                $"GameLevel: {GameLevel} " +
                $"PlayerSkillLevel: {PlayerSkillLevel} " +
                $"RulesSet: {RulesSet} " +
                $"GenericField1: {GenericField1:X8} " +
                $"GenericField2: {GenericField2:X8} " +
                $"GenericField3: {GenericField3:X8} " +
                $"GenericField4: {GenericField4:X8} " +
                $"GenericField5: {GenericField5:X8} " +
                $"GenericField6: {GenericField6:X8} " +
                $"GenericField7: {GenericField7:X8} " +
                $"GenericField8: {GenericField8:X8} " +
                $"GameHostType: {GameHostType} " +
                $"AddressList: {AddressList} " +
                $"WorldID: {WorldID} " +
                $"AccountID: {AccountID}";
        }
    }
}