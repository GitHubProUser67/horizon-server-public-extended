﻿using Deadlocked.Server.Medius;
using Deadlocked.Server.Mods;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using DotNetty.Common.Internal.Logging;
using RT.Common;
using RT.Models;

namespace Deadlocked.Server.Medius.Models
{
    public class Game
    {
        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<Game>();

        public static int IdCounter = 1;

        public class GameClient
        {
            public ClientObject Client;
            public bool InGame;
        }

        public int Id = 0;
        public int DMEWorldId = 0;
        public int ApplicationId = 0;
        public List<GameClient> Clients = new List<GameClient>();
        public string GameName;
        public string GamePassword;
        public string SpectatorPassword;
        public byte[] GameStats = new byte[Constants.GAMESTATS_MAXLEN];
        public MediusGameHostType GameHostType;
        public int MinPlayers;
        public int MaxPlayers;
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
        public MediusWorldStatus WorldStatus = MediusWorldStatus.WorldPendingCreation;
        public MediusWorldAttributesType Attributes;
        public DMEObject DMEServer;
        public Channel ChatChannel;
        public ClientObject Host;

        public Gamemode CustomGamemode = null;

        private bool hasHostJoined = false;
        private DateTime utcTimeCreated;
        private DateTime? utcTimeEmpty;

        public uint Time => (uint)(DateTime.UtcNow - utcTimeCreated).TotalMilliseconds;

        public int PlayerCount => Clients.Count(x => x != null && x.Client.IsConnected);

        public bool ReadyToDestroy => WorldStatus == MediusWorldStatus.WorldClosed && (DateTime.UtcNow - utcTimeEmpty)?.TotalSeconds > 1f;

        public Game(ClientObject client, MediusCreateGameRequest createGame, Channel chatChannel, DMEObject dmeServer)
        {
            Id = IdCounter++;
            ApplicationId = createGame.ApplicationID;
            GameName = createGame.GameName;
            MinPlayers = createGame.MinPlayers;
            MaxPlayers = createGame.MaxPlayers;
            GameLevel = createGame.GameLevel;
            PlayerSkillLevel = createGame.PlayerSkillLevel;
            RulesSet = createGame.RulesSet;
            GenericField1 = createGame.GenericField1;
            GenericField2 = createGame.GenericField2;
            GenericField3 = createGame.GenericField3;
            GenericField4 = createGame.GenericField4;
            GenericField5 = createGame.GenericField5;
            GenericField6 = createGame.GenericField6;
            GenericField7 = createGame.GenericField7;
            GenericField8 = createGame.GenericField8;
            GamePassword = createGame.GamePassword;
            SpectatorPassword = createGame.SpectatorPassword;
            GameHostType = createGame.GameHostType;
            Attributes = createGame.Attributes;
            WorldStatus = MediusWorldStatus.WorldPendingCreation;
            utcTimeCreated = DateTime.UtcNow;
            utcTimeEmpty = null;
            DMEServer = dmeServer;
            ChatChannel = chatChannel;
            ChatChannel?.RegisterGame(this);
            Host = client;

            Logger.Info($"Game {Id}:{GameName}: Created by {client}");

#if DEBUG
            CustomGamemode = Program.Settings.Gamemodes.LastOrDefault();
#endif
        }

        public void Tick()
        {
            // Remove timedout clients
            for (int i = 0; i < Clients.Count; ++i)
            {
                var client = Clients[i];

                if (client == null || client.Client == null || !client.Client.IsConnected || client.Client.CurrentGame?.Id != Id)
                {
                    Clients.RemoveAt(i);
                    --i;
                }
            }

            // Auto close when everyone leaves or if host fails to connect after timeout time
            if (!utcTimeEmpty.HasValue && Clients.Count(x=>x.InGame) == 0 && (hasHostJoined || (DateTime.UtcNow - utcTimeCreated).TotalSeconds > Program.Settings.GameTimeoutSeconds))
            {
                utcTimeEmpty = DateTime.UtcNow;
                WorldStatus = MediusWorldStatus.WorldClosed;
            }
        }

