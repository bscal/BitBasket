using Godot;
using System;
using System.Collections.Generic;
using static Godot.HttpClient;

namespace BitCup
{
	public enum TwitchOAuthState
	{
		Unknown,
		Valid,
		Invalid
	}

	// Custom twitch api calls
	public class TwitchAPI
	{
		BitManager BitManager;

		public TwitchOAuthState OAuthState;

		string FillTheCupRewardId;


		int RequestUserRetry;

		float Counter;
		const float COUNTER_START = 5.0f;

		public TwitchAPI(BitManager bitManager)
		{
			BitManager = bitManager;
			FillTheCupRewardId = string.Empty;
		}

		public void UpdateEventServer(float dt)
		{
			if (!IsOAuthValid())
			{
				BitManager.TwitchManager.FetchNewOAuth();
				return;
			}

			Counter -= dt;
			if (Counter < 0.0f)
			{
				Counter = COUNTER_START;

				CheckOAuth();
				BitManager.TwitchManager.Client.JoinChannel(BitManager.User.Username);

				if (!string.IsNullOrEmpty(FillTheCupRewardId))
					RequestRedeemedRewards(FillTheCupRewardId);
			}
		}

		public bool IsOAuthValid()
		{
			if (string.IsNullOrWhiteSpace(BitManager.User.Username))
			{
				Debug.LogErr("Invalid username");
				return false;
			}

			if (string.IsNullOrEmpty(BitManager.User.OAuth))
			{
				Debug.LogErr("Invalid or null OAuth token");
				return false;
			}

			return BitManager.TwitchAPI.OAuthState == TwitchOAuthState.Valid;
		}

		public void CheckOAuth()
		{
			const string URL = "https://id.twitch.tv/oauth2/validate";
			string[] headers = new string[1];
			headers[0] = "Authorization: OAuth " + BitManager.User.OAuth;

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += CheckOAuth_RequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void CheckOAuth_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (responseCode == 200)
			{
				Debug.LogInfo("(CheckOAuth) Ok");
			}
			else if (responseCode == 401)
			{
				BitManager.InvalidateTwitchState();
			}
		}

		public void ValidateOAuth()
		{
			const string URL = "https://id.twitch.tv/oauth2/validate";
			string[] headers = new string[1];
			headers[0] = "Authorization: OAuth " + BitManager.User.OAuth;

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += ValidateOAuth_RequestCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void ValidateOAuth_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
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
					OAuthState = TwitchOAuthState.Invalid;
				}
				else if (OAuthState != TwitchOAuthState.Valid)
				{
					OAuthState = TwitchOAuthState.Valid;
					BitManager.User.BroadcasterId = user_id;
					if (BitManager.Settings.ShouldAutoConnect)
					{
						BitManager.TwitchManager.ConnectAndInitClient();
					}
				}
			}
			else if (responseCode == 401)
			{
				Debug.LogInfo("OAuth token is invalid");
				if (BitManager.Settings.ShouldAutoConnect)
				{
					BitManager.TwitchManager.FetchNewOAuth();
				}
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
			request.Timeout = 10;
			Error err = request.Request(URL, headers, Method.Get);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		private void RequestUser_RequestCompleted(long result, long responseCode, string[] headers, byte[] body)
		{
			if (result == (long)HttpRequest.Result.Timeout)
			{
				if (RequestUserRetry <= 1)
				{
					++RequestUserRetry;
					RequestUser();
				}
				else
				{
					BitManager.State = State.PreStart;
					Debug.LogErr("(REQUEST_USER) Max retries reached!");
				}
            }

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
				BitManager.InvalidateTwitchState();
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
						Debug.LogInfo("401 Unauthorized");
						BitManager.InvalidateTwitchState();
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

		public void RequestCreateRewards()
		{
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.BroadcasterId));
			Debug.Assert(!string.IsNullOrWhiteSpace(BitManager.User.OAuth));

			bool shouldUpdate = !string.IsNullOrWhiteSpace(FillTheCupRewardId);

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id={BitManager.User.BroadcasterId}";
			string[] headers = new string[]
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

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += CreateRewards_OnCompleted;
			request.Timeout = 5;
			var method = (shouldUpdate) ? Method.Patch : Method.Post;
			Error err = request.Request(URL, headers, method, Json.Stringify(contents));
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
						BitManager.InvalidateTwitchState();
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
						BitManager.CreateOrderWithChecks(BitManager.Settings.FillTheCupBits);
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
			if (string.IsNullOrEmpty(FillTheCupRewardId))
			{
				Debug.LogErr("rewardId is null or empty");
				return;
			}

			Debug.Assert(ids != null);

			string URL = $"https://api.twitch.tv/helix/channel_points/custom_rewards/redemptions" +
				$"?broadcaster_id={BitManager.User.BroadcasterId}&reward_id={FillTheCupRewardId}";

			foreach (var id in ids)
			{
				URL += "?id=" + id;
			}

			string[] headers = new string[]
			{
				$"Client-Id: {TwitchManager.CLIENT_ID}",
				$"Authorization: Bearer {BitManager.User.OAuth}",
				"Content-Type: application/json",
			};

			var contents = new Godot.Collections.Dictionary<string, Variant>();
			contents.Add("status", "FULFILLED");

			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.RequestCompleted += RequestUpdateRedeems_OnCompleted;
			request.Timeout = 5;
			Error err = request.Request(URL, headers, HttpClient.Method.Patch, Json.Stringify(contents));
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
