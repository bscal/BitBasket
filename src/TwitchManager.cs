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
	public class TwitchManager
	{
		const string CLIENT_ID = "gsqo3a59xrqob257ku9ou70u9roqzy";
		const string TWITCH_REDIRECT_ADDRESS = "localhost";
		const ushort TWITCH_REDIRECT_PORT = 8080;

		public TwitchClient Client;

		BitManager BitManager;
		
		TcpServer OAuthTCPServer;

		StreamPeerTcp StreamPeerTcp;

		string OAuthBuffer;

		public TwitchManager(BitManager bitManager)
		{
			Debug.Assert(bitManager != null);
			BitManager = bitManager;
		}

		// https://github.com/FixItFreb/BeepoBits/blob/main/Modules/Core/Twitch/TwitchService_OAuth.cs
		// https://github.com/ExpiredPopsicle/KiriTwitchGD4/blob/master/TwitchService.gd
		private void OAuthServerStart(string username)
		{
			Debug.Assert(!string.IsNullOrEmpty(username));

			BitManager.State = State.OAuth;

			BitManager.User.Username = username;

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

			string[] scopeArray = new string[]{
			"channel:read:redemptions",
			"chat:read",
			"bits:read",
			"channel:read:subscriptions",
			"moderator:read:followers",
			"user:read:chat",
		};

			string scopeStr = String.Join(" ", scopeArray);
			scopeStr = scopeStr.URIEncode();

			string twitchRedirectURL = $"http://{TWITCH_REDIRECT_ADDRESS}:{TWITCH_REDIRECT_PORT}";

			string oAuthURL = string.Format(
				"https://id.twitch.tv/oauth2/authorize?response_type=token&client_id={0}&redirect_uri={1}&scope={2}",
			CLIENT_ID, twitchRedirectURL, scopeStr);

			GD.Print("OAuthUrl: " + oAuthURL);
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

					// All we care about here is the GET line
					if (getLine.StartsWith("GET "))
					{
						// Split "GET <path> HTTP/1.1" into "GET", <path>, and
						// "HTTP/1.1".
						string[] getLineParts = getLine.Split(" ");
						string httpGetPath = getLineParts[1];

						// If we get the root path without the arguments, then it means
						// that Twitch has stuffed the access token into the fragment
						// (after the #). Send a redirect page to read that and give it
						// to us in a GET request.
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
											string accessToken = argParts[1];
											Debug.Assert(!string.IsNullOrEmpty(accessToken));

											BitManager.User.OAuth = accessToken;
											ConnectAndInitClient(BitManager.User);
										}
									}
								}
								OAuthSendPageData(StreamPeerTcp, htmlResponse);
								StreamPeerTcp.DisconnectFromHost();
								StreamPeerTcp = null;
								OAuthServerStop();
							}
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
				GD.PrintErr("No username");
				return false;
			}

			if (string.IsNullOrEmpty(user.OAuth))
			{
				GD.Print("NO oAuth");
				FetchNewOAuth();
				return false;
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

		private void ConnectAndInitClient(User user)
		{
			if (string.IsNullOrEmpty(user.Username))
			{
				GD.PushError("user.Username is null or empty");
				return;
			}

			if (string.IsNullOrEmpty(user.OAuth))
			{
				GD.PushError("user.OAuth is null or empty");
				return;
			}

			ConnectionCredentials credentials = new ConnectionCredentials(user.Username, user.OAuth);

			var clientOptions = new ClientOptions
			{
				MessagesAllowedInPeriod = 750,
				ThrottlingPeriod = TimeSpan.FromSeconds(30)
			};

			WebSocketClient customClient = new WebSocketClient(clientOptions);
			Client = new TwitchClient(customClient);
			Client.Initialize(credentials, "channel");

			Client.OnConnected += Client_OnConnected;
			Client.OnMessageReceived += Client_OnMessageReceived;
			Client.OnError += Client_OnError;

			Client.OnJoinedChannel += Client_OnJoinedChannel;

			if (!Client.Connect())
			{
				GD.PrintErr("Could not connect to TwitchAPI");
			}
			else
			{
				GD.Print("Connected to TwitchAPI");
				Client.JoinChannel(Client.TwitchUsername);
			}

			Config.Save(BitManager);

			BitManager.State = State.Running;
		}

		private void FetchNewOAuth()
		{
			GD.Print("Fetching oAuth...");
			string username = BitManager.User.Username;
			OAuthServerStart(username);
		}

		// Not used, would be to get user_id
		private bool GetUser()
		{
			Debug.Assert(!string.IsNullOrEmpty(BitManager.User.OAuth));

			if (string.IsNullOrEmpty(BitManager.User.OAuth))
			{
				return false;
			}

			const string URL = "https://api.twitch.tv/helix/users";
			string[] headers = new string[1];
			headers[0] = "Authorization: Bearer " + BitManager.User.OAuth;

			return true;
		}


		private void Client_OnJoinedChannel(object sender, OnJoinedChannelArgs e)
		{
			GD.Print("Joined Channel: " +  e.Channel);
		}

		private void Client_OnConnected(object sender, OnConnectedArgs e)
		{
			GD.Print(e.BotUsername + " connected!");
			GD.Print(e.AutoJoinChannel);
		}

		private void Client_OnError(object sender, OnErrorEventArgs e)
		{
			GD.PrintErr(e.Exception.Message);
		}

		private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
		{
			if (e.ChatMessage.Bits > 0)
			{
				int bitsInMessage = e.ChatMessage.Bits;
				int countedTotalBits = 0;
				int orderIndex = 0;
				int[] orders = new int[BitManager.MAX_ORDERS];
				string[] bitMsges = e.ChatMessage.Message.Split(' ');
				foreach (var msg in bitMsges)
				{
					if (orderIndex == orders.Length - 1)
					{
						int bits = bitsInMessage - countedTotalBits;
						countedTotalBits += bits;
						orders[orders.Length] = bits;
						break;
					}

					if (msg.Length > 5 && msg.StartsWith("cheer"))
					{
						int parse = int.Parse(msg.Substring(5));
						countedTotalBits += parse;
						orders[orderIndex] = parse;
						++orderIndex;
					}
				}

				if (countedTotalBits == bitsInMessage)
				{
					foreach (var order in orders)
					{
						BitManager.CreateOrderWithChecks(order);
					}
				}
			}
			else if ((e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Moderator
				|| e.ChatMessage.UserType == TwitchLib.Client.Enums.UserType.Broadcaster)
				&& e.ChatMessage.Message.StartsWith("!bb"))
			{
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

				GD.Print("login and oAuth good, connecting...");
				BitManager.User.UserId = userId.AsString();
				ConnectAndInitClient(BitManager.User);
			}
			else if (responseCode == 401)
			{
				GD.PushError("Token is invalid");
				FetchNewOAuth();
			}
			else
			{
				GD.PushError("Unknown response code");
				FetchNewOAuth();
			}
		}
	}
}
