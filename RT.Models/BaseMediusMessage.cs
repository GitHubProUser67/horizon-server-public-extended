﻿using RT.Common;
using Server.Common;
using Server.Common.Logging;
using System;
using System.Collections.Generic;

namespace RT.Models
{
    #region MediusMessageAttribute
    [AttributeUsage(AttributeTargets.Class)]
    public class MediusMessageAttribute : Attribute
    {
        public NetMessageClass MessageClass;
        public byte MessageType;
        public GhsOpcode GhsMsgType;

        public MediusMessageAttribute(NetMessageClass msgClass, MediusDmeMessageIds msgType)
        {
            MessageClass = msgClass;
            MessageType = (byte)msgType;
        }

        public MediusMessageAttribute(NetMessageClass msgClass, MediusMGCLMessageIds msgType)
        {
            MessageClass = msgClass;
            MessageType = (byte)msgType;
        }

        public MediusMessageAttribute(NetMessageClass msgClass, MediusLobbyMessageIds msgType)
        {
            MessageClass = msgClass;
            MessageType = (byte)msgType;
        }

        public MediusMessageAttribute(NetMessageClass msgClass, MediusLobbyExtMessageIds msgType)
        {
            MessageClass = msgClass;
            MessageType = (byte)msgType;
        }

        public MediusMessageAttribute(GhsOpcode msgType)
        {
            GhsMsgType = msgType;
        }

        public MediusMessageAttribute(NetMessageClass msgClass, NetMessageTypeIds msgType)
        {
            MessageClass = msgClass;
            MessageType = (byte)msgType;
        }
    }
    #endregion

    #region BaseMediusMessage
    public abstract class BaseMediusMessage
    {
        /// <summary>
        /// Message class.
        /// </summary>
        public abstract NetMessageClass PacketClass { get; }

        /// <summary>
        /// Message type.
        /// </summary>
        public abstract byte PacketType { get; }

        /// <summary>
        /// When true, skips encryption when sending this particular message instance.
        /// </summary>
        public virtual bool SkipEncryption { get; set; } = false;

        public BaseMediusMessage()
        {

        }

        #region Serialization

        /// <summary>
        /// Deserializes the message from plaintext.
        /// </summary>
        /// <param name="reader"></param>
        public virtual void Deserialize(Server.Common.Stream.MessageReader reader)
        {

        }

        /// <summary>
        /// Serialize contents of the message.
        /// </summary>
        public virtual void Serialize(Server.Common.Stream.MessageWriter writer)
        {

        }

        #endregion

        #region Logging

        /// <summary>
        /// Whether or not this message passes the log filter.
        /// </summary>
        public virtual bool CanLog()
        {
            switch (PacketClass)
            {
                case NetMessageClass.MessageClassDME: return LogSettings.Singleton?.IsLog((MediusDmeMessageIds)this.PacketType) ?? false;
                case NetMessageClass.MessageClassLobby: return LogSettings.Singleton?.IsLog((MediusLobbyMessageIds)this.PacketType) ?? false;
                case NetMessageClass.MessageClassLobbyReport: return LogSettings.Singleton?.IsLog((MediusMGCLMessageIds)this.PacketType) ?? false;
                case NetMessageClass.MessageClassLobbyExt: return LogSettings.Singleton?.IsLog((MediusLobbyExtMessageIds)this.PacketType) ?? false;
                default: return true;
            }
        }

        #endregion

        #region Dynamic Instantiation

        private static Dictionary<MediusDmeMessageIds, Type> _dmeMessageClassById = null;
        private static Dictionary<MediusMGCLMessageIds, Type> _mgclMessageClassById = null;
        private static Dictionary<MediusLobbyMessageIds, Type> _lobbyMessageClassById = null;
        private static Dictionary<MediusLobbyExtMessageIds, Type> _lobbyExtMessageClassById = null;
        private static int _messageClassByIdLockValue = 0;
        private static object _messageClassByIdLockObject = _messageClassByIdLockValue;


        private static void Initialize()
        {
            lock (_messageClassByIdLockObject)
            {
                if (_dmeMessageClassById != null)
                    return;

                _dmeMessageClassById = new Dictionary<MediusDmeMessageIds, Type>();
                _mgclMessageClassById = new Dictionary<MediusMGCLMessageIds, Type>();
                _lobbyMessageClassById = new Dictionary<MediusLobbyMessageIds, Type>();
                _lobbyExtMessageClassById = new Dictionary<MediusLobbyExtMessageIds, Type>();

                // Populate
                var assembly = System.Reflection.Assembly.GetAssembly(typeof(BaseMediusMessage));
                var types = assembly.GetTypes();

                foreach (Type classType in types)
                {
                    // Objects by Id
                    var attrs = (MediusMessageAttribute[])classType.GetCustomAttributes(typeof(MediusMessageAttribute), true);
                    if (attrs != null && attrs.Length > 0)
                    {
                        switch (attrs[0].MessageClass)
                        {
                            case NetMessageClass.MessageClassDME:
                                {
                                    _dmeMessageClassById.Add((MediusDmeMessageIds)attrs[0].MessageType, classType);
                                    break;
                                }
                            case NetMessageClass.MessageClassLobbyReport:
                                {
                                    _mgclMessageClassById.Add((MediusMGCLMessageIds)attrs[0].MessageType, classType);
                                    break;
                                }
                            case NetMessageClass.MessageClassLobby:
                                {
                                    _lobbyMessageClassById.Add((MediusLobbyMessageIds)attrs[0].MessageType, classType);
                                    break;
                                }
                            case NetMessageClass.MessageClassLobbyExt:
                                {
                                    _lobbyExtMessageClassById.Add((MediusLobbyExtMessageIds)attrs[0].MessageType, classType);
                                    break;
                                }
                        }
                    }
                }
            }
        }

