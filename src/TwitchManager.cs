using System;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Client;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Clients;
using Godot;
using TwitchLib.Communication.Events;

namespace BitCup
{
	public enum OAuthState
	{
		Unknown,
		Valid,
		Invalid
	}

	public class TwitchManager
	{
		public const string CLIENT_ID = "gsqo3a59xrqob257ku9ou70u9roqzy";
		public const string TWITCH_REDIRECT_ADDRESS = "localhost";
		public const ushort TWITCH_REDIRECT_PORT = 8080;

		public TwitchClient Client;

		public OAuthState OAuthState;

		BitManager BitManager;

		TcpServer OAuthTCPServer;

		StreamPeerTcp StreamPeerTcp;

		string OAuthBuffer;

		public TwitchManager(BitManager bitManager)
		{
			Debug.Assert(bitManager != null);
			BitManager = bitManager;
		}

		private void ConnectAndInitClient(User user)
		{
			if (string.IsNullOrEmpty(user.Username))
			{
				Debug.Error("user.Username is null or empty");
				return;
			}

			if (string.IsNullOrEmpty(user.OAuth))
			{
				Debug.LogErr("user.OAuth is null or empty");
				FetchNewOAuth();
				return;
			}

			BitManager.TwitchAPI.RequestUser();

			ConnectionCredentials credentials = new ConnectionCredentials(user.Username, user.OAuth);

			ClientOptions clientOptions = new ClientOptions();
			clientOptions.MessagesAllowedInPeriod = 750;
			clientOptions.ThrottlingPeriod = TimeSpan.FromSeconds(30);
			clientOptions.ReconnectionPolicy = new ReconnectionPolicy(6_000, maxAttempts: 10);

			WebSocketClient customClient = new WebSocketClient(clientOptions);
			Client = new TwitchClient(customClient);
			// Note: "channel" is wrong, but doesn't work otherwise 
			Client.Initialize(credentials, "channel");

			Client.OnConnected += Client_OnConnected;
			Client.OnDisconnected += Client_OnDisconnected;
			Client.OnConnectionError += Client_OnConnectionError;
			Client.OnError += Client_OnError;
			Client.OnJoinedChannel += Client_OnJoinedChannel;
#if DEBUG
			Client.OnLog += Client_OnLog;
#endif
			Client.OnMessageReceived += Client_OnMessageReceived;

			//Client.OnNewSubscriber += Client_OnNewSub;
			//Client.OnReSubscriber += Client_OnResub;
			//Client.OnGiftedSubscription += Client_OnGiftSub;

			if (!Client.Connect())
			{
				Debug.Error("Could not connect to TwitchAPI");
				BitManager.State = State.PreStart;
				return;
			}
			else
			{
				Debug.LogInfo("Twitch Client Connected! Waiting for validation and TwitchAPI setup...");
				BitManager.State = State.WaitValidating;

				Client.JoinChannel(Client.TwitchUsername);
			}

			Config.Save(BitManager);
		}

		// https://github.com/FixItFreb/BeepoBits/blob/main/Modules/Core/Twitch/TwitchService_OAuth.cs
		// https://github.com/ExpiredPopsicle/KiriTwitchGD4/blob/master/TwitchService.gd
		private void OAuthServerStart()
		{
			BitManager.State = State.OAuth;

			// Kill any existing websocket server
			if (OAuthTCPServer != null)
			{
				OAuthTCPServer.Stop();
				OAuthTCPServer = null;
			}

			// Fire up a new server
			OAuthTCPServer = new TcpServer();
			OAuthTCPServer.Listen(TWITCH_REDIRECT_PORT, "127.0.0.1");

			OAuthBuffer = string.Empty;

			// Check client ID to make sure we aren't about to do something we'll regret
			byte[] asciiTwitchID = CLIENT_ID.ToAsciiBuffer();
			foreach (byte k in asciiTwitchID)
			{
				// Make sure we're only using alphanumeric values
				if ((k >= 65 && k <= 90) || (k >= 97 && k <= 122) || (k >= 48 && k <= 57))
				{
				}
				else
				{
					throw new ApplicationException("Tried to connect with invalid Twitch Client ID");
				}
			}

			// Notes on scopes used in this URL:
			// channel:read:redemptions - Needed for point redeems.
			// chat:read                - Needed for reading chat (and raids?).
			// bits:read                - Needed for reacting to bit donations.

			string[] scopeArray = new string[]
			{
				"channel:read:redemptions",
				"channel:manage:redemptions",
				"chat:read",
				"bits:read",
				"channel:read:subscriptions",
				"channel:read:hype_train",
				"moderator:read:followers",
				"user:read:chat",
			};

			string scopeStr = String.Join(" ", scopeArray);
			scopeStr = scopeStr.URIEncode();

			string twitchRedirectURL = $"http://{TWITCH_REDIRECT_ADDRESS}:{TWITCH_REDIRECT_PORT}";

			string oAuthURL = string.Format(
				"https://id.twitch.tv/oauth2/authorize?response_type=token&client_id={0}&redirect_uri={1}&scope={2}",
			CLIENT_ID, twitchRedirectURL, scopeStr);

			Debug.LogInfo("OAuthUrl: " + oAuthURL);
			OS.ShellOpen(oAuthURL);
		}

