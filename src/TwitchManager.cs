using System;
using System.Linq;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Client;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Clients;
using Godot;
using TwitchLib.Communication.Events;
using TwitchLib.Client.Enums;

namespace BitCup
{

	// TwitchLib and TwitchClient
	public class TwitchManager
	{
		public const string CLIENT_ID = "gsqo3a59xrqob257ku9ou70u9roqzy";
		
		const string TWITCH_REDIRECT_ADDRESS = "localhost";
		const ushort TWITCH_REDIRECT_PORT = 8080;

		public TwitchClient Client;

		BitManager BitManager;

		// Server for requesting OAuth tokens
		TcpServer OAuthTCPServer;
		StreamPeerTcp StreamPeerTcp;
		string OAuthBuffer;

		public TwitchManager(BitManager bitManager)
		{
			Debug.Assert(bitManager != null);
			BitManager = bitManager;
		}

		public void FetchNewOAuth()
		{
			Debug.LogInfo("Fetching new OAuth...");
			OAuthServerStart();
		}

		public void ConnectAndInitClient()
		{
			if (string.IsNullOrEmpty(BitManager.User.Username))
			{
				Debug.Error("user.Username is null or empty");
				BitManager.State = State.PreStart;
				return;
			}

			if (string.IsNullOrEmpty(BitManager.User.OAuth))
			{
				Debug.LogErr("user.OAuth is null or empty");
				BitManager.State = State.PreStart;
				FetchNewOAuth();
				return;
			}

			BitManager.State = State.WaitValidating;
			BitManager.TwitchAPI.OAuthState = TwitchOAuthState.Valid;

			Config.Save(BitManager);

			if (string.IsNullOrEmpty(BitManager.User.BroadcasterId))
			{
				BitManager.TwitchAPI.RequestUser();
			}

			ConnectionCredentials credentials = new ConnectionCredentials(
				BitManager.User.Username,
				BitManager.User.OAuth);

			if (Client != null)
			{
				Client.AutoReListenOnException = false;
				Client.DisableAutoPong = false;
				Client.OnConnected -= Client_OnConnected;
				Client.OnDisconnected -= Client_OnDisconnected;
				Client.OnConnectionError -= Client_OnConnectionError;
				Client.OnError -= Client_OnError;
				Client.OnJoinedChannel -= Client_OnJoinedChannel;
				Client.OnMessageReceived -= Client_OnMessageReceived;
				Client.OnNewSubscriber -= Client_OnNewSub;
				Client.OnReSubscriber -= Client_OnResub;
				Client.OnGiftedSubscription -= Client_OnGiftSub;
				#if DEBUG
								Client.OnLog -= Client_OnLog;
				#endif
				Client.Disconnect();
				Client = null;
			}

			ClientOptions clientOptions = new ClientOptions();
			clientOptions.MessagesAllowedInPeriod = 750;
			clientOptions.ThrottlingPeriod = TimeSpan.FromSeconds(30);
			clientOptions.ReconnectionPolicy = new ReconnectionPolicy(6_000, maxAttempts: 10);
			WebSocketClient customClient = new WebSocketClient(clientOptions);
			Client = new TwitchClient(customClient);

			Client.OnConnected += Client_OnConnected;
			Client.OnDisconnected += Client_OnDisconnected;
			Client.OnConnectionError += Client_OnConnectionError;
			Client.OnError += Client_OnError;
			Client.OnJoinedChannel += Client_OnJoinedChannel;
			Client.OnMessageReceived += Client_OnMessageReceived;
			Client.OnNewSubscriber += Client_OnNewSub;
			Client.OnReSubscriber += Client_OnResub;
			Client.OnGiftedSubscription += Client_OnGiftSub;
#if DEBUG
			Client.OnLog += Client_OnLog;
#endif

			// Note: "channel" is wrong, but doesn't work otherwise
			Client.Initialize(credentials, "channel");

			if (Client.Connect())
			{
				// Note: State doesn't update here, update loop will
				// update state when we are both Connected and response,
				// from twitch user
				Debug.LogInfo("Twitch Client Connected! Waiting for validation and TwitchAPI setup...");
				Client.JoinChannel(BitManager.User.Username);
			}
			else
			{
				Debug.Error("Could not connect to TwitchAPI");
				BitManager.State = State.PreStart;
			}

		}

