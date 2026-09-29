# AterraEngine

Modular, data-driven C# game-engine foundations.

## Dependency injection

The headless DI container supports Host → World → Scene ownership, additional typed scopes,
validated immutable inputs, asynchronous cleanup, and generated Native AOT-compatible activation.

- [API, lifetime rules, generator usage, and shutdown contract](docs/dependency-injection.md)
- [Runnable headless / Native AOT example](src/AterraEngine.DependencyInjection.Example/Program.cs)

```powershell
dotnet build AtteraEngine.slnx
dotnet test --solution AtteraEngine.slnx
dotnet run --project src/AterraEngine.DependencyInjection.Example
```
