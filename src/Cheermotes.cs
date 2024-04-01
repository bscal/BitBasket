using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace BitCup
{
	public class Cheermote
	{
		public string Prefix;
		public int id;

		public override bool Equals(object obj)
		{
			return obj is Cheermote cheermote
				&& id == cheermote.id
				&& string.Equals(Prefix, cheermote.Prefix, StringComparison.CurrentCultureIgnoreCase);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Prefix.ToLower(), id);
		}

		public override string ToString()
		{
			return $"{Prefix.ToLower()}{id}";
		}
	}

	public class CheermoteRequestData
	{
		public BitOrder Order;
		public float Lifetime;
		public float UpdateTimer;
	}

	public class RequestImgData
	{
		public Cheermote Cheermote;
		public string Url;
	}

	public class CheermotesManager
	{
		public Dictionary<Cheermote, string> CheermoteToUrl;
		public Dictionary<string, ImageTexture> PrefixToImageCache;

		private ConcurrentBag<CheermoteRequestData> CheermoteQueue;
		private ConcurrentQueue<RequestImgData> RequestImageQueue;

		private BitManager BitManager;

		public const string DEFAULT_1 = "cheer1";
		public const string DEFAULT_100 = "cheer100";
		public const string DEFAULT_1000 = "cheer1000";
		public const string DEFAULT_5000 = "cheer5000";
		public const string DEFAULT_10000 = "cheer10000";

		public CheermotesManager(BitManager bitManager)
		{
			BitManager = bitManager;

			CheermoteToUrl = new Dictionary<Cheermote, string>(64);
			PrefixToImageCache = new Dictionary<string, ImageTexture>(64);
			CheermoteQueue = new ConcurrentBag<CheermoteRequestData>();
			RequestImageQueue = new ConcurrentQueue<RequestImgData>();

			void AddCheerEmote(string name, int id, ImageTexture t)
			{
				var cheeremote = new Cheermote();
				cheeremote.Prefix = name;
				cheeremote.id = id;

				Debug.LogDebug($"( Cheermotes ) Default {name} {id} {cheeremote}");

				CheermoteToUrl.Add(cheeremote, string.Empty);
				PrefixToImageCache.Add(cheeremote.ToString(), t);
			}

			ImageTexture CompressedToImageTexture(CompressedTexture2D t)
			{
				Image i = t.GetImage();
				ImageTexture res = new();
				res.SetImage(i);
				return res;
			}

			AddCheerEmote("Cheer", 10000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit10000Texture));
			AddCheerEmote("Cheer", 5000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit5000Texture));
			AddCheerEmote("Cheer", 1000, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit1000Texture));
			AddCheerEmote("Cheer", 100, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit100Texture));
			AddCheerEmote("Cheer", 1, CompressedToImageTexture((CompressedTexture2D)BitManager.Bit1Texture));
		}

		public void UpdateQueue(float dt)
		{
			while (RequestImageQueue.TryDequeue(out var requestData))
			{
				RequestImage(requestData.Cheermote, requestData.Url);
			}

			while (CheermoteQueue.TryTake(out var data))
			{
				data.Lifetime += dt;
				data.UpdateTimer += dt;

				if (data.Lifetime > 10)
				{
					// Hard coded timeout, uses default bits
					BitManager.BitOrders.Add(data.Order);
					Debug.LogDebug("TESTF");
					return;
				}

				for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
				{
					if (data.Order.TextureId[i] == null)
						continue;

					if (PrefixToImageCache.TryGetValue(data.Order.TextureId[i], out var texture))
					{
						data.Order.Texture[i] = texture;
					}
					else
					{
						CheermoteQueue.Add(data);
						return;
					}
				}

				BitManager.BitOrders.Add(data.Order);
			}
		}

		public void SaveImages()
		{
			string dirPath = Path.Combine(Directory.GetCurrentDirectory(), "cache");

			Directory.CreateDirectory(dirPath);

			foreach (var pair in PrefixToImageCache)
			{
				if (string.IsNullOrWhiteSpace(pair.Key))
					continue;

				if (pair.Key.StartsWith("cheer"))
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
					if (creationTime.Subtract(DateTime.Now) > TimeSpan.FromDays(7.0))
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

						PrefixToImageCache.TryAdd(fileBaseName, imgTexture);
					}
				}
				catch (Exception ex)
				{
					Debug.LogDebug(ex.ToString());
					continue;
				}
			}

			foreach (string fileName in filesToRemove)
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
				&& PrefixToImageCache.TryGetValue(textureId, out ImageTexture texture))
			{
				return texture;
			}
			else
			{
				switch (type)
				{
					case BitTypes.Bit1: return PrefixToImageCache[DEFAULT_1];
					case BitTypes.Bit100: return PrefixToImageCache[DEFAULT_100];
					case BitTypes.Bit1000: return PrefixToImageCache[DEFAULT_1000];
					case BitTypes.Bit5000: return PrefixToImageCache[DEFAULT_5000];
					case BitTypes.Bit10000: return PrefixToImageCache[DEFAULT_10000];
					default: Debug.Error("Invalid Type"); return PrefixToImageCache[DEFAULT_1];
				}
			}
		}

		private void RequestImage(Cheermote cheermote, string url)
		{
			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.Timeout = 10;

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

					if (PrefixToImageCache.TryAdd(cheermote.ToString(), imgTexture))
					{
						 Debug.LogDebug($"Added Cheermote {cheermote.ToString()}" );
					}
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

		public static int CheermoteIdFromBits(int type)
		{
			if (type == (int)BitTypes.Bit10000)
				return 10000;
			if (type == (int)BitTypes.Bit5000)
				return 5000;
			if (type == (int)BitTypes.Bit1000)
				return 1000;
			if (type == (int)BitTypes.Bit100)
				return 100;
			else
				return 1;
		}

		public bool Exists(Cheermote cheermote)
		{
			return CheermoteToUrl.ContainsKey(cheermote);
		}

		internal void Add(Cheermote cheermote, string url)
		{
			if (!Exists(cheermote))
			{
				Debug.LogInfo($"( RequestCheermotes ) Added {cheermote.Prefix}, {cheermote.id}, {url}");
				CheermoteToUrl.Add(cheermote, url);
			}
			else
			{
#if DEBUG	
				string foundUrl = CheermoteToUrl[cheermote];
				Debug.LogInfo($"( RequestCheermotes ) Exists {cheermote.Prefix}, {cheermote.id}, {foundUrl}");
#endif
			}
		}

		public void ProcessOrderQueueForTexturesOrDefault(Cheermote cheermote, int amount)
		{
			CheermoteRequestData data = new CheermoteRequestData();
			data.Order = BitManager.CreateBitOrderSplitBits(amount);

			Debug.LogDebug($"Proccessing {cheermote.ToString()}, for {amount}");

			for (int i = 0; i < (int)BitTypes.MaxBitTypes; ++i)
			{
				short bits = data.Order.BitAmounts[i];
				if (bits > 0)
				{
					Cheermote cheermotePerBit = new();
					cheermotePerBit.Prefix = cheermote.Prefix;
					cheermotePerBit.id = CheermoteIdFromBits(i);
					Debug.LogDebug($"Proccessing sub-bit {cheermotePerBit.Prefix}, for {cheermotePerBit.id} {bits}");
					if (CheermoteToUrl.TryGetValue(cheermotePerBit, out string url))
					{
						data.Order.TextureId[i] = cheermotePerBit.ToString();

						if (!cheermote.Prefix.Equals("Cheer", StringComparison.CurrentCultureIgnoreCase) 
							&& !PrefixToImageCache.ContainsKey(cheermotePerBit.ToString()))
						{
							RequestImgData requestData = new();
							requestData.Cheermote = cheermotePerBit;
							requestData.Url = url;
							RequestImageQueue.Enqueue(requestData);
							
							Debug.LogDebug($"Enqueuing {cheermote.ToString()}");
						}
					}
					else
					{
						BitManager.CreateOrderWithChecks(amount);

						Debug.LogDebug($"Cheermote not found");
					}
				}
			}

			CheermoteQueue.Add(data);
		}
	}
}