        public static BaseMediusMessage Instantiate(Server.Common.Stream.MessageReader reader)
        {
            BaseMediusMessage msg;
            Type classType = null;

            // Init
            Initialize();

            NetMessageClass msgClass = reader.Read<NetMessageClass>();
            var msgType = reader.ReadByte();

            switch (msgClass)
            {
                case NetMessageClass.MessageClassDME:
                    {
                        if (!_dmeMessageClassById.TryGetValue((MediusDmeMessageIds)msgType, out classType))
                            classType = null;
                        break;
                    }
                case NetMessageClass.MessageClassLobbyReport:
                    {
                        if (!_mgclMessageClassById.TryGetValue((MediusMGCLMessageIds)msgType, out classType))
                            classType = null;
                        break;
                    }
                case NetMessageClass.MessageClassLobby:
                    {
                        if (!_lobbyMessageClassById.TryGetValue((MediusLobbyMessageIds)msgType, out classType))
                            classType = null;
                        break;
                    }
                case NetMessageClass.MessageClassLobbyExt:
                    {
                        if (!_lobbyExtMessageClassById.TryGetValue((MediusLobbyExtMessageIds)msgType, out classType))
                            classType = null;
                        break;
                    }

            }

            // Instantiate
            if (classType == null)
                msg = new RawMediusMessage(msgClass, msgType);
            else
                msg = (BaseMediusMessage)Activator.CreateInstance(classType);

            // Deserialize
            msg.Deserialize(reader);
            return msg;
        }

        #endregion

    }
    #endregion

    /*
    #region BaseMediusGHSMessage
    public abstract class BaseMediusGHSMessage
    {
        /// <summary>
        /// Message class.
        /// </summary>
        public abstract ushort msgSize { get; }

        /// <summary>
        /// Message type.
        /// </summary>
        public abstract GhsOpcode GhsOpcode { get; }

        /// <summary>
        /// When true, skips encryption when sending this particular message instance.
        /// </summary>
        public virtual bool SkipEncryption { get; set; } = false;

        public BaseMediusGHSMessage()
        {

        }

        #region Serialization

        /// <summary>
        /// Deserializes the message from plaintext.
        /// </summary>
        /// <param name="reader"></param>
        public virtual void Deserialize(Server.Common.Stream.MessageReader reader)
        {

        }

        /// <summary>
        /// Serialize contents of the message.
        /// </summary>
        public virtual void Serialize(Server.Common.Stream.MessageWriter writer)
        {

        }

        #endregion

        #region Logging

        
        /// <summary>
        /// Whether or not this message passes the log filter.
        /// </summary>
        public virtual bool CanLog()
        {
            switch (GhsOpcode)
            {
                case GhsOpcode.ghs_ServerProtocolNegotiation: return LogSettings.Singleton?.IsLogGHSPlugin(GhsOpcode) ?? false;
                //case GhsOpcode.ghs_ClientProtocolChoice: return LogSettings.Singleton?.IsLogGHSPlugin(GhsOpcode) ?? false;
                default: return true;
            }
        }
        
        #endregion

        #region Dynamic Instantiation

        private static Dictionary<GhsOpcode, Type> _ghsMessageClassById = null;
        private static int _messageClassByIdLockValue = 0;
        private static object _messageClassByIdLockObject = _messageClassByIdLockValue;


        private static void Initialize()
        {
            lock (_messageClassByIdLockObject)
            {
                if (_ghsMessageClassById != null)
                    return;

                _ghsMessageClassById = new Dictionary<GhsOpcode, Type>();

                // Populate
                var assembly = System.Reflection.Assembly.GetAssembly(typeof(BaseMediusMessage));
                var types = assembly.GetTypes();

                foreach (Type classType in types)
                {
                    // Objects by Id
                    var attrs = (MediusMessageAttribute[])classType.GetCustomAttributes(typeof(MediusMessageAttribute), true);
                    if (attrs != null && attrs.Length > 0)
                    {
                        switch (attrs[0].MessageClass)
                        {
                            case NetMessageClass.MessageClassGHS:
                                {
                                    _ghsMessageClassById.Add((GhsOpcode)attrs[0].MessageType, classType);
                                    break;
                                }
                        }
                    }
                }
            }
        }

        public static BaseMediusGHSMessage Instantiate(Server.Common.Stream.MessageReader reader)
        {
            BaseMediusGHSMessage msg;
            Type classType = null;

            // Init
            Initialize();

            ushort msgSize = reader.ReadUInt16();
            GhsOpcode msgType = reader.Read<GhsOpcode>();

            ReverseBytes16((ushort)msgType);

            // Instantiate
            if (classType == null)
                msg = new RawGHSMediusMessage(msgType, msgSize);
            else
                msg = (BaseMediusGHSMessage)Activator.CreateInstance(classType);

            // Deserialize
            msg.Deserialize(reader);
            return msg;
        }

        #endregion


        /// <summary>
        /// Reverses UInt16 
        /// </summary>
        /// <param name="nValue"></param>
        /// <returns></returns>
        public static ushort ReverseBytes16(ushort nValue)
        {
            return (ushort)((ushort)((nValue >> 8)) | (nValue << 8));
        }
    }
    #endregion
    */
}
