﻿using RT.Common;
using System;

namespace RT.Models
{
    [ScertMessage(RT_MSG_TYPE.RT_MSG_CLIENT_CRYPTKEY_PUBLIC)]
    public class RT_MSG_CLIENT_CRYPTKEY_PUBLIC : BaseScertMessage
    {
        public override RT_MSG_TYPE Id => RT_MSG_TYPE.RT_MSG_CLIENT_CRYPTKEY_PUBLIC;

        // 
        public byte[] PublicKey = null;

        public override void Deserialize(Server.Common.Stream.MessageReader reader)
        {
            PublicKey = reader.ReadBytes(0x40);
        }

        public override void Serialize(Server.Common.Stream.MessageWriter writer)
        {
            if (PublicKey == null || PublicKey.Length != 0x40)
                throw new InvalidOperationException("Unable to serialize CLIENT_GET_KEY key because key is either null or not 64 bytes long!");

            writer.Write(PublicKey);
        }
    }
}