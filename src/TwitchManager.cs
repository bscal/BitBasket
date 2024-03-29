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
using System.Collections.Generic;
using System.IO;

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

		struct BitOrderQueueData
		{
			public string EmoteId;
			public int BitAmount;
			public float Lifetime;
			public bool HasSendHTTPRequest;
		}

		List<BitOrderQueueData> EmoteRequestQueue;
		public Dictionary<string, ImageTexture> TextureCache;

		public const string DEFAULT_1 = "default1";
		public const string DEFAULT_100 = "default100";
		public const string DEFAULT_1000 = "default1000";
		public const string DEFAULT_5000 = "default5000";
		public const string DEFAULT_10000 = "default10000";

		public TwitchManager(BitManager bitManager)
		{
			Debug.Assert(bitManager != null);
			BitManager = bitManager;

			EmoteRequestQueue = new(64);
			TextureCache = new(64);

			ImageTexture CompressedToImageTexture(CompressedTexture2D t)
			{
				Image i = t.GetImage();
				ImageTexture res = new();
				res.SetImage(i);
				return res;
			}

			TextureCache.Add(DEFAULT_10000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit10000Texture));
			TextureCache.Add(DEFAULT_5000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit5000Texture));
			TextureCache.Add(DEFAULT_1000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit1000Texture));
			TextureCache.Add(DEFAULT_100, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit100Texture));
			TextureCache.Add(DEFAULT_1, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit1Texture));
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
			int months = e.ReSubscriber.Months;
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

			int convertedToBits = 100 * months * tier;
			BitManager.CreateOrderWithChecks(convertedToBits);
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

		public void ProcessQueues(float dt)
		{
			// Will be empty the majority of the time, and low chance
			// of handling several requests at a time.
			for (int i = EmoteRequestQueue.Count - 1; i >= 0; --i)
			{
				BitOrderQueueData newData = EmoteRequestQueue[i];

				if (!newData.HasSendHTTPRequest)
				{
					// TwitchLib chatmessage event is on another thread
					// but never tells you? lol
					newData.HasSendHTTPRequest = true;
					RequestImage(newData.EmoteId);
				}
				else
				{
					newData.Lifetime += dt;
					if (newData.Lifetime > 5)
					{
						// Hard coded timeout, uses default bits
						BitManager.CreateOrderWithChecks(newData.BitAmount);
						EmoteRequestQueue.RemoveAt(i);
						return;
					}

					if (TextureCache.TryGetValue(newData.EmoteId, out var bitTexture))
					{
						BitManager.CreateOrderWithCustomTexture(newData.BitAmount, newData.EmoteId, bitTexture);
						EmoteRequestQueue.RemoveAt(i);
						return;
					}
				}

				EmoteRequestQueue[i] = newData;
			}
		}

		public void SaveImages()
		{
			string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "cache");

			Directory.CreateDirectory(dirPath);

			foreach (var pair in TextureCache)
			{
				if (string.IsNullOrWhiteSpace(pair.Key))
					continue;

				if (pair.Key.StartsWith("default"))
					continue;

				string filePath = Path.Combine(dirPath, pair.Key + ".png");

				Error err = pair.Value.GetImage().SavePng(filePath);
				if (err != Error.Ok)
				{
					Debug.Error(err.ToString());
				}
			}
		}

		public void LoadImages()
		{
			string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "cache");
			if (!Directory.Exists(dirPath))
				return;
			
			List<string> filesToRemove = new(16);

			string[] files = Directory.GetFiles(dirPath);
			foreach (string filePath in files)
			{	
				try
				{
					DateTime creationTime = File.GetCreationTime(filePath);
					if (creationTime.Subtract(DateTime.Now) > TimeSpan.FromDays(15.0))
					{
						filesToRemove.Add(filePath);
						continue;
					}

					if (filePath.EndsWith(".png"))
					{
						Image image = Image.LoadFromFile(filePath);

						string fileBaseName = filePath.GetFile().GetBaseName();

						ImageTexture imgTexture = new ImageTexture();
						imgTexture.SetImage(image);

						Debug.LogInfo($"Loaded {fileBaseName} emote from file cache!");

						TextureCache.TryAdd(fileBaseName, imgTexture);
					}
				}
				catch
				{
					continue;
				}
			}

			foreach(string fileName in filesToRemove)
			{
				File.Delete(fileName);
			}
		}

		/// <summary>
		/// Get a texture or returns default texture for type. Will only check is
		/// Texture Cache contains it and not fetch.
		/// </summary>
		/// <param name="textureId"></param>
		/// <param name="type"></param>
		/// <returns></returns>
		public ImageTexture GetTextureOrDefault(string textureId, BitTypes type)
		{
			if (!string.IsNullOrWhiteSpace(textureId) 
				&& TextureCache.TryGetValue(textureId, out ImageTexture texture))
			{
				return texture;
			}
			else
			{
				switch (type)
				{
					case BitTypes.Bit1:		return TextureCache[DEFAULT_1];
					case BitTypes.Bit100:	return TextureCache[DEFAULT_100];
					case BitTypes.Bit1000:	return TextureCache[DEFAULT_1000];
					case BitTypes.Bit5000:	return TextureCache[DEFAULT_5000];
					case BitTypes.Bit10000: return TextureCache[DEFAULT_10000];
					default: Debug.Error("Invalid Type"); return TextureCache[DEFAULT_1];
				}
			}
		}

		private ImageTexture GetTextureOrQueueRequest(string textureId, int bitAmount)
		{
			if (string.IsNullOrWhiteSpace(textureId))
				return null;

			if (TextureCache.TryGetValue(textureId, out ImageTexture texture))
			{
				return texture;
			}
			else
			{
				BitOrderQueueData order = new();
				order.EmoteId = textureId;
				order.BitAmount = bitAmount;
				EmoteRequestQueue.Add(order);

				return null;
			}
		}

		private void RequestImage(string id)
		{
			string url = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/light/2.0";

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.Timeout = 5;

			request.RequestCompleted += (long result, long responseCode, string[] headers, byte[] body) =>
			{
				if (responseCode == 200)
				{
					Image image = new Image();
					var error = image.LoadPngFromBuffer(body);
					if (error != Error.Ok)
					{
						Debug.LogErr("( EmoteRequest ) Error " + error.ToString());
						return;
					}
					ImageTexture imgTexture = new ImageTexture();
					imgTexture.SetImage(image);

					Debug.LogDebug("Received emote png data! Caching");

					TextureCache.TryAdd(id, imgTexture);
				}
				else
				{
					Debug.LogErr("( EmoteRequest ) Unknown response " + responseCode);
				}
			};

			Error err = request.Request(url);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
		{
			if (e.ChatMessage.Bits > 0)
			{
				Debug.LogInfo($"(BITS) Order Received. Total: {e.ChatMessage.Bits}");
				Debug.LogInfo($"(BITS) Individual Cheers: {BitManager.Settings.ExperimentalBitParsing}");
				Debug.LogInfo($"(BITS) CombineBits. Total: {BitManager.Settings.CombineBits}");

				if (BitManager.Settings.ExperimentalBitParsing)
				{
					foreach (var emote in e.ChatMessage.EmoteSet.Emotes)
					{
						// Note: Have no idea what the case of cheer will be.
						// This may need to be changed? If a custom cheer does use
						// cheer in the mote then what? Probably don't have to worry
						// about a regular emote matching a cheer though? Could check if
						// a digit is at the end or substring after "cheer"?
						if (emote.Name.ToLower().Contains("cheer"))
						{

							// There is no static position in string to split.
							string valueStr = new string(emote.Name.Where(c => char.IsDigit(c)).ToArray());
							if (!int.TryParse(valueStr, out int amount))
								continue;

							Debug.LogInfo($"(BITS) Name: {emote.Name}, CheerAmount: {valueStr}, {amount}");

							// If texture is cached just create order,
							// otherwise we queue up the order and wait
							// for request to finish or timeout.
							ImageTexture texture = GetTextureOrQueueRequest(emote.Id, amount);
							if (texture != null)
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
#if DEBUG
			else if (e.ChatMessage.Message.StartsWith("!bbd"))
			{
				string[] split = e.ChatMessage.Message.Split(" ");

				string lowerName = split[1].ToLower();
				if (lowerName.Contains("cheer"))
				{
					string valueStr = new string(lowerName.Where(c => char.IsDigit(c)).ToArray());
					int amount = int.Parse(valueStr);

					Debug.LogInfo($"(BITS) Name: {split[1]}, CheerAmount: {valueStr}, {amount}");
				}

				foreach (var emote in e.ChatMessage.EmoteSet.Emotes)
				{
					ImageTexture texture = GetTextureOrQueueRequest(emote.Id, 1215);
					if (texture != null)
					{
						BitManager.CreateOrderWithCustomTexture(1215, emote.Id, texture);
						Debug.LogInfo("Found texture!");
					}
					else
					{
						Debug.LogInfo("Didnt find texture!");
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
