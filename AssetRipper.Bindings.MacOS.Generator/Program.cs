using AsmResolver.DotNet;
using SharpCompress.Archives.Zip;
using System.Diagnostics;

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

		// License
		File.WriteAllBytes(Path.Combine(outputDirectory, NuGetPackageContents.LicensePath), arm64Contents.License);

		// Managed library
		Directory.CreateDirectory(Path.Combine(outputDirectory, $"lib/{DotNetVersion}"));
		ConvertManagedLibrary(arm64Contents.ManagedLibrary).Write(Path.Combine(outputDirectory, $"lib/{DotNetVersion}/Microsoft.macOS.dll"));

		// Arm64 native library
		Directory.CreateDirectory(Path.Combine(outputDirectory, "runtimes/osx-arm64/native"));
		File.WriteAllBytes(Path.Combine(outputDirectory, "runtimes/osx-arm64/native/libxamarin-dotnet-coreclr.dylib"), arm64Contents.NativeLibrary);

		// X64 native library
		Directory.CreateDirectory(Path.Combine(outputDirectory, "runtimes/osx-x64/native"));
		File.WriteAllBytes(Path.Combine(outputDirectory, "runtimes/osx-x64/native/libxamarin-dotnet-coreclr.dylib"), x64Contents.NativeLibrary);
	}

	private static ModuleDefinition ConvertManagedLibrary(byte[] managedLibraryData)
	{
		const string OriginalName = "__Internal";
		const string NewName = "libxamarin-dotnet-coreclr";

		// There's two things we need to change in the managed library:
		// 1. References to __Internal need to be changed to libxamarin-dotnet-coreclr.
		// 2. Symbol names in the managed library are slightly different from the native library.
		//    They start with "xamarin_" instead of "_xamarin_", so we prefix them with an underscore.

		ModuleDefinition module = ModuleDefinition.FromBytes(managedLibraryData);

		foreach (MethodDefinition method in module.GetAllTypes().SelectMany(type => type.Methods))
		{
			if (method.ImplementationMap?.Scope?.Name?.Value is OriginalName)
			{
				Debug.Assert(method.ImplementationMap.Name is not null);
				Debug.Assert(method.ImplementationMap.Name.Value.StartsWith("xamarin_", StringComparison.Ordinal));
				method.ImplementationMap.Name = '_' + method.ImplementationMap.Name.Value;
			}
		}

		module.ModuleReferences.Single(r => r.Name == OriginalName).Name = NewName;

		return module;
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

	private readonly record struct NuGetPackageContents(byte[] License, byte[] ManagedLibrary, byte[] NativeLibrary)
	{
		public const string LicensePath = "LICENSE";
		public static string GetManagedLibraryPath(string runtime)
		{
			return $"runtimes/{runtime}/lib/{DotNetVersion}/Microsoft.macOS.dll";
		}
		public static string GetNativeLibraryPath(string runtime)
		{
			return $"runtimes/{runtime}/native/libxamarin-dotnet-coreclr.dylib";
		}

		public static NuGetPackageContents Read(Stream compressedStream, string runtime)
		{
			ZipArchive archive = ZipArchive.Open(compressedStream);

			byte[] license = [];
			byte[] managedLibrary = [];
			byte[] nativeLibrary = [];

			string managedLibraryPath = GetManagedLibraryPath(runtime);
			string nativeLibraryPath = GetNativeLibraryPath(runtime);

			foreach (ZipArchiveEntry entry in archive.Entries)
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
				else if (entryPath == nativeLibraryPath)
				{
					using Stream stream = entry.OpenEntryStream();
					nativeLibrary = new byte[entry.Size];
					stream.ReadExactly(nativeLibrary, 0, nativeLibrary.Length);
				}
			}

			return new(license, managedLibrary, nativeLibrary);
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
