using Godot.Collections;
using Godot;
using System;
using System.Xml;

namespace BitCup
{
	public class EventSub
	{
		public bool WereEventsCreated;

		BitManager BitManager;

		WebSocketPeer Peer;

		float ReconnectTimer;
		string SessionId;

		const float RECONNECT_TIMER_START = 10.0f;
		const string EVENTSUB_WEBSOCKET_URL = "wss://eventsub.wss.twitch.tv/ws";

		public EventSub(BitManager bitManager)
		{
			BitManager = bitManager;
			Peer = new WebSocketPeer();
			SessionId = string.Empty;
		}

		public void UpdateEvents(float delta)
		{
			Peer.Poll();

			Error err = Peer.GetPacketError();
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}

			while (Peer.GetAvailablePacketCount() > 0)
			{
				ReceivePacket();
				Peer.Poll();
			}

			// See if we need to reconnect.
			if (Peer.GetReadyState() == WebSocketPeer.State.Closed)
			{
				ReconnectTimer -= delta;
				if (ReconnectTimer < 0.0)
				{
					ReconnectTimer = RECONNECT_TIMER_START;

					// Reconnect
					err = Peer.ConnectToUrl(EVENTSUB_WEBSOCKET_URL);
					if (err != Error.Ok)
					{
						ReconnectTimer = RECONNECT_TIMER_START;
						Debug.LogErr("(EVENT_SUB) Couldn't connect, reconnecting. Error: " + err.ToString());
						return;
					}

					// Wait for the connection to be fully established.
					Peer.Poll();
					while (Peer.GetReadyState() == WebSocketPeer.State.Connecting)
					{
						Peer.Poll();
					}

					if (Peer.GetReadyState() == WebSocketPeer.State.Closing)
					{
						Debug.LogInfo("(EVENT_SUB) Closing");
						return;
					}

					if (Peer.GetReadyState() == WebSocketPeer.State.Closed)
					{
						Debug.LogInfo("(EVENT_SUB) Closed");
						return;
					}
				}
			}
		}

		private void CreateEvents()
		{
			{ // channel.hype_train.progress
				Dictionary body = new Dictionary();
				body.Add("type", "channel.hype_train.progress");
				body.Add("version", "1");

				Dictionary condition = new Dictionary();
				condition.Add("broadcaster_user_id", BitManager.User.BroadcasterId);
				body.Add("condition", condition);

				Dictionary transport = new Dictionary();
				transport.Add("method", "websocket");
				transport.Add("session_id", SessionId);
				body.Add("transport", transport);

				RequestEventSub(body);
			}

			{ // channel.hype_train.end
				Dictionary body = new Dictionary();
				body.Add("type", "channel.hype_train.end");
				body.Add("version", "1");

				Dictionary condition = new Dictionary();
				condition.Add("broadcaster_user_id", BitManager.User.BroadcasterId);
				body.Add("condition", condition);

				Dictionary transport = new Dictionary();
				transport.Add("method", "websocket");
				transport.Add("session_id", SessionId);
				body.Add("transport", transport);

				RequestEventSub(body);
			}

			Debug.LogInfo("(EVENT_SUB) Created Events");
		}

		private void RequestEventSub(Dictionary data)
		{
			HttpRequest req = new HttpRequest();
			BitManager.AddChild(req);
			req.Timeout = 30;
			req.RequestCompleted += (result, responseCode, headers, body) =>
			{
				Debug.LogInfo($"(EVENT_SUB) {data["type"].AsString()} Event registered with code {responseCode}. {result}");

				switch (responseCode)
				{
					case 202: WereEventsCreated = true; break;
					case 400: Debug.LogErr("(EVENT_SUB) 400 Bad Request"); break;
					case 401:
						{
							Debug.LogErr("(EVENT_SUB) 401 Unathuorized");
							BitManager.InvalidateTwitchState();
						}
						break;
					case 403: Debug.LogErr("(EVENT_SUB) 403 Forbidden"); break;
					case 409: Debug.LogErr("(EVENT_SUB) 409 Conflict"); break;
					case 429: Debug.LogErr("(EVENT_SUB) 429 Too Many Requests"); break;
				}
			};

			const string URL = "https://api.twitch.tv/helix/eventsub/subscriptions";
			string[] headers = new string[]
			{
				"Authorization: Bearer " + BitManager.User.OAuth,
				"Client-Id: " + TwitchManager.CLIENT_ID,
				"Content-Type: application/json"
			};

			req.Request(URL, headers, HttpClient.Method.Post, Json.Stringify(data));
		}

