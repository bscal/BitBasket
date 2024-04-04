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
		public int Id;

		public override bool Equals(object obj)
		{
			return obj is Cheermote cheermote
				&& Id == cheermote.Id
				&& string.Equals(Prefix, cheermote.Prefix, StringComparison.CurrentCultureIgnoreCase);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Prefix.ToLower(), Id);
		}

		public override string ToString()
		{
			return $"{Prefix.ToLower()}{Id}";
		}
	}

	public class CheermoteRequestData
	{
		public BitOrder Order;
		public float Lifetime;
	}

	public class RequestImgData
	{
		public Cheermote Cheermote;
		public CheermoteInfo CheermoteInfo;
	}

	public class CheermoteInfo
	{
		public string Url;
		public bool IsCustomEmote;

		public CheermoteInfo(string url, bool isCustomEmote)
		{
			Url = url;
			IsCustomEmote = isCustomEmote;
		}
	}

	public class CheermotesManager
	{
		public Dictionary<Cheermote, CheermoteInfo> CheermoteToUrl;
		public Dictionary<string, ImageTexture> PrefixToImageCache;

		public bool DebugFlag;

		private ConcurrentQueue<CheermoteRequestData> CheermoteQueue;
		private ConcurrentQueue<RequestImgData> RequestImageQueue;

		private BitManager BitManager;

		public CheermotesManager(BitManager bitManager)
		{
			BitManager = bitManager;

			CheermoteToUrl = new Dictionary<Cheermote, CheermoteInfo>(64);
			PrefixToImageCache = new Dictionary<string, ImageTexture>(64);
			CheermoteQueue = new ConcurrentQueue<CheermoteRequestData>();
			RequestImageQueue = new ConcurrentQueue<RequestImgData>();

			void AddCheerEmote(string name, int id, ImageTexture t)
			{
				var cheeremote = new Cheermote();
				cheeremote.Prefix = name;
				cheeremote.Id = id;

				Debug.LogDebug($"( Cheermotes ) Default {name} {id} {cheeremote}");

				CheermoteToUrl.Add(cheeremote, new( string.Empty, false ));
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

#if DEBUG
			var crenCheerTest1 = new Cheermote();
			crenCheerTest1.Prefix = "crendorcheer";
			crenCheerTest1.Id = 1;
			Add(crenCheerTest1,
				"https://d3aqoihi2n8ty8.cloudfront.net/partner-actions/7555574/f70a0f6f-1e64-4328-a5ae-abf3c8c56c5f/1/light/animated/1.5.gif",
				true);

			var crenCheerTest1000 = new Cheermote();
			crenCheerTest1000.Prefix = "crendorcheer";
			crenCheerTest1000.Id = 1000;
			Add(crenCheerTest1000,
				"https://d3aqoihi2n8ty8.cloudfront.net/partner-actions/7555574/f70a0f6f-1e64-4328-a5ae-abf3c8c56c5f/1000/light/animated/1.5.gif",
				true);
#endif
		}

		public void UpdateQueue(float dt)
		{
			while (RequestImageQueue.TryDequeue(out var requestData))
			{
				RequestImage(requestData.Cheermote, requestData.CheermoteInfo);
			}

			if (CheermoteQueue.TryPeek(out CheermoteRequestData data))
			{
				data.Lifetime += dt;

				// NOTE: RequestImage has 20sec timeout. This is also 20sec,
				// but doesn't start till it is the front of the queue.
				if (data.Lifetime > 20)
				{
					// Hard coded timeout, uses default bits
					BitOrderDefaultTextures(data.Order);
					BitManager.BitOrders.Add(data.Order);
					CheermoteQueue.TryDequeue(out var unused);
					return;
				}

				// Wait till all valid textures are loaded
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
						return;
					}
				}

				BitManager.BitOrders.Add(data.Order);
				CheermoteQueue.TryDequeue(out var unused2);
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
					case BitTypes.Bit1: return PrefixToImageCache[BitManager.DEFAULT_1];
					case BitTypes.Bit100: return PrefixToImageCache[BitManager.DEFAULT_100];
					case BitTypes.Bit1000: return PrefixToImageCache[BitManager.DEFAULT_1000];
					case BitTypes.Bit5000: return PrefixToImageCache[BitManager.DEFAULT_5000];
					case BitTypes.Bit10000: return PrefixToImageCache[BitManager.DEFAULT_10000];
					default: Debug.Error("Invalid Type"); return PrefixToImageCache[BitManager.DEFAULT_1];
				}
			}
		}

		private void RequestImage(Cheermote cheermote, CheermoteInfo cheermoteInfo)
		{
			HttpRequest request = new HttpRequest();
			BitManager.AddChild(request);
			request.Timeout = 20;

			request.RequestCompleted += (long result, long responseCode, string[] headers, byte[] body) =>
			{
				if (responseCode == 200)
				{
#if GODOT_WINDOWS
					Image image = new Image();
					Error loadImgError;

					if (cheermoteInfo.IsCustomEmote)
					{
						// TODO Currently dont care if you not on windows
						MemoryStream inStream = new MemoryStream(body);
						System.Drawing.Image drawingImg = System.Drawing.Image.FromStream(inStream);

						MemoryStream outStream = new MemoryStream();
						drawingImg.Save(outStream, System.Drawing.Imaging.ImageFormat.Png);
						loadImgError = image.LoadPngFromBuffer(outStream.ToArray());
					}
					else
					{
						loadImgError = image.LoadPngFromBuffer(body);
					}

					if (loadImgError != Error.Ok)
					{
						Debug.LogErr("( EmoteRequest ) Error " + loadImgError.ToString());
						return;
					}

					ImageTexture imgTexture = new ImageTexture();
					imgTexture.SetImage(image);

					Debug.LogDebug("Received emote png data! Caching");
#else
					imgTexture = GetTextureOrDefault(cheermote.ToString(), BitTypeFromBits(cheermote.Id));
#endif

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

			Error err = request.Request(cheermoteInfo.Url);
			if (err != Error.Ok)
			{
				Debug.Error(err.ToString());
			}
		}

		public static int CheermoteIdFromBitType(int type)
		{
			if (type == (int)BitTypes.Bit10000)
				return BitManager.BIT10000;
			if (type == (int)BitTypes.Bit5000)
				return BitManager.BIT5000;
			if (type == (int)BitTypes.Bit1000)
				return BitManager.BIT1000;
			if (type == (int)BitTypes.Bit100)
				return BitManager.BIT100;

			return BitManager.BIT1;
		}

		public static BitTypes BitTypeFromBits(int bitAmount)
		{
			if (bitAmount >= BitManager.BIT10000)
				return BitTypes.Bit10000;
			if (bitAmount >= BitManager.BIT5000)
				return BitTypes.Bit5000;
			if (bitAmount >= BitManager.BIT1000)
				return BitTypes.Bit1000;
			if (bitAmount >= BitManager.BIT100)
				return BitTypes.Bit100;

			return BitTypes.Bit1;
		}

		public static string CheermoteDefaultTextureFromId(int id)
		{
			return "Cheer" + id;
		}

		public void BitOrderDefaultTextures(BitOrder order)
		{
			order.Texture[(int)BitTypes.Bit10000] = PrefixToImageCache[BitManager.DEFAULT_10000];
			order.Texture[(int)BitTypes.Bit5000] = PrefixToImageCache[BitManager.DEFAULT_5000];
			order.Texture[(int)BitTypes.Bit1000] = PrefixToImageCache[BitManager.DEFAULT_1000];
			order.Texture[(int)BitTypes.Bit100] = PrefixToImageCache[BitManager.DEFAULT_100];
			order.Texture[(int)BitTypes.Bit1] = PrefixToImageCache[BitManager.DEFAULT_1];

			order.TextureId[(int)BitTypes.Bit10000] = BitManager.DEFAULT_10000;
			order.TextureId[(int)BitTypes.Bit5000] = BitManager.DEFAULT_5000;
			order.TextureId[(int)BitTypes.Bit1000] = BitManager.DEFAULT_1000;
			order.TextureId[(int)BitTypes.Bit100] = BitManager.DEFAULT_100;
			order.TextureId[(int)BitTypes.Bit1] = BitManager.DEFAULT_1;
		}

		public bool Exists(Cheermote cheermote)
		{
			return CheermoteToUrl.ContainsKey(cheermote);
		}

		internal void Add(Cheermote cheermote, string url, bool isCustomEmote)
		{
			if (!Exists(cheermote))
			{
				Debug.LogInfo($"( RequestCheermotes ) Added {cheermote.Prefix}, {cheermote.Id}, {url}");
				CheermoteInfo cheerInfo = new CheermoteInfo(url, isCustomEmote);
				CheermoteToUrl.Add(cheermote, cheerInfo);

				if (isCustomEmote)
					RequestImage(cheermote, cheerInfo);
			}
			else
			{
#if DEBUG
				CheermoteInfo found = CheermoteToUrl[cheermote];
				Debug.LogInfo($"( RequestCheermotes ) Exists {cheermote.Prefix}, {cheermote.Id}, {found.Url}");
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
					cheermotePerBit.Id = CheermoteIdFromBitType(i);
					Debug.LogDebug($"Proccessing sub-bit {cheermotePerBit.Prefix}, for {cheermotePerBit.Id} {bits}");

					if (CheermoteToUrl.TryGetValue(cheermotePerBit, out CheermoteInfo info))
					{
						data.Order.TextureId[i] = cheermotePerBit.ToString();

						if (!cheermote.Prefix.Equals("Cheer", StringComparison.CurrentCultureIgnoreCase) 
							&& !PrefixToImageCache.ContainsKey(cheermotePerBit.ToString()))
						{
							RequestImgData requestData = new();
							requestData.Cheermote = cheermotePerBit;
							requestData.CheermoteInfo = info;
							RequestImageQueue.Enqueue(requestData);
							
							Debug.LogDebug($"Enqueuing {cheermote.ToString()}");
						}
					}
					else
					{
						data.Order.TextureId[i] = CheermoteDefaultTextureFromId(cheermotePerBit.Id);
						Debug.LogDebug($"Cheermote url not found");
					}
				}
			}

			CheermoteQueue.Enqueue(data);
		}
	}
}