        public void OnMediusServerConnectNotification(MediusServerConnectNotification notification)
        {
            var player = Clients.FirstOrDefault(x => x.Client.SessionKey == notification.PlayerSessionKey);
            if (player == null)
                return;

            switch (notification.ConnectEventType)
            {
                case MGCL_EVENT_TYPE.MGCL_EVENT_CLIENT_CONNECT:
                    {
                        OnPlayerJoined(player);
                        break;
                    }
                case MGCL_EVENT_TYPE.MGCL_EVENT_CLIENT_DISCONNECT:
                    {
                        OnPlayerLeft(player);
                        break;
                    }
            }
        }

        private void OnPlayerJoined(GameClient player)
        {
            player.InGame = true;

            if (player.Client == Host)
                hasHostJoined = true;
        }

        public void AddPlayer(ClientObject client)
        {
            // Don't add again
            if (Clients.Any(x => x.Client == client))
                return;

            // 
            Logger.Info($"Game {Id}:{GameName}: {client} added.");

            Clients.Add(new GameClient()
            {
                Client = client
            });

            // Inform the client of any custom game mode
            client.CurrentChannel?.SendSystemMessage(client, $"Gamemode is {CustomGamemode?.FullName ?? "default"}.");
        }

        private void OnPlayerLeft(GameClient player)
        {
            // 
            Logger.Info($"Game {Id}:{GameName}: {player.Client} left.");

            // 
            player.InGame = false;

            // Update player object
            player.Client.LeaveGame(this);
            player.Client.LeaveChannel(ChatChannel);

            // Remove from collection
            RemovePlayer(player.Client);
        }

        public void RemovePlayer(ClientObject client)
        {
            // 
            Logger.Info($"Game {Id}:{GameName}: {client} removed.");

            // Remove host
            if (Host == client)
                Host = null;

            // Remove from clients list
            Clients.RemoveAll(x => x.Client == client);
        }

        public void OnEndGameReport(MediusEndGameReport report)
        {
            WorldStatus = MediusWorldStatus.WorldClosed;
        }


        public void OnPlayerReport(MediusPlayerReport report)
        {
            // Ensure report is for correct game world
            if (report.MediusWorldID != Id)
                return;
        }
        public void OnWorldReport(MediusWorldReport report)
        {
            // Ensure report is for correct game world
            if (report.MediusWorldID != Id)
                return;

            GameName = report.GameName;
            MinPlayers = report.MinPlayers;
            MaxPlayers = report.MaxPlayers;
            GameLevel = report.GameLevel;
            PlayerSkillLevel = report.PlayerSkillLevel;
            RulesSet = report.RulesSet;
            GenericField1 = report.GenericField1;
            GenericField2 = report.GenericField2;
            GenericField3 = report.GenericField3;
            GenericField4 = report.GenericField4;
            GenericField5 = report.GenericField5;
            GenericField6 = report.GenericField6;
            GenericField7 = report.GenericField7;
            GenericField8 = report.GenericField8;

            // Once the world has been closed then we force it closed.
            // This is because when the host hits 'Play Again' they tell the server the world has closed (EndGameReport)
            // but the existing clients tell the server the world is still active.
            // This gives the host a "Game Name Already Exists" when they try to remake with the same name.
            // This just fixes that. At the cost of the game not showing after a host leaves a game.
            if (WorldStatus != MediusWorldStatus.WorldClosed)
            {
                // When game starts, send game mode payload
                if (report.WorldStatus == MediusWorldStatus.WorldActive && WorldStatus != MediusWorldStatus.WorldActive)
                {
                    if (CustomGamemode != null)
                        CustomGamemode.Apply(Clients.Select(x => x.Client));
                    else
                        Gamemode.Disable(Clients.Select(x => x.Client));
                }

                WorldStatus = report.WorldStatus;
            }
        }

        public void EndGame()
        {
            // 
            Logger.Info($"Game {Id}:{GameName}: EndGame() called.");

            // Remove players from game world
            while (Clients.Count > 0)
            {
                var client = Clients[0].Client;
                if (client == null)
                {
                    Clients.RemoveAt(0);
                }
                else
                {
                    client.LeaveGame(this);
                    client.LeaveChannel(ChatChannel);
                }
            }


            // Unregister from channel
            ChatChannel?.UnregisterGame(this);

            // Send end game
            DMEServer?.Queue(new MediusServerEndGameRequest()
            {
                WorldID = this.DMEWorldId,
                BrutalFlag = false
            });
        }
    }
}
