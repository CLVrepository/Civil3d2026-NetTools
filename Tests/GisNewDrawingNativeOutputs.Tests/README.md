# Native Map output reader checks

This standalone console project links the production `GisNewDrawingNativeOutputs.cs` reader. It does not reference or load Autodesk assemblies and does not create or import a drawing.

Run with the .NET 10 SDK:

```powershell
dotnet run --project Tests/GisNewDrawingNativeOutputs.Tests/GisNewDrawingNativeOutputs.Tests.csproj --configuration Release
```

An explicitly selected compatible SDK target can be checked with `-p:GisTestTargetFramework=net8.0`. A pass on another runtime does not replace the .NET 10 check or Civil 3D acceptance.

Coverage includes the four documented getter contracts, genuine enum pointer parameters, reflected enum by-reference alternatives, nullable text, initialized output storage, all eight signed/unsigned enum underlying types, unchanged and undefined outputs, incorrect signatures and namespaces, ambiguous overloads, propagated native exceptions, repeated reads, and failure before invocation when an enum has no unused sentinel.

Column-only cases cover a caller's explicit permission for an untouched enum with null/empty returned destination text. The result retains a null mode. Strict default calls, all pair getters, nonempty/whitespace destinations, and other undefined enum values still fail. Both pointer and reference shapes verify this boundary, null-versus-empty diagnostics, preserved defined enum values, one invocation per call, and no permission leaking between calls. These fake cases do not prove that the actual failed native column was unmapped.

`AllowUnsafeBlocks` is enabled only in this test project to declare pointer-shaped fake getters. The production reader uses safe C# with a boxed `IntPtr`; the main application project needs no unsafe setting. The named fake enums reproduce the documented members; their numeric fixture values are not a measurement of installed Autodesk values. Emitted fixtures provide distinct enum types with the exact required full name for storage-width checks.

Allocation cleanup is enforced by the production `finally` around initialization, invocation, decoding, and validation. The exception test verifies propagation and subsequent independent operation; it does not read freed memory or claim to measure native allocation leaks.

Microsoft runtime references:

- [.NET 10 CoreCLR accepts `IntPtr` for pointer parameter types](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/System.Private.CoreLib/src/System/RuntimeType.CoreCLR.cs#L3739)
- [Runtime test invokes a pointer parameter with boxed `IntPtr`](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Runtime/tests/System.Reflection.Tests/MethodCommonTests.cs#L249)
- [`MethodInfoTests` runs those shared tests through `MethodInfo.Invoke`](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Runtime/tests/System.Reflection.Tests/MethodInfoTests.cs#L13)

These fixtures validate the reflection boundary. They do not establish that an installed Map build exposes the expected signatures or that a native import succeeds. Run the actual Civil 3D workflow with its real `ManagedMapApi` assembly after building the application. Tests were authored but not executed in the cloud workspace because its .NET SDK is unavailable.
