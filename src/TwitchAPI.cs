using Godot;
using System;
using System.Collections.Generic;
using static Godot.HttpClient;

namespace BitCup
{
	public class TwitchAPI
	{
		BitManager BitManager;

		string FillTheCupRewardId;

		float Counter;

		public TwitchAPI(BitManager bitManager)
		{
			BitManager = bitManager;
		}

		public void UpdateEventServer(float dt)
		{
			Counter += dt;
			if (Counter > 6f)
			{
				Counter = 0;

				RequestHypeTrain();

				if (!string.IsNullOrEmpty(FillTheCupRewardId))
				{
					RequestRedeemedRewards(FillTheCupRewardId);
				}
			}
		}

		public void RequestHypeTrain()
		{
			string URL = $"https://api.twitch.tv/helix/hypetrain/events?broadcaster_id={BitManager.User.BroadcasterId}&first=1";
			string[] headers = new string[]
			{
				"Authorization: Bearer " + BitManager.User.OAuth,
				"Client-Id: " + TwitchManager.CLIENT_ID
			};

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestHypeTrain_RequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers, Method.Get);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		static string LastId = string.Empty;
		private void RequestHypeTrain_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				Debug.LogInfo("(HYPETRAIN) Request ok");
				var json = new Json();
				json.Parse(body.GetStringFromUtf8());
				var response = json.Data.AsGodotDictionary();