		private bool ValidateTime(string timestamp, int minutesAgo)
		{
			DateTime time = FromRFC3339Time(timestamp);
			if (time.Ticks != 0)
			{
				if (DateTime.UtcNow.Subtract(time) < TimeSpan.FromMinutes(minutesAgo))
				{
					return true;
				}
				return false;
			}
			return false;
		}

		private void HandleEvent(Dictionary payload)
		{
			Dictionary subscription = payload["subscription"].AsGodotDictionary();
			Dictionary e = payload["event"].AsGodotDictionary();

			switch (subscription["type"].AsString())
			{
				case "channel.hype_train.progress":
					{
						Debug.LogDebug("(EVENT) channel.hype_train.progress");
						if (BitManager.Settings.EnableHypeTrainRain)
						{
							//int level = e["level"].AsInt32();
							BitManager.CreateRainOrderProgress(50);
						}
					} break;
				case "channel.hype_train.end":
					{
						Debug.LogDebug("(EVENT) channel.hype_train.end");
						if (BitManager.Settings.EnableHypeTrainRain)
						{
							int level = e["level"].AsInt32();
							BitManager.CreateRainOrder(level);
						}
					} break;
			}
		}

		private void ReceivePacket()
		{
			var json = new Json();
			json.Parse(Peer.GetPacket().GetStringFromUtf8());
			Dictionary data = json.Data.AsGodotDictionary();
			ParsePacket(data);
		}

		private void ParsePacket(Dictionary data)
		{
			if (data.TryGetValue("metadata", out Variant metadata)
				&& data.TryGetValue("payload", out Variant payload))
			{
				/* Not sure if we actually need this security
				string message_id = metadata.AsGodotDictionary()["message_id"].AsString();
				if (string.IsNullOrWhiteSpace(message_id))
					return;

				if (RecentId.Contains(message_id))
					return;

				RecentId[RecentIdIndex] = message_id;
				RecentIdIndex = (RecentIdIndex + 1) % RecentId.Length;
				*/

				Debug.LogInfo($"(EVENT_SUB) Packet Received {metadata.AsGodotDictionary()["message_type"].AsString()}");

				switch (metadata.AsGodotDictionary()["message_type"].AsString())
				{
					case "session_welcome":
						{
							if (Peer.GetReadyState() != WebSocketPeer.State.Open)
								break;

							SessionId = (string)payload.AsGodotDictionary()["session"].AsGodotDictionary()["id"];
							Debug.Assert(!string.IsNullOrEmpty(SessionId));

							CreateEvents();
						}
						break;
					case "notification":
						{
							Debug.Assert(payload.AsGodotDictionary().ContainsKey("subscription"));
							Debug.Assert(payload.AsGodotDictionary().ContainsKey("event"));

							HandleEvent(payload.AsGodotDictionary());
						}
						break;
				}
			}
		}