		// https://github.com/FixItFreb/BeepoBits/blob/main/Modules/Core/Twitch/TwitchService_OAuth.cs
		// https://github.com/ExpiredPopsicle/KiriTwitchGD4/blob/master/TwitchService.gd
		private void OAuthServerStart()
		{
			// Kill any existing websocket server
			if (OAuthTCPServer != null)
			{
				OAuthTCPServer.Stop();
				OAuthTCPServer = null;
			}

			BitManager.State = State.OAuth;

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
			GD.Print("Server received message");
			GD.Print(OAuthBuffer);
#endif

			if (OAuthBuffer.EndsWith("\n\n"))
			{
				// For each line...
				while (OAuthBuffer.Contains("\n"))
				{
					// Take the line and pop it out of the buffer.
					string getLine = OAuthBuffer.Split("\n", true)[0];
					OAuthBuffer = OAuthBuffer.Substring(getLine.Length + 1);

					if (getLine.StartsWith("GET "))
					{
						string[] getLineParts = getLine.Split(" ");
						string httpGetPath = getLineParts[1];
						if (httpGetPath == "/")
						{
							// Response page: Just a Javascript program to do a redirect
							// so we can get the access token into the a GET argument
							// instead of the fragment.
							string htmlResponse = @"
                            <html><head></head><body><script>
							  var url_parts = String(window.location).split(""#"");
							  if(url_parts.length > 1) {
								  var redirect_url = url_parts[0] + ""?"" + url_parts[1];
								  window.location = redirect_url;
							  }
						</script></body></html>
                        ";

							// Send webpage and disconnect.
							OAuthSendPageData(StreamPeerTcp, htmlResponse);
							StreamPeerTcp.DisconnectFromHost();
							StreamPeerTcp = null;
						}

						// If the path has a '?' in it at all, then it's probably our
						// redirected page
						else if (httpGetPath.Contains("?"))
						{
							string htmlResponse = @"<html><head></head><body>You may now close this window.</body></html>";

							// Attempt to extract the access token from the GET data.
							string[] pathParts = httpGetPath.Split("?");
							if (pathParts.Length > 1)
							{
								string parameters = pathParts[1];
								string[] argList = parameters.Split("&");
								foreach (string arg in argList)
								{
									string[] argParts = arg.Split("=");
									if (argParts.Length > 1)
									{
										if (argParts[0] == "access_token")
										{
											BitManager.User.OAuth = argParts[1];

											ConnectAndInitClient();
										}
									}
								}
							}

							// Send webpage and disconnect
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
			int months = 1;
			var plan = e.Subscriber.SubscriptionPlan;
			HandleSub(months, plan);
		}

		private void Client_OnGiftSub(object sender, OnGiftedSubscriptionArgs e)
		{
			int months = 1;
			var plan = e.GiftedSubscription.MsgParamSubPlan;
			HandleSub(months, plan);
		}

		private void Client_OnResub(object sender, OnReSubscriberArgs e)
		{
			int months = int.TryParse(e.ReSubscriber.MsgParamCumulativeMonths, out int value) ? value : 1;
			var plan = e.ReSubscriber.SubscriptionPlan;
			HandleSub(months, plan);
		}

		private void HandleSub(int months, SubscriptionPlan plan)
		{
			if (!BitManager.Settings.EnableSubBits)
				return;

			int tier = 1;
			if (plan == SubscriptionPlan.Tier2)
				tier = 2;
			else if (plan == SubscriptionPlan.Tier3)
				tier = 3;

			int convertedToBits = BitManager.Settings.SubBitsAmount * months * tier;
			if (BitManager.Settings.SubBitsAsCheer)
				BitManager.CreateOrderWithChecks(convertedToBits);
			else
			{
				convertedToBits = Mathf.Clamp(convertedToBits, 1, short.MaxValue);

				BitOrder bitOrder = new();
				bitOrder.BitAmounts[(int)BitTypes.Bit1] = (short)convertedToBits;
				BitManager.CheermotesManager.BitOrderDefaultTextures(bitOrder);
				BitManager.BitOrders.Add(bitOrder);
			}
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
			int bits = e.ChatMessage.Bits;
			if (bits > 0)
			{
				Debug.LogInfo($"(BITS) Order Received. Total: {e.ChatMessage.Bits}");
				Debug.LogInfo($"(BITS) Individual Cheers: {BitManager.Settings.ExperimentalBitParsing}");
				Debug.LogInfo($"(BITS) CombineBits. Total: {BitManager.Settings.CombineBits}");

				if (BitManager.Settings.ExperimentalBitParsing)
				{
					string[] split = e.ChatMessage.Message.Split(" ");

					int totalBitsFound = 0;

					foreach (string s in split)
					{
						if (string.IsNullOrWhiteSpace(s) || s.Length < 4)
							continue;

						if (totalBitsFound >= bits)
							return;

						string nameStr = new string(s.Where(c => char.IsLetter(c)).ToArray());
						string valueStr = new string(s.Where(c => char.IsDigit(c)).ToArray());
						if (!int.TryParse(valueStr, out int amount))
							continue;

						amount = Mathf.Min(amount, bits);

						Cheermote cheermote = new Cheermote();
						cheermote.Prefix = nameStr;
						cheermote.Id = CheermotesManager.CheermoteIdFromBits(amount);

						if (BitManager.CheermotesManager.Exists(cheermote))
						{
							Debug.LogDebug($"Cheermote found! {nameStr}, {amount}");

							totalBitsFound += amount;

							BitManager.CheermotesManager.ProcessOrderQueueForTexturesOrDefault(cheermote, amount);
						}
					}

					// A backup incase my parsing sucks
					if (totalBitsFound < bits)
					{
						int bitDifference = bits - totalBitsFound;
						BitManager.CreateOrderWithChecks(bitDifference);
					}
				}
				else
				{
					BitManager.CreateOrderWithChecks(e.ChatMessage.Bits);
				}
			}

#if DEBUG
			if ((e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Moderator
				|| e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Broadcaster)
				&& e.ChatMessage.Message.StartsWith("!bb "))
			{
				Debug.LogInfo("Command Recieved: " + e.ChatMessage.Message);

				string[] split = e.ChatMessage.Message.Split(" ");
				if (split[1] == "test" && split.Length >= 3)
				{
					if (int.TryParse(split[2], out int amount))
					{
						BitManager.CreateOrderWithChecks(amount);
					}
				}
				else if (split[1] == "rain" && split.Length >= 3)
				{
					if (int.TryParse(split[2], out int amount))
					{
						BitManager.CreateRainOrder(amount);
					}
				}
			}

			Debug.LogInfo(e.ChatMessage.RawIrcMessage);
			Debug.LogInfo(" ");
			foreach (var emote in e.ChatMessage.EmoteSet.Emotes)
			{
				Debug.LogInfo(emote.Name);
			}
#endif
		}		
	}
}
