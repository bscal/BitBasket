using System;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Client;
using TwitchLib.Communication.Models;
using TwitchLib.Communication.Clients;
using System.IO;
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
		public void OAuthServerStart()
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

			string oAuthURL = string.Format(
				"https://id.twitch.tv/oauth2/authorize?response_type=token&client_id={0}&redirect_uri=http://localhost:8080&scope={1}",
			CLIENT_ID, scopeStr);

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

											ConnectionCredentials credentials = new ConnectionCredentials("bscal", accessToken);
											InitTwitchClient(credentials);
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

		public void OAuthServerStop()
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

		private void InitTwitchClient(ConnectionCredentials credentials)
		{
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

			BitManager.State = State.Running;
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
				GD.Print("BitOrder receieved!");

				int bits = e.ChatMessage.Bits;

				BitManager.CreateOrderWithChecks(bits);
			}
			else if (e.ChatMessage.Message.StartsWith("!bb"))
			{
				GD.Print("BitOrder test!");

				string[] split = e.ChatMessage.Message.Split(" ");
				if (split[1] == "test" && split.Length == 3)
				{
					if (int.TryParse(split[2], out int amount))
					{
						BitManager.CreateOrderWithChecks(amount);
					}
				}
			}
		}
	}
}
