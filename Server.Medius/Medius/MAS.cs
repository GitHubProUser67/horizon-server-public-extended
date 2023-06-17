﻿using DotNetty.Common.Internal.Logging;
using DotNetty.Transport.Channels;
using Microsoft.Extensions.Logging;
using RT.Common;
using RT.Cryptography;
using RT.Models;
using Server.Common;
using Server.Medius.Config;
using Server.Medius.Models;
using Server.Medius.PluginArgs;
using Server.Plugins.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Server.Medius
{
    public class MAS : BaseMediusComponent
    {
        static readonly IInternalLogger _logger = InternalLoggerFactory.GetInstance<MAS>();
        static readonly TimeSpan _defaultTimeout = TimeSpan.FromMilliseconds(3000);

        protected override IInternalLogger Logger => _logger;

        public override int TCPPort => Program.Settings.MASPort;
        public override int UDPPort => 00000;

        public static ServerSettings Settings = new ServerSettings();

        public MAS()
        {

        }

        public ClientObject ReserveClient(MediusServerSessionBeginRequest request)
        {
            var client = new ClientObject();
            client.BeginSession();
            return client;
        }

        protected override async Task ProcessMessage(BaseScertMessage message, IChannel clientChannel, ChannelData data)
        {
            // Get ScertClient data
            var scertClient = clientChannel.GetAttribute(Pipeline.Constants.SCERT_CLIENT).Get();
            var enableEncryption = Program.GetAppSettingsOrDefault(data.ApplicationId).EnableEncryption;
            scertClient.CipherService.EnableEncryption = enableEncryption;

            // 
            switch (message)
            {
                case RT_MSG_CLIENT_HELLO clientHello:
                    {
                        // send hello
                        Queue(new RT_MSG_SERVER_HELLO() { RsaPublicKey = enableEncryption ? Program.Settings.DefaultKey.N : Org.BouncyCastle.Math.BigInteger.Zero }, clientChannel);
                        break;
                    }
                case RT_MSG_CLIENT_CRYPTKEY_PUBLIC clientCryptKeyPublic:
                    {
                        // generate new client session key
                        scertClient.CipherService.GenerateCipher(CipherContext.RSA_AUTH, clientCryptKeyPublic.PublicKey.Reverse().ToArray());
                        scertClient.CipherService.GenerateCipher(CipherContext.RC_CLIENT_SESSION);

                        Queue(new RT_MSG_SERVER_CRYPTKEY_PEER() { SessionKey = scertClient.CipherService.GetPublicKey(CipherContext.RC_CLIENT_SESSION) }, clientChannel);
                        break;
                    }
                case RT_MSG_CLIENT_CONNECT_TCP clientConnectTcp:
                    {
                        List<int> pre108ServerComplete = new List<int>() { 10164, 10190, 10124, 10284, 10414, 10540, 10680 };

                        data.ApplicationId = clientConnectTcp.AppId;
                        scertClient.ApplicationID = clientConnectTcp.AppId;

                        #region Check if AppId from Client matches Server
                        if (!Program.Manager.IsAppIdSupported(clientConnectTcp.AppId))
                        {
                            Logger.Error($"Client {clientChannel.RemoteAddress} attempting to authenticate with incompatible app id {clientConnectTcp.AppId}");
                            await clientChannel.CloseAsync();
                            return;
                        }
                        #endregion

                        if(clientConnectTcp.AccessToken == null && clientConnectTcp.SessionKey == null)
                        {
                            var clientObjects = Program.Manager.GetClients(data.ApplicationId);
                            data.ClientObject = clientObjects.FirstOrDefault();

                            Logger.Warn($"clientobject: {data.ClientObject}");
                        }

                        //If this is a PS3 client
                        if (scertClient.IsPS3Client)
                        {
                            //Send a Server_Connect_Require with no Password needed
                            Queue(new RT_MSG_SERVER_CONNECT_REQUIRE() { ReqServerPassword = 0x00 }, clientChannel);
                        }
                        // Deadlocked check can be simplifed
                        else if (scertClient.MediusVersion >= 109 && scertClient.MediusVersion != null && scertClient.MediusVersion != 111)
                        {
                            //Do NOT send hereCryptKey Game
                            Queue(new RT_MSG_SERVER_CONNECT_ACCEPT_TCP()
                            {
                                PlayerId = 0,
                                ScertId = GenerateNewScertClientId(),
                                PlayerCount = 0x0001,
                                IP = (clientChannel.RemoteAddress as IPEndPoint)?.Address
                            }, clientChannel);
                        }

                        #region PSP specific
                        // Games that DO NOT send a Client Hello 
                        if (scertClient.MediusVersion == 111)
                        {
                            //Do NOT send hereCryptKey Game
                            Queue(new RT_MSG_SERVER_CONNECT_ACCEPT_TCP()
                            {
                                PlayerId = 0,
                                ScertId = GenerateNewScertClientId(),
                                PlayerCount = 0x0001,
                                IP = (clientChannel.RemoteAddress as IPEndPoint)?.Address
                            }, clientChannel);

                            if (scertClient.RsaAuthKey != null)
                            {
                                Queue(new RT_MSG_SERVER_CRYPTKEY_GAME() { GameKey = scertClient.CipherService.GetPublicKey(CipherContext.RC_CLIENT_SESSION) }, clientChannel);
                            }

                            Queue(new RT_MSG_SERVER_CONNECT_COMPLETE() { ClientCountAtConnect = 0x0001 }, clientChannel);
                        }
                        #endregion

                        #region Legacy PS2 specific
                        // Games that DO NOT send a Client Hello 
                        if (scertClient.MediusVersion <= 108)
                        {
                            //Do NOT send hereCryptKey Game
                            Queue(new RT_MSG_SERVER_CONNECT_ACCEPT_TCP()
                            {
                                PlayerId = 0,
                                ScertId = GenerateNewScertClientId(),
                                PlayerCount = 0x0001,
                                IP = (clientChannel.RemoteAddress as IPEndPoint)?.Address
                            }, clientChannel);
                            
                            if (scertClient.RsaAuthKey != null)
                            {
                                Queue(new RT_MSG_SERVER_CRYPTKEY_GAME() { GameKey = scertClient.CipherService.GetPublicKey(CipherContext.RC_CLIENT_SESSION) }, clientChannel);
                            }
                            if (scertClient.MediusVersion > 108 || pre108ServerComplete.Contains(data.ApplicationId))
                            {
                                Queue(new RT_MSG_SERVER_CONNECT_COMPLETE() { ClientCountAtConnect = 0x0001 }, clientChannel);
                            }
                            
                        }
                        #endregion

                        break;
                    }

                case RT_MSG_CLIENT_CONNECT_READY_REQUIRE clientConnectReadyRequire:
                    {
                        if (scertClient.RsaAuthKey != null)
                        {
                            Queue(new RT_MSG_SERVER_CRYPTKEY_GAME() { GameKey = scertClient.CipherService.GetPublicKey(CipherContext.RC_CLIENT_SESSION) }, clientChannel);
                        }

                        Queue(new RT_MSG_SERVER_CONNECT_ACCEPT_TCP()
                        {
                            PlayerId = 0,
                            ScertId = GenerateNewScertClientId(),
                            PlayerCount = 0x0001,
                            IP = (clientChannel.RemoteAddress as IPEndPoint)?.Address
                        }, clientChannel);
                        break;
                    }

                case RT_MSG_CLIENT_CONNECT_READY_TCP clientConnectReadyTcp:
                    {
                        Queue(new RT_MSG_SERVER_CONNECT_COMPLETE() { ClientCountAtConnect = 0x0001 }, clientChannel);

                        if (scertClient.MediusVersion > 108) //data.ApplicationId != 10694
                            Queue(new RT_MSG_SERVER_ECHO(), clientChannel);
                        break;
                    }

                #region Echos
                case RT_MSG_SERVER_ECHO serverEchoReply:
                    {

                        break;
                    }
                case RT_MSG_CLIENT_ECHO clientEcho:
                    {
                        Queue(new RT_MSG_CLIENT_ECHO() { Value = clientEcho.Value }, clientChannel);
                        break;
                    }
                #endregion

                case RT_MSG_CLIENT_APP_TOSERVER clientAppToServer:
                    {
                        await ProcessMediusMessage(clientAppToServer.Message, clientChannel, data);
                        break;
                    }

                #region Client Disconnect
                case RT_MSG_CLIENT_DISCONNECT _: {
                        //Logger.Info($"Client id = {data.ClientObject} disconnected by request with no specific reason\n");
                        break;
                    }
                case RT_MSG_CLIENT_DISCONNECT_WITH_REASON clientDisconnectWithReason:
                    { 
                        data.State = ClientState.DISCONNECTED;
                        _ = clientChannel.CloseAsync();
                        break;
                    }
                #endregion

                default:
                    {
                        Logger.Warn($"UNHANDLED RT MESSAGE: {message}");
                        break;
                    }
            }

            return;
        }

        protected virtual async Task ProcessMediusMessage(BaseMediusMessage message, IChannel clientChannel, ChannelData data)
        {
            var scertClient = clientChannel.GetAttribute(Pipeline.Constants.SCERT_CLIENT).Get();
            if (message == null)
                return;

            var appSettings = Program.GetAppSettingsOrDefault(data.ApplicationId);

            switch (message)
            {
                #region MGCL - Dme

                case MediusServerSessionBeginRequest mgclSessionBeginRequest:
                    {
                        List<int> nonSecure = new List<int>() { 10010, 10031,  };
                        //UYA Public Beta v1.0
                        if (mgclSessionBeginRequest.ApplicationID == 10680)
                        {
                            Logger.Info("R&C 3: UYA Public Beta v1.0 reserving MGCL Client prior to MAS login!");
                            // Create client object
                            data.ClientObject = Program.ProxyServer.ReserveClient(mgclSessionBeginRequest);
                        }


                        //If Message Routing App id
                        if (data.ApplicationId == 120)
                        {

                            data.ClientObject = new ClientObject();
                            data.ClientObject = Program.ProxyServer.ReserveDMEObject(mgclSessionBeginRequest);
                        }

                        //data.ClientObject = Program.ProxyServer.ReserveDMEObject(mgclSessionBeginRequest);

                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.OnConnected();

                        IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);

                        //MGCL_SEND_FAILED, MGCL_UNSUCCESSFUL
                        if (!data.ClientObject.IsConnected)
                        {
                            data.ClientObject.Queue(new MediusServerSessionBeginResponse()
                            {
                                MessageID = mgclSessionBeginRequest.MessageID,
                                Confirmation = MGCL_ERROR_CODE.MGCL_UNSUCCESSFUL
                            });
                        }
                        else
                        {

                            if(nonSecure.Contains(data.ClientObject.ApplicationId))
                            {

                                //TM:BO Reply
                                data.ClientObject.Queue(new MediusServerSessionBeginResponse()
                                {
                                    MessageID = mgclSessionBeginRequest.MessageID,
                                    Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                                    ConnectInfo = new NetConnectionInfo()
                                    {
                                        AccessKey = data.ClientObject.Token,
                                        SessionKey = data.ClientObject.SessionKey,
                                        WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                        ServerKey = new RSA_KEY(),
                                        AddressList = new NetAddressList()
                                        {
                                            AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                                {
                                                new NetAddress() { Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService },
                                                new NetAddress() { AddressType = NetAddressType.NetAddressNone },
                                                }
                                        },
                                        Type = NetConnectionType.NetConnectionTypeClientServerUDP
                                    }
                                });
                            } else
                            {

                                // Default Reply
                                data.ClientObject.Queue(new MediusServerSessionBeginResponse()
                                {
                                    MessageID = mgclSessionBeginRequest.MessageID,
                                    Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                                    ConnectInfo = new NetConnectionInfo()
                                    {
                                        AccessKey = data.ClientObject.Token,
                                        SessionKey = data.ClientObject.SessionKey,
                                        WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                        ServerKey = Program.GlobalAuthPublic,
                                        AddressList = new NetAddressList()
                                        {
                                            AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                                {
                                                new NetAddress() { Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService },
                                                new NetAddress() { AddressType = NetAddressType.NetAddressNone },
                                                }
                                        },
                                        Type = NetConnectionType.NetConnectionTypeClientServerUDP
                                    }
                                });
                            }
                        }



                        break;
                    }

                case MediusServerSessionBeginRequest1 serverSessionBeginRequest1:
                    {
                        // Create DME object
                        //data.ClientObject = Program.ProxyServer.ReserveDMEObject(serverSessionBeginRequest1);

                        //
                        data.ClientObject.ServerType = serverSessionBeginRequest1.ServerType;
                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.OnConnected();

                        IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);

                        //Send NAT Service
                        data.ClientObject.Queue(new MediusServerSessionBeginResponse()
                        {
                            MessageID = serverSessionBeginRequest1.MessageID,
                            Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                            ConnectInfo = new NetConnectionInfo()
                            {
                                AccessKey = data.ClientObject.Token,
                                SessionKey = data.ClientObject.SessionKey,
                                WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                ServerKey = Program.GlobalAuthPublic,
                                AddressList = new NetAddressList()
                                {
                                    AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                    {
                                        new NetAddress() { Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService },
                                        new NetAddress() { AddressType = NetAddressType.NetAddressNone },
                                    }
                                },
                                Type = NetConnectionType.NetConnectionTypeClientServerUDP
                            }
                        });
                        break;
                    }

                case MediusServerSessionBeginRequest2 serverSessionBeginRequest2:
                    {
                        // Create DME object
                        //data.ClientObject = Program.ProxyServer.ReserveDMEObject(serverSessionBeginRequest2);

                        Logger.Warn($"Client {data.ClientObject.AccountName}");

                        //
                        data.ClientObject.ServerType = serverSessionBeginRequest2.ServerType;
                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.OnConnected();

                        IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);

                        //Send NAT Service
                        data.ClientObject.Queue(new MediusServerSessionBeginResponse()
                        {
                            MessageID = serverSessionBeginRequest2.MessageID,
                            Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                            ConnectInfo = new NetConnectionInfo()
                            {
                                AccessKey = data.ClientObject.Token,
                                SessionKey = data.ClientObject.SessionKey,
                                WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                ServerKey = Program.GlobalAuthPublic,
                                AddressList = new NetAddressList()
                                {
                                    AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                    {
                                        new NetAddress() { Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService },
                                        new NetAddress() { AddressType = NetAddressType.NetAddressNone },
                                    }
                                },
                                Type = NetConnectionType.NetConnectionTypeClientServerUDP
                            }
                        });
                        break;
                    }

                case MediusServerAuthenticationRequest mgclAuthRequest:
                    {
                        List<int> nonSecure = new List<int>() { 10010, 10031, 10190};

                        //var dmeObject = data.ClientObject as DMEObject;
                        //data.ClientObject = dmeObject;

                        data.ClientObject.NetConnectionType = NetConnectionType.NetConnectionTypeClientServerTCP;

                        if (mgclAuthRequest.AddressList.AddressList[0].AddressType == NetAddressType.NetAddressTypeBinaryExternal)
                        {
                            // NetAddressTypeBinaryExternal
                            Logger.Info($"AddressConverted: {ConvertFromIntegerToIpAddress(mgclAuthRequest.AddressList.AddressList[0].BinaryAddress)}");
                            data.ClientObject.SetIp(ConvertFromIntegerToIpAddress(mgclAuthRequest.AddressList.AddressList[0].BinaryAddress));
                        } else if (mgclAuthRequest.AddressList.AddressList[0].AddressType == NetAddressType.NetAddressTypeBinaryExternalVport
                            || mgclAuthRequest.AddressList.AddressList[0].AddressType == NetAddressType.NetAddressTypeBinaryInternalVport)
                        {
                            string BinaryIp = mgclAuthRequest.AddressList.AddressList[0].IPBinaryBitOne + "." +
                                    mgclAuthRequest.AddressList.AddressList[0].IPBinaryBitTwo + "." +
                                    mgclAuthRequest.AddressList.AddressList[0].IPBinaryBitThree + "." +
                                    mgclAuthRequest.AddressList.AddressList[0].IPBinaryBitFour;

                            data.ClientObject.SetIp(BinaryIp);
                        }
                        else
                        {
                            // NetAddressTypeExternal
                            data.ClientObject.SetIp(mgclAuthRequest.AddressList.AddressList[0].Address);
                        }



                        if(nonSecure.Contains(data.ClientObject.ApplicationId))
                        {

                            IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);

                            data.ClientObject.Queue(new MediusServerAuthenticationResponse()
                            {
                                MessageID = mgclAuthRequest.MessageID,
                                Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                                ConnectInfo = new NetConnectionInfo()
                                {
                                    AccessKey = data.ClientObject.Token,
                                    SessionKey = data.ClientObject.SessionKey,
                                    WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                    ServerKey = new RSA_KEY(),
                                    AddressList = new NetAddressList()
                                    {
                                        AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                        {
                                            new NetAddress() { Address = Program.ProxyServer.IPAddress.ToString(), Port = (uint)Program.ProxyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal },
                                            new NetAddress() { AddressType = NetAddressType.NetAddressNone }
                                        }
                                    },
                                    Type = NetConnectionType.NetConnectionTypeClientServerTCP
                                }
                            });

                            // Keep the client alive until the dme objects connects to MPS or times out
                            data.ClientObject.OnConnected();
                            data.ClientObject.KeepAliveUntilNextConnection();
                        } else
                        {

                            data.ClientObject.Queue(new MediusServerAuthenticationResponse()
                            {
                                MessageID = mgclAuthRequest.MessageID,
                                Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS,
                                ConnectInfo = new NetConnectionInfo()
                                {
                                    AccessKey = data.ClientObject.Token,
                                    SessionKey = data.ClientObject.SessionKey,
                                    WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId).Id,
                                    ServerKey = Program.GlobalAuthPublic,
                                    AddressList = new NetAddressList()
                                    {
                                        AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                        {
                                            new NetAddress() { Address = Program.ProxyServer.IPAddress.ToString(), Port = (uint)Program.ProxyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal },
                                            new NetAddress() { AddressType = NetAddressType.NetAddressNone },
                                        }
                                    },
                                    Type = NetConnectionType.NetConnectionTypeClientServerTCP
                                }
                            });

                            // Keep the client alive until the dme objects connects to MPS or times out
                            data.ClientObject.OnConnected();
                            data.ClientObject.KeepAliveUntilNextConnection();
                        }


                        /*
                        var appIdList = Program.Database.GetAppIds();
                        if(appIdList.Result)
                        {
                        } else {
                            dmeObject.Queue(new MediusServerAuthenticationResponse()
                            {
                                MessageID = mgclAuthRequest.MessageID,
                                Confirmation = MGCL_ERROR_CODE.MGCL_AUTHENTICATION_FAILED,
                                ConnectInfo = new NetConnectionInfo()
                                {
                                    Type = NetConnectionType.NetConnectionNone
                                }
                            });
                        }
                        */
                        break;
                    }

                case MediusServerSetAttributesRequest mgclSetAttrRequest:
                    {
                        var dmeObject = data.ClientObject as DMEObject;
                        if (dmeObject == null)
                            throw new InvalidOperationException($"Non-DME Client sending MGCL messages.");

                        // Reply with success
                        dmeObject.Queue(new MediusServerSetAttributesResponse()
                        {
                            MessageID = mgclSetAttrRequest.MessageID,
                            Confirmation = MGCL_ERROR_CODE.MGCL_SUCCESS
                        });
                        break;
                    }

                case MediusServerSessionEndRequest sessionEndRequest:
                    {
                        data?.ClientObject.Queue(new MediusServerSessionEndResponse()
                        {
                            MessageID = sessionEndRequest.MessageID,
                            ErrorCode = MGCL_ERROR_CODE.MGCL_SUCCESS
                        });
                        break;
                    }

                case MediusServerReport serverReport:
                    {
                        (data.ClientObject as DMEObject)?.OnServerReport(serverReport);
                        data.ClientObject.OnConnected();
                        Logger.Info($"ServerReport SessionKey {serverReport.SessionKey} MaxWorlds {serverReport.MaxWorlds} MaxPlayersPerWorld {serverReport.MaxPlayersPerWorld} TotalWorlds {serverReport.ActiveWorldCount} TotalPlayers {serverReport.TotalActivePlayers} Alert {serverReport.AlertLevel} ConnIndex {data.ClientObject.DmeId} WorldID {data.ClientObject.WorldId}");
                        break;
                    }

                #endregion


                #region Session

                case MediusExtendedSessionBeginRequest extendedSessionBeginRequest:
                    {
                        // Create client object
                        data.ClientObject = Program.LobbyServer.ReserveClient(extendedSessionBeginRequest);
                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.MediusConnectionType = extendedSessionBeginRequest.ConnectionClass;
                        data.ClientObject.OnConnected();


                        _ = Program.Database.GetServerFlags().ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully && r.Result != null && r.Result.MaintenanceMode != null)
                            {
                                // Ensure that maintenance is active
                                // Ensure that we're past the from date
                                // Ensure that we're before the to date (if set)
                                if (r.Result.MaintenanceMode.IsActive
                                     && Utils.GetHighPrecisionUtcTime() > r.Result.MaintenanceMode.FromDt
                                     && (!r.Result.MaintenanceMode.ToDt.HasValue
                                         || r.Result.MaintenanceMode.ToDt > Utils.GetHighPrecisionUtcTime()))
                                {
                                    QueueBanMessage(data, "Server in maintenance mode.");
                                }
                                else
                                {
                                    // Reply
                                    data.ClientObject.Queue(new MediusSessionBeginResponse()
                                    {
                                        MessageID = extendedSessionBeginRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        SessionKey = data.ClientObject.SessionKey
                                    });
                                }
                            }
                        });

                        break;
                    }
                case MediusSessionBeginRequest sessionBeginRequest:
                    {

                        if (data.ApplicationId != 10442)
                        { // if this is Killzone PS2, DON'T make the Session here 

                            // Create client object
                            data.ClientObject = Program.LobbyServer.ReserveClient(sessionBeginRequest);
                        }


                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.MediusConnectionType = sessionBeginRequest.ConnectionClass;
                        data.ClientObject.OnConnected();

                        Logger.Info($"Retrieved ApplicationID {data.ClientObject.ApplicationId} from client connection");

                        _ = Program.Database.GetServerFlags().ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully && r.Result != null && r.Result.MaintenanceMode != null)
                            {

                                #region Maintenance Mode?
                                // Ensure that maintenance is active
                                // Ensure that we're past the from date
                                // Ensure that we're before the to date (if set)
                                if (r.Result.MaintenanceMode.IsActive
                                     && Utils.GetHighPrecisionUtcTime() > r.Result.MaintenanceMode.FromDt
                                     && (!r.Result.MaintenanceMode.ToDt.HasValue
                                         || r.Result.MaintenanceMode.ToDt > Utils.GetHighPrecisionUtcTime()))
                                {
                                    QueueBanMessage(data, "Server in maintenance mode.");
                                }
                                #endregion

                                #region Send Response
                                else
                                {
                                    // Reply
                                    data.ClientObject.Queue(new MediusSessionBeginResponse()
                                    {
                                        MessageID = sessionBeginRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        SessionKey = data.ClientObject.SessionKey
                                    });
                                }
                                #endregion
                            }
                        });
                        break;
                    }

                case MediusSessionBeginRequest1 sessionBeginRequest1:
                    {
                        // Create client object
                        data.ClientObject = Program.LobbyServer.ReserveClient1(sessionBeginRequest1);
                        data.ClientObject.ApplicationId = data.ApplicationId;
                        data.ClientObject.MediusVersion = (int)scertClient.MediusVersion;
                        data.ClientObject.MediusConnectionType = sessionBeginRequest1.ConnectionClass;
                        data.ClientObject.OnConnected();



                        Logger.Info($"Retrieved ApplicationID {data.ClientObject.ApplicationId} from client connection");

                        #region SystemMessageSingleTest Disabled?
                        if (Program.Settings.SystemMessageSingleTest != false)
                        {
                            QueueBanMessage(data, "MAS.Notification Test:\nYou have been banned from this server.");

                            await data.ClientObject.Logout();
                        }
                        #endregion

                        else
                        {
                            _ = Program.Database.GetServerFlags().ContinueWith((r) =>
                            {
                                if (r.IsCompletedSuccessfully && r.Result != null && r.Result.MaintenanceMode != null)
                                {
                                    #region Maintenance Mode?
                                    // Ensure that maintenance is active
                                    // Ensure that we're past the from date
                                    // Ensure that we're before the to date (if set)
                                    if (r.Result.MaintenanceMode.IsActive
                                         && Utils.GetHighPrecisionUtcTime() > r.Result.MaintenanceMode.FromDt
                                         && (!r.Result.MaintenanceMode.ToDt.HasValue
                                             || r.Result.MaintenanceMode.ToDt > Utils.GetHighPrecisionUtcTime()))
                                    {
                                        QueueBanMessage(data, "Server in maintenance mode.");
                                    }
                                    #endregion

                                    #region Send Response
                                    else
                                    {
                                        // Reply
                                        data.ClientObject.Queue(new MediusSessionBeginResponse()
                                        {
                                            MessageID = sessionBeginRequest1.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            SessionKey = data.ClientObject.SessionKey
                                        });
                                    }
                                    #endregion
                                }
                            });
                        }
                        break;
                    }

                case MediusSessionEndRequest sessionEndRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} is trying to end session without an Client Object");

                        Queue(new RT_MSG_SERVER_APP()
                        {
                            Message = new MediusSessionEndResponse()
                            {
                                MessageID = sessionEndRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusSuccess,
                            }
                        }, clientChannel);

                        // Remove
                        data.ClientObject.EndSession();
                        data.ClientObject = null;
                        /*
                        if (data.ClientObject.IsLoggedIn)
                        {
                            Logger.Info($"SessionEnd Success");
                            await data.ClientObject.Logout();
                        }
                        */
                        break;
                    }

                #endregion

                #region Localization

                case MediusSetLocalizationParamsRequest setLocalizationParamsRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {setLocalizationParamsRequest} without a session.");

                        data.ClientObject.CharacterEncoding = setLocalizationParamsRequest.CharacterEncoding;
                        data.ClientObject.LanguageType = setLocalizationParamsRequest.Language;

                        data.ClientObject.Queue(new MediusStatusResponse()
                        {
                            Type = 0xA4,
                            Class = setLocalizationParamsRequest.PacketClass,
                            MessageID = setLocalizationParamsRequest.MessageID,
                            StatusCode = MediusCallbackStatus.MediusSuccess
                        });
                        break;
                    }

                case MediusSetLocalizationParamsRequest1 setLocalizationParamsRequest1:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {setLocalizationParamsRequest1} without a session.");

                        data.ClientObject.CharacterEncoding = setLocalizationParamsRequest1.CharacterEncoding;
                        data.ClientObject.LanguageType = setLocalizationParamsRequest1.Language;
                        data.ClientObject.TimeZone = setLocalizationParamsRequest1.TimeZone;
                        data.ClientObject.LocationId = setLocalizationParamsRequest1.LocationID;

                        data.ClientObject.Queue(new MediusStatusResponse()
                        {
                            Type = 0xA4,
                            Class = (NetMessageClass)1,
                            MessageID = setLocalizationParamsRequest1.MessageID,
                            StatusCode = MediusCallbackStatus.MediusSuccess
                        });
                        break;
                    }
                case MediusSetLocalizationParamsRequest2 setLocalizationParamsRequest2:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {setLocalizationParamsRequest2} without a session.");

                        data.ClientObject.CharacterEncoding = setLocalizationParamsRequest2.CharacterEncoding;
                        data.ClientObject.LanguageType = setLocalizationParamsRequest2.Language;
                        data.ClientObject.TimeZone = setLocalizationParamsRequest2.TimeZone;

                        data.ClientObject.Queue(new MediusStatusResponse()
                        {
                            Type = 0xA4,
                            Class = (NetMessageClass)1,
                            MessageID = setLocalizationParamsRequest2.MessageID,
                            StatusCode = MediusCallbackStatus.MediusSuccess
                        });
                        break;
                    }

                #endregion

                #region Game

                case MediusGetTotalGamesRequest getTotalGamesRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getTotalGamesRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getTotalGamesRequest} without being logged in.");

                        data.ClientObject.Queue(new MediusGetTotalGamesResponse()
                        {
                            MessageID = getTotalGamesRequest.MessageID,
                            Total = 0,
                            StatusCode = MediusCallbackStatus.MediusRequestDenied
                        });
                        break;
                    }

                #endregion

                #region Channel

                case MediusGetTotalChannelsRequest getTotalChannelsRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getTotalChannelsRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getTotalChannelsRequest} without being logged in.");


                        data.ClientObject.Queue(new MediusGetTotalChannelsResponse()
                        {
                            MessageID = getTotalChannelsRequest.MessageID,
                            Total = 0,
                            StatusCode = MediusCallbackStatus.MediusRequestDenied,
                        });
                        break;
                    }

                case MediusSetLobbyWorldFilterRequest setLobbyWorldFilterRequest:
                    {
                        //WRC 4 Sets LobbyWorldFilter Prior to making a session.
                        // ERROR - Need a session
                        /*
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {setLobbyWorldFilterRequest} without a session.");
                        */
                        // ERROR -- Need to be logged in
                        /*
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {setLobbyWorldFilterRequest} without being logged in.");
                        */
                        /*
                        data.ClientObject.Queue(new MediusSetLobbyWorldFilterResponse()
                        {
                            MessageID = setLobbyWorldFilterRequest.MessageID,
                            StatusCode = MediusCallbackStatus.MediusRequestDenied,
                        });
                        */
                        Queue(new RT_MSG_SERVER_APP() 
                        {
                             Message = new MediusSetLobbyWorldFilterResponse()
                             {
                                 MessageID = setLobbyWorldFilterRequest.MessageID,
                                 StatusCode = MediusCallbackStatus.MediusRequestDenied
                             }
                        });


                        break;
                    }
                #endregion

                #region DNAS CID Check

                case MediusMachineSignaturePost machineSignaturePost:
                    {
                        if (Settings.DnasEnablePost == true)
                        {
                            //Sets the CachedPlayer's MachineId
                            data.MachineId = BitConverter.ToString(machineSignaturePost.MachineSignature);

                            Logger.Info($"Session Key {machineSignaturePost.SessionKey} | Posting Machine signatures");

                            // Then post to the Database if logged in
                            if (data.ClientObject?.IsLoggedIn ?? false)
                                _ = Program.Database.PostMachineId(data.ClientObject.AccountId, data.MachineId);
                        }
                        else
                        {
                            //DnasEnablePost set to false;
                        }

                        break;
                    }

                case MediusDnasSignaturePost dnasSignaturePost:
                    {
                        if (Settings.DnasEnablePost != true)
                        {
                            //If DNAS Signature Post is the PS2/PSP/PS3 Console ID then continue
                            if (dnasSignaturePost.DnasSignatureType == MediusDnasCategory.DnasConsoleID)
                            {
                                data.MachineId = BitConverter.ToString(dnasSignaturePost.DnasSignature);

                                Logger.Info($"Posting ConsoleID - ConsoleSigSize={dnasSignaturePost.DnasSignatureLength}");

                                // Then post to the Database if logged in
                                if (data.ClientObject?.IsLoggedIn ?? false)
                                    _ = Program.Database.PostMachineId(data.ClientObject.AccountId, data.MachineId);
                            }

                            if (dnasSignaturePost.DnasSignatureType == MediusDnasCategory.DnasTitleID)
                            {
                                Logger.Info($"DnasSignaturePost Error - Invalid SignatureType");

                            }

                            if (dnasSignaturePost.DnasSignatureType == MediusDnasCategory.DnasDiskID)
                            {

                                Logger.Info($"Posting DiskID - DiskSigSize={dnasSignaturePost.DnasSignatureLength}");

                            }
                        }
                        else
                        {
                            //DnasEnablePost false, no Post;
                        }
                        break;
                    }
                #endregion

                #region AccessLevel (2.12)

                case MediusGetAccessLevelInfoRequest getAccessLevelInfoRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getAccessLevelInfoRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getAccessLevelInfoRequest} without being logged in.");

                        //int adminAccessLevel = 4;

                        data.ClientObject.Queue(new MediusGetAccessLevelInfoResponse()
                        {
                            MessageID = getAccessLevelInfoRequest.MessageID,
                            StatusCode = MediusCallbackStatus.MediusSuccess,
                            AccessLevel = MediusAccessLevelType.MEDIUS_ACCESSLEVEL_MODERATOR,
                        });
                        break;
                    }

                #endregion 

                #region Version Server
                case MediusVersionServerRequest mediusVersionServerRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null && data.ApplicationId != 10442) // KILLZONE PS2 CREATES A SESSION HERE FIRST ON CONNECT
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {mediusVersionServerRequest} without a session.");

                        if (Settings.MediusServerVersionOverride == true)
                        {
                            #region F1 2005 PAL
                            // F1 2005 PAL SCES / F1 2005 PAL TCES
                            if (data.ApplicationId == 10954 || data.ApplicationId == 10952)
                            {
                                data.ClientObject.Queue(new MediusVersionServerResponse()
                                {
                                    MessageID = mediusVersionServerRequest.MessageID,
                                    VersionServer = "Medius Authentication Server Version 2.9.0009",
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                });
                            }
                            #endregion

                            #region Socom 1
                            else if (data.ApplicationId == 10274)
                            {
                                data.ClientObject.Queue(new MediusVersionServerResponse()
                                {
                                    MessageID = mediusVersionServerRequest.MessageID,
                                    VersionServer = "Medius Authentication Server Version 1.40.PRE8",
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                });
                            }
                            #endregion

                            #region Killzone TCES
                            else if (data.ApplicationId == 10442)
                            {
                                data.ClientObject = Program.LobbyServer.ReserveClient(mediusVersionServerRequest);


                                data.ClientObject.Queue(new MediusVersionServerResponse()
                                {
                                    MessageID = mediusVersionServerRequest.MessageID,
                                    VersionServer = "Medius Authentication Server Version 1.50.0009",
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                });
                            }
                            else
                            #endregion

                            //Default
                            {
                                data.ClientObject.Queue(new MediusVersionServerResponse()
                                {
                                    MessageID = mediusVersionServerRequest.MessageID,
                                    VersionServer = "Medius Authentication Server Version 3.09",
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                });
                            }
                        }
                        else
                        {
                            // If MediusServerVersionOverride is false, we send our own Version String
                            // AND if its Killzone PS2 we make the ClientObject
                            if(data.ApplicationId == 10442)
                            {
                                data.ClientObject = Program.LobbyServer.ReserveClient(mediusVersionServerRequest);
                            }


                            data.ClientObject.Queue(new MediusVersionServerResponse()
                            {
                                MessageID = mediusVersionServerRequest.MessageID,
                                VersionServer = Settings.MASVersion,
                                StatusCode = MediusCallbackStatus.MediusSuccess,
                            });
                        }

                        break;
                    }

                #endregion

                #region Co-Locations
                case MediusGetLocationsRequest getLocationsRequest:
                    {
                        // ERROR - Need a session but doesn't need to be logged in
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getLocationsRequest} without a session.");

                        Logger.Info($"Get Locations Request Received Sessionkey: {getLocationsRequest.SessionKey}");
                        await Program.Database.GetLocations(data.ClientObject.ApplicationId).ContinueWith(r =>
                        {
                            var locations = r.Result;

                            if (r.IsCompletedSuccessfully)
                            {
                                if (locations == null || locations.Length == 0)
                                {
                                    data.ClientObject.Queue(new MediusGetLocationsResponse()
                                    {
                                        MessageID = getLocationsRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusNoResult,
                                        EndOfList = true
                                    });
                                }
                                else
                                {
                                    var responses = locations.Select(x => new MediusGetLocationsResponse()
                                    {
                                        MessageID = getLocationsRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        LocationId = x.Id,
                                        LocationName = x.Name
                                    }).ToList();

                                    Logger.Info("GetLocationsRequest  success");
                                    Logger.Info($"NumLocations returned[{responses.Count}]");

                                    responses[responses.Count - 1].EndOfList = true;
                                    data.ClientObject.Queue(responses);
                                }
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusGetLocationsResponse()
                                {
                                    MessageID = getLocationsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusDBError,
                                    LocationId = -1,
                                    LocationName = "0",
                                    EndOfList = true
                                });
                            }
                        });
                        break;
                    }

                case MediusPickLocationRequest pickLocationRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {pickLocationRequest} without a session.");

                        data.ClientObject.LocationId = pickLocationRequest.LocationID;

                        data.ClientObject.Queue(new MediusPickLocationResponse()
                        {
                            MessageID = pickLocationRequest.MessageID,
                            StatusCode = MediusCallbackStatus.MediusSuccess
                        });
                        break;
                    }

                #endregion

                #region Account

                case MediusAccountRegistrationRequest accountRegRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountRegRequest} without a session.");

                        // Check that account creation is enabled
                        if (appSettings.DisableAccountCreation)
                        {
                            // Reply error
                            data.ClientObject.Queue(new MediusAccountRegistrationResponse()
                            {
                                MessageID = accountRegRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusFail
                            });

                            return;
                        }

                        // validate name
                        if (!Program.PassTextFilter(data.ApplicationId, Config.TextFilterContext.ACCOUNT_NAME, accountRegRequest.AccountName))
                        {
                            data.ClientObject.Queue(new MediusAccountRegistrationResponse()
                            {
                                MessageID = accountRegRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusFail,
                            });
                            return;
                        }

                        _ = Program.Database.CreateAccount(new Database.Models.CreateAccountDTO()
                        {
                            AccountName = accountRegRequest.AccountName,
                            AccountPassword = Utils.ComputeSHA256(accountRegRequest.Password),
                            MachineId = data.MachineId,
                            MediusStats = Convert.ToBase64String(new byte[Constants.ACCOUNTSTATS_MAXLEN]),
                            AppId = data.ClientObject.ApplicationId
                        }).ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully && r.Result != null)
                            {
                                // Reply with account id
                                data.ClientObject.Queue(new MediusAccountRegistrationResponse()
                                {
                                    MessageID = accountRegRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    AccountID = r.Result.AccountId
                                });
                            }
                            else
                            {
                                // Reply error
                                data.ClientObject.Queue(new MediusAccountRegistrationResponse()
                                {
                                    MessageID = accountRegRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusAccountAlreadyExists
                                });
                            }
                        });
                        break;
                    }
                case MediusAccountGetIDRequest accountGetIdRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountGetIdRequest} without a session.");

                        _ = Program.Database.GetAccountByName(accountGetIdRequest.AccountName, data.ClientObject.ApplicationId).ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully && r.Result != null)
                            {
                                // Success
                                data?.ClientObject?.Queue(new MediusAccountGetIDResponse()
                                {
                                    MessageID = accountGetIdRequest.MessageID,
                                    AccountID = r.Result.AccountId,
                                    StatusCode = MediusCallbackStatus.MediusSuccess
                                });
                            }
                            else
                            {
                                // Fail
                                data?.ClientObject?.Queue(new MediusAccountGetIDResponse()
                                {
                                    MessageID = accountGetIdRequest.MessageID,
                                    AccountID = -1,
                                    StatusCode = MediusCallbackStatus.MediusAccountNotFound
                                });
                            }
                        });

                        break;
                    }
                case MediusAccountDeleteRequest accountDeleteRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountDeleteRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountDeleteRequest} without being logged in.");

                        _ = Program.Database.DeleteAccount(data.ClientObject.AccountName, data.ClientObject.ApplicationId).ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully && r.Result)
                            {
                                Logger.Info($"Logging out {data?.ClientObject?.AccountName}'s account\nDeleting from Medius Server");

                                data?.ClientObject?.Logout();

                                data?.ClientObject?.Queue(new MediusAccountDeleteResponse()
                                {
                                    MessageID = accountDeleteRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess
                                });
                            }
                            else
                            {
                                Logger.Warn($"Logout FAILED for {data?.ClientObject?.AccountName}'s account\nData still persistent on Medius Server");

                                data?.ClientObject?.Queue(new MediusAccountDeleteResponse()
                                {
                                    MessageID = accountDeleteRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusDBError
                                });
                            }
                        });
                        break;
                    }
                case MediusAnonymousLoginRequest anonymousLoginRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {anonymousLoginRequest} without a session.");

                        await LoginAnonymous(anonymousLoginRequest, clientChannel, data);
                        break;
                    }
                case MediusAccountLoginRequest accountLoginRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountLoginRequest} without a session.");

                        // Check the client isn't already logged in
                        if (Program.Manager.GetClientByAccountName(accountLoginRequest.Username, data.ClientObject.ApplicationId)?.IsLoggedIn ?? false)
                        {
                            data.ClientObject.Queue(new MediusAccountLoginResponse()
                            {
                                MessageID = accountLoginRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusAccountLoggedIn
                            });
                        }
                        else
                        {
                            #region SystemMessageSingleTest Disabled?
                            if (Program.Settings.SystemMessageSingleTest != false)
                            {
                                QueueBanMessage(data, "MAS.Notification Test:\nYou have been banned from this server.");

                                await data.ClientObject.Logout();
                            }
                            #endregion
                            else
                            {

                                _ = Program.Database.GetAccountByName(accountLoginRequest.Username, data.ClientObject.ApplicationId).ContinueWith(async (r) =>
                                {
                                    if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                        return;

                                    if (r.IsCompletedSuccessfully && r.Result != null && data != null && data.ClientObject != null && data.ClientObject.IsConnected)
                                    {

                                        if (r.Result.IsBanned)
                                        {
                                            // Send ban message
                                            QueueBanMessage(data);

                                            // Account is banned
                                            // Temporary solution is to tell the client the login failed
                                            data?.ClientObject?.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusAccountBanned
                                            });

                                        }
                                        else if (appSettings.EnableAccountWhitelist && !appSettings.AccountIdWhitelist.Contains(r.Result.AccountId))
                                        {
                                            // Account not allowed to sign in
                                            data?.ClientObject?.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusFail
                                            });
                                        }
                                        else if (Program.Manager.GetClientByAccountName(accountLoginRequest.Username, data.ClientObject.ApplicationId)?.IsLoggedIn ?? false)
                                        {
                                            data.ClientObject.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusAccountLoggedIn
                                            });
                                        }

                                        else if (Utils.ComputeSHA256(accountLoginRequest.Password) == r.Result.AccountPassword)
                                        {
                                            await Login(accountLoginRequest.MessageID, clientChannel, data, r.Result, false);
                                        }
                                        else
                                        {
                                            // Incorrect password
                                            data?.ClientObject?.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusInvalidPassword
                                            });
                                        }
                                    }
                                    else if (appSettings.CreateAccountOnNotFound)
                                    {
                                        // Account not found, create new and login
                                        // Check that account creation is enabled
                                        if (appSettings.DisableAccountCreation)
                                        {
                                            // Reply error
                                            data.ClientObject.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusFail,
                                            });
                                            return;
                                        }

                                        // validate name
                                        if (!Program.PassTextFilter(data.ApplicationId, Config.TextFilterContext.ACCOUNT_NAME, accountLoginRequest.Username))
                                        {
                                            data.ClientObject.Queue(new MediusAccountLoginResponse()
                                            {
                                                MessageID = accountLoginRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusFail,
                                            });
                                            return;
                                        }

                                        _ = Program.Database.CreateAccount(new Database.Models.CreateAccountDTO()
                                        {
                                            AccountName = accountLoginRequest.Username,
                                            AccountPassword = Utils.ComputeSHA256(accountLoginRequest.Password),
                                            MachineId = data.MachineId,
                                            MediusStats = Convert.ToBase64String(new byte[Constants.ACCOUNTSTATS_MAXLEN]),
                                            AppId = data.ClientObject.ApplicationId
                                        }).ContinueWith(async (r) =>
                                        {
                                            if (r.IsCompletedSuccessfully && r.Result != null)
                                            {
                                                await Login(accountLoginRequest.MessageID, clientChannel, data, r.Result, false);
                                            }
                                            else
                                            {
                                                // Reply error
                                                data.ClientObject.Queue(new MediusAccountLoginResponse()
                                                {
                                                    MessageID = accountLoginRequest.MessageID,
                                                    StatusCode = MediusCallbackStatus.MediusInvalidPassword
                                                });
                                            }
                                        });
                                    }
                                    else
                                    {
                                        // Account not found
                                        data.ClientObject.Queue(new MediusAccountLoginResponse()
                                        {
                                            MessageID = accountLoginRequest.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusAccountNotFound,
                                        });
                                    }
                                });
                            }
                        }
                        break;
                    }

                    case MediusAccountUpdatePasswordRequest accountUpdatePasswordRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountUpdatePasswordRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountUpdatePasswordRequest} without being logged in.");

                        // Post New Password to Database
                        _ = Program.Database.PostAccountUpdatePassword(data.ClientObject.AccountId, accountUpdatePasswordRequest.OldPassword, accountUpdatePasswordRequest.NewPassword).ContinueWith((r) =>
                        {
                            if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                return;

                            if (r.IsCompletedSuccessfully && r.Result)
                            {
                                data.ClientObject.Queue(new MediusAccountUpdatePasswordStatusResponse()
                                {
                                    MessageID = accountUpdatePasswordRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess
                                });
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusAccountUpdatePasswordStatusResponse()
                                {
                                    MessageID = accountUpdatePasswordRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusDBError
                                });
                            }
                        });
                        break;
                    }

                case MediusAccountLogoutRequest accountLogoutRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountLogoutRequest} without a session.");

                        MediusCallbackStatus result = MediusCallbackStatus.MediusFail;

                        // Check token
                        if (data.ClientObject.IsLoggedIn && accountLogoutRequest.SessionKey == data.ClientObject.SessionKey)
                        {
                            // 
                            result = MediusCallbackStatus.MediusSuccess;

                            // Logout
                            await data.ClientObject.Logout();
                        }

                        data.ClientObject.Queue(new MediusAccountLogoutResponse()
                        {
                            MessageID = accountLogoutRequest.MessageID,
                            StatusCode = result
                        });
                        break;
                    }

                case MediusAccountUpdateStatsRequest accountUpdateStatsRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountUpdateStatsRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {accountUpdateStatsRequest} without being logged in.");

                        _ = Program.Database.PostMediusStats(data.ClientObject.AccountId, Convert.ToBase64String(accountUpdateStatsRequest.Stats)).ContinueWith((r) =>
                        {
                            if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                return;

                            if (r.IsCompletedSuccessfully && r.Result)
                            {
                                data.ClientObject.Queue(new MediusAccountUpdateStatsResponse()
                                {
                                    MessageID = accountUpdateStatsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess
                                });
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusAccountUpdateStatsResponse()
                                {
                                    MessageID = accountUpdateStatsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusDBError
                                });
                            }
                        });
                        break;
                    }

                case MediusTicketLoginRequest ticketLoginRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {ticketLoginRequest} without a session.");

                        // Check the client isn't already logged in
                        if (Program.Manager.GetClientByAccountName(ticketLoginRequest.UserOnlineId, data.ClientObject.ApplicationId)?.IsLoggedIn ?? false)
                        {
                            data.ClientObject.Queue(new MediusTicketLoginResponse()
                            {
                                MessageID = ticketLoginRequest.MessageID,
                                StatusCodeTicketLogin = MediusCallbackStatus.MediusAccountLoggedIn
                            });
                        }
                        else
                        {   //Check if their MacBanned
                            _ = Program.Database.GetIsMacBanned(data.MachineId).ContinueWith((r) =>
                            {
                                if (r.IsCompletedSuccessfully && data != null && data.ClientObject != null && data.ClientObject.IsConnected)
                                {

                                    #region isBanned?
                                    Logger.Info(msg: $"Is Connected User MAC Banned: {r.Result}");

                                    if (r.Result)
                                    {

                                        // Account is banned
                                        // Temporary solution is to tell the client the login failed
                                        data?.ClientObject?.Queue(new MediusTicketLoginResponse()
                                        {
                                            MessageID = ticketLoginRequest.MessageID,
                                            StatusCodeTicketLogin = MediusCallbackStatus.MediusAccountBanned
                                        });

                                        // Send ban message
                                        QueueBanMessage(data);
                                    }
                                    #endregion

                                    _ = Program.Database.GetAccountByName(ticketLoginRequest.UserOnlineId, data.ClientObject.ApplicationId).ContinueWith(async (r) =>
                                    {

                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null && data != null && data.ClientObject != null && data.ClientObject.IsConnected)
                                        {

                                            Logger.Info($"Account found for AppId from Client: {data.ClientObject.ApplicationId}");

                                            if (r.Result.IsBanned == true)
                                            {
                                                // Account is banned
                                                // Respond with Statuscode MediusAccountBanned
                                                data?.ClientObject?.Queue(new MediusTicketLoginResponse()
                                                {
                                                    MessageID = ticketLoginRequest.MessageID,
                                                    StatusCodeTicketLogin = MediusCallbackStatus.MediusAccountBanned
                                                });

                                                // Then queue send ban message
                                                QueueBanMessage(data, "Your CID has been banned");
                                            }

                                            #region AccountWhitelist Check
                                            else if (appSettings.EnableAccountWhitelist && !appSettings.AccountIdWhitelist.Contains(r.Result.AccountId))
                                            {

                                                Logger.Error($"AppId {data.ClientObject.ApplicationId} has EnableAccountWhitelist enabled or\n" +
                                                    $"Contains a AccountIdWhitelist!");

                                                // Account not allowed to sign in
                                                data?.ClientObject?.Queue(new MediusTicketLoginResponse()
                                                {
                                                    MessageID = ticketLoginRequest.MessageID,
                                                    StatusCodeTicketLogin = MediusCallbackStatus.MediusFail
                                                });
                                            }
                                            #endregion

                                            await Login(ticketLoginRequest.MessageID, clientChannel, data, r.Result, true);
                                        }
                                        else
                                        {
                                            // Account not found, create new and login
                                            #region AccountCreationDisabled?
                                            // Check that account creation is enabled
                                            if (appSettings.DisableAccountCreation)
                                            {
                                                Logger.Error($"AppId {data.ClientObject.ApplicationId} has DisableAllowCreation enabled!");

                                                // Reply error
                                                data.ClientObject.Queue(new MediusTicketLoginResponse()
                                                {
                                                    MessageID = ticketLoginRequest.MessageID,
                                                    StatusCodeTicketLogin = MediusCallbackStatus.MediusFail,
                                                });
                                                return;
                                            }
                                            #endregion

                                            Logger.Info($"Account not found for AppId from Client: {data.ClientObject.ApplicationId}");

                                            _ = Program.Database.CreateAccount(new Database.Models.CreateAccountDTO()
                                            {
                                                AccountName = ticketLoginRequest.UserOnlineId,
                                                AccountPassword = "TEMP",
                                                MachineId = data.MachineId,
                                                MediusStats = Convert.ToBase64String(new byte[Constants.ACCOUNTSTATS_MAXLEN]),
                                                AppId = data.ClientObject.ApplicationId
                                            }).ContinueWith(async (r) =>
                                            {
                                                Logger.Info($"Creating New Account for user {ticketLoginRequest.UserOnlineId}!");

                                                if (r.IsCompletedSuccessfully && r.Result != null)
                                                {
                                                    await Login(ticketLoginRequest.MessageID, clientChannel, data, r.Result, true);
                                                }
                                                else
                                                {
                                                    // Reply error
                                                    data.ClientObject.Queue(new MediusTicketLoginResponse()
                                                    {
                                                        MessageID = ticketLoginRequest.MessageID,
                                                        StatusCodeTicketLogin = MediusCallbackStatus.MediusDBError
                                                    });
                                                }

                                            });
                                        }
                                    });

                                }
                                else
                                {
                                    Logger.Info($"Account MachineID {data.MachineId} is BANNED!");

                                    // Reply error
                                    data.ClientObject.Queue(new MediusTicketLoginResponse()
                                    {
                                        MessageID = ticketLoginRequest.MessageID,
                                        StatusCodeTicketLogin = MediusCallbackStatus.MediusMachineBanned,
                                    });
                                }
                            });
                        }
                        break;
                    }

                #endregion

                #region Policy / Announcements

                case MediusGetAllAnnouncementsRequest getAllAnnouncementsRequest:
                    {
                        // Send to plugins
                        await Program.Plugins.OnEvent(PluginEvent.MEDIUS_PLAYER_ON_GET_ALL_ANNOUNCEMENTS, new OnPlayerRequestArgs()
                        {
                            Player = data.ClientObject,
                            Request = getAllAnnouncementsRequest
                        });

                        _ = Program.Database.GetLatestAnnouncements(data.ApplicationId).ContinueWith((r) =>
                        {
                            if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                return;

                            if (r.IsCompletedSuccessfully && r.Result != null && r.Result.Length > 0)
                            {
                                List<MediusGetAnnouncementsResponse> responses = new List<MediusGetAnnouncementsResponse>();
                                foreach (var result in r.Result)
                                {
                                    responses.Add(new MediusGetAnnouncementsResponse()
                                    {
                                        MessageID = getAllAnnouncementsRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        Announcement = string.IsNullOrEmpty(result.AnnouncementTitle) ? $"{result.AnnouncementBody}" : $"{result.AnnouncementTitle}\n{result.AnnouncementBody}\n",
                                        AnnouncementID = result.Id++,
                                        EndOfList = false
                                    });
                                }

                                responses[responses.Count - 1].EndOfList = true;
                                data.ClientObject.Queue(responses);
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusGetAnnouncementsResponse()
                                {
                                    MessageID = getAllAnnouncementsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    Announcement = "",
                                    AnnouncementID = 0,
                                    EndOfList = true
                                });
                            }
                        });
                        break;
                    }

                case MediusGetAnnouncementsRequest getAnnouncementsRequest:
                    {
                        // Send to plugins
                        await Program.Plugins.OnEvent(PluginEvent.MEDIUS_PLAYER_ON_GET_ANNOUNCEMENTS, new OnPlayerRequestArgs()
                        {
                            Player = data.ClientObject,
                            Request = getAnnouncementsRequest
                        });

                        _ = Program.Database.GetLatestAnnouncement(data.ApplicationId).ContinueWith((r) =>
                        {
                            if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                return;

                            if (r.IsCompletedSuccessfully && r.Result != null)
                            {
                                data.ClientObject.Queue(new MediusGetAnnouncementsResponse()
                                {
                                    MessageID = getAnnouncementsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    Announcement = string.IsNullOrEmpty(r.Result.AnnouncementTitle) ? $"{r.Result.AnnouncementBody}" : $"{r.Result.AnnouncementTitle}\n{r.Result.AnnouncementBody}\n",
                                    AnnouncementID = r.Result.Id++,
                                    EndOfList = true
                                });
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusGetAnnouncementsResponse()
                                {
                                    MessageID = getAnnouncementsRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    Announcement = "",
                                    AnnouncementID = 0,
                                    EndOfList = true
                                });
                            }
                        });
                        break;
                    }

                case MediusGetPolicyRequest getPolicyRequest:
                    {
                        // Send to plugins
                        await Program.Plugins.OnEvent(PluginEvent.MEDIUS_PLAYER_ON_GET_POLICY, new OnPlayerRequestArgs()
                        {
                            Player = data.ClientObject,
                            Request = getPolicyRequest
                        });

                        switch (getPolicyRequest.Policy)
                        {
                            case MediusPolicyType.Privacy:
                                {
                                    _ = Program.Database.GetPolicy((int)MediusPolicyType.Privacy, data.ClientObject.ApplicationId).ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            string txt = r.Result.EulaBody;
                                            if (!string.IsNullOrEmpty(r.Result.EulaTitle))
                                                txt = r.Result.EulaTitle + "\n" + txt;
                                            data.ClientObject.Queue(MediusGetPolicyResponse.FromText(getPolicyRequest.MessageID, txt));
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetPolicyResponse() { MessageID = getPolicyRequest.MessageID, StatusCode = MediusCallbackStatus.MediusSuccess, Policy = "", EndOfText = true });
                                        }
                                    });
                                    break;
                                }
                            case MediusPolicyType.Usage:
                                {
                                    _ = Program.Database.GetPolicy((int)MediusPolicyType.Usage, data.ClientObject.ApplicationId).ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            string txt = r.Result.EulaBody;
                                            if (!string.IsNullOrEmpty(r.Result.EulaTitle))
                                                txt = r.Result.EulaTitle + "\n" + txt;
                                            data.ClientObject.Queue(MediusGetPolicyResponse.FromText(getPolicyRequest.MessageID, txt));
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetPolicyResponse() { MessageID = getPolicyRequest.MessageID, StatusCode = MediusCallbackStatus.MediusSuccess, Policy = "", EndOfText = true });
                                        }
                                    });

                                    break;
                                }
                        }
                        break;
                    }

                #endregion

                #region Ladders

                case MediusGetLadderStatsRequest getLadderStatsRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getLadderStatsRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getLadderStatsRequest} without being logged in.");

                        switch (getLadderStatsRequest.LadderType)
                        {
                            case MediusLadderType.MediusLadderTypePlayer:
                                {
                                    _ = Program.Database.GetAccountById(getLadderStatsRequest.AccountID_or_ClanID).ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsResponse()
                                            {
                                                MessageID = getLadderStatsRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                Stats = Array.ConvertAll(r.Result.AccountStats, Convert.ToInt32)
                                            });
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsResponse()
                                            {
                                                MessageID = getLadderStatsRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusDBError
                                            });
                                        }
                                    });
                                    break;
                                }
                            case MediusLadderType.MediusLadderTypeClan:
                                {
                                    _ = Program.Database.GetClanById(getLadderStatsRequest.AccountID_or_ClanID,
                                        data.ClientObject.ApplicationId)
                                    .ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsResponse()
                                            {
                                                MessageID = getLadderStatsRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                Stats = r.Result.ClanStats
                                            });
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsResponse()
                                            {
                                                MessageID = getLadderStatsRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusDBError
                                            });
                                        }
                                    });
                                    break;
                                }
                            default:
                                {
                                    Logger.Warn($"Unhandled MediusGetLadderStatsRequest {getLadderStatsRequest}");
                                    break;
                                }
                        }
                        break;
                    }

                case MediusGetLadderStatsWideRequest getLadderStatsWideRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getLadderStatsWideRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getLadderStatsWideRequest} without being logged in.");

                        switch (getLadderStatsWideRequest.LadderType)
                        {
                            case MediusLadderType.MediusLadderTypePlayer:
                                {
                                    _ = Program.Database.GetAccountById(getLadderStatsWideRequest.AccountID_or_ClanID).ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsWideResponse()
                                            {
                                                MessageID = getLadderStatsWideRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                AccountID_or_ClanID = r.Result.AccountId,
                                                Stats = r.Result.AccountWideStats
                                            });
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsWideResponse()
                                            {
                                                MessageID = getLadderStatsWideRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusDBError
                                            });
                                        }
                                    });
                                    break;
                                }
                            case MediusLadderType.MediusLadderTypeClan:
                                {
                                    _ = Program.Database.GetClanById(getLadderStatsWideRequest.AccountID_or_ClanID,
                                        data.ClientObject.ApplicationId)
                                    .ContinueWith((r) =>
                                    {
                                        if (data == null || data.ClientObject == null || !data.ClientObject.IsConnected)
                                            return;

                                        if (r.IsCompletedSuccessfully && r.Result != null)
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsWideResponse()
                                            {
                                                MessageID = getLadderStatsWideRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                                AccountID_or_ClanID = r.Result.ClanId,
                                                Stats = r.Result.ClanWideStats
                                            });
                                        }
                                        else
                                        {
                                            data.ClientObject.Queue(new MediusGetLadderStatsWideResponse()
                                            {
                                                MessageID = getLadderStatsWideRequest.MessageID,
                                                StatusCode = MediusCallbackStatus.MediusDBError
                                            });
                                        }
                                    });
                                    break;
                                }
                            default:
                                {
                                    Logger.Warn($"Unhandled MediusGetLadderStatsWideRequest {getLadderStatsWideRequest}");
                                    break;
                                }
                        }
                        break;
                    }

                #endregion

                #region Channels

                case MediusChannelListRequest channelListRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {channelListRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {channelListRequest} without being logged in.");

                        List<MediusChannelListResponse> channelResponses = new List<MediusChannelListResponse>();

                        var lobbyChannels = Program.Manager.GetChannelList(
                            data.ClientObject.ApplicationId,
                            channelListRequest.PageID,
                            channelListRequest.PageSize,
                            ChannelType.Lobby
                        );


                        foreach (var channel in lobbyChannels)
                        {
                            channelResponses.Add(new MediusChannelListResponse()
                            {
                                MessageID = channelListRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusSuccess,
                                MediusWorldID = channel.Id,
                                LobbyName = channel.Name,
                                PlayerCount = channel.PlayerCount,
                                EndOfList = false
                            });
                        }

                        if (channelResponses.Count == 0)
                        {
                            // Return none
                            data.ClientObject.Queue(new MediusChannelListResponse()
                            {
                                MessageID = channelListRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                EndOfList = true
                            });
                        }
                        else
                        {
                            // Ensure the end of list flag is set
                            channelResponses[channelResponses.Count - 1].EndOfList = true;

                            // Add to responses
                            data.ClientObject.Queue(channelResponses);
                        }


                        break;
                    }

                #endregion

                #region Deadlocked No-op Messages (MAS)

                case MediusGetBuddyList_ExtraInfoRequest getBuddyList_ExtraInfoRequest:
                    {
                        Queue(new RT_MSG_SERVER_APP()
                        {
                            Message = new MediusGetBuddyList_ExtraInfoResponse()
                            {
                                MessageID = getBuddyList_ExtraInfoRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                EndOfList = true
                            }
                        }, clientChannel);
                        break;
                    }

                case MediusGetIgnoreListRequest getIgnoreListRequest:
                    {
                        Queue(new RT_MSG_SERVER_APP()
                        {
                            Message = new MediusGetIgnoreListResponse()
                            {
                                MessageID = getIgnoreListRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                EndOfList = true
                            }
                        }, clientChannel);
                        break;
                    }

                case MediusGetMyClansRequest getMyClansRequest:
                    {
                        Queue(new RT_MSG_SERVER_APP()
                        {
                            Message = new MediusGetMyClansResponse()
                            {
                                MessageID = getMyClansRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusNoResult,
                                EndOfList = true
                            }
                        }, clientChannel);
                        break;
                    }

                #endregion

                #region TextFilter

                case MediusTextFilterRequest textFilterRequest:
                    {
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {textFilterRequest} without a session.");

                        // Deny special characters
                        // Also trim any whitespace
                        switch (textFilterRequest.TextFilterType)
                        {
                            case MediusTextFilterType.MediusTextFilterPassFail:
                                {
                                    // validate name
                                    if (!Program.PassTextFilter(data.ApplicationId, Config.TextFilterContext.ACCOUNT_NAME, textFilterRequest.Text))
                                    {
                                        // Failed due to special characters
                                        data.ClientObject.Queue(new MediusTextFilterResponse()
                                        {
                                            MessageID = textFilterRequest.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusFail
                                        });
                                        return;
                                    }
                                    else
                                    {
                                        //
                                        data.ClientObject.Queue(new MediusTextFilterResponse()
                                        {
                                            MessageID = textFilterRequest.MessageID,
                                            StatusCode = MediusCallbackStatus.MediusSuccess,
                                            Text = textFilterRequest.Text.Trim()
                                        });
                                    }
                                    break;
                                }
                            case MediusTextFilterType.MediusTextFilterReplace:
                                {
                                    data.ClientObject.Queue(new MediusTextFilterResponse()
                                    {
                                        MessageID = textFilterRequest.MessageID,
                                        StatusCode = MediusCallbackStatus.MediusSuccess,
                                        Text = Program.FilterTextFilter(data.ApplicationId, Config.TextFilterContext.ACCOUNT_NAME, textFilterRequest.Text).Trim()
                                    });
                                    break;
                                }
                        }
                        break;
                    }

                #endregion

                #region Time
                case MediusGetServerTimeRequest getServerTimeRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getServerTimeRequest} without a session.");

                        // ERROR -- Need to be logged in
                        if (!data.ClientObject.IsLoggedIn)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getServerTimeRequest} without being logged in.");

                        var time = DateTime.Now;

                        _ = GetTimeZone(time).ContinueWith((r) =>
                        {
                            if (r.IsCompletedSuccessfully)
                            {
                                //Fetched
                                data.ClientObject.Queue(new MediusGetServerTimeResponse()
                                {
                                    MessageID = getServerTimeRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    Local_server_timezone = r.Result,
                                });
                            } else {

                                //default
                                data.ClientObject.Queue(new MediusGetServerTimeResponse()
                                {
                                    MessageID = getServerTimeRequest.MessageID,
                                    StatusCode = MediusCallbackStatus.MediusSuccess,
                                    Local_server_timezone = MediusTimeZone.MediusTimeZone_GMT,
                                });
                            }
                        });
                        break;
                    }
                #endregion

                #region GetMyIP
                //Syphon Filter - The Omega Strain Beta

                case MediusGetMyIPRequest getMyIpRequest:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {getMyIpRequest} without a session.");

                        if (!data.ClientObject.IsLoggedIn && data.ClientObject.ApplicationId != 10411)
                        {
                            Logger.Info($"Get My IP Request Handler Error: [{data.ClientObject}]: Player Not Privileged\nPlayer not logged in");
                            data.ClientObject.Queue(new MediusGetMyIPResponse()
                            {
                                MessageID = getMyIpRequest.MessageID,
                                StatusCode = MediusCallbackStatus.MediusPlayerNotPrivileged
                            });
                        }
                        else
                        {
                            var ClientIP = (clientChannel.RemoteAddress as IPEndPoint)?.Address;

                            if (ClientIP == null)
                            {
                                //var temp = DmeServerClientIpQuery()

                                Logger.Info($"Error: Retrieving Client IP address {clientChannel.RemoteAddress} = [{ClientIP}]");
                                data.ClientObject.Queue(new MediusGetMyIPResponse()
                                {
                                    MessageID = getMyIpRequest.MessageID,
                                    IP = null,
                                    StatusCode = MediusCallbackStatus.MediusDMEError
                                });
                            }
                            else
                            {
                                data.ClientObject.Queue(new MediusGetMyIPResponse()
                                {
                                    MessageID = getMyIpRequest.MessageID,
                                    IP = ClientIP,
                                    StatusCode = MediusCallbackStatus.MediusSuccess
                                });
                            }


                        }


                        break;
                    }

                #endregion

                #region UpdateUserState
                case MediusUpdateUserState updateUserState:
                    {
                        // ERROR - Need a session
                        if (data.ClientObject == null)
                            throw new InvalidOperationException($"INVALID OPERATION: {clientChannel} sent {updateUserState} without a session.");

                        // ERROR - Needs to be logged in --Doesn't need to be logged in on older clients

                        switch (updateUserState.UserAction)
                        {
                            case MediusUserAction.KeepAlive:
                                {
                                    data.ClientObject.KeepAliveUntilNextConnection();
                                    break;
                                }
                            case MediusUserAction.JoinedChatWorld:
                                {
                                    //await data.ClientObject.JoinChannel(Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId));
                                    break;
                                }
                            case MediusUserAction.LeftGameWorld:
                                {
                                    await data.ClientObject.LeaveGame(data.ClientObject.CurrentGame);
                                    Program.AntiCheatPlugin.mc_anticheat_event_msg_UPDATEUSERSTATE(AnticheatEventCode.anticheatLEAVEGAME, data.ClientObject.WorldId, data.ClientObject.AccountId, Program.AntiCheatClient, updateUserState, 256);
                                    break;
                                }
                        }

                        break;
                    }

                #endregion

                default:
                    {
                        Logger.Warn($"Unhandled Medius Message: {message}");
                        break;
                    }
            }
        }

        #region Login
        private async Task Login(MessageId messageId, IChannel clientChannel, ChannelData data, Database.Models.AccountDTO accountDto, bool ticket)
        {
            var fac = new PS2CipherFactory();
            var rsa = fac.CreateNew(CipherContext.RSA_AUTH) as PS2_RSA;

            List<int> pre108Secure = new List<int>() { 10124, 10680 };

            //
            await data.ClientObject.Login(accountDto);

            #region Update DB IP and CID
            _ = Program.Database.PostAccountIp(accountDto.AccountId, (clientChannel.RemoteAddress as IPEndPoint).Address.MapToIPv4().ToString());
            if (!string.IsNullOrEmpty(data.MachineId))
                _ = Program.Database.PostMachineId(data.ClientObject.AccountId, data.MachineId);
            #endregion

            // Add to logged in clients
            Program.Manager.AddClient(data.ClientObject);

            // 
            Logger.Info($"LOGGING IN AS {data.ClientObject.AccountName} with access token {data.ClientObject.Token}");


            IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);

            // Tell client
            if (ticket == true)
            {
                #region PS Home PS3
                //If PS Home don't GetOrCreateDefaultLobbyChannel, Home creates their own channels
                if (data.ClientObject.ApplicationId == 20371 || data.ClientObject.ApplicationId == 20374)
                {
                    data.ClientObject.Queue(new MediusTicketLoginResponse()
                    {
                        //TicketLoginResponse
                        MessageID = messageId,
                        StatusCodeTicketLogin = MediusCallbackStatus.MediusSuccess,
                        PasswordType = MediusPasswordType.MediusPasswordNotSet,

                        //AccountLoginResponse Wrapped
                        MessageID2 = messageId,
                        StatusCodeAccountLogin = MediusCallbackStatus.MediusSuccess,
                        AccountID = data.ClientObject.AccountId,
                        AccountType = MediusAccountType.MediusMasterAccount,
                        MediusWorldID = 1, // Reserved
                        ConnectInfo = new NetConnectionInfo()
                        {
                            AccessKey = data.ClientObject.Token,
                            SessionKey = data.ClientObject.SessionKey,
                            WorldID = 1, // Reserved,
                            ServerKey = new RSA_KEY(), //Program.GlobalAuthPublic,
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                            {
                                new NetAddress() {Address = Program.LobbyServer.IPAddress.ToString(), Port = (uint)Program.LobbyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal},
                                new NetAddress() {Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService},
                            }
                            },
                            Type = NetConnectionType.NetConnectionTypeClientServerTCP
                        },
                    });
                }
                #endregion

                //Default
                else
                {
                    // Put client in default channel
                    await data.ClientObject.JoinChannel(Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId));

                    #region IF PS3 Client
                    data.ClientObject.Queue(new MediusTicketLoginResponse()
                    {
                        //TicketLoginResponse
                        MessageID = messageId,
                        StatusCodeTicketLogin = MediusCallbackStatus.MediusSuccess,
                        PasswordType = MediusPasswordType.MediusPasswordNotSet,

                        //AccountLoginResponse Wrapped
                        MessageID2 = messageId,
                        StatusCodeAccountLogin = MediusCallbackStatus.MediusSuccess,
                        AccountID = data.ClientObject.AccountId,
                        AccountType = MediusAccountType.MediusMasterAccount,
                        MediusWorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                        ConnectInfo = new NetConnectionInfo()
                        {
                            AccessKey = data.ClientObject.Token,
                            SessionKey = data.ClientObject.SessionKey,
                            WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                            ServerKey = new RSA_KEY(), //Program.GlobalAuthPublic,
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                    new NetAddress() {Address = Program.LobbyServer.IPAddress.ToString(), Port = (uint)Program.LobbyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal},
                                    new NetAddress() {Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService},
                                }
                            },
                            Type = NetConnectionType.NetConnectionTypeClientServerTCP
                        },
                    });
                    #endregion
                }

                // Prepare for transition to lobby server
                data.ClientObject.KeepAliveUntilNextConnection();
            }
            else
            {
                // Put client in default channel
                await data.ClientObject.JoinChannel(Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId));

                #region If PS2/PSP

                if (data.ClientObject.MediusVersion > 108 || pre108Secure.Contains(data.ClientObject.ApplicationId))
                {

                    data.ClientObject.Queue(new MediusAccountLoginResponse()
                    {
                        MessageID = messageId,
                        StatusCode = MediusCallbackStatus.MediusSuccess,
                        AccountID = data.ClientObject.AccountId,
                        AccountType = MediusAccountType.MediusMasterAccount,
                        MediusWorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                        ConnectInfo = new NetConnectionInfo()
                        {
                            AccessKey = data.ClientObject.Token,
                            SessionKey = data.ClientObject.SessionKey,
                            WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                            ServerKey = Program.GlobalAuthPublic,
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                new NetAddress() {Address = Program.LobbyServer.IPAddress.ToString(), Port = (uint)Program.LobbyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal},
                                new NetAddress() {Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService},
                                }
                            },
                            Type = NetConnectionType.NetConnectionTypeClientServerTCP
                        },
                    });
                }
                else
                {

                    data.ClientObject.Queue(new MediusAccountLoginResponse()
                    {
                        MessageID = messageId,
                        StatusCode = MediusCallbackStatus.MediusSuccess,
                        AccountID = data.ClientObject.AccountId,
                        AccountType = MediusAccountType.MediusMasterAccount,
                        MediusWorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                        ConnectInfo = new NetConnectionInfo()
                        {
                            AccessKey = data.ClientObject.Token,
                            SessionKey = data.ClientObject.SessionKey,
                            WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                            ServerKey = new RSA_KEY() { }, //Some Older Medius games don't set a RSA Key
                            AddressList = new NetAddressList()
                            {
                                AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                                {
                                new NetAddress() {Address = Program.LobbyServer.IPAddress.ToString(), Port = (uint)Program.LobbyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal},
                                new NetAddress() {Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService},
                                }
                            },
                            Type = NetConnectionType.NetConnectionTypeClientServerTCP
                        },
                    });
                }

                // Prepare for transition to lobby server
                data.ClientObject.KeepAliveUntilNextConnection();
                #endregion
            }
        }
        #endregion

        #region AnonymousLogin
        private async Task LoginAnonymous(MediusAnonymousLoginRequest anonymousLoginRequest, IChannel clientChannel, ChannelData data)
        {
            IPHostEntry host = Dns.GetHostEntry(Program.Settings.NATIp);
            var fac = new PS2CipherFactory();
            var rsa = fac.CreateNew(CipherContext.RSA_AUTH) as PS2_RSA;

            int iAccountID = Program.Manager.AnonymousAccountIDGenerator(Program.Settings.AnonymousIDRangeSeed);
            Logger.Info($"AnonymousIDRangeSeedGenerator AccountID returned {iAccountID}");

            //
            //await data.ClientObject.Login(accountDto);


            // Login
            await data.ClientObject.LoginAnonymous(anonymousLoginRequest, iAccountID);

            #region Update DB IP and CID

            //We don't post to the database as anonymous... This ain't it chief

            #endregion

            // Add to logged in clients
            Program.Manager.AddClient(data.ClientObject);

            // 
            Logger.Info($"LOGGING IN ANONYMOUSLY AS {data.ClientObject.AccountDisplayName} with access token {data.ClientObject.Token}");

            // Put client in default channel
            //await data.ClientObject.JoinChannel(Program.Manager.GetOrCreateDefaultLobbyChannel(data.ApplicationId));

            // Tell client
            data.ClientObject.Queue(new MediusAnonymousLoginResponse()
            {
                MessageID = anonymousLoginRequest.MessageID,
                StatusCode = MediusCallbackStatus.MediusSuccess,
                AccountID = iAccountID,
                AccountType = MediusAccountType.MediusMasterAccount,
                MediusWorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                ConnectInfo = new NetConnectionInfo()
                {
                    AccessKey = data.ClientObject.Token,
                    SessionKey = data.ClientObject.SessionKey,
                    WorldID = Program.Manager.GetOrCreateDefaultLobbyChannel(data.ClientObject.ApplicationId).Id,
                    ServerKey = new RSA_KEY(), // Null for 108 clients
                    AddressList = new NetAddressList()
                    {
                        AddressList = new NetAddress[Constants.NET_ADDRESS_LIST_COUNT]
                        {
                               new NetAddress() {Address = Program.LobbyServer.IPAddress.ToString(), Port = (uint)Program.LobbyServer.TCPPort, AddressType = NetAddressType.NetAddressTypeExternal},
                               new NetAddress() {Address = host.AddressList.First().ToString(), Port = (uint)Program.Settings.NATPort, AddressType = NetAddressType.NetAddressTypeNATService},
                        }
                    },
                    Type = NetConnectionType.NetConnectionTypeClientServerTCP
                }
            });

            data.ClientObject.KeepAliveUntilNextConnection();

        }
        #endregion

        #region TimeZone
        public Task<MediusTimeZone> GetTimeZone(DateTime time) {

            var tz = TimeZoneInfo.Local;
            var tzInt = Convert.ToInt32(tz.Id);


            var tzStanName = tz.StandardName;

            /*
            if (tzTime. == 7200)
            {

            }
            */

            if (tzStanName == "CEST")
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_CEST);
            }
            else if (tzInt == 83 && (tzInt + 1) == 83 && (tzInt + 2) == 84)
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_SWEDISHST);
            }
            else if (tzInt == 70 && (tzInt + 1) == 83 && (tzInt + 2) == 84)
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_FST);
            } 
            else if (tzInt == 67 && (tzInt + 1) == 65 && (tzInt + 2) == 84)
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_CAT);
            }
            else if (tzStanName == "SAST")
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_SAST);
            }
            else if (tzInt == 69 && (tzInt + 1) == 65 && (tzInt + 2) == 84)
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_EET);
            }
            else if (tzInt == 73 && (tzInt + 1) == 65 && (tzInt + 2) == 84)
            {
                return Task.FromResult(MediusTimeZone.MediusTimeZone_ISRAELST);
            }

            return Task.FromResult(MediusTimeZone.MediusTimeZone_GMT);
        }
        #endregion

        #region ConvertFromIntegerToIpAddress
        /// <summary>
        /// Convert from Binary Ip Address to UInt
        /// </summary>
        /// <param name="ipAddress">Binary formatted IP Address</param>
        /// <returns></returns>
        public static string ConvertFromIntegerToIpAddress(uint ipAddress)
        {
            byte[] bytes = BitConverter.GetBytes(ipAddress);
            string ipAddressConverted = new IPAddress(bytes).ToString();

            // flip little-endian to big-endian(network order)
            /* NOT NEEDED
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            */
            return ipAddressConverted;
        }
        #endregion

        #region ConvertFromIntegerToPort
        /// <summary>
        /// Convert from Binary Ip Address to UInt
        /// </summary>
        /// <param name="port">Binary formatted IP Address</param>
        /// <returns></returns>
        public static int ConvertFromIntegerToPort(string port)
        {
            int i = Convert.ToInt32(port, 2);

            // flip little-endian to big-endian(network order)
            /* NOT NEEDED
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            */
            return i;
        }
        #endregion

        #region ConvertFromIntegerToIpAddress
        /// <summary>
        /// Convert from Binary Ip Address to UInt
        /// </summary>
        /// <param name="ipAddress">Binary formatted IP Address</param>
        /// <returns></returns>
        public static uint ConvertFromIpAddressToBinary(IPAddress ipAddress)
        {
            uint Uint = (uint)BitConverter.ToInt32(ipAddress.GetAddressBytes());

            // flip little-endian to big-endian(network order)
            /* NOT NEEDED
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            */
            return Uint;
        }
        #endregion
    }
}