namespace AssetRipper.Bindings.MacOS.Generator;

internal static class Program
{
	// https://www.nuget.org/packages/Microsoft.macOS.Runtime.osx-arm64
	// https://www.nuget.org/packages/Microsoft.macOS.Runtime.osx-x64
	// https://github.com/dotnet/macios

	private const string Arm64Link = "https://www.nuget.org/api/v2/package/Microsoft.macOS.Runtime.osx-arm64/14.2.9244-net9-p2";
	private const string X64Link = "https://www.nuget.org/api/v2/package/Microsoft.macOS.Runtime.osx-x64/14.2.9244-net9-p2";
	private const string DotNetVersion = "net9.0";

	static async Task Main(string[] args)
	{
		string? outputDirectory = args.Length > 0 ? args[0] : null;
		if (!Directory.Exists(outputDirectory))
		{
			Console.WriteLine("Please provide an output directory as the first argument.");
			return;
		}

		HttpClient client = CreateHttpClient();
		MemoryStream arm64Data = await Download(client, Arm64Link);
		MemoryStream x64Data = await Download(client, X64Link);

		NuGetPackageContents arm64Contents = NuGetPackageContents.Read(arm64Data, "osx-arm64");
		NuGetPackageContents x64Contents = NuGetPackageContents.Read(x64Data, "osx-x64");

		if (!NuGetPackageContents.EqualLicenses(arm64Contents, x64Contents))
		{
			Console.WriteLine("The licenses of the two packages do not match.");
			return;
		}

		if (!NuGetPackageContents.EqualManagedLibraries(arm64Contents, x64Contents))
		{
			Console.WriteLine("The managed libraries of the two packages do not match, but that's okay.");
		}

		File.WriteAllBytes(Path.Combine(outputDirectory, NuGetPackageContents.LicensePath), arm64Contents.License);
		Directory.CreateDirectory(Path.Combine(outputDirectory, $"lib/{DotNetVersion}"));
		File.WriteAllBytes(Path.Combine(outputDirectory, $"lib/{DotNetVersion}/Microsoft.macOS.dll"), arm64Contents.ManagedLibrary);
		foreach ((string relativePath, byte[] data) in arm64Contents.NativeLibraries)
		{
			string fullPath = Path.Combine(outputDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
			File.WriteAllBytes(fullPath, data);
		}
		foreach ((string relativePath, byte[] data) in x64Contents.NativeLibraries)
		{
			string fullPath = Path.Combine(outputDirectory, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
			File.WriteAllBytes(fullPath, data);
		}
	}

	private static async Task<MemoryStream> Download(HttpClient client, string link)
	{
		using Stream stream = await client.GetStreamAsync(link);
		MemoryStream memoryStream = new();
		await stream.CopyToAsync(memoryStream);
		memoryStream.Position = 0; // Reset the position to the beginning of the stream
		return memoryStream;
	}

	private static HttpClient CreateHttpClient()
	{
		HttpClient client = new();
		const string userAgent = "AssetRipper.Bindings.MacOS/1.0";
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
		return client;
	}

	private readonly record struct NuGetPackageContents(byte[] License, byte[] ManagedLibrary, List<(string, byte[])> NativeLibraries)
	{
		public const string LicensePath = "LICENSE";
		public static string GetManagedLibraryPath(string runtime)
		{
			return $"runtimes/{runtime}/lib/{DotNetVersion}/Microsoft.macOS.dll";
		}
		public static string GetNativeLibraryDirectory(string runtime)
		{
			return $"runtimes/{runtime}/native/";
		}

		public static NuGetPackageContents Read(Stream compressedStream, string runtime)
		{
			var archive = SharpCompress.Archives.Zip.ZipArchive.Open(compressedStream);

			byte[] license = [];
			byte[] managedLibrary = [];
			List<(string, byte[])> nativeLibraries = new();

			string managedLibraryPath = GetManagedLibraryPath(runtime);
			string nativeLibraryDirectory = GetNativeLibraryDirectory(runtime);

			foreach (var entry in archive.Entries)
			{
				if (entry.IsDirectory)
				{
					continue;
				}

				string? entryPath = entry.Key;
				if (entryPath == null)
				{
					continue;
				}

				if (entryPath == LicensePath)
				{
					using Stream stream = entry.OpenEntryStream();
					license = new byte[entry.Size];
					stream.ReadExactly(license, 0, license.Length);
				}
				else if (entryPath == managedLibraryPath)
				{
					using Stream stream = entry.OpenEntryStream();
					managedLibrary = new byte[entry.Size];
					stream.ReadExactly(managedLibrary, 0, managedLibrary.Length);
				}
				else if (entryPath.StartsWith(nativeLibraryDirectory, StringComparison.Ordinal))
				{
					using Stream stream = entry.OpenEntryStream();
					byte[] data = new byte[entry.Size];
					stream.ReadExactly(data, 0, data.Length);
					nativeLibraries.Add((entryPath, data));
				}
			}

			return new(license, managedLibrary, nativeLibraries);
		}

		public static bool EqualLicenses(NuGetPackageContents left, NuGetPackageContents right)
		{
			return left.License.SequenceEqual(right.License);
		}

		public static bool EqualManagedLibraries(NuGetPackageContents left, NuGetPackageContents right)
		{
			return left.ManagedLibrary.SequenceEqual(right.ManagedLibrary);
		}
	}
}