		public void OAuthServerUpdate()
		{
			if (OAuthTCPServer != null && OAuthTCPServer.IsConnectionAvailable())
			{
				StreamPeerTcp = OAuthTCPServer.TakeConnection();
			}

			if (StreamPeerTcp != null)
			{
				while (StreamPeerTcp.GetAvailableBytes() > 0)
				{
					string incomingByte = StreamPeerTcp.GetUtf8String(1);
					if (incomingByte != "\r")
					{
						OAuthBuffer += incomingByte;
					}
				}
			}

#if DEBUG
			//GD.Print("Server received message");
			//GD.Print(OAuthBuffer);
#endif

			if (OAuthBuffer.EndsWith("\n\n"))
			{
				// For each line...
				while (OAuthBuffer.Contains("\n"))
				{
					// Take the line and pop it out of the buffer.
					string getLine = OAuthBuffer.Split("\n", true)[0];
					OAuthBuffer = OAuthBuffer.Substring(getLine.Length + 1);

					// All we care about here is the GET line
					if (getLine.StartsWith("GET "))
					{
						int tokenStart = getLine.Find("access_token=");
						Debug.LogDebug("(PRINTING)" + getLine);
						if (tokenStart == -1)
						{
							Debug.LogErr("Couldn't find access_token in uri");
						}
						else
						{
							int endOfToken = getLine.Find("&", tokenStart);
							if (endOfToken == -1)
							{
								endOfToken = getLine.Length;
							}
							int start = tokenStart + "access_token=".Length;
							int end = endOfToken - start;
							string token = getLine.Substring(start, end);
							Debug.LogInfo("OAuth token found: " + token);

							BitManager.User.OAuth = token;

							ConnectAndInitClient(BitManager.User);

							string htmlResponse = @"<html><head></head><body>You may now close this window.</body></html>";
							OAuthSendPageData(StreamPeerTcp, htmlResponse);
							StreamPeerTcp.DisconnectFromHost();
							StreamPeerTcp = null;
							OAuthServerStop();
						}
					}
				}
			}
		}

		private void OAuthServerStop()
		{
			if (OAuthTCPServer != null)
			{
				OAuthTCPServer.Stop();
			}
		}

		public bool ValidateThanFetchOrConnect(User user)
		{
			if (string.IsNullOrEmpty(user.Username))
			{
				Debug.LogErr("Username is null");
				return false;
			}

			if (string.IsNullOrEmpty(user.OAuth))
			{
				Debug.LogErr("No OAuth token found.");
				FetchNewOAuth();
				return false;
			}

			// Client may not be initialized here
			if (Client != null && Client.IsConnected)
			{
				Client.Disconnect();
			}

			BitManager.User = user;

			const string URL = "https://id.twitch.tv/oauth2/validate";
			string[] headers = new string[1];
			headers[0] = "Authorization: OAuth " + BitManager.User.OAuth;

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += Client_OnRequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers);
			if (err != Error.Ok)
			{
				GD.PushError(err);
				return false;
			}
			return true;
		}

		private void OAuthSendPageData(StreamPeer peer, string data)
		{
			string httpResponse = string.Join(
				"\r\n",
				"HTTP/1.1 200 OK",
				"Content-Type: text/html; charset=utf-8",
				"Content-Length: " + (Int64)data.Length,
				"Connection: close",
				"Cache-Control: max-age=0",
				"",
				""
			);

			string fullResponse = httpResponse + data + "\n\n\n\n\n";
			byte[] responseAscii = fullResponse.ToAsciiBuffer();
			peer.PutData(responseAscii);
		}

		private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
		{
			Debug.LogInfo($"Joined channel {e.Channel}");
		}

		private void Client_OnNewSub(object sender, OnNewSubscriberArgs e)
		{
			throw new NotImplementedException();
		}