				if (response.TryGetValue("data", out var data)
					 && data.AsGodotArray().Count > 0)
				{
					var first = data.AsGodotArray()[0].AsGodotDictionary();

					if ((string)first["event_type"] == "hypetrain.end")
					{
						string timestamp = (string)first["event_timestamp"];
						DateTime time = DateTime.Parse(timestamp);
						Debug.LogDebug(timestamp + " " + time.ToString());
						if (DateTime.Now.Subtract(time).Minutes > 15)
						{
							return;
						}

						var eventData = first["event_data"].AsGodotDictionary();

						string eventId = (string)eventData["id"];
						if (eventId == LastId)
							return;

						LastId = eventId;

						// TODO trigger hypetrain event

						int level = (int)eventData["level"];

						Debug.LogInfo($"(HYPETRAIN) New HypeTrain. Level: {level}");
					}
				}
				else
				{
					Debug.LogInfo("(HYPETRAIN) None found");
				}
			}
			else if (responseCode == 401)
			{

			}
			else
				Debug.LogErr("Unknown reponse " + responseCode);
		}

		public void ValidateOAuth()
		{
			const string URL = "https://id.twitch.tv/oauth2/validate";
			string[] headers = new string[1];
			headers[0] = "Authorization: OAuth " + BitManager.User.OAuth;

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += Validate_RequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void Validate_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				Debug.LogInfo("OAuth token is valid");
				var json = new Json();
				json.Parse(body.GetStringFromUtf8());
				var response = json.Data.AsGodotDictionary();

				string login = (string)response["login"];
				string user_id = (string)response["user_id"];

				Debug.Assert(!string.IsNullOrWhiteSpace(login));
				Debug.Assert(!string.IsNullOrWhiteSpace(user_id));

				if (!BitManager.User.Username.Equals(login))
				{
					Debug.LogErr("Usernames did not match");
					BitManager.TwitchManager.OAuthState = OAuthState.Invalid;
				}
				else
				{
					BitManager.User.BroadcasterId = user_id;
					BitManager.TwitchManager.OAuthState = OAuthState.Valid;
				}
			}
			else if (responseCode == 401)
			{
				Debug.LogInfo("OAuth token is invalid");
				BitManager.TwitchManager.OAuthState = OAuthState.Invalid;
			}
            else
            {
				Debug.LogDebug("Unknown code " + responseCode);
            }
		}

		public void RequestUser()
		{
			string URL = $"https://api.twitch.tv/helix/users?login={BitManager.User.Username}";
			string[] headers = new string[]
			{
				"Authorization: Bearer " + BitManager.User.OAuth,
				"Client-Id: " + TwitchManager.CLIENT_ID
			};

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestUser_RequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers, Method.Get);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void RequestUser_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				var json = new Json();
				json.Parse(body.GetStringFromUtf8());
				var response = json.Data.AsGodotDictionary();

				if (response.TryGetValue("data", out var data)
					&& data.AsGodotArray().Count > 0)
				{
					try
					{
						string id = (string)data.AsGodotArray()[0].AsGodotDictionary()["id"];
						BitManager.User.BroadcasterId = id;

						if (!BitManager.IsReady)
						{
							BitManager.IsReady = true;

							Debug.LogInfo($"User Received (Id: {id})");

							BitManager.TwitchAPI.RequestGetRewards();
						}
					}
					catch (Exception e)
					{
						Debug.Error(e.Message);
					}
				}
			}
			else if (responseCode == 401)
			{
				Debug.LogErr("401 Unauthorized");
			}
			else
			{
				Debug.LogErr("Unknown response " + responseCode);
			}
		}

		public void RequestGetRewards()
		{
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.BroadcasterId));
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.OAuth));

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={BitManager.User.BroadcasterId}";
			string[] headers = new string[]
			{
				$"client-id: {TwitchManager.CLIENT_ID}",
				$"Authorization: Bearer {BitManager.User.OAuth}",
			};

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestGetRewards_OnCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers, HttpClient.Method.Get);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void RequestGetRewards_OnCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			switch (responseCode)
			{
				case 200:
					{
						var json = new Json();
						json.Parse(body.GetStringFromUtf8());
						var response = json.Data.AsGodotDictionary();

						if (response.TryGetValue("data", out Variant data)
							&& data.AsGodotArray().Count > 0)
						{
							var dataArray = data.AsGodotArray();
							foreach (var obj in dataArray)
							{
								bool wasOurReward = (string)obj.AsGodotDictionary()["title"] == "Fill The Cup";
								if (wasOurReward)
									FillTheCupRewardId = (string)obj.AsGodotDictionary()["id"];

								// Creates or updates depending on if out reward was found
								RequestCreateRewards(wasOurReward);
								return;
							}
						}
					}
					break;
				case 400:
					{
						Debug.LogErr("Broadcaster is not a partner or affiliate.");

						foreach (var h in headers)
							Debug.LogInfo(h);

						var json = new Json();
						json.Parse(body.GetStringFromUtf8());
						var response = json.Data.AsGodotDictionary();
						Debug.LogInfo(response.ToString());
					}
					break;
				case 401:
					{
						Debug.LogInfo("Unauthorized.");
						// TODO refresh token?
					}
					break;
				case 403:
					{
						Debug.LogInfo("Broadcaster is not a partner or affiliate.");
					}
					break;
				default:
					Debug.Error($"Unknown error occured. {result}, {responseCode}, {body}");
					break;
			}
		}

		public void RequestCreateRewards(bool shouldUpdate)
		{
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.BroadcasterId));
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.OAuth));

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={BitManager.User.BroadcasterId}";
			List<string> headers = new List<string>(4)
			{
				$"client-id: {TwitchManager.CLIENT_ID}",
				$"Authorization: Bearer {BitManager.User.OAuth}",
				"Content-Type: application/json",
			};

			var contents = new Godot.Collections.Dictionary<string, Variant>();
			contents.Add("title", "Fill The Cup");
			contents.Add("cost", (BitManager.Settings.FillTheCupCost > 0) ? BitManager.Settings.FillTheCupCost : 100);
			contents.Add("is_max_per_user_per_stream_enabled", BitManager.Settings.FillTheCupPerStream);
			contents.Add("max_per_user_per_stream", 1);
			contents.Add("is_global_cooldown_enabled", BitManager.Settings.FillTheCupCooldown > 0);
			contents.Add("global_cooldown_seconds", (BitManager.Settings.FillTheCupCooldown > 0) ? BitManager.Settings.FillTheCupCooldown : 100);

			headers.Add(Json.Stringify(contents));

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += CreateRewards_OnCompleted;
			request.Timeout = 5;
			var method = (shouldUpdate) ? Method.Patch : Method.Post;
			Error err = request.Request(URL, headers.ToArray(), method);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void CreateRewards_OnCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			Debug.LogDebug("RequestRewards_OnCompleted - " + responseCode);
			switch (responseCode)
			{
				case 200:
					{
						Debug.LogInfo("Succesfully created!");
					} break;
				case 400:
					{
						Debug.LogInfo("Broadcaster is not a partner or affiliate.");
						foreach (var h in headers) Debug.LogInfo(h);
						Debug.LogInfo(body.ToString());
					} break;
				case 401:
					{
						Debug.LogInfo("Unauthorized.");
						// TODO refresh token?
					} break;
				case 403:
					{
						Debug.LogInfo("Broadcaster is not a partner or affiliate.");
					} break;
				default:
					Debug.Error($"Unknown error occured. {result}, {responseCode}, {body}");
					break;
			}
		}

		public void RequestRedeemedRewards(string rewardId)
		{
			if (string.IsNullOrEmpty(rewardId))
			{
				Debug.LogErr("rewardId is null or empty");
				return;
			}	

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions?" +
				$"broadcaster_id={BitManager.User.BroadcasterId}&reward_id={rewardId}&status=UNFULFILLED";

			string[] headers = new string[]
			{
				$"Client-Id: {TwitchManager.CLIENT_ID}",
				$"Authorization: Bearer {BitManager.User.OAuth}",
			};

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestRedeemededRewards_OnCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers, HttpClient.Method.Get);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void RequestRedeemededRewards_OnCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				var json = new Json();
				json.Parse(body.GetStringFromUtf8());
				var response = json.Data.AsGodotDictionary();

				if (response.TryGetValue("data", out Variant data)
					&& data.AsGodotArray().Count > 0)
				{
					string rewardId = null;
					List<string> ids = new List<string>();

					var dataArray = data.AsGodotArray();
					foreach (var obj in dataArray)
					{
						ids.Add((string)obj.AsGodotDictionary()["id"]);

						var rewardData = obj.AsGodotDictionary()["reward"];
						rewardId = (string)rewardData.AsGodotDictionary()["id"];
					}

					RequestUpdateRedeems(rewardId, ids, true);
				}
			}
			else if (responseCode == 401)
			{
				Debug.LogErr("401 Unauthorized");
				// TODO
			}
			else
				Debug.Error("Unknown response " + responseCode);
		}

		public void RequestUpdateRedeems(string rewardId, List<string> ids, bool areFullfilled)
		{
			if (string.IsNullOrEmpty(rewardId))
			{
				Debug.LogErr("rewardId is null or empty");
				return;
			}

			Debug.Assert(ids != null);

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions" +
				$"?broadcaster_id={BitManager.User.BroadcasterId}&reward_id={rewardId}";

			foreach (var id in ids)
			{
				URL += "?id=" + id;
			}

			List<string> headers = new List<string>()
			{
				$"Client-Id: {TwitchManager.CLIENT_ID}",
				$"Authorization: Bearer {BitManager.User.OAuth}",
				"Content-Type: application/json",
			};

			var contents = new Godot.Collections.Dictionary<string, Variant>();
			contents.Add("status", "FULFILLED");
			headers.Add(Json.Stringify(contents));

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestUpdateRedeems_OnCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers.ToArray(), HttpClient.Method.Patch);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void RequestUpdateRedeems_OnCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				Debug.LogInfo("Updated redeems OK!");
			}
			else
			{
				Debug.LogInfo("Error updating redeems " + responseCode);
			}
		}
	}
}