		public void TestHypeTrain()
		{
			string str = @"
				{
				    ""metadata"": {
				        ""message_id"": ""befa7b53-d79d-478f-86b9-120f112b044e"",
				        ""message_type"": ""notification"",
				        ""message_timestamp"": ""2022-11-16T10:11:12.464757833Z"",
				        ""subscription_type"": ""channel.follow"",
				        ""subscription_version"": ""1""
				    },
				    ""payload"":
					{
						""subscription"": {
							""id"": ""f1c2a387-161a-49f9-a165-0f21d7a4e1c4"",
							""type"": ""channel.hype_train.end"",
							""version"": ""1"",
							""status"": ""enabled"",
							""cost"": 0,
							""condition"": {
								""broadcaster_user_id"": ""1337""
							},
							 ""transport"": {
								""method"": ""webhook"",
								""callback"": ""https://example.com/webhooks/callback""
							},
							""created_at"": ""2019-11-16T10:11:12.634234626Z""
						},
						""event"": {
							""id"": ""1b0AsbInCHZW2SQFQkCzqN07Ib2"",
							""broadcaster_user_id"": ""1337"",
							""broadcaster_user_login"": ""cool_user"",
							""broadcaster_user_name"": ""Cool_User"",
							""level"": 3,
							""total"": 137,
							""top_contributions"": [
								{ ""user_id"": ""123"", ""user_login"": ""pogchamp"", ""user_name"": ""PogChamp"", ""type"": ""bits"", ""total"": 50 },
								{ ""user_id"": ""456"", ""user_login"": ""kappa"", ""user_name"": ""Kappa"", ""type"": ""subscription"", ""total"": 45 }
							],
							""started_at"": ""2020-07-15T17:16:03.17106713Z"",
							""ended_at"": ""2024-03-18T12:12:12.17106713Z"",
							""cooldown_ends_at"": ""2020-07-15T18:16:11.17106713Z""
						}
				    }
				}
				";

			string strProgress = @"
				{
				    ""metadata"": {
				        ""message_id"": ""befa7b53-d79d-478f-86b9-120f112b044e"",
				        ""message_type"": ""notification"",
				        ""message_timestamp"": ""2022-11-16T10:11:12.464757833Z"",
				        ""subscription_type"": ""channel.follow"",
				        ""subscription_version"": ""1""
				    },
				    ""payload"":
					{
						 ""subscription"": {
								""id"": ""f1c2a387-161a-49f9-a165-0f21d7a4e1c4"",
								""type"": ""channel.hype_train.progress"",
								""version"": ""1"",
								""status"": ""enabled"",
								""cost"": 0,
								""condition"": {
									""broadcaster_user_id"": ""1337""
								},
								 ""transport"": {
									""method"": ""webhook"",
									""callback"": ""https://example.com/webhooks/callback""
								},
								""created_at"": ""2019-11-16T10:11:12.634234626Z""
							},
							""event"": {
								""id"": ""1b0AsbInCHZW2SQFQkCzqN07Ib2"",
								""broadcaster_user_id"": ""1337"",
								""broadcaster_user_login"": ""cool_user"",
								""broadcaster_user_name"": ""Cool_User"",
								""level"": 2,
								""total"": 700,
								""progress"": 200,
								""goal"": 1000,
								""top_contributions"": [
									{ ""user_id"": ""123"", ""user_login"": ""pogchamp"", ""user_name"": ""PogChamp"", ""type"": ""bits"", ""total"": 50 },
									{ ""user_id"": ""456"", ""user_login"": ""kappa"", ""user_name"": ""Kappa"", ""type"": ""subscription"", ""total"": 45 }
								],
								""last_contribution"": { ""user_id"": ""123"", ""user_login"": ""pogchamp"", ""user_name"": ""PogChamp"", ""type"": ""bits"", ""total"": 50 },
								""started_at"": ""2020-07-15T17:16:03.17106713Z"",
								""expires_at"": ""2020-07-15T17:16:11.17106713Z""
							}
				    }
				}
			";

			for (int i = 0; i < 3; ++i)
			{
				var json = new Json();
				json.Parse(strProgress);
				var data = json.Data.AsGodotDictionary();
				data["metadata"].AsGodotDictionary()["message_id"] = "befa7b53-d79d-478f-86b9-120f112b044e" + i;
				data["payload"].AsGodotDictionary()["event"].AsGodotDictionary()["started_at"] = ToRFC3339String(DateTime.UtcNow);
				GD.Print(json.Data);
				ParsePacket(data);
			}

			{
				var json = new Json();
				json.Parse(str);
				var data = json.Data.AsGodotDictionary();
				data["payload"].AsGodotDictionary()["event"].AsGodotDictionary()["ended_at"] = ToRFC3339String(DateTime.UtcNow);
				GD.Print(json.Data);
				ParsePacket(data);
			}
		}

		public static string ToRFC3339String(DateTime dateTime)
		{
			return XmlConvert.ToString(dateTime, XmlDateTimeSerializationMode.Utc);
		}

		public static DateTime FromRFC3339Time(string str)
		{
			try
			{
				return XmlConvert.ToDateTime(str, XmlDateTimeSerializationMode.Utc);
			}
			catch
			{
				return new DateTime();
			}
		}
	}
}