		private void Client_OnGiftSub(object sender, OnGiftedSubscriptionArgs e)
		{
			throw new NotImplementedException();
		}

		private void Client_OnResub(object sender, OnReSubscriberArgs e)
		{
			throw new NotImplementedException();
		}

		private void FetchNewOAuth()
		{
			Debug.LogInfo("Fetching new OAuth...");
			OAuthServerStart();
		}

		private void Client_OnConnected(object sender, OnConnectedArgs e)
		{
			Debug.LogInfo("Connected, {0}", e.BotUsername);
		}

		private void Client_OnDisconnected(object sender, OnDisconnectedEventArgs e)
		{
			Debug.LogInfo("Disconnected");
		}

		private void Client_OnConnectionError(object sender, OnConnectionErrorArgs e)
		{
			Debug.LogErr($"Connection Error: {0}", e.Error.Message);
		}

		private void Client_OnError(object sender, OnErrorEventArgs e)
		{
			Debug.LogErr($"TwitchLib Error: {0}", e.Exception.Message);
		}

		private void Client_OnLog(object sender, OnLogArgs e)
		{
			Debug.LogInfo(e.Data);
		}

		private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
		{
			Debug.LogInfo("Test");
			if (e.ChatMessage.Bits > 0)
			{
				Debug.LogInfo($"(BITS) Order Received. Total: {e.ChatMessage.Bits}");
				Debug.LogInfo($"(BITS) Individual Cheers: {BitManager.Settings.ExperimentalBitParsing}");
				Debug.LogInfo($"(BITS) CombineBits. Total: {BitManager.Settings.CombineBits}");

				if (BitManager.Settings.ExperimentalBitParsing)
				{
					foreach (var emote in e.ChatMessage.EmoteSet.Emotes)
					{
						if (emote.Name.StartsWith("cheer"))
						{
							int amount = int.Parse(emote.Name.Substring(5));

							Debug.LogInfo($"(BITS) EmoteName: {emote.Name}");

							BitManager.CreateOrderWithChecks(amount);
						}
					}
				}
				else
				{
					BitManager.CreateOrderWithChecks(e.ChatMessage.Bits);
				}
			}
			else if ((e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Moderator
				|| e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Broadcaster)
				&& e.ChatMessage.Message.StartsWith("!bb"))
			{
				Debug.LogInfo("Command Recieved: " + e.ChatMessage.Message);

				string[] split = e.ChatMessage.Message.Split(" ");
				if (split[1] == "test" && split.Length >= 3)
				{
					if (int.TryParse(split[2], out int amount))
					{
						BitManager.CreateOrderWithChecks(amount);
					}
					else
					{
						GD.PrintErr("Couldnt parse amount string");
					}
				}
				else
				{
					GD.PrintErr("Command invalid, " + e.ChatMessage.Message);
				}
			}
#if DEBUG
			Debug.LogInfo(e.ChatMessage.RawIrcMessage);
			Debug.LogInfo(" ");
			foreach (var emote in e.ChatMessage.EmoteSet.Emotes)
			{
				Debug.LogInfo(emote.Name);
			}
#endif
		}

		private void Client_OnRequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				Debug.Assert(!string.IsNullOrEmpty(BitManager.User.Username));
				Debug.Assert(!string.IsNullOrEmpty(BitManager.User.OAuth));

				var json = new Json();
				json.Parse(body.GetStringFromUtf8());

				var response = json.Data.AsGodotDictionary();

				if (!response.TryGetValue("client_id", out Variant clientId))
				{
					GD.PushError("client_id not found");
					FetchNewOAuth();
					return;
				}

				if (!response.TryGetValue("login", out Variant login))
				{
					GD.PushError("login not found");
					FetchNewOAuth();
					return;
				}

				if (!response.TryGetValue("user_id", out Variant userId))
				{
					GD.PushError("user_id not found");
					FetchNewOAuth();
					return;
				}

				if (BitManager.User.Username != login.AsString())
				{
					GD.PushError("Current Username does not equal found login");
					FetchNewOAuth();
					return;
				}

				Debug.LogInfo($"Login and OAuth good. UserId: {userId}, Connecting...");
				BitManager.User.BroadcasterId = userId.AsString();
				ConnectAndInitClient(BitManager.User);
			}
			else if (responseCode == 401)
			{
				Debug.LogErr($"401 Unauthorized. OAuth probably invalid.");
				FetchNewOAuth();
			}
			else
			{
				Debug.Error("Unknown response code");
				BitManager.State = State.PreStart;
			}
		}
	}
}
