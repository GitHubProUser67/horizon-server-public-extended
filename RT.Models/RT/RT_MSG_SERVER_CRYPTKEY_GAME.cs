﻿using RT.Common;
using System;

namespace RT.Models
{
    [ScertMessage(RT_MSG_TYPE.RT_MSG_SERVER_CRYPTKEY_GAME)]
    public class RT_MSG_SERVER_CRYPTKEY_GAME : BaseScertMessage
    {
        public override RT_MSG_TYPE Id => RT_MSG_TYPE.RT_MSG_SERVER_CRYPTKEY_GAME;

        // 
        public byte[] GameKey = null;
        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            GameKey = reader.ReadBytes(0x40);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            if (GameKey == null || GameKey.Length != 0x40)
                throw new InvalidOperationException("Unable to serialize SERVER_SET_SERVER_GAME_KEY because key is either null or not 64 bytes long!");

            writer.Write(GameKey);
        }
    }
}