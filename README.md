# AssetRipper.Bindings.MacOS

Portable bindings for MacOS system libraries.

## Generation Process

The generator downloads the "official" bindings from Microsoft.

* https://www.nuget.org/packages/Microsoft.macOS.Runtime.osx-x64
* https://www.nuget.org/packages/Microsoft.macOS.Runtime.osx-arm64

Then, it combines them into a single package and removes runtime identifier requirements.

```
./nuget.exe pack AssetRipper.Bindings.MacOS/AssetRipper.Bindings.MacOS.nuspec
```
